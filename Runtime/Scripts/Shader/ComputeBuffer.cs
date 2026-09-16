// ComputeBuffer 4.2 Inspector 中文化 - 分组标题改为中文；参数标签交由 ComputeBufferLightManagerEditor 映射显示（字段名不变，已有序列化数据不受影响）
// ComputeBuffer 4.1 跨场景渲染保护 - 修复共享材质在缺少灯光管理器的场景变黑/跳过渲染；对外接口与既有功能保持不变
//   1. 兜底：新增全局安全位 _CustomLightSystemActive（同步 PBR_Mobile_NEW 8.5 / PBR_Mobile 7.3），未接管时 Shader 跳过自定义光照，杜绝采样已释放 GraphicsBuffer 产生 NaN 变黑
//   2. 清理：ReleaseBuffers 同步关闭安全位、清零光源计数并解绑全局缓冲区；OnDestroy 清空静态单例 _instance
//   3. 钩子：订阅 sceneLoaded，缓冲区已释放则重建、否则重申接管状态，并重新下发材质参数
//   4. 回滚：恢复 ResetMaterialToDefaults 中被注释的点光源复位；运行期销毁时还原受控材质的光照开关
// ComputeBuffer 4.0 性能与可维护性重构 - 消除重复逻辑、修复聚光灯自适应间隔失效、清理死代码；功能与对外接口（含反射字段）保持不变
//   1. 去重：UpdateAllMaterials 复用 SetMaterialParameters；三项光照开关的"属性值+关键字"同步统一为 ApplyToggleToMaterial / ApplyToggleToAllMaterials；缓冲释放统一为 ReleaseBuffers
//   2. 复用：点光源与聚光灯的自适应间隔共用参数化的 ComputeCameraMotion
//   3. 修复：聚光灯自适应间隔曾读到同帧刚刷新的相机位置，速度恒为 0 导致该逻辑实际失效；现两者各持独立相机状态快照
//   4. 清理：删除死代码 _lightDistanceCache / _activeLightIndices / cameraMoved / OptimizedLightSort / CalculateLightLOD / LightDistanceComparer / _cameraMovementThreshold
//   5. 性能：缓存 Camera.main、剔除结果 List 复用、距离排序改用 sqrMagnitude 免开方
// ComputeBuffer 2.0.4 匹配PBR_Mobile_NEW材质
// ComputeBuffer 2.0.3 编辑器模式实时更新优化 - 增强Compute Buffer系统与编辑器集成，支持非运行模式下点光效果预览
// ComputeBuffer 2.0.2 改进 活动光源数量 显示准确度 _currentLightCount
// ComputeBuffer 2.0.1 测试
// ComputeBuffer2.0  自定义点光照明Compute Buffer计算缓冲区方案
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;

[ExecuteInEditMode]
public class ComputeBufferLightManager : MonoBehaviour
{
    // ● 定义与Shader匹配的点光源结构体
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct CustomPointLight
    {
        public Vector3 position;
        public float range;
        public Vector4 color; // RGB + Intensity in alpha
        public Vector4 parameters; // x: falloff, yzw: reserved
    }

    // ● 定义与Shader匹配的聚光灯结构体
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct CustomSpotLight
    {
        public Vector3 position;      // 光源位置
        public float range;           // 光源范围
        public Vector4 color;         // RGB + Intensity in alpha
        public Vector3 direction;     // 光源方向（归一化）
        public float spotAngle;       // 聚光灯角度（度）
        public float innerSpotAngle;  // 内锥角（度）
        public float falloff;         // 衰减幂次
        public float padding;         // 填充对齐
    }

    [Header("全局点光源材质参数")]
    [Tooltip("启用点光照效果")] 
    [SerializeField] private bool _usePointLight = false;
    
    [Tooltip("点光照强度")]
    [Range(0, 8)] 
    [SerializeField] private float _pointLightIntensity = 1.0f;
    
    [Tooltip("点光照范围倍增")]
    [Range(0.1f, 3)] 
    [SerializeField] private float _lightRangeMultiplier = 1.0f;
    
    [Tooltip("点光照衰减幂次")]
    [Range(0.5f, 8)]
    [SerializeField] private float _lightFalloff = 3.0f;

    // ==========================================
    // ● 回弹动画参数配置区域
    // ==========================================
    
    [Header("点光源回弹动画")]
    [Tooltip("启用回弹动画效果 - 控制点光源强度在起始值和目标值之间来回弹跳")]
    [SerializeField] private bool _enableBounceAnimation = false;
    
    [Tooltip("回弹动画起始强度 - 动画循环开始时的光照强度值")]
    [Range(0, 8)]
    [SerializeField] private float _bounceStartIntensity = 1.0f;

    [Tooltip("回弹动画目标强度 - 动画循环达到峰值时的光照强度值")]
    [Range(0, 8)]
    [SerializeField] private float _bounceTargetIntensity = 6.0f;

    [Tooltip("回弹动画速度 - 控制动画循环的快慢，值越大动画越快")]
    [Range(0.1f, 10f)]
    [SerializeField] private float _bounceAnimationSpeed = 2.0f;

    [Header("全局聚光灯材质参数")]
    [Tooltip("启用聚光照效果")] 
    [SerializeField] private bool _useSpotLight = false;
    
    [Tooltip("聚光照强度")]
    [Range(0, 8)] 
    [SerializeField] private float _spotLightIntensity = 1.0f;
    
    [Tooltip("聚光照范围倍增")]
    [Range(0.1f, 3)] 
    [SerializeField] private float _spotLightRangeMultiplier = 1.0f;
    
    [Tooltip("聚光照衰减幂次")]
    [Range(0.1f, 2)]
    [SerializeField] private float _spotLightFalloff = 2.0f;
    
    [Tooltip("最大聚光灯数量")]
    [Range(1, 2)]
    [SerializeField] private int _spotLightAmount = 2;
    
    [Header("聚光灯纹理参数")]
    [Tooltip("启用光斑纹理效果")]
    [SerializeField] private bool _useSpotTexture = false;
    
    [Tooltip("聚光灯纹理 - 统一控制所有PBR_Mobile_NEW材质的光斑纹理")]
    [SerializeField] private Texture2D _spotTexture = null;
    
    [Tooltip("光斑纹理对比度")]
    [Range(0.1f, 5)]
    [SerializeField] private float _spotTextureContrast = 1.0f;
    
    [Tooltip("光斑纹理大小")]
    [Range(0.1f, 1)]
    [SerializeField] private float _spotTextureSize = 0.5f;
    
    [Tooltip("光斑纹理强度")]
    [Range(0, 2)]
    [SerializeField] private float _spotTextureIntensity = 1.0f;

    [Header("材质管理 (PBR_Mobile_NEW)")]
    [Tooltip("在载入新场景时不删除此对象 (启动时)")] public bool dontDestroyOnLoad = false;
    [Tooltip("自动查找场景中使用 PBR_Mobile_NEW 材质 (启动时)")]
    public bool autoFindMaterials = false;
    
    [Tooltip("手动指定的材质列表")]
    public List<Material> targetMaterials = new List<Material>();
    
    // ● 材质管理变量，用于放置PBR_Mobile_NEW材质，批量控制材质参数
    private List<Material> _controlledMaterials = new List<Material>();
    private const string POINT_LIGHT_KEYWORD = "_USEPOINTLIGHT";
    private const string SPOT_LIGHT_KEYWORD = "_USESPOTLIGHT";
    private const string SPOT_TEXTURE_KEYWORD = "_USESPOTTEXTURE";

    // ● 全局灯光系统安全位，与 PBR_Mobile_NEW.shader / PBR_Mobile.shader 中的 _CustomLightSystemActive 一一对应。
    //   缓冲区释放后必须置 0，否则残留的材质关键字会让 Shader 采样已销毁的 StructuredBuffer 而变黑。
    private const string LIGHT_SYSTEM_ACTIVE_PROP = "_CustomLightSystemActive";

    /// <summary>把开关状态同步到单个材质：写入浮点属性并启用/禁用对应着色器关键字。</summary>
    private static void ApplyToggleToMaterial(Material material, int propertyId, string keyword, bool enabled)
    {
        if (material == null) return;

        if (material.HasProperty(propertyId))
        {
            material.SetFloat(propertyId, enabled ? 1 : 0);
        }

        if (enabled)
        {
            material.EnableKeyword(keyword);
        }
        else
        {
            material.DisableKeyword(keyword);
        }
    }

    /// <summary>把开关状态广播到所有受控材质。</summary>
    private void ApplyToggleToAllMaterials(int propertyId, string keyword, bool enabled)
    {
        for (int i = 0; i < targetMaterials.Count; i++)
        {
            ApplyToggleToMaterial(targetMaterials[i], propertyId, keyword, enabled);
        }
    }
    
    // ● Shader属性ID缓存（性能优化）
    private static class ShaderPropertyIDs
    {
        // ● 开关属性ID
        public static readonly int UsePointlight = Shader.PropertyToID("_UsePointlight");
        public static readonly int UseSpotlight = Shader.PropertyToID("_UseSpotlight");
        public static readonly int UseSpotTexture = Shader.PropertyToID("_UseSpotTexture");
        
        // ● 点光照参数ID
        public static readonly int PointLightIntensity = Shader.PropertyToID("_PointLightIntensity");
        public static readonly int PointLightRangeMultiplier = Shader.PropertyToID("_PointLightRangeMultiplier");
        public static readonly int PointLightFalloff = Shader.PropertyToID("_PointLightFalloff");
        // ● 点光源数量：移动端Shader用它作为逐像素遍历上限（MAX_POINT_LIGHTS），需与 maxLights 保持一致
        public static readonly int PointLightAmount = Shader.PropertyToID("_PointLightAmount");
        
        // ● 聚光照参数ID
        public static readonly int SpotLightIntensity = Shader.PropertyToID("_SpotLightIntensity");
        public static readonly int SpotLightRangeMultiplier = Shader.PropertyToID("_SpotLightRangeMultiplier");
        public static readonly int SpotLightFalloff = Shader.PropertyToID("_SpotLightFalloff");
        public static readonly int SpotLightAmount = Shader.PropertyToID("_SpotLightAmount");
        
        // ● 光斑纹理相关属性ID
        public static readonly int SpotTexture = Shader.PropertyToID("_SpotTexture");
        public static readonly int SpotTextureContrast = Shader.PropertyToID("_SpotTextureContrast");
        public static readonly int SpotTextureSize = Shader.PropertyToID("_SpotTextureSize");
        public static readonly int SpotTextureIntensity = Shader.PropertyToID("_SpotTextureIntensity");
    }

    [Header("点光源配置")]
    [Tooltip("拖拽场景中的点光源到此列表（最多8盏）")]
    public List<Light> pointLights = new List<Light>();
    
    [Header("聚光灯配置")]
    [Tooltip("拖拽场景中的聚光灯到此列表（最多2盏）")]
    public List<Light> spotLights = new List<Light>();
    
    // ● 距离剔除参数
    [Header("距离剔除设置")]
    [Tooltip("启用光源距离剔除 - 基于光源与摄像机的距离线性剔除过远的光源")]
    [SerializeField] private bool _enableDistanceCulling = false;
    [Tooltip("距离剔除系数 - 光源范围乘以此系数作为剔除距离阈值")]
    [Range(1.0f, 50.0f)]
    [SerializeField] private float _distanceCullFactor = 2.5f;
    [Tooltip("启用视锥体剔除 - 基于主相机视锥体剔除不在视野内的光源")]
    [SerializeField] private bool _enableFrustumCulling = false;
    [Tooltip("视锥体剔除容差 - 值越大剔除越宽松，0=严格剔除，1=最宽松剔除")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float _frustumCullTolerance = 0.1f;
    
    [Header("性能设置")]
    [Tooltip("最大支持的点光源数量")]
    [Range(1, 8)] public int maxLights = 8;
    [Tooltip("点光源更新频率 (Hz)")]
    [Range(1, 60)] public int updateFrequency = 16;
    [Tooltip("聚光灯更新频率 (Hz) - 独立控制聚光灯刷新率")]
    [Range(1, 60)] public int spotLightUpdateFrequency = 30;
    
    // ● Compute Buffer相关变量
    private GraphicsBuffer _lightsBuffer;
    private GraphicsBuffer _spotLightsBuffer;
    private CustomPointLight[] _lightsData;
    private CustomSpotLight[] _spotLightsData;
    private float _updateInterval;
    private float _spotLightUpdateInterval;
    private float _lastUpdateTime;
    private float _lastSpotLightUpdateTime;
    private int _currentLightCount = 0;
    private int _currentSpotLightCount = 0;
    
    // ● 参数变更标记 - 用于跟踪哪些参数发生了变化
    private bool _parametersDirty = false;
    
    // ● 编辑器模式更新相关变量
    private bool _editorUpdateInitialized = false;
    private float _lastEditorUpdateTime = 0f;
    private float _editorUpdateInterval = 0.1f; // 编辑器模式下默认更新间隔
    
    // ● 存储上一次的参数值，用于检测变化
    private bool _lastUsePointLight;
    private float _lastPointLightIntensity;
    private float _lastLightRangeMultiplier;
    private float _lastLightFalloff;
    private int _lastMaxLights;
    
    // ● 聚光灯相关参数缓存
    private bool _lastUseSpotLight;
    private float _lastSpotLightIntensity;
    private float _lastSpotLightRangeMultiplier;
    private float _lastSpotLightFalloff;
    private int _lastSpotLightAmount;
    
    // ● 光斑纹理相关参数缓存
    private bool _lastUseSpotTexture;
    private Texture2D _lastSpotTexture;
    private float _lastSpotTextureContrast;
    private float _lastSpotTextureSize;
    private float _lastSpotTextureIntensity;
    
    // ● 回弹动画参数缓存 - 用于检测Inspector参数变化
    // 存储上一次的参数值，当参数发生变化时自动更新材质
    private bool _lastEnableBounceAnimation;
    private float _lastBounceStartIntensity;
    private float _lastBounceTargetIntensity;
    private float _lastBounceAnimationSpeed;
    
    // ● 编辑器模式下回弹动画相关变量
    private float _animationTime = 0f;
    
    // ● 用户手动修改参数跟踪 - 用于检测用户是否手动修改了参数
    private bool _userModifiedIntensity = false;
    
    // ● 动画更新标志 - 用于区分参数变化是否来自动画
    private bool _isAnimationUpdate = false;
    
    // ● 性能优化：剔除与自适应更新所需的状态
    private Vector3 _lastCameraPosition;
    private Vector3 _lastCameraVelocity;
    private float _cameraSpeedThreshold = 5.0f; // 相机速度阈值，超过此值启用自适应更新

    // ● 聚光灯自适应间隔专用的相机运动状态：与点光源相互独立，
    //   否则后计算的聚光灯会读到同帧刚刷新的位置，算出的速度恒为 0
    private Vector3 _lastSpotCameraPosition;
    private Vector3 _lastSpotCameraVelocity;

    // ● 主相机缓存：Camera.main 内部按标签查找，每帧只查询一次
    private Camera _mainCameraCache;

    // ● 复用的剔除结果缓冲，避免每帧 new List 造成 GC
    private readonly List<int> _visibleLightIndices = new List<int>();
    // ● 与 _visibleLightIndices 平行的平方距离缓存：排序只比较相对大小，无需开方
    private float[] _visibleLightSqrDistances = new float[32];
    
    // ● 单例模式，便于全局访问
    private static ComputeBufferLightManager _instance;
    public static ComputeBufferLightManager Instance => _instance;

    void Awake()
    {
        // ● 初始化单例模式
        if (_instance && _instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        _instance = this;
        
        // ● 如希望持久存在，取消下一行注释
        // 只在游戏运行时调用DontDestroyOnLoad，避免编辑器模式错误
        if (dontDestroyOnLoad && Application.isPlaying)
        {
            DontDestroyOnLoad(this.gameObject);
        }
    }

    void Start()
    {
        InitializeComputeBuffer();
        InitializeMaterialController();
        
        // ● 初始化上一次的参数值
        CacheCurrentParameters();
        
        // ● 如果启用了回弹动画，自动开始动画
        // 在游戏启动时自动运行回弹动画效果
        if (_enableBounceAnimation && _usePointLight)
        {
            StartBounceAnimation();
        }
    }

    //● 初始化Compute Buffer系统
    //创建GraphicsBuffer并设置为全局Shader属性
    //所有使用Custom/PBR_Mobile_NEW Shader的材质都能访问这些光源数据

    // ReSharper disable Unity.PerformanceAnalysis
    void InitializeComputeBuffer()
    {
        _updateInterval = 1f / updateFrequency;
        _spotLightUpdateInterval = 1f / spotLightUpdateFrequency;
        _editorUpdateInterval = _updateInterval; // 编辑器模式下使用相同的更新间隔

        // ● 关键修复：重建前先释放已存在的缓冲区，避免重复调用 InitializeComputeBuffer
        // （重新初始化 / 编辑器刷新 / 安全兜底路径）时泄漏旧的 GraphicsBuffer（原生 GPU 内存）。
        if (_lightsBuffer != null) { _lightsBuffer.Release(); _lightsBuffer = null; }
        if (_spotLightsBuffer != null) { _spotLightsBuffer.Release(); _spotLightsBuffer = null; }

        // ● 计算结构体大小，确保内存对齐
        int pointLightStride = System.Runtime.InteropServices.Marshal.SizeOf<CustomPointLight>();
        int spotLightStride = System.Runtime.InteropServices.Marshal.SizeOf<CustomSpotLight>();
        
        // ● 创建点光源GraphicsBuffer (新API，兼容性更好)
        // GraphicsBuffer.Target.Structured 表示这是一个结构化缓冲区
        _lightsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxLights, pointLightStride);
        _lightsData = new CustomPointLight[maxLights];
        
        // ● 创建聚光灯GraphicsBuffer
        _spotLightsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _spotLightAmount, spotLightStride);
        _spotLightsData = new CustomSpotLight[_spotLightAmount];
        
        // ● 设置为全局Shader属性，所有Shader都可访问
        Shader.SetGlobalBuffer("_CustomPointLights", _lightsBuffer);
        Shader.SetGlobalInt("_CustomPointLightCount", 0);
        
        Shader.SetGlobalBuffer("_CustomSpotLights", _spotLightsBuffer);
        Shader.SetGlobalInt("_CustomSpotLightCount", 0);

        // ● 声明灯光系统已接管：Shader 侧据此决定是否采样自定义光照
        Shader.SetGlobalFloat(LIGHT_SYSTEM_ACTIVE_PROP, 1f);

        Debug.Log($"Compute Buffer初始化完成: {maxLights}个点光源容量，{_spotLightAmount}个聚光灯容量");
        Debug.Log($"点光源结构体大小: {pointLightStride}字节，聚光灯结构体大小: {spotLightStride}字节");
    }

    //● 初始化材质控制器
    //自动查找或使用手动指定的材质列表

    void InitializeMaterialController()
    {
        if (autoFindMaterials)
        {
            FindPBRMobileMaterials();
        }
        else
        {
            _controlledMaterials = new List<Material>(targetMaterials);
        }
        
        // ● 应用初始材质参数
        UpdateAllMaterials();
        
        Debug.Log($"材质控制器初始化完成，控制 {_controlledMaterials.Count} 个材质");
    }

    void Update()
    {
        // ● 每帧只查询一次主相机，后续所有方法共用缓存
        RefreshMainCameraCache();

        // ● 计算自适应更新间隔
        float adaptiveUpdateInterval = CalculateAdaptiveUpdateInterval();
        float adaptiveSpotLightInterval = CalculateAdaptiveSpotLightUpdateInterval();
        
        // ● 频率限制更新点光源Compute Buffer
        if (Time.time - _lastUpdateTime >= adaptiveUpdateInterval)
        {
            UpdateLightsBuffer();
            _lastUpdateTime = Time.time;
        }
        
        // ● 频率限制更新聚光灯Compute Buffer（独立刷新率）
        if (Time.time - _lastSpotLightUpdateTime >= adaptiveSpotLightInterval)
        {
            UpdateSpotLightsBuffer();
            _lastSpotLightUpdateTime = Time.time;
        }
        
        // ● 只在需要时检查参数变化（每5帧检查一次，减少性能开销）
        if (Time.frameCount % 5 == 0)
        {
            CheckForParameterChanges();
        }
        
        // ● 检查参数是否发生变化，如有变化则更新材质
        // 这种机制比每帧更新更高效，只在参数实际变化时更新
        if (_parametersDirty)
        {
            UpdateAllMaterials();
            _parametersDirty = false; // 重置脏标记
            CacheCurrentParameters(); // 缓存当前参数值
        }
    }
    
    //● 检查Inspector参数变化
    //这个方法检测通过Inspector直接修改的序列化字段的变化
    private void CheckForParameterChanges()
    {
        bool changed = false;
        
        // ● 检查点光照开关变化并立即同步材质
        if (_usePointLight != _lastUsePointLight)
        {
            changed = true;
            ApplyToggleToAllMaterials(ShaderPropertyIDs.UsePointlight, POINT_LIGHT_KEYWORD, _usePointLight);
        }
        
        if (!Mathf.Approximately(_pointLightIntensity, _lastPointLightIntensity))
        {
            changed = true;
        }
        
        if (!Mathf.Approximately(_lightRangeMultiplier, _lastLightRangeMultiplier))
        {
            changed = true;
        }
        
        if (!Mathf.Approximately(_lightFalloff, _lastLightFalloff))
        {
            changed = true;
        }
        
        // ● 点光源数量变化时需要重新同步材质的 _PointLightAmount
        if (maxLights != _lastMaxLights)
        {
            changed = true;
        }
        
        // ● 检查聚光照开关变化并立即同步材质
        if (_useSpotLight != _lastUseSpotLight)
        {
            changed = true;
            ApplyToggleToAllMaterials(ShaderPropertyIDs.UseSpotlight, SPOT_LIGHT_KEYWORD, _useSpotLight);
        }
        
        if (!Mathf.Approximately(_spotLightIntensity, _lastSpotLightIntensity))
        {
            changed = true;
        }
        
        if (!Mathf.Approximately(_spotLightRangeMultiplier, _lastSpotLightRangeMultiplier))
        {
            changed = true;
        }
        
        if (!Mathf.Approximately(_spotLightFalloff, _lastSpotLightFalloff))
        {
            changed = true;
        }
        
        if (_spotLightAmount != _lastSpotLightAmount)
        {
            changed = true;
        }
        
        // ● 检查光斑纹理开关变化并立即同步材质
        if (_useSpotTexture != _lastUseSpotTexture)
        {
            changed = true;
            ApplyToggleToAllMaterials(ShaderPropertyIDs.UseSpotTexture, SPOT_TEXTURE_KEYWORD, _useSpotTexture);
        }

        if (_spotTexture != _lastSpotTexture)
        {
            changed = true;
            // 立即同步更新所有材质的聚光灯纹理
            if (_spotTexture != null)
            {
                foreach (Material mat in targetMaterials)
                {
                    if (mat != null)
                    {
                        mat.SetTexture(ShaderPropertyIDs.SpotTexture, _spotTexture);
                    }
                }
            }
        }
        
        if (!Mathf.Approximately(_spotTextureContrast, _lastSpotTextureContrast))
        {
            changed = true;
        }
        
        if (!Mathf.Approximately(_spotTextureSize, _lastSpotTextureSize))
        {
            changed = true;
        }
        
        if (!Mathf.Approximately(_spotTextureIntensity, _lastSpotTextureIntensity))
        {
            changed = true;
        }
        
    // ● 检查回弹动画参数变化
    // 当用户在Inspector中修改回弹动画参数时，自动检测并更新材质
    if (!Mathf.Approximately(_bounceStartIntensity, _lastBounceStartIntensity) ||
        !Mathf.Approximately(_bounceTargetIntensity, _lastBounceTargetIntensity) ||
        !Mathf.Approximately(_bounceAnimationSpeed, _lastBounceAnimationSpeed) ||
        _enableBounceAnimation != _lastEnableBounceAnimation)
    {
        changed = true;
    }
        
        // ● 如果检测到变化，标记参数为脏
        if (changed)
        {
            _parametersDirty = true;
        }
    }
    
    //● 缓存当前参数值
    //用于下一次变化检测的对比基准
    private void CacheCurrentParameters()
    {
        _lastUsePointLight = _usePointLight;
        _lastPointLightIntensity = _pointLightIntensity;
        _lastLightRangeMultiplier = _lightRangeMultiplier;
        _lastLightFalloff = _lightFalloff;
        _lastMaxLights = maxLights;
        
        // 缓存聚光灯相关参数
        _lastUseSpotLight = _useSpotLight;
        _lastSpotLightIntensity = _spotLightIntensity;
        _lastSpotLightRangeMultiplier = _spotLightRangeMultiplier;
        _lastSpotLightFalloff = _spotLightFalloff;
        _lastSpotLightAmount = _spotLightAmount;
        
        // 缓存光斑纹理相关参数
        _lastUseSpotTexture = _useSpotTexture;
        _lastSpotTexture = _spotTexture;
        _lastSpotTextureContrast = _spotTextureContrast;
        _lastSpotTextureSize = _spotTextureSize;
        _lastSpotTextureIntensity = _spotTextureIntensity;
        
        // 缓存回弹动画参数 - 为下一次变化检测提供基准值
        _lastEnableBounceAnimation = _enableBounceAnimation;
        _lastBounceStartIntensity = _bounceStartIntensity;
        _lastBounceTargetIntensity = _bounceTargetIntensity;
        _lastBounceAnimationSpeed = _bounceAnimationSpeed;
    }

    /// <summary>每帧刷新一次主相机缓存，避免重复调用 Camera.main（内部按标签查找，开销较高）。</summary>
    private void RefreshMainCameraCache()
    {
        _mainCameraCache = Camera.main;
    }

    /// <summary>获取主相机。缓存被销毁或尚未填充时回退到实时查询。</summary>
    private Camera GetMainCamera()
    {
        // UnityEngine.Object 的 == 重载会把已销毁对象视为 null，因此这里同时处理销毁与未缓存两种情况
        if (_mainCameraCache == null)
        {
            _mainCameraCache = Camera.main;
        }
        return _mainCameraCache;
    }

    /// <summary>
    /// 收集通过距离剔除与视锥剔除的光源索引，并按到相机的距离由近到远排序。
    /// 点光源与聚光灯共用此流程。返回的是复用的内部列表，调用方需在下次调用前使用完毕。
    /// </summary>
    private List<int> CollectVisibleLightIndices(List<Light> lights, LightType expectedType, float rangeMultiplier)
    {
        _visibleLightIndices.Clear();

        Camera mainCamera = GetMainCamera();
        bool hasCamera = mainCamera != null;
        Vector3 cameraPosition = hasCamera ? mainCamera.transform.position : Vector3.zero;

        // 确保平方距离缓存容量足够
        if (_visibleLightSqrDistances.Length < lights.Count)
        {
            _visibleLightSqrDistances = new float[Mathf.Max(lights.Count, 32)];
        }

        for (int i = 0; i < lights.Count; i++)
        {
            Light light = lights[i];
            if (light == null || !light.enabled || light.type != expectedType)
                continue;

            Vector3 lightPosition = light.transform.position;
            bool shouldIncludeLight = true;

            // ● 缓存到相机的平方距离（sqrt 是单调函数，平方距离与真实距离排序结果一致）
            float sqrDistanceToCamera = hasCamera ? (lightPosition - cameraPosition).sqrMagnitude : 0f;
            _visibleLightSqrDistances[i] = sqrDistanceToCamera;

            // ● 距离剔除：比较平方距离，避免逐光源开方
            if (hasCamera && _enableDistanceCulling)
            {
                float cullDistance = light.range * rangeMultiplier * _distanceCullFactor;

                // 严格的距离检查：使用 >= 确保边界条件正确处理（修复最后一盏灯始终亮着的问题）
                if (sqrDistanceToCamera >= cullDistance * cullDistance)
                {
                    shouldIncludeLight = false;
                }
            }

            // ● 视锥体剔除
            if (shouldIncludeLight && hasCamera && _enableFrustumCulling)
            {
                if (!IsLightInFrustum(mainCamera, lightPosition, light.range * rangeMultiplier))
                {
                    shouldIncludeLight = false;
                }
            }

            if (shouldIncludeLight)
            {
                _visibleLightIndices.Add(i);
            }
        }

        // ● 按距离由近到远排序：直接比较缓存值，避免比较器内重复计算距离
        if (_visibleLightIndices.Count > 1 && hasCamera)
        {
            float[] distances = _visibleLightSqrDistances;
            _visibleLightIndices.Sort((a, b) => distances[a].CompareTo(distances[b]));
        }

        return _visibleLightIndices;
    }

    //● 更新点光源数据到Compute Buffer
    //收集场景中所有有效点光源数据，批量上传到GPU
    //性能优化：剔除（距离/视锥）+ 按距离排序后批量上传，缓冲区与结果列表均复用，避免每帧分配

    // ReSharper disable Unity.PerformanceAnalysis
    public int UpdateLightsBuffer()
    {
        // ● 安全检查：确保Compute Buffer已正确初始化
        if (_lightsBuffer == null || _lightsData == null)
        {
            Debug.LogWarning("PointLight Compute Buffer未正确初始化，重新初始化");
            InitializeComputeBuffer();
            
            // 重新检查初始化是否成功
            if (_lightsBuffer == null || _lightsData == null)
            {
                Debug.LogWarning("PointLight Compute Buffer重新初始化失败，跳过更新");
                return _currentLightCount;
            }
        }

        _currentLightCount = 0;

        // ● 收集通过剔除并已按距离排序的光源索引
        List<int> validLightIndices = CollectVisibleLightIndices(pointLights, LightType.Point, _lightRangeMultiplier);

        // ● 填充数据到_lightsData数组，不超过maxLights限制
        //   同时受已分配数组长度约束：缓冲区在初始化时按 maxLights 创建，运行中调大 maxLights 也不会越界
        int maxPointLights = Mathf.Min(maxLights, _lightsData.Length);
        for (int i = 0; i < validLightIndices.Count && _currentLightCount < maxPointLights; i++)
        {
            int lightIndex = validLightIndices[i];

            // ● 安全检查：确保索引在pointLights数组范围内
            if (lightIndex < 0 || lightIndex >= pointLights.Count)
            {
                Debug.LogWarning($"无效的光源索引: {lightIndex}, 跳过此光源");
                continue;
            }

            var light = pointLights[lightIndex];

            // ● 再次检查光源是否有效
            if (light == null || !light.enabled || light.type != LightType.Point)
                continue;

            _lightsData[_currentLightCount] = new CustomPointLight
            {
                position = light.transform.position,
                range = light.range,
                color = new Vector4(light.color.r, light.color.g, light.color.b, light.intensity),
                parameters = new Vector4(_lightFalloff, 0, 0, 0) // 使用配置的falloff参数
            };

            _currentLightCount++;
        }

        // ● 如果没有有效光源，确保Shader知道光源数量为0
        if (_currentLightCount == 0)
        {
            Shader.SetGlobalInt("_CustomPointLightCount", 0);
            return 0;
        }

        _lightsBuffer.SetData(_lightsData, 0, 0, _currentLightCount);

        // ● 更新全局光源数量，Shader根据这个值决定循环次数
        Shader.SetGlobalInt("_CustomPointLightCount", _currentLightCount);
        return _currentLightCount;
    }

    //● 更新聚光灯数据到Compute Buffer
    //收集场景中所有有效聚光灯数据，批量上传到GPU
    //性能优化：与点光源共用剔除与排序流程，缓冲区复用，避免每帧分配

    public int UpdateSpotLightsBuffer()
    {
        // ● 安全检查：确保Compute Buffer已正确初始化
        if (_spotLightsBuffer == null || _spotLightsData == null)
        {
            Debug.LogWarning("SpotLight Compute Buffer未正确初始化，重新初始化");
            InitializeComputeBuffer();
            
            // 重新检查初始化是否成功
            if (_spotLightsBuffer == null || _spotLightsData == null)
            {
                Debug.LogWarning("SpotLight Compute Buffer重新初始化失败，跳过更新");
                return _currentSpotLightCount;
            }
        }

        _currentSpotLightCount = 0;

        // ● 收集通过剔除并已按距离排序的聚光灯索引
        List<int> validSpotLightIndices = CollectVisibleLightIndices(spotLights, LightType.Spot, _spotLightRangeMultiplier);

        // ● 填充数据到_spotLightsData数组，不超过_spotLightAmount限制
        int maxSpotLights = Mathf.Min(_spotLightAmount, _spotLightsData.Length);
        for (int i = 0; i < validSpotLightIndices.Count && _currentSpotLightCount < maxSpotLights; i++)
        {
            int lightIndex = validSpotLightIndices[i];
            
            // ● 安全检查：确保索引在spotLights数组范围内
            if (lightIndex < 0 || lightIndex >= spotLights.Count)
            {
                Debug.LogWarning($"无效的聚光灯索引: {lightIndex}, 跳过此光源");
                continue;
            }
            
            var light = spotLights[lightIndex];
            
            // ● 再次检查光源是否有效
            if (light == null || !light.enabled || light.type != LightType.Spot)
                continue;
            
            // 计算聚光灯方向（归一化）
            Vector3 direction = light.transform.forward; // Unity的聚光灯方向是transform.forward
            
            _spotLightsData[_currentSpotLightCount] = new CustomSpotLight
            {
                position = light.transform.position,
                range = light.range,
                color = new Vector4(light.color.r, light.color.g, light.color.b, light.intensity),
                direction = direction,
                spotAngle = light.spotAngle,
                innerSpotAngle = light.innerSpotAngle,
                falloff = _spotLightFalloff,
                padding = 0
            };
            
            _currentSpotLightCount++;
        }

        // ● 如果没有有效聚光灯，确保Shader知道聚光灯数量为0
        if (_currentSpotLightCount == 0)
        {
            Shader.SetGlobalInt("_CustomSpotLightCount", 0);
            return 0;
        }

        _spotLightsBuffer.SetData(_spotLightsData, 0, 0, _currentSpotLightCount);

        // ● 更新全局聚光灯数量，Shader根据这个值决定循环次数
        Shader.SetGlobalInt("_CustomSpotLightCount", _currentSpotLightCount);
        return _currentSpotLightCount;
    }
    
    // ● 视锥体剔除检查（带容差距离）- 改进版本
    private bool IsLightInFrustum(Camera camera, Vector3 lightPosition, float lightRange)
    {
        // 如果禁用视锥剔除，所有光源都在视锥体内
        if (!_enableFrustumCulling)
        {
            return true;
        }
        
        Vector3 viewportPoint = camera.WorldToViewportPoint(lightPosition);
        
        // 如果容差为0，进行严格剔除（灯光在视锥边缘立即熄灭）
        if (_frustumCullTolerance <= 0f)
        {
            // 严格剔除：只检查光源位置是否在视锥体内
            bool inFrustumStrict = viewportPoint.z > 0 && 
                                  viewportPoint.x >= 0 && viewportPoint.x <= 1.0f &&
                                  viewportPoint.y >= 0 && viewportPoint.y <= 1.0f;
            return inFrustumStrict;
        }
        
        // 改进的容差计算：使用平方根映射使小值更不敏感，大值更敏感
        // 当_frustumCullTolerance=0.01时，容差约为光源范围的20%
        // 当_frustumCullTolerance=0.1时，容差约为光源范围的63%
        // 当_frustumCullTolerance=0.5时，容差约为光源范围的141%
        // 当_frustumCullTolerance=1.0时，容差约为光源范围的200%
        float toleranceMultiplier = Mathf.Sqrt(_frustumCullTolerance) * 2.0f;
        float effectiveRadius = lightRange * toleranceMultiplier;
        
        // 计算光源在视口空间中的影响范围
        float viewportRadius = CalculateViewportRadius(camera, lightPosition, effectiveRadius);
        
        // 检查光源是否在视锥体内（考虑容差距离）
        // 只有当光源完全离开视锥体一定距离后才剔除
        bool inFrustumWithTolerance = viewportPoint.z > 0 && 
                                     viewportPoint.x >= -viewportRadius && viewportPoint.x <= 1.0f + viewportRadius &&
                                     viewportPoint.y >= -viewportRadius && viewportPoint.y <= 1.0f + viewportRadius;
        
        return inFrustumWithTolerance;
    }
    
    // ● 计算光源在视口空间中的影响半径
    private float CalculateViewportRadius(Camera camera, Vector3 lightPosition, float effectiveRadius)
    {
        // 将光源位置转换到视口空间
        Vector3 viewportCenter = camera.WorldToViewportPoint(lightPosition);
        
        // 计算光源在X轴方向上的边界点
        Vector3 worldRight = lightPosition + camera.transform.right * effectiveRadius;
        Vector3 viewportRight = camera.WorldToViewportPoint(worldRight);
        
        // 计算光源在Y轴方向上的边界点
        Vector3 worldUp = lightPosition + camera.transform.up * effectiveRadius;
        Vector3 viewportUp = camera.WorldToViewportPoint(worldUp);
        
        // 计算视口空间中的最大半径
        float radiusX = Mathf.Abs(viewportRight.x - viewportCenter.x);
        float radiusY = Mathf.Abs(viewportUp.y - viewportCenter.y);
        
        // 返回较大的半径值，确保完全覆盖光源影响范围
        return Mathf.Max(radiusX, radiusY, 0.1f); // 最小容差0.1f
    }
    
    /// <summary>
    /// 计算相机当前速度与加速度大小，并刷新传入的状态快照。
    /// 点光源与聚光灯各持有一套独立状态，互不干扰。
    /// </summary>
    /// <param name="lastPosition">上一帧相机位置，函数内更新为当前位置。</param>
    /// <param name="lastVelocity">上一帧相机速度，函数内更新为当前速度。</param>
    private void ComputeCameraMotion(ref Vector3 lastPosition, ref Vector3 lastVelocity,
                                     out float speed, out float accelerationMagnitude)
    {
        Camera mainCamera = GetMainCamera();
        if (mainCamera == null)
        {
            speed = 0f;
            accelerationMagnitude = 0f;
            return;
        }

        Vector3 currentCameraPosition = mainCamera.transform.position;
        Vector3 currentCameraVelocity = (currentCameraPosition - lastPosition) / Time.deltaTime;
        Vector3 cameraAcceleration   = (currentCameraVelocity - lastVelocity) / Time.deltaTime;

        speed = currentCameraVelocity.magnitude;
        accelerationMagnitude = cameraAcceleration.magnitude;

        // ● 缓存当前速度和位置用于下一次计算
        lastVelocity = currentCameraVelocity;
        lastPosition = currentCameraPosition;
    }

    // ● 计算自适应更新间隔 - 根据相机移动速度和场景复杂度动态调整更新频率
    // 性能优化：当相机移动缓慢或场景变化不大时，降低更新频率以节省性能
    // 当相机快速移动或场景变化剧烈时，提高更新频率以保证视觉效果
    private float CalculateAdaptiveUpdateInterval()
    {
        // ● 如果没有主相机，返回基础间隔
        if (GetMainCamera() == null)
        {
            return _updateInterval;
        }

        ComputeCameraMotion(ref _lastCameraPosition, ref _lastCameraVelocity,
                            out float cameraSpeed, out float accelerationMagnitude);

        // ● 计算自适应因子
        float adaptiveFactor = 1.0f;

        // ● 基于相机速度的调整
        if (cameraSpeed > _cameraSpeedThreshold)
        {
            // 相机快速移动，提高更新频率（缩短间隔）
            float speedRatio = cameraSpeed / _cameraSpeedThreshold;
            adaptiveFactor *= Mathf.Clamp(1.0f / speedRatio, 0.1f, 1.0f);
        }
        else if (cameraSpeed < _cameraSpeedThreshold * 0.1f)
        {
            // 相机几乎静止，降低更新频率（延长间隔）
            adaptiveFactor *= 2.0f; // 延长更新间隔
        }

        // ● 基于相机加速度的调整
        if (accelerationMagnitude > 5.0f) // 加速度阈值
        {
            // 相机正在加速或减速，提高更新频率
            adaptiveFactor *= 0.7f;
        }

        // ● 基于活跃光源数量的调整
        if (_currentLightCount > maxLights * 0.7f)
        {
            // 活跃光源数量较多，稍微降低更新频率以节省性能
            adaptiveFactor *= 1.2f;
        }
        else if (_currentLightCount < maxLights * 0.3f)
        {
            // 活跃光源数量较少，可以稍微提高更新频率
            adaptiveFactor *= 0.9f;
        }

        // ● 确保自适应因子在合理范围内
        adaptiveFactor = Mathf.Clamp(adaptiveFactor, 0.1f, 3.0f);

        return _updateInterval * adaptiveFactor;
    }

    // ● 计算聚光灯自适应更新间隔 - 独立于点光源的刷新率控制
    // 聚光灯通常需要更高的刷新率以保证视觉效果的流畅性
    private float CalculateAdaptiveSpotLightUpdateInterval()
    {
        // ● 如果没有主相机，返回基础间隔
        if (GetMainCamera() == null)
        {
            return _spotLightUpdateInterval;
        }

        // ● 使用聚光灯专属状态，确保算到的是真实帧间速度
        ComputeCameraMotion(ref _lastSpotCameraPosition, ref _lastSpotCameraVelocity,
                            out float cameraSpeed, out float accelerationMagnitude);

        float adaptiveFactor = 1.0f;

        // ● 基于相机速度的调整（聚光灯对速度更敏感）
        if (cameraSpeed > _cameraSpeedThreshold)
        {
            // 相机快速移动，提高更新频率（缩短间隔）
            float speedRatio = cameraSpeed / _cameraSpeedThreshold;
            adaptiveFactor *= Mathf.Clamp(1.0f / speedRatio, 0.2f, 1.0f); // 最小0.2，比点光源更激进
        }
        else if (cameraSpeed < _cameraSpeedThreshold * 0.1f)
        {
            // 相机几乎静止，可以稍微降低更新频率
            adaptiveFactor *= 1.5f; // 比点光源降低得少
        }

        // ● 基于相机加速度的调整
        if (accelerationMagnitude > 5.0f)
        {
            // 相机正在加速或减速，提高更新频率
            adaptiveFactor *= 0.6f; // 比点光源更激进
        }

        // ● 基于活跃聚光灯数量的调整
        if (_currentSpotLightCount > 1)
        {
            // 多个聚光灯时，稍微降低更新频率以节省性能
            adaptiveFactor *= 1.1f;
        }

        // ● 确保自适应因子在合理范围内
        adaptiveFactor = Mathf.Clamp(adaptiveFactor, 0.2f, 2.0f);

        return _spotLightUpdateInterval * adaptiveFactor;
    }
    
    // ==========================================
    // ● 材质批量控制功能
    // ==========================================

    //● 自动查找场景中使用PBR_Mobile_NEW Shader的所有材质，遍历所有Renderer组件，收集其使用的材质
    [ContextMenu("查找PBR Mobile材质")]
    public void FindPBRMobileMaterials()
    {
        _controlledMaterials.Clear();
        targetMaterials.Clear();
        
        // ● 查找所有Renderer（包含未激活的物体）
        Renderer[] allRenderers = FindObjectsOfType<Renderer>(true);
        
        foreach (Renderer renderer in allRenderers)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material != null && material.shader.name == "Custom/PBR_Mobile_NEW")
                {
                    if (!_controlledMaterials.Contains(material))
                    {
                        _controlledMaterials.Add(material);
                    }
                    
                    // ● 同时添加到targetMaterials列表，方便在Inspector中查看
                    if (!targetMaterials.Contains(material))
                    {
                        targetMaterials.Add(material);
                    }
                }
            }
        }
        Debug.Log($"找到 {_controlledMaterials.Count} 个使用PBR_Mobile_NEW Shader的材质，已添加到Target Materials列表");
    }

    //● 批量更新所有受控材质的点光源参数
    //设置Shader关键字和浮点参数
    [ContextMenu("更新所有材质参数")]
    public void UpdateAllMaterials()
    {
        // ● 安全检查：清理null材质引用
        targetMaterials.RemoveAll(material => material == null);
        
        if (targetMaterials.Count == 0)
        {
            Debug.LogWarning("没有找到需要更新的材质");
            return;
        }

        int updatedCount = 0;

        foreach (Material material in targetMaterials)
        {
            if (material == null) continue;

            try
            {
                // ● 复用单材质参数设置逻辑，避免两处同步维护
                SetMaterialParameters(material);
                updatedCount++;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"更新材质 {material.name} 时出错: {e.Message}");
            }
        }
    }

    //● 为单个材质设置参数
    //用于精确控制特定材质

    public void SetMaterialParameters(Material material)
    {
        if (material == null) return;
        
        // ● 设置三个光照开关（属性值和关键字）
        ApplyToggleToMaterial(material, ShaderPropertyIDs.UsePointlight, POINT_LIGHT_KEYWORD, _usePointLight);
        ApplyToggleToMaterial(material, ShaderPropertyIDs.UseSpotlight, SPOT_LIGHT_KEYWORD, _useSpotLight);
        ApplyToggleToMaterial(material, ShaderPropertyIDs.UseSpotTexture, SPOT_TEXTURE_KEYWORD, _useSpotTexture);

        // ● 设置数值参数
        material.SetFloat(ShaderPropertyIDs.PointLightIntensity, _pointLightIntensity);
        material.SetFloat(ShaderPropertyIDs.PointLightRangeMultiplier, _lightRangeMultiplier);
        material.SetFloat(ShaderPropertyIDs.PointLightFalloff, _lightFalloff);
        // ● 与 maxLights 同步：移动端Shader逐像素遍历上限取自该材质属性，不下发则真机仍按材质旧值（默认4）截断
        material.SetFloat(ShaderPropertyIDs.PointLightAmount, maxLights);
        
        // ● 设置SpotLight数值参数
        material.SetFloat(ShaderPropertyIDs.SpotLightIntensity, _spotLightIntensity);
        material.SetFloat(ShaderPropertyIDs.SpotLightRangeMultiplier, _spotLightRangeMultiplier);
        material.SetFloat(ShaderPropertyIDs.SpotLightFalloff, _spotLightFalloff);
        material.SetFloat(ShaderPropertyIDs.SpotLightAmount, _spotLightAmount);
        
        // ● 设置光斑纹理数值参数
        if (_spotTexture != null)
        {
            material.SetTexture(ShaderPropertyIDs.SpotTexture, _spotTexture);
        }
        material.SetFloat(ShaderPropertyIDs.SpotTextureContrast, _spotTextureContrast);
        material.SetFloat(ShaderPropertyIDs.SpotTextureSize, _spotTextureSize);
        material.SetFloat(ShaderPropertyIDs.SpotTextureIntensity, _spotTextureIntensity);
    }

    //● 动态添加材质到控制列表
    //适用于运行时创建的材质

    public void AddMaterial(Material material)
    {
        if (material != null && !_controlledMaterials.Contains(material))
        {
            _controlledMaterials.Add(material);
            SetMaterialParameters(material); // 立即应用当前设置
        }
    }

    //● 从控制列表移除材质

    public void RemoveMaterial(Material material)
    {
        if (_controlledMaterials.Contains(material))
        {
            _controlledMaterials.Remove(material);
        }
    }

    //● 获取当前控制的材质数量

    public int GetControlledMaterialCount()
    {
        // return _controlledMaterials.Count;
        return targetMaterials.Count;
    }

    // ==========================================
    // ● 材质参数控制方法（带参数变更检测）
    // ==========================================

    //● 设置点光照开关状态
    public void SetPointLightEnabled(bool enabled)
    {
        if (_usePointLight != enabled)
        {
            _usePointLight = enabled;
            _parametersDirty = true; // 标记参数已变更

            // 立即同步更新所有材质的点光照开关
            ApplyToggleToAllMaterials(ShaderPropertyIDs.UsePointlight, POINT_LIGHT_KEYWORD, enabled);
        }
    }

    //● 获取点光照开关状态
    public bool GetPointLightEnabled()
    {
        return _usePointLight;
    }

    //● 设置聚光照开关状态
    public void SetSpotLightEnabled(bool enabled)
    {
        if (_useSpotLight != enabled)
        {
            _useSpotLight = enabled;
            _parametersDirty = true; // 标记参数已变更

            // 立即同步更新所有材质的聚光照开关
            ApplyToggleToAllMaterials(ShaderPropertyIDs.UseSpotlight, SPOT_LIGHT_KEYWORD, enabled);
        }
    }

    //● 获取聚光照开关状态
    public bool GetSpotLightEnabled()
    {
        return _useSpotLight;
    }

    //● 设置点光照强度
    public void SetPointLightIntensity(float intensity)
    {
        float clampedIntensity = Mathf.Clamp(intensity, 0, 8);
        if (!Mathf.Approximately(_pointLightIntensity, clampedIntensity))
        {
            _pointLightIntensity = clampedIntensity;
            _parametersDirty = true; // 标记参数已变更
        }
    }

    //● 获取点光照强度
    public float GetPointLightIntensity()
    {
        return _pointLightIntensity;
    }

    //● 设置点光照范围倍增
    public void SetPointLightRangeMultiplier(float multiplier)
    {
        float clampedMultiplier = Mathf.Clamp(multiplier, 0.1f, 3f);
        if (!Mathf.Approximately(_lightRangeMultiplier, clampedMultiplier))
        {
            _lightRangeMultiplier = clampedMultiplier;
            _parametersDirty = true; // 标记参数已变更
        }
    }

    //● 获取点光照范围倍增
    public float GetPointLightRangeMultiplier()
    {
        return _lightRangeMultiplier;
    }

    //● 设置点光照衰减幂次
    public void SetPointLightFalloff(float falloff)
    {
        float clampedFalloff = Mathf.Clamp(falloff, 0.5f, 8f);
        if (!Mathf.Approximately(_lightFalloff, clampedFalloff))
        {
            _lightFalloff = clampedFalloff;
            _parametersDirty = true; // 标记参数已变更
        }
    }

    //● 获取点光照衰减幂次
    public float GetPointLightFalloff()
    {
        return _lightFalloff;
    }

    //● 批量设置所有材质参数
    public void SetAllMaterialParameters(bool usePointLight, float intensity, float rangeMultiplier, float falloff)
    {
        bool changed = false;
        
        // ● 检查每个参数是否发生变化
        if (_usePointLight != usePointLight)
        {
            _usePointLight = usePointLight;
            changed = true;
        }
        
        float clampedIntensity = Mathf.Clamp(intensity, 0, 8);
        if (!Mathf.Approximately(_pointLightIntensity, clampedIntensity))
        {
            _pointLightIntensity = clampedIntensity;
            changed = true;
        }
        
        float clampedRangeMultiplier = Mathf.Clamp(rangeMultiplier, 0.1f, 3f);
        if (!Mathf.Approximately(_lightRangeMultiplier, clampedRangeMultiplier))
        {
            _lightRangeMultiplier = clampedRangeMultiplier;
            changed = true;
        }
        
        float clampedFalloff = Mathf.Clamp(falloff, 0.5f, 8f);
        if (!Mathf.Approximately(_lightFalloff, clampedFalloff))
        {
            _lightFalloff = clampedFalloff;
            changed = true;
        }
        
        // ● 只有参数实际发生变化时才标记为脏
        if (changed)
        {
            _parametersDirty = true;
        }
    }

    //● 设置聚光灯纹理
    public void SetSpotTexture(Texture2D texture)
    {
        if (_spotTexture != texture)
        {
            _spotTexture = texture;
            _parametersDirty = true; // 标记参数已变更
            
            // 立即更新所有受控材质的纹理
            foreach (Material mat in targetMaterials)
            {
                if (mat != null && texture != null)
                {
                    mat.SetTexture(ShaderPropertyIDs.SpotTexture, texture);
                }
            }
        }
    }

    //● 获取聚光灯纹理
    public Texture2D GetSpotTexture()
    {
        return _spotTexture;
    }

    //● 设置是否启用光斑纹理
    public void SetUseSpotTexture(bool enabled)
    {
        if (_useSpotTexture != enabled)
        {
            _useSpotTexture = enabled;
            _parametersDirty = true; // 标记参数已变更

            // 立即更新所有受控材质的光斑纹理开关
            ApplyToggleToAllMaterials(ShaderPropertyIDs.UseSpotTexture, SPOT_TEXTURE_KEYWORD, enabled);
        }
    }

    //● 获取是否启用光斑纹理
    public bool GetUseSpotTexture()
    {
        return _useSpotTexture;
    }

    //● 设置光斑纹理对比度
    public void SetSpotTextureContrast(float contrast)
    {
        float clampedContrast = Mathf.Clamp(contrast, 0.1f, 5f);
        if (!Mathf.Approximately(_spotTextureContrast, clampedContrast))
        {
            _spotTextureContrast = clampedContrast;
            _parametersDirty = true; // 标记参数已变更
            
            // 立即更新所有受控材质的对比度
            foreach (Material mat in targetMaterials)
            {
                if (mat != null)
                {
                    mat.SetFloat(ShaderPropertyIDs.SpotTextureContrast, clampedContrast);
                }
            }
        }
    }

    //● 获取光斑纹理对比度
    public float GetSpotTextureContrast()
    {
        return _spotTextureContrast;
    }

    //● 设置光斑纹理大小
    public void SetSpotTextureSize(float size)
    {
        float clampedSize = Mathf.Clamp(size, 0.1f, 1f);
        if (!Mathf.Approximately(_spotTextureSize, clampedSize))
        {
            _spotTextureSize = clampedSize;
            _parametersDirty = true; // 标记参数已变更
            
            // 立即更新所有受控材质的大小
            foreach (Material mat in targetMaterials)
            {
                if (mat != null)
                {
                    mat.SetFloat(ShaderPropertyIDs.SpotTextureSize, clampedSize);
                }
            }
        }
    }

    //● 获取光斑纹理大小
    public float GetSpotTextureSize()
    {
        return _spotTextureSize;
    }

    //● 设置光斑纹理强度
    public void SetSpotTextureIntensity(float intensity)
    {
        float clampedIntensity = Mathf.Clamp(intensity, 0f, 2f);
        if (!Mathf.Approximately(_spotTextureIntensity, clampedIntensity))
        {
            _spotTextureIntensity = clampedIntensity;
            _parametersDirty = true; // 标记参数已变更
            
            // 立即更新所有受控材质的强度
            foreach (Material mat in targetMaterials)
            {
                if (mat != null)
                {
                    mat.SetFloat(ShaderPropertyIDs.SpotTextureIntensity, clampedIntensity);
                }
            }
        }
    }

    //● 获取光斑纹理强度
    public float GetSpotTextureIntensity()
    {
        return _spotTextureIntensity;
    }

    //● 重置所有材质到默认值
    [ContextMenu("重置材质到默认值")]
    public void ResetMaterialToDefaults()
    {
        // ● 检查是否有参数与默认值不同
        bool changed = false;

        // ● 复位点光源开关：此前该段被注释掉，导致开关无法被还原，残留关键字会污染其它场景
        if (_usePointLight)
        {
            _usePointLight = false;
            changed = true;
        }

        if (!Mathf.Approximately(_pointLightIntensity, 1.0f))
        {
            _pointLightIntensity = 1.0f;
            changed = true;
        }
        
        if (!Mathf.Approximately(_lightRangeMultiplier, 1.3f))
        {
            _lightRangeMultiplier = 1.3f;
            changed = true;
        }
        
        if (!Mathf.Approximately(_lightFalloff, 3.0f))
        {
            _lightFalloff = 3.0f;
            changed = true;
        }
        
        if (changed)
        {
            _parametersDirty = true;
            Debug.Log("已重置所有材质参数到默认值");
        }
    }

    // ==========================================
    // ● 动态效果方法
    // ==========================================

    //● 渐变强度变化效果
    //使用协程实现平滑过渡

    public void FadeIntensity(float targetIntensity, float duration)
    {
        StartCoroutine(FadeIntensityCoroutine(targetIntensity, duration));
    }

    private IEnumerator FadeIntensityCoroutine(float targetIntensity, float duration)
    {
        float startIntensity = _pointLightIntensity;
        float elapsedTime = 0f;
        
        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / duration;
            
            // ● 使用平滑的插值
            float newIntensity = Mathf.Lerp(startIntensity, targetIntensity, t);
            
            // ● 直接设置值并标记为脏，避免每帧检查
            if (!Mathf.Approximately(_pointLightIntensity, newIntensity))
            {
                _pointLightIntensity = newIntensity;
                _parametersDirty = true;
            }
            
            yield return null;
        }
        
        // ● 确保最终值准确
        if (!Mathf.Approximately(_pointLightIntensity, targetIntensity))
        {
            _pointLightIntensity = targetIntensity;
            _parametersDirty = true;
        }
    }

    //● 闪烁效果
    //使用Perlin噪声产生自然的闪烁

    public void StartFlickerEffect(float minIntensity, float maxIntensity, float speed)
    {
        StartCoroutine(FlickerCoroutine(minIntensity, maxIntensity, speed));
    }

    //● 停止闪烁效果

    public void StopFlickerEffect()
    {
        StopAllCoroutines();
    }

    private IEnumerator FlickerCoroutine(float minIntensity, float maxIntensity, float speed)
    {
        float originalIntensity = _pointLightIntensity;
        
        while (true)
        {
            // ● 使用Perlin噪声产生自然的闪烁效果
            float noise = Mathf.PerlinNoise(Time.time * speed, 0f);
            float newIntensity = Mathf.Lerp(minIntensity, maxIntensity, noise);
            
            // ● 只在强度实际变化时更新
            if (!Mathf.Approximately(_pointLightIntensity, newIntensity))
            {
                _pointLightIntensity = newIntensity;
                _parametersDirty = true;
            }
            
            yield return null;
        }
    }

    // ==========================================
    // ● 回弹动画控制方法
    // ==========================================

    //● 回弹式强度动画效果
    //在起始值和目标值之间来回弹跳，创建平滑的呼吸灯效果
    //用法：调用此方法开始回弹动画，动画会自动循环直到停止

    public void StartBounceAnimation()
    {
        StartCoroutine(BounceAnimationCoroutine());
    }

    //● 停止回弹动画效果
    //停止当前正在运行的回弹动画协程
    //用法：当需要停止动画时调用此方法

    public void StopBounceAnimation()
    {
        StopCoroutine("BounceAnimationCoroutine");
    }

    //● 设置回弹动画启用状态
    //通过代码控制回弹动画的启用/禁用状态
    //参数：enabled - true启用动画，false禁用动画
    //用法：用于运行时动态控制动画状态

    public void SetBounceAnimationEnabled(bool enabled)
    {
        if (_enableBounceAnimation != enabled)
        {
            _enableBounceAnimation = enabled;
            
            if (_enableBounceAnimation && _usePointLight)
            {
                StartBounceAnimation();
            }
            else
            {
                StopBounceAnimation();
            }
        }
    }

    //● 获取回弹动画启用状态
    //返回当前回弹动画是否启用的状态
    //返回值：true表示动画启用，false表示动画禁用
    //用法：用于检查当前动画状态

    public bool GetBounceAnimationEnabled()
    {
        return _enableBounceAnimation;
    }

    //● 回弹动画协程 - 核心动画逻辑
    //创建平滑的点光源强度回弹效果，在起始值和目标值之间循环
    //工作原理：
    // 1. 使用Mathf.PingPong在0-1之间循环时间值
    // 2. 应用缓动函数使动画在端点处有缓冲效果
    // 3. 使用Lerp在起始值和目标值之间插值
    // 4. 只在强度实际变化时更新材质，优化性能
    // 5. 检测用户手动修改参数，自动停止动画
    //用法：此协程由StartBounceAnimation()自动启动

    private IEnumerator BounceAnimationCoroutine()
    {
        float currentTime = 0f;
        
        while (true)
        {
            // ● 检查用户是否手动修改了强度参数
            // 如果用户手动修改了参数，停止动画并重置用户修改标记
            if (_userModifiedIntensity)
            {
                _enableBounceAnimation = false;
                _userModifiedIntensity = false;
                yield break; // 退出协程
            }
            
            // ● 设置动画更新标志
            _isAnimationUpdate = true;
            
            // ● 累积时间并应用动画速度
            currentTime += Time.deltaTime * _bounceAnimationSpeed;
            
            // ● 使用Mathf.PingPong在0-1之间循环时间值
            // 创建来回弹跳的效果，自动在0和1之间循环
            float t = Mathf.PingPong(currentTime, 1f);
            
            // ● 应用缓动函数，在整个动画过程中添加缓冲效果
            // 使动画在端点处有自然的缓冲，避免生硬的反转
            float easedT = SmoothBounceEase(t);
            
            // ● 在起始值和目标值之间插值计算新的强度值
            float newIntensity = Mathf.Lerp(_bounceStartIntensity, _bounceTargetIntensity, easedT);
            
            // ● 只在强度实际变化时更新，优化性能
            // 避免每帧都更新材质，只在数值真正变化时标记为脏
            if (!Mathf.Approximately(_pointLightIntensity, newIntensity))
            {
                _pointLightIntensity = newIntensity;
                _parametersDirty = true;
            }
            
            // ● 重置动画更新标志
            _isAnimationUpdate = false;
            
            yield return null;
        }
    }
    
    // ● 缓动函数 - 在整个动画过程中添加平滑缓冲效果
    // 使用余弦函数创建平滑的缓冲效果
    // 参数：t - 0到1之间的插值参数
    // 返回值：应用了缓动效果的插值参数
    // 特点：在整个动画过程中保持均匀速度，在端点处有自然的缓冲
    // 中间部分不会快速移动，避免生硬的反转效果

    private float SmoothBounceEase(float t)
    {
        // 使用正弦函数创建平滑的缓冲效果，整个动画过程都保持均匀速度
        // 在端点处有自然的缓冲，中间部分也不会快速移动
        return 0.5f * (1f - Mathf.Cos(t * Mathf.PI));
    }

    // ==========================================
    // ● 原有的点光源管理方法
    // ==========================================

    //● 动态添加点光源 (运行时)

    public void AddPointLight(Light pointLight)
    {
        if (pointLight != null && pointLight.type == LightType.Point)
        {
            if (!pointLights.Contains(pointLight))
            {
                pointLights.Add(pointLight);
                UpdateLightsBuffer(); // 立即更新
            }
        }
    }

    //● 动态移除点光源 (运行时)

    public void RemovePointLight(Light pointLight)
    {
        if (pointLights.Contains(pointLight))
        {
            pointLights.Remove(pointLight);
            UpdateLightsBuffer(); // 立即更新
        }
    }

    //● 获取当前有效光源数量

    public int GetActiveLightCount()
    {
        return _currentLightCount;
    }

    //● 获取当前有效聚光灯数量

    public int GetActiveSpotLightCount()
    {
        return _currentSpotLightCount;
    }

    //● 自动收集场景中的所有点光源

    [ContextMenu("收集场景点光源")]
    public void CollectScenePointLights()
    {
        try
        {
            // ● 安全检查：确保Compute Buffer已正确初始化
            if (_lightsBuffer == null || _lightsData == null)
            {
                Debug.LogWarning("Compute Buffer未正确初始化，重新初始化");
                InitializeComputeBuffer();
            }
            
            pointLights.Clear();
            var allLights = FindObjectsOfType<Light>();
            
            foreach (var light in allLights)
            {
                if (light != null && light.type == LightType.Point && light.enabled)
                {
                    pointLights.Add(light);
                }
            }
            
            Debug.Log($"收集到 {pointLights.Count} 个点光源");
            UpdateLightsBuffer();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"收集场景点光源时出错: {e.Message}");
            Debug.LogException(e);
        }
    }

    //● 自动收集场景中的所有聚光灯

    [ContextMenu("收集场景聚光灯")]
    public void CollectSceneSpotLights()
    {
        try
        {
            // ● 安全检查：确保Compute Buffer已正确初始化
            if (_spotLightsBuffer == null || _spotLightsData == null)
            {
                Debug.LogWarning("SpotLight Compute Buffer未正确初始化，重新初始化");
                InitializeComputeBuffer();
            }
            
            spotLights.Clear();
            var allLights = FindObjectsOfType<Light>();
            
            foreach (var light in allLights)
            {
                if (light != null && light.type == LightType.Spot && light.enabled)
                {
                    spotLights.Add(light);
                }
            }
            
            Debug.Log($"收集到 {spotLights.Count} 个聚光灯");
            UpdateSpotLightsBuffer();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"收集场景聚光灯时出错: {e.Message}");
            Debug.LogException(e);
        }
    }

    // ==========================================
    // ● 编辑器专用方法
    // ==========================================

    #if UNITY_EDITOR
    //● 编辑器专用的材质查找方法
    //在编辑器模式下查找场景中使用PBR_Mobile_NEW Shader的所有材质
    //与运行时方法不同，此方法使用UnityEditor API来查找所有游戏对象
    [ContextMenu("编辑器查找PBR Mobile材质")]
    public void EditorFindPBRMobileMaterials()
    {
        _controlledMaterials.Clear();
        targetMaterials.Clear();
        
        // ● 使用UnityEditor API查找所有游戏对象（包含未激活的）
        var allGameObjects = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().GetRootGameObjects();
        
        int materialCount = 0;
        
        foreach (var rootObject in allGameObjects)
        {
            // ● 递归查找所有子对象中的Renderer组件
            var renderers = rootObject.GetComponentsInChildren<Renderer>(true);
            
            foreach (Renderer renderer in renderers)
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && material.shader.name == "Custom/PBR_Mobile_NEW")
                    {
                        if (!_controlledMaterials.Contains(material))
                        {
                            _controlledMaterials.Add(material);
                        }
                        
                        // ● 同时添加到targetMaterials列表，方便在Inspector中查看
                        if (!targetMaterials.Contains(material))
                        {
                            targetMaterials.Add(material);
                            materialCount++;
                        }
                        
                    }
                }
            }
        }
        
        Debug.Log($"编辑器模式下找到 {materialCount} 个使用PBR_Mobile_NEW Shader的材质，已添加到Target Materials列表");
        
        // ● 在编辑器模式下强制刷新Inspector显示
        UnityEditor.EditorUtility.SetDirty(this);
    }

    //● 初始化编辑器模式更新循环
    //在编辑器模式下模拟游戏循环，定期更新点光灯数据
    private void InitializeEditorUpdate()
    {
        if (!_editorUpdateInitialized)
        {
            // ● 注册编辑器更新事件
            UnityEditor.EditorApplication.update += OnEditorUpdate;
            _editorUpdateInitialized = true;
            _lastEditorUpdateTime = (float)UnityEditor.EditorApplication.timeSinceStartup;
            
            Debug.Log("编辑器模式点光灯更新循环已初始化");
        }
    }
    
    //● 编辑器模式更新方法
    //在编辑器模式下定期更新点光灯数据
    private void OnEditorUpdate()
    {
        if (!Application.isPlaying)
        {
            // ● 编辑器模式下同样每轮只查询一次主相机
            RefreshMainCameraCache();

            float currentTime = (float)UnityEditor.EditorApplication.timeSinceStartup;
            float deltaTime = currentTime - _lastEditorUpdateTime;
            
            // ● 按固定时间间隔更新点光灯数据
            if (deltaTime >= _editorUpdateInterval)
            {
                // ● 确保Compute Buffer已初始化
                if (_lightsBuffer == null && pointLights.Count > 0)
                {
                    InitializeComputeBuffer();
                }
                
                // ● 更新点光灯数据
                if (_lightsBuffer != null)
                {
                    UpdateLightsBuffer();
                }
                
                // ● 更新聚光灯数据
                if (_spotLightsBuffer != null)
                {
                    UpdateSpotLightsBuffer();
                }
                
                // ● 在编辑器模式下更新回弹动画
                // 动画更新完全由 _enableBounceAnimation 控制
                // 只有当动画明确启用时才更新动画，避免干扰手动参数设置
                if (_enableBounceAnimation)
                {
                    UpdateBounceAnimation(deltaTime);
                }
                
                // ● 检查参数变化并更新材质
                CheckForParameterChanges();
                if (_parametersDirty)
                {
                    UpdateAllMaterials();
                    _parametersDirty = false;
                    CacheCurrentParameters();
                }
                
                _lastEditorUpdateTime = currentTime;
            }
        }
    }
    
    //● 编辑器模式下更新回弹动画
    //在编辑器模式下驱动回弹动画，不依赖协程
    private void UpdateBounceAnimation(float deltaTime)
    {
        if (!_enableBounceAnimation) return;
        
        // ● 检查用户是否手动修改了强度参数
        // 如果用户手动修改了参数，停止动画并重置用户修改标记
        if (_userModifiedIntensity)
        {
            _enableBounceAnimation = false;
            _userModifiedIntensity = false;
            return;
        }
        
        // ● 设置动画更新标志
        _isAnimationUpdate = true;
        
        // ● 累积时间并应用动画速度
        _animationTime += deltaTime * _bounceAnimationSpeed;
        
        // ● 使用Mathf.PingPong在0-1之间循环时间值
        float t = Mathf.PingPong(_animationTime, 1f);
        
        // ● 应用缓动函数，在整个动画过程中添加缓冲效果
        float easedT = SmoothBounceEase(t);
        
        // ● 在起始值和目标值之间插值计算新的强度值
        float newIntensity = Mathf.Lerp(_bounceStartIntensity, _bounceTargetIntensity, easedT);
        
        // ● 只在强度实际变化时更新，优化性能
        if (!Mathf.Approximately(_pointLightIntensity, newIntensity))
        {
            _pointLightIntensity = newIntensity;
            _parametersDirty = true;
        }
        
        // ● 重置动画更新标志
        _isAnimationUpdate = false;
    }
    
    //● 清理编辑器模式更新循环
    //当对象被销毁或禁用时，取消注册编辑器更新事件
    private void OnDisable()
    {
        // 安全检查：确保对象仍然有效
        if (this == null) return;

        // ● 解除场景加载回调，与 OnEnable 中的订阅严格配对
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (_editorUpdateInitialized)
        {
            UnityEditor.EditorApplication.update -= OnEditorUpdate;
            _editorUpdateInitialized = false;
        }

        ReleaseBuffers();
    }

    //● 场景加载完成后的恢复
    //新场景的全局 Shader 状态可能残留上一场景的值：缓冲区若已释放则重建，
    //并重新下发材质参数，修正跨场景残留的开关与关键字状态
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 安全检查：确保对象仍然有效且处于启用状态
        if (this == null || !enabled) return;

        // ● 缓冲区若已随上一场景释放，此处重建；否则只需重新声明接管状态
        if (_lightsBuffer == null || _spotLightsBuffer == null)
        {
            InitializeComputeBuffer();
        }
        else
        {
            Shader.SetGlobalFloat(LIGHT_SYSTEM_ACTIVE_PROP, 1f);
        }

        // ● 重新收集并下发材质参数（autoFindMaterials 时需按新场景重新收集）
        if (autoFindMaterials)
        {
            InitializeMaterialController();
        }
        else
        {
            UpdateAllMaterials();
        }
    }

    /// <summary>
    /// 释放点光源与聚光灯的 GraphicsBuffer，并同步复位全局 Shader 状态。
    /// 全局属性（Shader.SetGlobalX）不随场景卸载自动清除，若不复位，
    /// 切场景后 Shader 会继续按上一场景的光源数量采样已销毁的缓冲区，导致材质变黑。
    /// </summary>
    private void ReleaseBuffers()
    {
        if (_lightsBuffer != null)
        {
            _lightsBuffer.Release();
            _lightsBuffer = null;
        }

        if (_spotLightsBuffer != null)
        {
            _spotLightsBuffer.Release();
            _spotLightsBuffer = null;
        }

        ResetGlobalLightState();
    }

    /// <summary>把全局灯光状态复位为“未接管”：关闭安全位、清零计数并解除缓冲区绑定。</summary>
    private static void ResetGlobalLightState()
    {
        // ● 先关闭安全位，确保 Shader 不再进入自定义光照分支
        Shader.SetGlobalFloat(LIGHT_SYSTEM_ACTIVE_PROP, 0f);

        Shader.SetGlobalInt("_CustomPointLightCount", 0);
        Shader.SetGlobalInt("_CustomSpotLightCount", 0);

        // ● 解除全局缓冲区绑定，避免 Shader 持有已释放的原生资源（显式转型以消除重载歧义）
        Shader.SetGlobalBuffer("_CustomPointLights", (GraphicsBuffer)null);
        Shader.SetGlobalBuffer("_CustomSpotLights", (GraphicsBuffer)null);
    }
    
    //● 启用时重新初始化编辑器更新
    private void OnEnable()
    {
        // 安全检查：确保对象仍然有效
        if (this == null) return;

        // ● 订阅场景加载回调：先移除再添加，避免编辑器域重载造成的重复订阅
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        if (!Application.isPlaying)
        {
            // ● 确保在编辑器模式下正确初始化
            if (!_editorUpdateInitialized)
            {
                InitializeEditorUpdate();
            }
            
            // ● 强制在编辑器模式下立即初始化Compute Buffer
            // 检查点光源和聚光灯缓冲区，确保两者都正确初始化
            if ((_lightsBuffer == null && pointLights.Count > 0) || 
                (_spotLightsBuffer == null && spotLights.Count > 0))
            {
                InitializeComputeBuffer();
                UpdateLightsBuffer(); // 立即更新一次
                UpdateSpotLightsBuffer(); // 立即更新聚光灯
            }
            
            Debug.Log("编辑器模式点光灯系统已激活");
        }
    }
    
    //● 对象销毁时清理资源
    //确保GraphicsBuffer被正确释放，防止内存泄漏
    private void OnDestroy()
    {
        // 安全检查：确保对象仍然有效
        if (this == null) return;

        // ● 解除场景加载回调，避免悬挂引用
        SceneManager.sceneLoaded -= OnSceneLoaded;

        ReleaseBuffers();

        // ● 运行期销毁时关闭受控材质上的自定义灯光开关，避免状态残留在共享材质资产上。
        //   编辑器下不执行：脚本重编译同样会触发 OnDestroy，此时复位会干扰正在编辑的场景配置。
        if (Application.isPlaying)
        {
            ReleaseControlledMaterials();
        }

        // ● 清空静态单例引用，避免其他脚本后续访问到已销毁的实例
        if (_instance == this)
        {
            _instance = null;
        }

        // 清理编辑器更新事件
        if (_editorUpdateInitialized)
        {
            UnityEditor.EditorApplication.update -= OnEditorUpdate;
            _editorUpdateInitialized = false;
        }
    }

    /// <summary>
    /// 关闭受控材质上的自定义灯光开关与关键字。
    /// 材质资产是跨场景共享的，销毁前若不还原，其它未配置灯光系统的场景会带着残留关键字渲染。
    /// </summary>
    private void ReleaseControlledMaterials()
    {
        if (targetMaterials == null) return;

        for (int i = 0; i < targetMaterials.Count; i++)
        {
            Material material = targetMaterials[i];
            if (material == null) continue;

            ApplyToggleToMaterial(material, ShaderPropertyIDs.UsePointlight, POINT_LIGHT_KEYWORD, false);
            ApplyToggleToMaterial(material, ShaderPropertyIDs.UseSpotlight, SPOT_LIGHT_KEYWORD, false);
            ApplyToggleToMaterial(material, ShaderPropertyIDs.UseSpotTexture, SPOT_TEXTURE_KEYWORD, false);
        }
    }

    void OnDrawGizmosSelected()
    {
        // ● 在Scene视图中可视化点光源影响范围
        if (!enabled) return;
        
        Gizmos.color = Color.cyan;
        foreach (var light in pointLights)
        {
            if (light != null && light.enabled)
            {
                Gizmos.DrawWireSphere(light.transform.position, light.range);
            }
        }
    }

    //● 实时响应Inspector参数变化
    //当用户在Inspector中修改任何参数时立即更新效果
    private void OnValidate()
    {
        // ● 确保在编辑器模式下
        if (!Application.isPlaying)
        {
            // ● 安全检查：确保对象有效
            if (this == null) return;
            
            // ● 检测用户手动修改强度参数
            // 只有在不是动画更新且参数实际发生变化时才标记为用户手动修改
            if (!_isAnimationUpdate && !Mathf.Approximately(_pointLightIntensity, _lastPointLightIntensity))
            {
                _userModifiedIntensity = true;
            }
            
            // ● 检查回弹动画状态变化，立即启动或停止动画
            if (_enableBounceAnimation != _lastEnableBounceAnimation)
            {
                // 在编辑器模式下，我们使用编辑器更新循环来驱动动画，而不是协程
                // 动画更新完全由 _enableBounceAnimation 控制
                if (_enableBounceAnimation)
                {
                    _animationTime = 0f; // 重置动画时间
                    
                    // ● 如果用户手动修改了参数，取消用户修改标记
                    if (_userModifiedIntensity)
                    {
                        _userModifiedIntensity = false;
                    }
                }
                else
                {
                    // 当动画关闭时，只有在当前值不等于起始值且不是用户手动修改时才重置
                    // 避免在用户手动设置参数时强制重置
                    if (Mathf.Approximately(_pointLightIntensity, _bounceStartIntensity) == false)
                    {
                        // 检查是否是用户手动修改了参数
                        bool userModified = _userModifiedIntensity;
                        
                        // 如果不是用户手动修改，才重置为起始值
                        if (!userModified)
                        {
                            _pointLightIntensity = _bounceStartIntensity;
                            _parametersDirty = true;
                        }
                    }
                }
            }
            
            // ● 检查targetMaterials列表变化，确保_controlledMaterials同步更新
            SyncControlledMaterials();
            
            // ● 立即更新材质参数（添加空值检查）
            try
            {
                UpdateAllMaterials();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"在OnValidate中更新材质时出错: {e.Message}");
            }
            
            // ● 立即更新点光灯数据（添加空值检查）
            try
            {
                if (_lightsBuffer != null)
                {
                    UpdateLightsBuffer();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"在OnValidate中更新点光灯数据时出错: {e.Message}");
            }
            
            // ● 重置参数缓存，确保下次变化检测正确
            CacheCurrentParameters();
            
            // ● 强制刷新Inspector显示
            try
            {
                UnityEditor.EditorUtility.SetDirty(this);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"在OnValidate中设置Dirty时出错: {e.Message}");
            }
        }
    }
    
    //● 同步_controlledMaterials与targetMaterials列表
    //当用户在Inspector中修改targetMaterials列表时，确保_controlledMaterials同步更新
    private void SyncControlledMaterials()
    {
        // ● 首先清理两个列表中的null材质，防止编辑器序列化错误
        _controlledMaterials.RemoveAll(material => material == null);
        targetMaterials.RemoveAll(material => material == null);
        
        // ● 检查是否有材质被从targetMaterials中移除
        List<Material> materialsToRemove = new List<Material>();
        foreach (var material in _controlledMaterials)
        {
            if (material != null && !targetMaterials.Contains(material))
            {
                materialsToRemove.Add(material);
            }
        }
        
        // ● 移除不在targetMaterials中的材质
        foreach (var material in materialsToRemove)
        {
            _controlledMaterials.Remove(material);
            // ● 对于被移除的材质，禁用所有灯光效果
            if (material != null)
            {
                // 禁用点光源 / 聚光灯 / 光斑纹理
                ApplyToggleToMaterial(material, ShaderPropertyIDs.UsePointlight, POINT_LIGHT_KEYWORD, false);
                ApplyToggleToMaterial(material, ShaderPropertyIDs.UseSpotlight, SPOT_LIGHT_KEYWORD, false);
                ApplyToggleToMaterial(material, ShaderPropertyIDs.UseSpotTexture, SPOT_TEXTURE_KEYWORD, false);
            }
        }
        
        // ● 检查是否有新材质被添加到targetMaterials中
        foreach (var material in targetMaterials)
        {
            if (material != null && !_controlledMaterials.Contains(material))
            {
                _controlledMaterials.Add(material);
                // ● 立即应用当前参数设置到新材质
                SetMaterialParameters(material);
            }
        }
    }
    #endif
}
