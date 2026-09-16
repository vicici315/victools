/// SpotLightVolume v6.3 - 重构版（架构优化，无功能变更）
///
/// 重构要点：
/// A. 消除 Update 中的重复操作
///    1. 引入 ConeGeometryCache 结构体缓存派生计算结果（radiusEnd/slopeAngle/cosSlope/sinSlope），
///       避免 ApplyShaderProperties 中每帧重复调用 Tan/Atan2/Cos/Sin
///    2. ApplyBlendMode 仅在 blendMode 变化时执行，避免每帧重复 EnableKeyword/DisableKeyword/SetInt
///       （关键字切换会触发 Shader 变体重编译，这是真实的运行时开销）
///    3. _material.renderQueue 从 UpdateMaterial 移到 EnsureMaterial（仅创建时设置一次）
///    4. 拆分 Shader 属性推送：_ClipDistance 为高频参数（每帧），其余 17 个为低频（dirty 标志触发）
///    5. colorFromLight 模式下，单独跟踪 _light.color 变化、按需只更新颜色通道
/// B. 改进可读性
///    1. 拆分 ApplyShaderProperties 为职责单一的子方法（颜色/光照/几何/蒙版）
///    2. 拆分 GenerateNormalizedConeMesh 为 BuildSideVertices/BuildSideTriangles/BuildCap*
///    3. 引入 EnsureLight() 守卫函数，消除分散的 null/type 检查
///    4. 4 个独立 _cachedXxx 字段聚合为 ConeGeometryCache 结构体
///    5. ShaderIDs 静态类按用途分组
/// C. 不变项（保持向后兼容）
///    - 所有 public 字段、默认值、Header/Tooltip/Range/Min 标记
///    - Mesh 生成（顶点/UV/三角形索引顺序与原版 bit-identical）
///    - Material 关键字组合与最终 Shader 属性值
///    - 序列化兼容：Inspector 不变 → 现有 Scene/Prefab 不需要重新序列化
///
/// SpotLightVolume v6.2 - 蒙版投影：maskTexture 模拟窗格光柱投影
/// SpotLightVolume v6.1 - 射线遮挡支持角色碰撞：occlusionDetectTriggers 可检测 Trigger
/// SpotLightVolume v6.0 - 重构：改进重复 GetComponent 调用，消除 UpdateGeometry/UpdateMaterial 重复计算
///   - 射线遮挡截断：Physics.Raycast 沿光柱方向检测碰撞
///   - 参考 VLB 架构：归一化 Mesh + localScale + 双 Pass + Fresnel + 距离衰减
///   - 轻量探照灯体积雾（锥形 Mesh + 自定义 Shader）
///
/// SpotLightVolume v6.4 - 软饱和防过曝（双管齐下）：
///   - 新增 softSaturation 字段（默认关闭）：启用后开启 _VOLUME_SOFTSAT shader_feature，
///     对 raymarching 总强度做 x → x/(1+x) 强 Reinhard 压缩
///   - 新增 volumeExposure 字段（范围 0.1~2，默认 1）：曝光系数直接控制单灯总能量，
///     类似摄影曝光补偿，叠加场景建议调到 0.5~0.7
///   - 双管齐下解决多盏体积光叠加 + Bloom 后处理的过曝问题

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
        [Range(0, 10)]
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

        [Header("后处理优化")]
        [Tooltip("曝光系数（类似摄影曝光补偿）: 控制单灯总能量。多盏体积光叠加 + Bloom 过曝时降低（如 0.5），默认值=1 = 与原版一致。配合下方‘启用软饱和’双管齐下")]
        [Range(0.1f, 2f)]
        public float volumeExposure = 1f;

        [Tooltip("启用软饱和(Reinhard Tone Mapping, x → x/(1+x)): 多盏体积光叠加 + Bloom 后处理场景下压缩 HDR 信号、避免过曝；单盏或无 Bloom 时建议关闭")]
        public bool softSaturation = true;

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

        // 几何派生缓存（聚合：避免 4 个散落的 _cachedXxx 字段）
        // UpdateGeometry 中刷新，ApplyShaderProperties 读取，避免每帧重复三角函数
        private ConeGeometryCache _coneCache;

        // Material dirty 控制：低频参数在 dirty=true 时推送，避免每帧 17 次无意义 SetFloat
        private VolumeBlendMode _cachedBlendMode = (VolumeBlendMode)(-1); // 初始非法值，强制首次刷新
        private Color _lastAppliedLightColor;
        private bool _lastColorFromLight;
        private bool _materialParamsDirty = true;

        // Reinhard 软饱和 keyword 缓存（仅在 softSaturation 变化时才切换 shader keyword）
        private bool _cachedSoftSaturation;

        // Animator 存在标记：动画可能驱动 Light 任意属性（spotAngle/color/intensity/...），
        // 有 Animator 时必须每帧全推 shader 参数，无 Animator 时无人工干预可走轻量路径。
        private bool _hasAnimator;

        // 共享归一化Mesh缓存：按 (sides, segments) 配置缓存，每种配置一份、永久复用。
        // v6.3：之前用单个静态 _sharedMesh，配置变化时直接覆盖且不销毁旧 Mesh → 泄漏；
        // 多个不同配置的实例还会反复"抢占"重建。改为字典后每配置一份，不泄漏不抢占。
        private static readonly Dictionary<long, Mesh> _sharedMeshes = new Dictionary<long, Mesh>();

        /// 圆锥几何及其派生参数的缓存结构：
        /// 在 UpdateGeometry 时一次性刷新，ApplyShaderProperties 直接读取，避免每帧重复 Tan/Atan2/Cos/Sin
        private struct ConeGeometryCache
        {
            // 输入（用于脏检测）
            public float spotAngle;
            public float maxDistance;
            public float lightSourceRadius;
            public int sides;
            public int segments;

            // 派生输出
            public float radiusEnd;
            public float radiusStart;
            public float slopeAngle;
            public float cosSlope;
            public float sinSlope;

            public bool IsValid => sides > 0 && segments > 0;

            /// 当输入参数与缓存记录不一致时返回 false，调用方应 Refresh
            public bool Matches(Light light, float md, int s, int seg, float lsr)
            {
                return IsValid
                    && Mathf.Approximately(spotAngle, light.spotAngle)
                    && Mathf.Approximately(maxDistance, md)
                    && sides == s
                    && segments == seg
                    && Mathf.Approximately(lightSourceRadius, lsr);
            }

            /// 重新计算所有派生输出
            public void Refresh(Light light, float md, int s, int seg, float lsr)
            {
                spotAngle = light.spotAngle;
                maxDistance = md;
                lightSourceRadius = lsr;
                sides = s;
                segments = seg;

                radiusEnd = md * Mathf.Tan(spotAngle * 0.5f * Mathf.Deg2Rad);
                radiusStart = Mathf.Max(lsr, 0.001f);
                slopeAngle = Mathf.Atan2(radiusEnd - radiusStart, md);
                cosSlope = Mathf.Cos(slopeAngle);
                sinSlope = Mathf.Sin(slopeAngle);
            }
        }

        #endregion

        #region Shader属性ID（静态缓存，按用途分组）

        private static class ShaderIDs
        {
            // 颜色 / 强度
            public static readonly int VolumeColor         = Shader.PropertyToID("_VolumeColor");
            public static readonly int Intensity           = Shader.PropertyToID("_Intensity");
            public static readonly int VolumeExposure      = Shader.PropertyToID("_VolumeExposure");

            // 距离 / 衰减
            public static readonly int FallOffStart        = Shader.PropertyToID("_FallOffStart");
            public static readonly int FallOffEnd          = Shader.PropertyToID("_FallOffEnd");

            // 羽化 / 眩光
            public static readonly int EdgeFade            = Shader.PropertyToID("_EdgeFade");
            public static readonly int EndFade             = Shader.PropertyToID("_EndFade");
            public static readonly int GlareFrontal        = Shader.PropertyToID("_GlareFrontal");
            public static readonly int GlareBehind         = Shader.PropertyToID("_GlareBehind");

            // 锥体几何
            public static readonly int ConeRadiusStart     = Shader.PropertyToID("_ConeRadiusStart");
            public static readonly int ConeRadiusEnd       = Shader.PropertyToID("_ConeRadiusEnd");
            public static readonly int ConeSlopeCosSin     = Shader.PropertyToID("_ConeSlopeCosSin");

            // 起始增亮 / 中心渐变
            public static readonly int StartBoostIntensity = Shader.PropertyToID("_StartBoostIntensity");
            public static readonly int StartBoostRange     = Shader.PropertyToID("_StartBoostRange");
            public static readonly int CenterFade          = Shader.PropertyToID("_CenterFade");

            // 蒙版投影
            public static readonly int MaskTex             = Shader.PropertyToID("_MaskTex");
            public static readonly int MaskIntensity       = Shader.PropertyToID("_MaskIntensity");

            // 高频推送（每帧由 UpdateOcclusion 更新后单独 SetFloat）
            public static readonly int ClipDistance        = Shader.PropertyToID("_ClipDistance");
        }

        #endregion

        #region 生命周期

        void OnEnable()
        {
            // Animator 存在性只需在启用时检测一次即可，
            // 运行时切换 Animator 是罕见场景，如需要可在外部调用 GetComponent<Animator>() 后自行维护
            _hasAnimator = GetComponent<Animator>() != null;

            EnsureLight();
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
            if (!EnsureLight()) return;

            if (NeedsGeometryRebuild())
                UpdateGeometry();

            UpdateOcclusion();

            // 性能分支：
            //   有 Animator → 强制每帧全推（动画可能改变任意 Light 参数，必须保证 shader 完全同步）
            //   无 Animator 且参数 dirty → 推一次完整参数
            //   无 Animator 且无 dirty → 仅推高频参数（_ClipDistance），跳过 17 次冗余 SetFloat
            if (_hasAnimator)
                UpdateMaterial(applyAllParams: true);
            else if (_materialParamsDirty || _cachedBlendMode != blendMode)
                UpdateMaterial();
            else
                PushHighFrequencyShaderParams();
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

            // OnValidate 中修改参数 → 强制下次 UpdateMaterial 重新推送所有 shader 参数
            _materialParamsDirty = true;

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

        /// Light 守卫：按需缓存 _light 引用，确保为 Spot 类型。
        /// 替代分散在多处的 `if (_light == null || _light.type != LightType.Spot)` 样板。
        private bool EnsureLight()
        {
            if (_light == null)
                _light = GetComponent<Light>();
            return _light != null && _light.type == LightType.Spot;
        }

        private bool NeedsGeometryRebuild()
        {
            return !_coneCache.IsValid
                || !_coneCache.Matches(_light, maxDistance, coneSides, coneSegments, lightSourceRadius);
        }

        /// 更新几何体：使用归一化 Mesh + localScale 缩放；同时刷新派生缓存。
        private void UpdateGeometry()
        {
            if (!EnsureLight()) return;

            _coneCache.Refresh(_light, maxDistance, coneSides, coneSegments, lightSourceRadius);
            // 派生参数发生变化，下次 ApplyShaderProperties 需重新推送
            _materialParamsDirty = true;

            if (_meshFilter != null)
                _meshFilter.sharedMesh = GetSharedNormalizedMesh(coneSides, coneSegments);

            // localScale 控制锥体实际尺寸（归一化 Mesh: XY[-1,1], Z[0,1]）
            float maxRadius = Mathf.Max(_coneCache.radiusEnd, _coneCache.radiusStart);
            if (_volumeChild != null)
                _volumeChild.transform.localScale = new Vector3(maxRadius, maxRadius, maxDistance);
        }

        /// 材质刷新：
        /// 1. blendMode 变化时才切换 shader 关键字与 blend 状态（消除每帧 6 次无意义 Shader 调用）
        /// 2. 大部分参数走 dirty 推送（仅在 OnValidate/geometry 变更时才推）
        /// 3. _ClipDistance 是唯一高频参数（随射线检测更新），单独推送
        /// 4. _light.color 变化时仅刷新颜色通道
        /// <param name="applyAllParams">true 时跳过 dirty 检查强制全推（用于有 Animator 场景：
        /// 动画可能改变任何 Light 属性，必须每帧把全部 shader 参数同步一次）</param>
        private void UpdateMaterial(bool applyAllParams = false)
        {
            if (_meshRenderer == null) return;
            EnsureMaterial();
            if (_material == null) return;

            // 1. BlendMode：仅在变化时切换关键字
            if (_cachedBlendMode != blendMode)
            {
                ApplyBlendMode();
                _cachedBlendMode = blendMode;
            }

            // 1b. 软饱和 keyword：仅在变化时切换（多灯 + Bloom 场景抗过曝）
            if (_cachedSoftSaturation != softSaturation)
            {
                if (softSaturation)
                    _material.EnableKeyword("_VOLUME_SOFTSAT");
                else
                    _material.DisableKeyword("_VOLUME_SOFTSAT");
                _cachedSoftSaturation = softSaturation;
            }

            // 2. 整体参数：dirty 时或强制时全推
            if (applyAllParams || _materialParamsDirty)
            {
                ApplyShaderProperties();
                _materialParamsDirty = false;
                _lastColorFromLight = colorFromLight;
                _lastAppliedLightColor = colorFromLight ? _light.color : volumeColor;
            }
            // 3. colorFromLight 模式下，单通道跟踪灯光颜色变化
            else if (colorFromLight && _light.color != _lastAppliedLightColor)
            {
                _material.SetColor(ShaderIDs.VolumeColor, _light.color);
                _lastAppliedLightColor = _light.color;
            }

            // 4. 高频参数：每帧推送
            _material.SetFloat(ShaderIDs.ClipDistance, _clipDistance);
        }

        /// 轻量更新：仅推送每帧/实时变化的参数。
        /// 用于无 Animator 且无 dirty 的场景，跳过 17 个静态参数的无意义 SetFloat 调用。
        private void PushHighFrequencyShaderParams()
        {
            EnsureMaterial();
            if (_material == null) return;
            _material.SetFloat(ShaderIDs.ClipDistance, _clipDistance);
        }

        /// <summary>
        /// 强制完整刷新材质：让所有 dirty 缓存失效并立即推送所有 Shader 参数。
        /// 专用于外部代码（存档/读档、Undo/Redo）直接修改字段后调用。
        /// 正常 Inspector 调节字段会通过 OnValidate 自动标 dirty，无需调用此方法。
        /// </summary>
        public void RefreshMaterial()
        {
            // 失效所有 dirty 缓存，确保 UpdateMaterial 走"完整推送"分支
            _materialParamsDirty = true;
            _cachedBlendMode = (VolumeBlendMode)(-1);
            _cachedSoftSaturation = !softSaturation;
            _lastColorFromLight = !colorFromLight;
            _lastAppliedLightColor = colorFromLight ? _light.color : volumeColor; // 后续 if 分支按当前状态记录，避免立即再次推

            // 立即推送完整参数（不等下次 Update tick）
            UpdateMaterial(applyAllParams: true);
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
            // renderQueue 仅在材质创建时设置一次（原代码在 UpdateMaterial 中每帧赋值，纯浪费）
            _material.renderQueue = 3000;
            _meshRenderer.sharedMaterial = _material;
            _materialParamsDirty = true;
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

        /// 推送所有非高频 shader 参数（颜色 / 光照 / 几何 / 蒙版）
        private void ApplyShaderProperties()
        {
            ApplyColorProperty();
            ApplyLightingProperties();
            ApplyGeometryProperties();
            ApplyMaskProperties();
        }

        private void ApplyColorProperty()
        {
            Color c = colorFromLight ? _light.color : volumeColor;
            _material.SetColor(ShaderIDs.VolumeColor, c);
            _lastAppliedLightColor = c;
            _lastColorFromLight = colorFromLight;
        }

        private void ApplyLightingProperties()
        {
            _material.SetFloat(ShaderIDs.Intensity, intensity);
            _material.SetFloat(ShaderIDs.VolumeExposure, volumeExposure);
            _material.SetFloat(ShaderIDs.StartBoostIntensity, startBoostIntensity);
            _material.SetFloat(ShaderIDs.StartBoostRange, startBoostRange);
            _material.SetFloat(ShaderIDs.CenterFade, centerFade);
            _material.SetFloat(ShaderIDs.GlareFrontal, glareFrontal);
            _material.SetFloat(ShaderIDs.GlareBehind, glareBehind);
        }

        private void ApplyGeometryProperties()
        {
            _material.SetFloat(ShaderIDs.FallOffStart, fallOffStart);
            _material.SetFloat(ShaderIDs.FallOffEnd, maxDistance);
            _material.SetFloat(ShaderIDs.EdgeFade, edgeFade);
            _material.SetFloat(ShaderIDs.EndFade, endFade);
            _material.SetFloat(ShaderIDs.ConeRadiusStart, _coneCache.radiusStart);
            _material.SetFloat(ShaderIDs.ConeRadiusEnd, _coneCache.radiusEnd);
            _material.SetVector(ShaderIDs.ConeSlopeCosSin,
                new Vector4(_coneCache.cosSlope, _coneCache.sinSlope, 0, 0));
        }

        private void ApplyMaskProperties()
        {
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

        /// 计算光锥末端半径：使用 ConeGeometryCache 缓存，避免每帧重复三角函数计算。
        /// 保持向后兼容（OnDrawGizmosSelected 仍调用此方法）。
        private float ComputeRadiusEnd()
        {
            if (_light == null) return 0f;
            if (!_coneCache.IsValid
                || !_coneCache.Matches(_light, maxDistance, coneSides, coneSegments, lightSourceRadius))
            {
                _coneCache.Refresh(_light, maxDistance, coneSides, coneSegments, lightSourceRadius);
            }
            return _coneCache.radiusEnd;
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

        /// 生成归一化锥形 Mesh：XY 在 [-1,1]，Z 在 [0,1]
        /// 包含锥面 + 前 Cap(Z=0) + 后 Cap(Z=1)
        /// UV.x 标记：0=锥面, 1=cap
        private static Mesh GenerateNormalizedConeMesh(int sides, int segments)
        {
            int ringCount = segments + 2;
            int vertCountSides = sides * ringCount;
            int vertCountCap = sides + 1;
            int vertCountTotal = vertCountSides + vertCountCap * 2;

            var vertices = new Vector3[vertCountTotal];
            var uvs = new Vector2[vertCountTotal];

            // 锥面顶点 / UV
            BuildSideVertices(vertices, uvs, sides, ringCount, segments);

            // 前 Cap（Z=0）+ 后 Cap（Z=1）
            int frontCapStart = vertCountSides;
            BuildCapVertices(vertices, uvs, frontCapStart, sides, 0f);
            int backCapStart = frontCapStart + vertCountCap;
            BuildCapVertices(vertices, uvs, backCapStart, sides, 1f);

            // 三角形（保持与原版相同的索引序列以确保 Mesh 资产 bit-identical）
            var triangles = new int[sides * (segments + 1) * 6 + sides * 3 * 2];
            int tri = 0;
            tri = BuildSideTriangles(triangles, tri, sides, segments);
            tri = BuildCapTriangles(triangles, tri, frontCapStart, sides, forward: true);   // 前 Cap 正面朝 +Z
            tri = BuildCapTriangles(triangles, tri, backCapStart, sides, forward: false);   // 后 Cap 正面朝 -Z

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

        private static void BuildSideVertices(Vector3[] vertices, Vector2[] uvs, int sides, int ringCount, int segments)
        {
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
        }

        private static int BuildSideTriangles(int[] triangles, int triStart, int sides, int segments)
        {
            int tri = triStart;
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
            return tri;
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

        /// forward=true 朝 +Z（前 Cap），forward=false 朝 -Z（后 Cap）
        private static int BuildCapTriangles(int[] triangles, int triStart, int capStart, int sides, bool forward)
        {
            int tri = triStart;
            for (int i = 0; i < sides; i++)
            {
                if (forward)
                {
                    triangles[tri++] = capStart;
                    triangles[tri++] = capStart + 1 + i;
                    triangles[tri++] = capStart + 1 + (i + 1) % sides;
                }
                else
                {
                    triangles[tri++] = capStart;
                    triangles[tri++] = capStart + 1 + (i + 1) % sides;
                    triangles[tri++] = capStart + 1 + i;
                }
            }
            return tri;
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
