/// SpotLightVolume v1.0 - 轻量探照灯体积雾效果
/// 基于锥形Mesh + 自定义Shader的简洁实现，性能优化版本
/// 参数：起始距离、最长距离、边缘羽化、末端羽化、混合方式等

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
        [Header("距离控制")]
        [Tooltip("雾效起始距离（从光源出发）")]
        [Min(0f)]
        public float startDistance = 0f;

        [Tooltip("雾效最长距离（从光源出发）")]
        [Min(0.1f)]
        public float maxDistance = 10f;

        [Header("羽化控制")]
        [Tooltip("边缘羽化强度（0=硬边，1=完全柔化）")]
        [Range(0f, 1f)]
        public float edgeFade = 0.5f;

        [Tooltip("末端羽化强度（0=硬截断，1=完全淡出）")]
        [Range(0f, 1f)]
        public float endFade = 1.0f;

        [Tooltip("深度混合距离（与场景模型交叠时的过渡距离，0=关闭）")]
        [Range(0f, 5f)]
        public float depthFadeDistance = 1.5f;

        [Header("外观")]
        [Tooltip("雾效整体强度")]
        [Range(0f, 2f)]
        public float intensity = 1f;

        [Tooltip("雾效颜色（默认跟随灯光颜色）")]
        public bool colorFromLight = true;

        [ColorUsage(false, true)]
        public Color volumeColor = Color.white;

        [Tooltip("混合方式")]
        public VolumeBlendMode blendMode = VolumeBlendMode.Additive;

        [Header("Mesh质量")]
        [Tooltip("圆锥面数（越高越圆滑，越低越省）")]
        [Range(6, 32)]
        public int coneSides = 8;

        [Tooltip("圆锥分段数")]
        [Range(1, 8)]
        public int coneSegments = 1;

        // 内部引用
        private Light _light;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _coneMesh;
        private Material _material;
        private GameObject _volumeChild;

        // Shader属性ID缓存
        private static readonly int _ColorID = Shader.PropertyToID("_VolumeColor");
        private static readonly int _IntensityID = Shader.PropertyToID("_Intensity");
        private static readonly int _StartDistID = Shader.PropertyToID("_StartDistance");
        private static readonly int _MaxDistID = Shader.PropertyToID("_MaxDistance");
        private static readonly int _EdgeFadeID = Shader.PropertyToID("_EdgeFade");
        private static readonly int _EndFadeID = Shader.PropertyToID("_EndFade");
        private static readonly int _DepthFadeDistID = Shader.PropertyToID("_DepthFadeDistance");
        private static readonly int _ConeRadiusStartID = Shader.PropertyToID("_ConeRadiusStart");
        private static readonly int _ConeRadiusEndID = Shader.PropertyToID("_ConeRadiusEnd");

        // 缓存值用于变更检测
        private float _cachedSpotAngle;
        private float _cachedMaxDistance;
        private int _cachedSides;
        private int _cachedSegments;

        void OnEnable()
        {
            _light = GetComponent<Light>();
            EnsureVolumeChild();
            RebuildMesh();
            UpdateMaterial();
            
            // 确保相机渲染深度贴图（深度混合功能需要）
            if (depthFadeDistance > 0 && Camera.main != null)
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

            // 检测是否需要重建Mesh
            bool needRebuild = !Mathf.Approximately(_cachedSpotAngle, _light.spotAngle)
                            || !Mathf.Approximately(_cachedMaxDistance, maxDistance)
                            || _cachedSides != coneSides
                            || _cachedSegments != coneSegments;

            if (needRebuild)
                RebuildMesh();

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

            if (_coneMesh != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(_coneMesh);
                else
#endif
                    Destroy(_coneMesh);
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
                return;
            }

            // 查找已有的子对象
            Transform existing = transform.Find("__SpotLightVolumeMesh__");
            if (existing != null)
            {
                _volumeChild = existing.gameObject;
                _meshFilter = _volumeChild.GetComponent<MeshFilter>();
                _meshRenderer = _volumeChild.GetComponent<MeshRenderer>();
                _volumeChild.SetActive(true);
                return;
            }

            // 创建新子对象
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

        private void RebuildMesh()
        {
            if (_light == null || _light.type != LightType.Spot) return;

            _cachedSpotAngle = _light.spotAngle;
            _cachedMaxDistance = maxDistance;
            _cachedSides = coneSides;
            _cachedSegments = coneSegments;

            float radiusEnd = maxDistance * Mathf.Tan(_light.spotAngle * 0.5f * Mathf.Deg2Rad);
            float radiusStart = 0.001f; // 微小起始半径避免法线退化

            if (_coneMesh == null)
                _coneMesh = new Mesh();
            else
                _coneMesh.Clear();

            _coneMesh.name = "SpotLightVolumeCone";
            GenerateConeMesh(_coneMesh, maxDistance, radiusStart, radiusEnd, coneSides, coneSegments);

            if (_meshFilter != null)
                _meshFilter.sharedMesh = _coneMesh;
        }

        private void UpdateMaterial()
        {
            if (_meshRenderer == null) return;

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

            // 设置混合模式关键字
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
            Color c = colorFromLight && _light != null ? _light.color : volumeColor;
            _material.SetColor(_ColorID, c);
            _material.SetFloat(_IntensityID, intensity);
            _material.SetFloat(_StartDistID, startDistance);
            _material.SetFloat(_MaxDistID, maxDistance);
            _material.SetFloat(_EdgeFadeID, edgeFade);
            _material.SetFloat(_EndFadeID, endFade);
            _material.SetFloat(_DepthFadeDistID, depthFadeDistance);

            float radiusEnd = maxDistance * Mathf.Tan(_light.spotAngle * 0.5f * Mathf.Deg2Rad);
            _material.SetFloat(_ConeRadiusStartID, 0.001f);
            _material.SetFloat(_ConeRadiusEndID, radiusEnd);

            // 渲染队列
            _material.renderQueue = 3100;
        }

        /// 生成锥形Mesh
        private static void GenerateConeMesh(Mesh mesh, float length, float radiusStart, float radiusEnd, int sides, int segments)
        {
            int vertCount = sides * (segments + 2);
            var vertices = new Vector3[vertCount];
            var uvs = new Vector2[vertCount]; // uv.x = 归一化深度, uv.y = 归一化径向位置

            float angleOffset = sides == 4 ? Mathf.PI * 0.25f : 0f;

            for (int i = 0; i < sides; i++)
            {
                float angle = angleOffset + 2f * Mathf.PI * i / sides;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                for (int seg = 0; seg <= segments + 1; seg++)
                {
                    float t = (float)seg / (segments + 1);
                    // 非线性分布：靠近光源更密集
                    float tz = t * t;
                    float radius = Mathf.Lerp(radiusStart, radiusEnd, tz);
                    int idx = i + seg * sides;
                    vertices[idx] = new Vector3(radius * cos, radius * sin, tz * length);
                    uvs[idx] = new Vector2(tz, 1f); // x=深度比, y=边缘标记
                }
            }

            // 单面三角形（配合Cull Front，只渲染内表面）
            int triCount = sides * (segments + 1) * 6;
            var triangles = new int[triCount];
            int tri = 0;

            for (int seg = 0; seg < segments + 1; seg++)
            {
                for (int i = 0; i < sides; i++)
                {
                    int current = seg * sides + i;
                    int next = seg * sides + (i + 1) % sides;
                    int currentNext = (seg + 1) * sides + i;
                    int nextNext = (seg + 1) * sides + (i + 1) % sides;

                    triangles[tri++] = current;
                    triangles[tri++] = currentNext;
                    triangles[tri++] = next;

                    triangles[tri++] = next;
                    triangles[tri++] = currentNext;
                    triangles[tri++] = nextNext;
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        void OnValidate()
        {
            maxDistance = Mathf.Max(0.1f, maxDistance);
            startDistance = Mathf.Clamp(startDistance, 0f, maxDistance - 0.01f);

            if (_light != null && enabled)
            {
                RebuildMesh();
                UpdateMaterial();
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (_light == null || _light.type != LightType.Spot) return;

            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.3f);
            Gizmos.matrix = transform.localToWorldMatrix;

            // 绘制雾效范围辅助线
            float radiusEnd = maxDistance * Mathf.Tan(_light.spotAngle * 0.5f * Mathf.Deg2Rad);
            float radiusStart = startDistance * Mathf.Tan(_light.spotAngle * 0.5f * Mathf.Deg2Rad);

            // 起始面
            if (startDistance > 0)
            {
                Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
                DrawWireCircle(Vector3.forward * startDistance, radiusStart, 16);
            }

            // 末端面
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
