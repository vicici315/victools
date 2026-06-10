/// SpotLightVolume v1.0 - 轻量探照灯体积雾效果
/// 基于锥形Mesh + 自定义Shader的简洁实现，性能优化版本
/// 参数：起始距离、最长距离、边缘羽化、末端羽化、混合方式等
/// SpotLightVolume v2.0 - 参考VLB架构重写
/// 归一化Mesh + localScale缩放 + 双Pass渲染 + Fresnel + 距离衰减 + DepthBlend

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Vic.Runtime
{
    public enum VolumeBlendMode
    {
        Additive = 0,
        SoftAdditive = 1,
        Alpha = 2
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public class SpotLightVolume : MonoBehaviour
    {
        // [Header("距离控制")]
        [Tooltip("光源半径(光柱始端宽度): 0=尖锥, 越大始端越宽")]
        [Min(0f)]
        public float lightSourceRadius = 0f;

        [Tooltip("衰减起始距离")]
        [Min(0f)]
        public float fallOffStart = 0f;

        [Tooltip("衰减结束距离(最远距离)")]
        [Min(0.1f)]
        public float maxDistance = 3f;

        // [Header("羽化控制")]
        [Tooltip("边缘羽化: 值越大边缘越软，越小越硬")]
        [Range(0.01f, 2f)]
        public float edgeFade = 0.3f;

        [Tooltip("末端羽化强度")]
        [Range(0f, 1f)]
        public float endFade = 0.5f;

        [Tooltip("正面眩光: 从光源方向看的亮度提升")]
        [Range(0f, 1f)]
        public float glareFrontal = 0.5f;

        [Tooltip("背面眩光: 从光束后方看的亮度提升")]
        [Range(0f, 1f)]
        public float glareBehind = 0.3f;

        // [Header("外观")]
        [Tooltip("雾效整体强度")]
        [Range(0f, 5f)]
        public float intensity = 1f;

        [Tooltip("跟随灯光颜色")]
        public bool colorFromLight = true;

        [ColorUsage(false, true)]
        public Color volumeColor = Color.white;

        [Tooltip("混合方式")]
        public VolumeBlendMode blendMode = VolumeBlendMode.Additive;

        // [Header("Mesh质量")]
        [Tooltip("圆锥面数")]
        [Range(3, 32)]
        public int coneSides = 12;

        [Tooltip("圆锥分段数")]
        [Range(1, 10)]
        public int coneSegments = 1;

        // 内部引用
        private Light _light;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Material _material;
        private GameObject _volumeChild;

        // 共享归一化Mesh缓存 (同sides+segments的beam共享一个mesh)
        private static Mesh _sharedMesh;
        private static int _sharedMeshSides;
        private static int _sharedMeshSegments;

        // Shader属性ID缓存
        private static readonly int _ColorID = Shader.PropertyToID("_VolumeColor");
        private static readonly int _IntensityID = Shader.PropertyToID("_Intensity");
        private static readonly int _FallOffStartID = Shader.PropertyToID("_FallOffStart");
        private static readonly int _FallOffEndID = Shader.PropertyToID("_FallOffEnd");
        private static readonly int _EdgeFadeID = Shader.PropertyToID("_EdgeFade");
        private static readonly int _EndFadeID = Shader.PropertyToID("_EndFade");
        private static readonly int _GlareFrontalID = Shader.PropertyToID("_GlareFrontal");
        private static readonly int _GlareBehindID = Shader.PropertyToID("_GlareBehind");
        private static readonly int _ConeRadiusStartID = Shader.PropertyToID("_ConeRadiusStart");
        private static readonly int _ConeRadiusEndID = Shader.PropertyToID("_ConeRadiusEnd");
        private static readonly int _ConeSlopeCosSinID = Shader.PropertyToID("_ConeSlopeCosSin");

        // 缓存值
        private float _cachedSpotAngle;
        private float _cachedMaxDistance;
        private int _cachedSides;
        private int _cachedSegments;

        void OnEnable()
        {
            _light = GetComponent<Light>();
            EnsureVolumeChild();
            UpdateGeometry();
            UpdateMaterial();

            // 确保深度纹理可用（shader需要采样场景深度来做遮挡）
            if (Camera.main != null)
            {
                Camera.main.depthTextureMode |= DepthTextureMode.Depth;
            }
        }

        void OnDisable()
        {
            if (_volumeChild != null)
                _volumeChild.SetActive(false);
        }

        void Update()
        {
            if (_light == null || _light.type != LightType.Spot) return;

            bool needRebuild = !Mathf.Approximately(_cachedSpotAngle, _light.spotAngle)
                            || !Mathf.Approximately(_cachedMaxDistance, maxDistance)
                            || _cachedSides != coneSides
                            || _cachedSegments != coneSegments;

            if (needRebuild)
                UpdateGeometry();

            UpdateMaterial();
        }

        void OnDestroy()
        {
            if (_volumeChild != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(_volumeChild);
                else
#endif
                    Destroy(_volumeChild);
            }

            if (_material != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(_material);
                else
#endif
                    Destroy(_material);
            }
        }

        private void EnsureVolumeChild()
        {
            if (_volumeChild != null)
            {
                _volumeChild.SetActive(true);
                _meshFilter = _volumeChild.GetComponent<MeshFilter>();
                _meshRenderer = _volumeChild.GetComponent<MeshRenderer>();
                return;
            }

            Transform existing = transform.Find("__SpotLightVolumeMesh__");
            if (existing != null)
            {
                _volumeChild = existing.gameObject;
                _meshFilter = _volumeChild.GetComponent<MeshFilter>();
                _meshRenderer = _volumeChild.GetComponent<MeshRenderer>();
                _volumeChild.SetActive(true);
                return;
            }

            _volumeChild = new GameObject("__SpotLightVolumeMesh__");
            _volumeChild.transform.SetParent(transform, false);
            _volumeChild.transform.localPosition = Vector3.zero;
            _volumeChild.transform.localRotation = Quaternion.identity;
            _volumeChild.transform.localScale = Vector3.one;
            _volumeChild.hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable;

            _meshFilter = _volumeChild.AddComponent<MeshFilter>();
            _meshRenderer = _volumeChild.AddComponent<MeshRenderer>();
            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// 更新几何体：使用归一化Mesh + localScale缩放
        private void UpdateGeometry()
        {
            if (_light == null || _light.type != LightType.Spot) return;

            _cachedSpotAngle = _light.spotAngle;
            _cachedMaxDistance = maxDistance;
            _cachedSides = coneSides;
            _cachedSegments = coneSegments;

            // 获取或创建共享归一化Mesh
            Mesh mesh = GetSharedNormalizedMesh(coneSides, coneSegments);
            if (_meshFilter != null)
                _meshFilter.sharedMesh = mesh;

            // 通过localScale控制锥体实际尺寸
            // 归一化Mesh: XY在[-1,1], Z在[0,1]
            // localScale使其变为实际世界尺寸
            float radiusEnd = maxDistance * Mathf.Tan(_light.spotAngle * 0.5f * Mathf.Deg2Rad);
            float maxRadius = Mathf.Max(radiusEnd, Mathf.Max(lightSourceRadius, 0.001f));
            if (_volumeChild != null)
                _volumeChild.transform.localScale = new Vector3(maxRadius, maxRadius, maxDistance);
        }

        private void UpdateMaterial()
        {
            if (_meshRenderer == null || _light == null) return;

            if (_material == null)
            {
                var shader = Shader.Find("Hidden/VicTools/SpotLightVolume");
                if (shader == null)
                {
                    Debug.LogError("SpotLightVolume: 找不到Shader 'Hidden/VicTools/SpotLightVolume'");
                    return;
                }
                _material = new Material(shader);
                _material.name = "SpotLightVolume_Mat";
                _meshRenderer.sharedMaterial = _material;
            }

            // 混合模式
            _material.DisableKeyword("_BLEND_ADDITIVE");
            _material.DisableKeyword("_BLEND_SOFTADD");
            _material.DisableKeyword("_BLEND_ALPHA");
            switch (blendMode)
            {
                case VolumeBlendMode.Additive:
                    _material.EnableKeyword("_BLEND_ADDITIVE");
                    _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    break;
                case VolumeBlendMode.SoftAdditive:
                    _material.EnableKeyword("_BLEND_SOFTADD");
                    _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusDstColor);
                    _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    break;
                case VolumeBlendMode.Alpha:
                    _material.EnableKeyword("_BLEND_ALPHA");
                    _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    break;
            }

            // 颜色
            Color c = colorFromLight ? _light.color : volumeColor;
            _material.SetColor(_ColorID, c);
            _material.SetFloat(_IntensityID, intensity);
            _material.SetFloat(_FallOffStartID, fallOffStart);
            _material.SetFloat(_FallOffEndID, maxDistance);
            _material.SetFloat(_EdgeFadeID, edgeFade);
            _material.SetFloat(_EndFadeID, endFade);
            _material.SetFloat(_GlareFrontalID, glareFrontal);
            _material.SetFloat(_GlareBehindID, glareBehind);

            // 锥体参数
            float radiusEnd = maxDistance * Mathf.Tan(_light.spotAngle * 0.5f * Mathf.Deg2Rad);
            float radiusStart = Mathf.Max(lightSourceRadius, 0.001f); // 最小保留微小值避免法线退化
            _material.SetFloat(_ConeRadiusStartID, radiusStart);
            _material.SetFloat(_ConeRadiusEndID, radiusEnd);

            // 锥面斜率 cos/sin（基于实际radiusStart和radiusEnd）
            float slopeAngle = Mathf.Atan2(radiusEnd - radiusStart, maxDistance);
            float cosSlope = Mathf.Cos(slopeAngle);
            float sinSlope = Mathf.Sin(slopeAngle);
            _material.SetVector(_ConeSlopeCosSinID, new Vector4(cosSlope, sinSlope, 0, 0));

            _material.renderQueue = 3100;
        }

        /// 获取或创建共享的归一化锥形Mesh
        /// 归一化Mesh：顶点XY在[-1,1]，Z在[0,1]，含前后两个Cap
        private static Mesh GetSharedNormalizedMesh(int sides, int segments)
        {
            if (_sharedMesh != null && _sharedMeshSides == sides && _sharedMeshSegments == segments)
                return _sharedMesh;

            _sharedMesh = GenerateNormalizedConeMesh(sides, segments);
            _sharedMeshSides = sides;
            _sharedMeshSegments = segments;
            return _sharedMesh;
        }

        /// 生成归一化锥形Mesh：XY在[-1,1], Z在[0,1]
        /// 包含锥面 + 前Cap(Z=0) + 后Cap(Z=1)，确保Cull Front时从任何角度都有面可渲染
        /// UV.x标记：0=锥面, 1=cap
        private static Mesh GenerateNormalizedConeMesh(int sides, int segments)
        {
            var mesh = new Mesh();
            mesh.name = "SpotLightVolume_SharedCone";

            // 锥面顶点 + 前Cap(Z=0) + 后Cap(Z=1)
            int vertCountSides = sides * (segments + 2);
            int vertCountFrontCap = sides + 1; // 中心 + 一圈
            int vertCountBackCap = sides + 1;  // 中心 + 一圈
            int vertCountTotal = vertCountSides + vertCountFrontCap + vertCountBackCap;

            var vertices = new Vector3[vertCountTotal];
            var uvs = new Vector2[vertCountTotal]; // uv.x: 0=sides, 1=cap

            // === 锥面顶点 ===
            for (int i = 0; i < sides; i++)
            {
                float angle = 2f * Mathf.PI * i / sides;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                for (int seg = 0; seg <= segments + 1; seg++)
                {
                    float t = (float)seg / (segments + 1);
                    int idx = i + seg * sides;
                    vertices[idx] = new Vector3(cos, sin, t);
                    uvs[idx] = new Vector2(0, 0);
                }
            }

            // === 前Cap顶点（Z=0处）===
            int frontCapStart = vertCountSides;
            vertices[frontCapStart] = Vector3.zero;
            uvs[frontCapStart] = new Vector2(1, 0);
            for (int i = 0; i < sides; i++)
            {
                float angle = 2f * Mathf.PI * i / sides;
                vertices[frontCapStart + 1 + i] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                uvs[frontCapStart + 1 + i] = new Vector2(1, 0);
            }

            // === 后Cap顶点（Z=1处）===
            int backCapStart = frontCapStart + vertCountFrontCap;
            vertices[backCapStart] = new Vector3(0, 0, 1f);
            uvs[backCapStart] = new Vector2(1, 0);
            for (int i = 0; i < sides; i++)
            {
                float angle = 2f * Mathf.PI * i / sides;
                vertices[backCapStart + 1 + i] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 1f);
                uvs[backCapStart + 1 + i] = new Vector2(1, 0);
            }

            // === 三角形 ===
            int triCountSides = sides * (segments + 1) * 6;
            int triCountFrontCap = sides * 3;
            int triCountBackCap = sides * 3;
            var triangles = new int[triCountSides + triCountFrontCap + triCountBackCap];
            int tri = 0;

            // 锥面三角形（正面朝外 — Cull Front时背面对相机可见）
            for (int seg = 0; seg < segments + 1; seg++)
            {
                for (int i = 0; i < sides; i++)
                {
                    int current = seg * sides + i;
                    int next = seg * sides + (i + 1) % sides;
                    int currentUp = (seg + 1) * sides + i;
                    int nextUp = (seg + 1) * sides + (i + 1) % sides;

                    triangles[tri++] = current;
                    triangles[tri++] = next;
                    triangles[tri++] = currentUp;

                    triangles[tri++] = next;
                    triangles[tri++] = nextUp;
                    triangles[tri++] = currentUp;
                }
            }

            // 前Cap三角形（正面朝+Z方向，背面朝-Z = 从正面看时Cull Front可见）
            for (int i = 0; i < sides; i++)
            {
                int nextI = (i + 1) % sides;
                triangles[tri++] = frontCapStart;
                triangles[tri++] = frontCapStart + 1 + i;
                triangles[tri++] = frontCapStart + 1 + nextI;
            }

            // 后Cap三角形（正面朝-Z方向，背面朝+Z = 从背后看时Cull Front可见）
            for (int i = 0; i < sides; i++)
            {
                int nextI = (i + 1) % sides;
                triangles[tri++] = backCapStart;
                triangles[tri++] = backCapStart + 1 + nextI;
                triangles[tri++] = backCapStart + 1 + i;
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        void OnValidate()
        {
            maxDistance = Mathf.Max(0.1f, maxDistance);
            fallOffStart = Mathf.Clamp(fallOffStart, 0f, maxDistance - 0.01f);
            lightSourceRadius = Mathf.Max(0f, lightSourceRadius);

            // 延迟到下一帧执行，避免OnValidate中SendMessage错误
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                if (_light != null && enabled)
                {
                    UpdateGeometry();
                    UpdateMaterial();
                }
            };
#endif
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (_light == null || _light.type != LightType.Spot) return;

            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.3f);
            Gizmos.matrix = transform.localToWorldMatrix;

            float radiusEnd = maxDistance * Mathf.Tan(_light.spotAngle * 0.5f * Mathf.Deg2Rad);
            float radiusStart = fallOffStart * Mathf.Tan(_light.spotAngle * 0.5f * Mathf.Deg2Rad);

            if (fallOffStart > 0)
            {
                Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
                DrawWireCircle(Vector3.forward * fallOffStart, radiusStart, 16);
            }

            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.4f);
            DrawWireCircle(Vector3.forward * maxDistance, radiusEnd, 16);
        }

        private void DrawWireCircle(Vector3 center, float radius, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a1 = 2f * Mathf.PI * i / segments;
                float a2 = 2f * Mathf.PI * (i + 1) / segments;
                Vector3 p1 = center + new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, 0);
                Vector3 p2 = center + new Vector3(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius, 0);
                Gizmos.DrawLine(p1, p2);
            }
        }
#endif
    }
}
