/// SpotLightVolume v1.0 - 轻量探照灯体积雾效果
/// 基于锥形Mesh + 自定义Shader的简洁实现，性能优化版本
/// 参数：起始距离、最长距离、边缘羽化、末端羽化、混合方式等
/// SpotLightVolume v2.0 - 参考VLB架构重写
/// 归一化Mesh + localScale缩放 + 双Pass渲染 + Fresnel + 距离衰减 + DepthBlend
/// SpotLightVolume v5.0 - 射线遮挡截断
/// - Physics.Raycast沿光柱forward方向检测第一个碰撞物体（忽略Trigger）
/// - 碰撞距离通过_ClipDistance传给Shader，在raymarching中平滑羽化截断
/// - 支持occlusionLayerMask选择检测层，occlusionUpdateInterval控制检测频率
/// - 不影响原始光柱衰减、双面显示、深度遮挡等效果
/// SpotLightVolume v6.0 重构代码，改进重复的GetComponent调用，消除 UpdateGeometry 和 UpdateMaterial 中的重复计算
/// SpotLightVolume v6.1 - 射线遮挡支持角色碰撞：新增occlusionDetectTriggers选项，可检测Trigger类型碰撞体
/// SpotLightVolume v6.2 - 蒙版投影：新增maskTexture蒙版纹理模拟窗格光柱投影，沿光轴等比投射到锥体横截面，支持enableMask开关和maskIntensity强度控制

using UnityEngine;
using System.Collections.Generic;

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
        #region 公开参数

        [Header("距离控制")]
        [Tooltip("光源半径(光柱始端宽度): 0=尖锥, 越大始端越宽")]
        [Min(0f)]
        public float lightSourceRadius = 0f;

        [Tooltip("衰减起始距离")]
        [Min(0f)]
        public float fallOffStart = 0f;

        [Tooltip("衰减结束距离(最远距离)")]
        [Min(0.1f)]
        public float maxDistance = 3f;

        [Header("羽化控制")]
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

        [Header("外观")]
        [Tooltip("雾效整体强度")]
        [Range(0f, 5f)]
        public float intensity = 1f;

        [Tooltip("起始亮度增强幅度: 光柱起始处额外提亮")]
        [Range(0f, 8f)]
        public float startBoostIntensity = 1.5f;

        [Tooltip("起始亮度范围: 增亮区域的绝对距离，值越小亮区越短")]
        [Range(0.01f, 15f)]
        public float startBoostRange = 1f;

        [Tooltip("中心渐变距离: 控制中心高亮向外扩散的范围，值越小高亮越集中")]
        [Range(0.01f, 1f)]
        public float centerFade = 0.5f;

        [Tooltip("跟随灯光颜色")]
        public bool colorFromLight = true;

        [ColorUsage(false, true)]
        public Color volumeColor = Color.white;

        [Tooltip("混合方式")]
        public VolumeBlendMode blendMode = VolumeBlendMode.Additive;

        [Header("Mesh质量")]
        [Tooltip("圆锥面数")]
        [Range(3, 32)]
        public int coneSides = 12;

        [Tooltip("圆锥分段数")]
        [Range(1, 10)]
        public int coneSegments = 1;

        [Header("射线遮挡")]
        [Tooltip("是否启用射线遮挡检测")]
        public bool enableOcclusion = false;

        [Tooltip("射线遮挡检测的Layer Mask")]
        public LayerMask occlusionLayerMask = ~0;

        [Tooltip("射线遮挡更新间隔(秒), 0=每帧更新")]
        [Range(0f, 0.5f)]
        public float occlusionUpdateInterval = 0.05f;

        [Tooltip("是否检测Trigger碰撞体（角色可能使用Trigger类型的Collider）")]
        public bool occlusionDetectTriggers = false;

        [Header("蒙版投影")]
        [Tooltip("启用蒙版纹理投影")]
        public bool enableMask = false;

        [Tooltip("蒙版纹理（模拟窗格光柱投影，黑色区域无光）")]
        public Texture2D maskTexture;

        [Tooltip("蒙版强度: 0=无蒙版效果, 1=完全按蒙版遮挡")]
        [Range(0f, 1f)]
        public float maskIntensity = 1f;

        #endregion

        #region 内部状态

        private Light _light;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Material _material;
        private GameObject _volumeChild;

        // 射线遮挡
        private float _clipDistance = -1f;
        private float _lastOcclusionCheckTime;

        // 缓存值（用于判断是否需要重建Mesh）
        private float _cachedSpotAngle;
        private float _cachedMaxDistance;
        private int _cachedSides;
        private int _cachedSegments;

        // 共享归一化Mesh缓存：按 (sides, segments) 配置缓存，每种配置一份、永久复用。
        // v6.3：之前用单个静态 _sharedMesh，配置变化时直接覆盖且不销毁旧 Mesh → 泄漏；
        // 多个不同配置的实例还会反复"抢占"重建。改为字典后每配置一份，不泄漏不抢占。
        private static readonly Dictionary<long, Mesh> _sharedMeshes = new Dictionary<long, Mesh>();

        #endregion

        #region Shader属性ID（静态缓存，避免每帧字符串查找）

        private static class ShaderIDs
        {
            public static readonly int VolumeColor = Shader.PropertyToID("_VolumeColor");
            public static readonly int Intensity = Shader.PropertyToID("_Intensity");
            public static readonly int FallOffStart = Shader.PropertyToID("_FallOffStart");
            public static readonly int FallOffEnd = Shader.PropertyToID("_FallOffEnd");
            public static readonly int EdgeFade = Shader.PropertyToID("_EdgeFade");
            public static readonly int EndFade = Shader.PropertyToID("_EndFade");
            public static readonly int GlareFrontal = Shader.PropertyToID("_GlareFrontal");
            public static readonly int GlareBehind = Shader.PropertyToID("_GlareBehind");
            public static readonly int ConeRadiusStart = Shader.PropertyToID("_ConeRadiusStart");
            public static readonly int ConeRadiusEnd = Shader.PropertyToID("_ConeRadiusEnd");
            public static readonly int ConeSlopeCosSin = Shader.PropertyToID("_ConeSlopeCosSin");
            public static readonly int ClipDistance = Shader.PropertyToID("_ClipDistance");
            public static readonly int StartBoostIntensity = Shader.PropertyToID("_StartBoostIntensity");
            public static readonly int StartBoostRange = Shader.PropertyToID("_StartBoostRange");
            public static readonly int CenterFade = Shader.PropertyToID("_CenterFade");
            public static readonly int MaskTex = Shader.PropertyToID("_MaskTex");
            public static readonly int MaskIntensity = Shader.PropertyToID("_MaskIntensity");
        }

        #endregion

        #region 生命周期

        void OnEnable()
        {
            _light = GetComponent<Light>();
            EnsureVolumeChild();
            UpdateGeometry();
            UpdateMaterial();

            if (Camera.main != null)
                Camera.main.depthTextureMode |= DepthTextureMode.Depth;
        }

        void OnDisable()
        {
            if (_volumeChild != null)
                _volumeChild.SetActive(false);
        }

        void Update()
        {
            if (_light == null || _light.type != LightType.Spot) return;

            if (NeedsGeometryRebuild())
                UpdateGeometry();

            UpdateOcclusion();
            UpdateMaterial();
        }

        void OnDestroy()
        {
            SafeDestroy(_volumeChild);
            SafeDestroy(_material);
        }

        void OnValidate()
        {
            maxDistance = Mathf.Max(0.1f, maxDistance);
            fallOffStart = Mathf.Clamp(fallOffStart, 0f, maxDistance - 0.01f);
            lightSourceRadius = Mathf.Max(0f, lightSourceRadius);

#if UNITY_EDITOR
            EditorApplication.delayCall += () =>
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

        #endregion

        #region 核心逻辑

        private bool NeedsGeometryRebuild()
        {
            return !Mathf.Approximately(_cachedSpotAngle, _light.spotAngle)
                || !Mathf.Approximately(_cachedMaxDistance, maxDistance)
                || _cachedSides != coneSides
                || _cachedSegments != coneSegments;
        }

        /// 射线检测：沿光柱方向发射单条射线，找到第一个碰撞物体并截断光柱
        private void UpdateOcclusion()
        {
            if (!enableOcclusion)
            {
                _clipDistance = -1f;
                return;
            }

            if (occlusionUpdateInterval > 0f && Time.time - _lastOcclusionCheckTime < occlusionUpdateInterval)
                return;
            _lastOcclusionCheckTime = Time.time;

            var triggerInteraction = occlusionDetectTriggers
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;

            if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit,
                maxDistance, occlusionLayerMask, triggerInteraction))
            {
                _clipDistance = hit.distance;
            }
            else
            {
                _clipDistance = -1f;
            }
        }

        /// 更新几何体：使用归一化Mesh + localScale缩放
        private void UpdateGeometry()
        {
            if (_light == null || _light.type != LightType.Spot) return;

            _cachedSpotAngle = _light.spotAngle;
            _cachedMaxDistance = maxDistance;
            _cachedSides = coneSides;
            _cachedSegments = coneSegments;

            if (_meshFilter != null)
                _meshFilter.sharedMesh = GetSharedNormalizedMesh(coneSides, coneSegments);

            // localScale控制锥体实际尺寸（归一化Mesh: XY[-1,1], Z[0,1]）
            float maxRadius = Mathf.Max(ComputeRadiusEnd(), Mathf.Max(lightSourceRadius, 0.001f));
            if (_volumeChild != null)
                _volumeChild.transform.localScale = new Vector3(maxRadius, maxRadius, maxDistance);
        }

        /// 更新材质属性
        private void UpdateMaterial()
        {
            if (_meshRenderer == null || _light == null) return;

            EnsureMaterial();
            if (_material == null) return;

            ApplyBlendMode();
            ApplyShaderProperties();

            _material.renderQueue = 3100;
        }

        #endregion

        #region 材质辅助

        private void EnsureMaterial()
        {
            if (_material != null) return;

            var shader = Shader.Find("Hidden/VicTools/SpotLightVolume");
            if (shader == null)
            {
                Debug.LogError("SpotLightVolume: 找不到Shader 'Hidden/VicTools/SpotLightVolume'");
                return;
            }

            _material = new Material(shader) { name = "SpotLightVolume_Mat" };
            _meshRenderer.sharedMaterial = _material;
        }

        private void ApplyBlendMode()
        {
            _material.DisableKeyword("_BLEND_ADDITIVE");
            _material.DisableKeyword("_BLEND_SOFTADD");
            _material.DisableKeyword("_BLEND_ALPHA");

            switch (blendMode)
            {
                case VolumeBlendMode.Additive:
                    _material.EnableKeyword("_BLEND_ADDITIVE");
                    SetBlend(UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.One);
                    break;
                case VolumeBlendMode.SoftAdditive:
                    _material.EnableKeyword("_BLEND_SOFTADD");
                    SetBlend(UnityEngine.Rendering.BlendMode.OneMinusDstColor, UnityEngine.Rendering.BlendMode.One);
                    break;
                case VolumeBlendMode.Alpha:
                    _material.EnableKeyword("_BLEND_ALPHA");
                    SetBlend(UnityEngine.Rendering.BlendMode.SrcAlpha, UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    break;
            }
        }

        private void SetBlend(UnityEngine.Rendering.BlendMode src, UnityEngine.Rendering.BlendMode dst)
        {
            _material.SetInt("_SrcBlend", (int)src);
            _material.SetInt("_DstBlend", (int)dst);
        }

        private void ApplyShaderProperties()
        {
            Color c = colorFromLight ? _light.color : volumeColor;
            float radiusEnd = ComputeRadiusEnd();
            float radiusStart = Mathf.Max(lightSourceRadius, 0.001f);
            float slopeAngle = Mathf.Atan2(radiusEnd - radiusStart, maxDistance);

            _material.SetColor(ShaderIDs.VolumeColor, c);
            _material.SetFloat(ShaderIDs.Intensity, intensity);
            _material.SetFloat(ShaderIDs.FallOffStart, fallOffStart);
            _material.SetFloat(ShaderIDs.FallOffEnd, maxDistance);
            _material.SetFloat(ShaderIDs.EdgeFade, edgeFade);
            _material.SetFloat(ShaderIDs.EndFade, endFade);
            _material.SetFloat(ShaderIDs.GlareFrontal, glareFrontal);
            _material.SetFloat(ShaderIDs.GlareBehind, glareBehind);
            _material.SetFloat(ShaderIDs.ConeRadiusStart, radiusStart);
            _material.SetFloat(ShaderIDs.ConeRadiusEnd, radiusEnd);
            _material.SetVector(ShaderIDs.ConeSlopeCosSin, new Vector4(Mathf.Cos(slopeAngle), Mathf.Sin(slopeAngle), 0, 0));
            _material.SetFloat(ShaderIDs.ClipDistance, _clipDistance);
            _material.SetFloat(ShaderIDs.StartBoostIntensity, startBoostIntensity);
            _material.SetFloat(ShaderIDs.StartBoostRange, startBoostRange);
            _material.SetFloat(ShaderIDs.CenterFade, centerFade);

            // 蒙版投影
            if (enableMask && maskTexture != null)
            {
                _material.SetTexture(ShaderIDs.MaskTex, maskTexture);
                _material.SetFloat(ShaderIDs.MaskIntensity, maskIntensity);
            }
            else
            {
                _material.SetTexture(ShaderIDs.MaskTex, Texture2D.whiteTexture);
                _material.SetFloat(ShaderIDs.MaskIntensity, 0f);
            }
        }

        #endregion

        #region 几何体辅助

        /// 计算光锥末端半径
        private float ComputeRadiusEnd()
        {
            return maxDistance * Mathf.Tan(_light.spotAngle * 0.5f * Mathf.Deg2Rad);
        }

        #endregion

        #region 子物体管理

        private void EnsureVolumeChild()
        {
            if (_volumeChild != null)
            {
                _volumeChild.SetActive(true);
                CacheChildComponents();
                return;
            }

            Transform existing = transform.Find("__SpotLightVolumeMesh__");
            if (existing != null)
            {
                _volumeChild = existing.gameObject;
                _volumeChild.SetActive(true);
                CacheChildComponents();
                return;
            }

            CreateVolumeChild();
        }

        private void CacheChildComponents()
        {
            _meshFilter = _volumeChild.GetComponent<MeshFilter>();
            _meshRenderer = _volumeChild.GetComponent<MeshRenderer>();
        }

        private void CreateVolumeChild()
        {
            _volumeChild = new GameObject("__SpotLightVolumeMesh__");
            _volumeChild.transform.SetParent(transform, false);
            _volumeChild.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            _volumeChild.transform.localScale = Vector3.one;
            _volumeChild.hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable;

            _meshFilter = _volumeChild.AddComponent<MeshFilter>();
            _meshRenderer = _volumeChild.AddComponent<MeshRenderer>();
            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        #endregion

        #region Mesh生成（静态共享）

        private static Mesh GetSharedNormalizedMesh(int sides, int segments)
        {
            long key = ((long)sides << 32) | (uint)segments;
            if (_sharedMeshes.TryGetValue(key, out var mesh) && mesh != null)
                return mesh;

            mesh = GenerateNormalizedConeMesh(sides, segments);
            _sharedMeshes[key] = mesh;
            return mesh;
        }

        /// 生成归一化锥形Mesh：XY在[-1,1], Z在[0,1]
        /// 包含锥面 + 前Cap(Z=0) + 后Cap(Z=1)
        /// UV.x标记：0=锥面, 1=cap
        private static Mesh GenerateNormalizedConeMesh(int sides, int segments)
        {
            int ringCount = segments + 2;
            int vertCountSides = sides * ringCount;
            int vertCountCap = sides + 1;
            int vertCountTotal = vertCountSides + vertCountCap * 2;

            var vertices = new Vector3[vertCountTotal];
            var uvs = new Vector2[vertCountTotal];

            // 锥面顶点
            for (int i = 0; i < sides; i++)
            {
                float angle = 2f * Mathf.PI * i / sides;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                for (int seg = 0; seg < ringCount; seg++)
                {
                    float t = (float)seg / (segments + 1);
                    int idx = i + seg * sides;
                    vertices[idx] = new Vector3(cos, sin, t);
                    uvs[idx] = Vector2.zero;
                }
            }

            // 前Cap（Z=0）
            int frontCapStart = vertCountSides;
            BuildCapVertices(vertices, uvs, frontCapStart, sides, 0f);

            // 后Cap（Z=1）
            int backCapStart = frontCapStart + vertCountCap;
            BuildCapVertices(vertices, uvs, backCapStart, sides, 1f);

            // 三角形
            int triCountSides = sides * (segments + 1) * 6;
            int triCountCaps = sides * 3 * 2;
            var triangles = new int[triCountSides + triCountCaps];
            int tri = 0;

            // 锥面
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

            // 前Cap（正面朝+Z）
            for (int i = 0; i < sides; i++)
            {
                triangles[tri++] = frontCapStart;
                triangles[tri++] = frontCapStart + 1 + i;
                triangles[tri++] = frontCapStart + 1 + (i + 1) % sides;
            }

            // 后Cap（正面朝-Z）
            for (int i = 0; i < sides; i++)
            {
                triangles[tri++] = backCapStart;
                triangles[tri++] = backCapStart + 1 + (i + 1) % sides;
                triangles[tri++] = backCapStart + 1 + i;
            }

            var mesh = new Mesh
            {
                name = "SpotLightVolume_SharedCone",
                vertices = vertices,
                uv = uvs,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void BuildCapVertices(Vector3[] vertices, Vector2[] uvs, int startIdx, int sides, float z)
        {
            vertices[startIdx] = new Vector3(0, 0, z);
            uvs[startIdx] = new Vector2(1, 0);
            for (int i = 0; i < sides; i++)
            {
                float angle = 2f * Mathf.PI * i / sides;
                vertices[startIdx + 1 + i] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), z);
                uvs[startIdx + 1 + i] = new Vector2(1, 0);
            }
        }

        #endregion

        #region 工具方法

        private static void SafeDestroy(Object obj)
        {
            if (obj == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(obj);
            else
#endif
                Destroy(obj);
        }

        #endregion

        #region Gizmos

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (_light == null || _light.type != LightType.Spot) return;

            Gizmos.matrix = transform.localToWorldMatrix;

            if (fallOffStart > 0)
            {
                Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
                float radiusStart = fallOffStart * Mathf.Tan(_light.spotAngle * 0.5f * Mathf.Deg2Rad);
                DrawWireCircle(Vector3.forward * fallOffStart, radiusStart, 16);
            }

            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.4f);
            DrawWireCircle(Vector3.forward * maxDistance, ComputeRadiusEnd(), 16);
        }

        private static void DrawWireCircle(Vector3 center, float radius, int segments)
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

        #endregion
    }
}
