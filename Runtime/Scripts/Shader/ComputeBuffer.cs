// Compute Buffer 2.0.3 编辑器模式实时更新优化 - 增强Compute Buffer系统与编辑器集成，支持非运行模式下点光效果预览
// Compute Buffer 2.0.2 改进 活动光源数量 显示准确度 _currentLightCount
// Compute Buffer 2.0.1 测试
//ComputeBuffer2.0  自定义点光照明Compute Buffer计算缓冲区方案
using UnityEngine;
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

    [Header("Global Point Light Material Parameters")]
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
    
    // [Header("Point Light Bounce Animation")]
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

    [Header("Global Spot Light Material Parameters")]
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
    
    [Header("Spot Light Texture Parameters")]
    [Tooltip("启用光斑纹理效果")]
    [SerializeField] private bool _useSpotTexture = false;
    
    [Tooltip("光斑纹理对比度")]
    [Range(0.1f, 5)]
    [SerializeField] private float _spotTextureContrast = 1.0f;
    
    [Tooltip("光斑纹理大小")]
    [Range(0.1f, 1)]
    [SerializeField] private float _spotTextureSize = 0.5f;
    
    [Tooltip("光斑纹理强度")]
    [Range(0, 2)]
    [SerializeField] private float _spotTextureIntensity = 1.0f;

    [Header("(PBR_Mobile) Material Management")]
    [Tooltip("在载入新场景时不删除此对象 (启动时)")] public bool dontDestroyOnLoad = false;
    [Tooltip("自动查找场景中使用 PBR_Mobile 材质 (启动时)")]
    public bool autoFindMaterials = false;
    
    [Tooltip("手动指定的材质列表")]
    public List<Material> targetMaterials = new List<Material>();
    
    // ● 材质管理变量，用于放置PBR_Mobile材质，批量控制材质参数
    private List<Material> _controlledMaterials = new List<Material>();
    private const string POINT_LIGHT_KEYWORD = "_USEPOINTLIGHT";
    private const string SPOT_LIGHT_KEYWORD = "_USESPOTLIGHT";
    
    // ● Shader属性ID缓存（性能优化）
    private static class ShaderPropertyIDs
    {
        public static readonly int PointLightIntensity = Shader.PropertyToID("_PointLightIntensity");
        public static readonly int PointLightRangeMultiplier = Shader.PropertyToID("_PointLightRangeMultiplier");
        public static readonly int PointLightFalloff = Shader.PropertyToID("_PointLightFalloff");
        
        public static readonly int SpotLightIntensity = Shader.PropertyToID("_SpotLightIntensity");
        public static readonly int SpotLightRangeMultiplier = Shader.PropertyToID("_SpotLightRangeMultiplier");
        public static readonly int SpotLightFalloff = Shader.PropertyToID("_SpotLightFalloff");
        public static readonly int SpotLightAmount = Shader.PropertyToID("_SpotLightAmount");
        
        // ● 光斑纹理相关属性ID
        public static readonly int SpotTextureContrast = Shader.PropertyToID("_SpotTextureContrast");
        public static readonly int SpotTextureSize = Shader.PropertyToID("_SpotTextureSize");
        public static readonly int SpotTextureIntensity = Shader.PropertyToID("_SpotTextureIntensity");
    }

    // [Header("Point Light Configuration")]
    [Tooltip("拖拽场景中的点光源到此列表")]
    public List<Light> pointLights = new List<Light>();
    
    [Header("Spot Light Configuration")]
    [Tooltip("拖拽场景中的聚光灯到此列表")]
    public List<Light> spotLights = new List<Light>();
    
    // ● 距离剔除参数
    [Header("Distance Culling Settings")]
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
    
    [Header("Performance Settings")]
    [Tooltip("最大支持的点光源数量")]
    [Range(1, 32)] public int maxLights = 8;
    [Tooltip("更新频率 (Hz)")]
    [Range(1, 60)] public int updateFrequency = 16;
    
    
    // ● Compute Buffer相关变量
    private GraphicsBuffer _lightsBuffer;
    private GraphicsBuffer _spotLightsBuffer;
    private CustomPointLight[] _lightsData;
    private CustomSpotLight[] _spotLightsData;
    private float _updateInterval;
    private float _lastUpdateTime;
    private int _currentLightCount = 0;
    private int _currentSpotLightCount = 0;
    
    // ● 参数变更标记 - 用于跟踪哪些参数发生了变化
    private bool _parametersDirty = false;
    
    // ● 编辑器模式更新相关变量
    private bool _editorUpdateInitialized = false;
    private float _lastEditorUpdateTime = 0f;
    private float _editorUpdateInterval = 0.1f; // 编辑器模式下默认更新间隔
    private bool _editorModeActive = false; // 标记编辑器模式是否激活
    
    // ● 存储上一次的参数值，用于检测变化
    private bool _lastUsePointLight;
    private float _lastPointLightIntensity;
    private float _lastLightRangeMultiplier;
    private float _lastLightFalloff;
    
    // ● 聚光灯相关参数缓存
    private bool _lastUseSpotLight;
    private float _lastSpotLightIntensity;
    private float _lastSpotLightRangeMultiplier;
    private float _lastSpotLightFalloff;
    private int _lastSpotLightAmount;
    
    // ● 光斑纹理相关参数缓存
    private bool _lastUseSpotTexture;
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
    
    // ● 性能优化：距离缓存和剔除系统
    private Vector3 _lastCameraPosition;
    private float _cameraMovementThreshold = 0.1f; // 降低摄像机移动阈值，提高响应性
    private Vector3 _lastCameraVelocity;
    private float _cameraSpeedThreshold = 5.0f; // 相机速度阈值，超过此值启用自适应更新
    private Dictionary<Light, float> _lightDistanceCache = new Dictionary<Light, float>();
    private List<int> _activeLightIndices = new List<int>(); // 活跃光源索引缓存
    
    
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
    //所有使用Custom/PBR_Mobile Shader的材质都能访问这些光源数据

    // ReSharper disable Unity.PerformanceAnalysis
    void InitializeComputeBuffer()
    {
        _updateInterval = 1f / updateFrequency;
        _editorUpdateInterval = _updateInterval; // 编辑器模式下使用相同的更新间隔
        
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
        // ● 计算自适应更新间隔
        float adaptiveUpdateInterval = CalculateAdaptiveUpdateInterval();
        
        // ● 频率限制更新Compute Buffer
        if (Time.time - _lastUpdateTime >= adaptiveUpdateInterval)
        {
            UpdateLightsBuffer();
            UpdateSpotLightsBuffer();
            _lastUpdateTime = Time.time;
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
        
        // ● 检查每个参数是否与上一次缓存的值不同
        if (_usePointLight != _lastUsePointLight)
        {
            changed = true;
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
        
        // ● 检查聚光灯相关参数变化
        if (_useSpotLight != _lastUseSpotLight)
        {
            changed = true;
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
        
        // ● 检查光斑纹理参数变化
        if (_useSpotTexture != _lastUseSpotTexture)
        {
            changed = true;
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
        
        // 缓存聚光灯相关参数
        _lastUseSpotLight = _useSpotLight;
        _lastSpotLightIntensity = _spotLightIntensity;
        _lastSpotLightRangeMultiplier = _spotLightRangeMultiplier;
        _lastSpotLightFalloff = _spotLightFalloff;
        _lastSpotLightAmount = _spotLightAmount;
        
        // 缓存光斑纹理相关参数
        _lastUseSpotTexture = _useSpotTexture;
        _lastSpotTextureContrast = _spotTextureContrast;
        _lastSpotTextureSize = _spotTextureSize;
        _lastSpotTextureIntensity = _spotTextureIntensity;
        
        // 缓存回弹动画参数 - 为下一次变化检测提供基准值
        _lastEnableBounceAnimation = _enableBounceAnimation;
        _lastBounceStartIntensity = _bounceStartIntensity;
        _lastBounceTargetIntensity = _bounceTargetIntensity;
        _lastBounceAnimationSpeed = _bounceAnimationSpeed;
    }


    //● 更新点光源数据到Compute Buffer
    //收集场景中所有有效点光源数据，批量上传到GPU
    //性能优化：改进剔除系统、距离缓存和LOD优化

    // ReSharper disable Unity.PerformanceAnalysis
    public int UpdateLightsBuffer()
    {
        // ● 安全检查：确保Compute Buffer已正确初始化
        if (_lightsBuffer == null || _lightsData == null)
        {
            Debug.LogWarning("Compute Buffer未正确初始化，跳过更新");
            return _currentLightCount;
        }
        
        _currentLightCount = 0;
        Camera mainCamera = Camera.main;
        
        // ● 性能优化：检查摄像机移动，减少不必要的更新
        bool cameraMoved = false;
        if (mainCamera)
        {
            Vector3 currentCameraPosition = mainCamera.transform.position;
            cameraMoved = Vector3.Distance(currentCameraPosition, _lastCameraPosition) > _cameraMovementThreshold;
            _lastCameraPosition = currentCameraPosition;
        }
        
        // ● 清空活跃光源索引缓存
        _activeLightIndices.Clear();
        
        // ● 第一步：收集所有通过剔除检查的光源索引
        List<int> validLightIndices = new List<int>();
        for (int i = 0; i < pointLights.Count; i++)
        {
            var light = pointLights[i];
            if (light == null || !light.enabled || light.type != LightType.Point) 
                continue;
            
            Vector3 lightPosition = light.transform.position;
            bool shouldIncludeLight = true;
            
            // ● 应用距离剔除逻辑
            if (mainCamera != null && _enableDistanceCulling)
            {
                float distanceToCamera = Vector3.Distance(lightPosition, mainCamera.transform.position);
                float effectiveRange = light.range * _lightRangeMultiplier;
                float cullDistance = effectiveRange * _distanceCullFactor;
                
                // 严格的距离检查：使用>=确保边界条件正确处理
                // 修复最后一盏灯始终亮着的问题
                if (distanceToCamera >= cullDistance)
                {
                    shouldIncludeLight = false;
                    // 调试信息：记录被距离剔除的光源
                    // #if UNITY_EDITOR
                    // if (UnityEditor.Selection.activeGameObject == this.gameObject)
                    // {
                    //     Debug.Log($"距离剔除光源: {light.name}, 距离: {distanceToCamera:F2}, 剔除距离: {cullDistance:F2}");
                    // }
                    // #endif
                }
                
                // 缓存距离信息用于排序
                _lightDistanceCache[light] = distanceToCamera;
            }
            
            // ● 应用视锥体剔除逻辑
            if (mainCamera && _enableFrustumCulling && shouldIncludeLight)
            {
                // 检查光源是否在相机视锥体外
                if (!IsLightInFrustum(mainCamera, lightPosition, light.range * _lightRangeMultiplier))
                {
                    shouldIncludeLight = false;
                }
            }
            
            // ● 如果光源通过剔除检查，添加到有效列表
            if (shouldIncludeLight)
            {
                validLightIndices.Add(i);
            }
        }
        
        // ● 第二步：按距离排序有效光源（只在有多个光源时进行）
        if (validLightIndices.Count > 1 && mainCamera != null)
        {
            Vector3 cameraPosition = mainCamera.transform.position;
            
            // 使用优化的排序算法对有效光源索引进行排序
            validLightIndices.Sort((a, b) => 
            {
                float distA = Vector3.Distance(pointLights[a].transform.position, cameraPosition);
                float distB = Vector3.Distance(pointLights[b].transform.position, cameraPosition);
                return distA.CompareTo(distB);
            });
        }
        
        // ● 第三步：填充数据到_lightsData数组，不超过maxLights限制
        for (int i = 0; i < validLightIndices.Count && _currentLightCount < maxLights; i++)
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
            
            _activeLightIndices.Add(lightIndex);
            
            _lightsData[_currentLightCount] = new CustomPointLight
            {
                position = light.transform.position,
                range = light.range,
                color = new Vector4(light.color.r, light.color.g, light.color.b, light.intensity),
                parameters = new Vector4(_lightFalloff, 0, 0, 0) // 使用配置的falloff参数
            };
            
            _currentLightCount++;
        }
        
        // ● 如果达到最大光源数量且有更多有效光源被跳过，记录警告
        // if (validLightIndices.Count > maxLights)
        // {
        //     Debug.Log($"已达到最大光源数量限制({maxLights})，跳过了 {validLightIndices.Count - maxLights} 个有效光源");
        // }
        
        // ● 更新Compute Buffer数据到GPU
        if (_currentLightCount > 0)
        {
            _lightsBuffer.SetData(_lightsData, 0, 0, _currentLightCount);
        }
        else
        {
            // 如果没有有效光源，确保Shader知道光源数量为0
            Shader.SetGlobalInt("_CustomPointLightCount", 0);
            return 0;
        }
        
        // ● 更新全局光源数量，Shader根据这个值决定循环次数
        Shader.SetGlobalInt("_CustomPointLightCount", _currentLightCount);
        return _currentLightCount;
    }

    //● 更新聚光灯数据到Compute Buffer
    //收集场景中所有有效聚光灯数据，批量上传到GPU
    //性能优化：改进剔除系统、距离缓存和LOD优化

    public int UpdateSpotLightsBuffer()
    {
        // ● 安全检查：确保Compute Buffer已正确初始化
        if (_spotLightsBuffer == null || _spotLightsData == null)
        {
            Debug.LogWarning("SpotLight Compute Buffer未正确初始化，跳过更新");
            return _currentSpotLightCount;
        }
        
        _currentSpotLightCount = 0;
        Camera mainCamera = Camera.main;
        
        // ● 清空活跃光源索引缓存
        List<int> validSpotLightIndices = new List<int>();
        
        // ● 第一步：收集所有通过剔除检查的聚光灯索引
        for (int i = 0; i < spotLights.Count; i++)
        {
            var light = spotLights[i];
            if (light == null || !light.enabled || light.type != LightType.Spot) 
                continue;
            
            Vector3 lightPosition = light.transform.position;
            bool shouldIncludeLight = true;
            
            // ● 应用距离剔除逻辑
            if (mainCamera != null && _enableDistanceCulling)
            {
                float distanceToCamera = Vector3.Distance(lightPosition, mainCamera.transform.position);
                float effectiveRange = light.range * _spotLightRangeMultiplier;
                float cullDistance = effectiveRange * _distanceCullFactor;
                
                // 严格的距离检查：使用>=确保边界条件正确处理
                if (distanceToCamera >= cullDistance)
                {
                    shouldIncludeLight = false;
                }
                
                // 缓存距离信息用于排序
                _lightDistanceCache[light] = distanceToCamera;
            }
            
            // ● 应用视锥体剔除逻辑
            if (mainCamera && _enableFrustumCulling && shouldIncludeLight)
            {
                // 检查光源是否在相机视锥体外
                if (!IsLightInFrustum(mainCamera, lightPosition, light.range * _spotLightRangeMultiplier))
                {
                    shouldIncludeLight = false;
                }
            }
            
            // ● 如果光源通过剔除检查，添加到有效列表
            if (shouldIncludeLight)
            {
                validSpotLightIndices.Add(i);
            }
        }
        
        // ● 第二步：按距离排序有效聚光灯（只在有多个光源时进行）
        if (validSpotLightIndices.Count > 1 && mainCamera)
        {
            Vector3 cameraPosition = mainCamera.transform.position;
            
            // 使用优化的排序算法对有效聚光灯索引进行排序
            validSpotLightIndices.Sort((a, b) => 
            {
                float distA = Vector3.Distance(spotLights[a].transform.position, cameraPosition);
                float distB = Vector3.Distance(spotLights[b].transform.position, cameraPosition);
                return distA.CompareTo(distB);
            });
        }
        
        // ● 第三步：填充数据到_spotLightsData数组，不超过_spotLightAmount限制
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
        
        // ● 如果达到最大聚光灯数量且有更多有效光源被跳过，记录警告
        // if (validSpotLightIndices.Count > maxSpotLights)
        // {
        //     Debug.Log($"已达到最大聚光灯数量限制({maxSpotLights})，跳过了 {validSpotLightIndices.Count - maxSpotLights} 个有效聚光灯");
        // }
        
        // ● 更新Compute Buffer数据到GPU
        if (_currentSpotLightCount > 0)
        {
            _spotLightsBuffer.SetData(_spotLightsData, 0, 0, _currentSpotLightCount);
        }
        else
        {
            // 如果没有有效聚光灯，确保Shader知道聚光灯数量为0
            Shader.SetGlobalInt("_CustomSpotLightCount", 0);
            return 0;
        }
        
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
    
    // ● 计算自适应更新间隔 - 根据相机移动速度和场景复杂度动态调整更新频率
    // 性能优化：当相机移动缓慢或场景变化不大时，降低更新频率以节省性能
    // 当相机快速移动或场景变化剧烈时，提高更新频率以保证视觉效果
    private float CalculateAdaptiveUpdateInterval()
    {
        Camera mainCamera = Camera.main;
        
        // ● 基础更新间隔
        float baseUpdateInterval = _updateInterval;
        
        // ● 如果没有主相机，返回基础间隔
        if (mainCamera == null)
        {
            return baseUpdateInterval;
        }
        
        // ● 计算相机移动速度
        Vector3 currentCameraPosition = mainCamera.transform.position;
        Vector3 currentCameraVelocity = (currentCameraPosition - _lastCameraPosition) / Time.deltaTime;
        float cameraSpeed = currentCameraVelocity.magnitude;
        
        // ● 计算相机加速度（速度变化率）
        Vector3 cameraAcceleration = (currentCameraVelocity - _lastCameraVelocity) / Time.deltaTime;
        float accelerationMagnitude = cameraAcceleration.magnitude;
        
        // ● 缓存当前速度和位置用于下一次计算
        _lastCameraVelocity = currentCameraVelocity;
        _lastCameraPosition = currentCameraPosition;
        
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
        
        // ● 计算最终的自适应更新间隔
        float adaptiveUpdateInterval = baseUpdateInterval * adaptiveFactor;
        
        // ● 调试信息
        // #if UNITY_EDITOR
        // if (UnityEditor.Selection.activeGameObject == this.gameObject && Time.frameCount % 60 == 0)
        // {
        //     Debug.Log($"自适应更新间隔: {adaptiveUpdateInterval:F4}s (基础: {baseUpdateInterval:F4}s, 因子: {adaptiveFactor:F2})");
        // }
        // #endif
        
        return adaptiveUpdateInterval;
    }
    
    // ● 光源LOD级别枚举
    private enum LightLODLevel
    {
        None = 0,      // 不渲染
        Low = 1,       // 简化计算
        Medium = 2,    // 中等计算
        High = 3       // 完整计算
    }
    
    // ● 计算光源LOD级别
    private LightLODLevel CalculateLightLOD(float distance, float range)
    {
        float distanceRatio = distance / (range * _lightRangeMultiplier);
        
        if (distanceRatio > 1.5f) return LightLODLevel.None;      // 太远，不渲染
        else if (distanceRatio > 1.0f) return LightLODLevel.Low;  // 远距离，简化计算
        else if (distanceRatio > 0.5f) return LightLODLevel.Medium; // 中等距离
        else return LightLODLevel.High;                           // 近距离，完整计算
    }
    
    // ● 优化的光源排序算法
    private void OptimizedLightSort(Vector3 cameraPosition)
    {
        // 使用插入排序对活跃光源进行排序（对小数组更高效）
        for (int i = 1; i < _currentLightCount; i++)
        {
            CustomPointLight currentLight = _lightsData[i];
            float currentDistance = Vector3.Distance(currentLight.position, cameraPosition);
            
            int j = i - 1;
            while (j >= 0 && Vector3.Distance(_lightsData[j].position, cameraPosition) > currentDistance)
            {
                _lightsData[j + 1] = _lightsData[j];
                j--;
            }
            _lightsData[j + 1] = currentLight;
        }
    }

    // ==========================================
    // ● 材质批量控制功能
    // ==========================================

    //● 自动查找场景中使用PBR_Mobile Shader的所有材质，遍历所有Renderer组件，收集其使用的材质
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
                if (material != null && material.shader.name == "Custom/PBR_Mobile")
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
        Debug.Log($"找到 {_controlledMaterials.Count} 个使用PBR_Mobile Shader的材质，已添加到Target Materials列表");
    }


    //● 批量更新所有受控材质的点光源参数
    //设置Shader关键字和浮点参数
    [ContextMenu("更新所有材质参数")]
    public void UpdateAllMaterials()
    {
        // ● 安全检查：清理null材质引用
        _controlledMaterials.RemoveAll(material => material == null);
        
        if (_controlledMaterials.Count == 0)
        {
            Debug.LogWarning("没有找到需要更新的材质");
            return;
        }

        int updatedCount = 0;
        
        foreach (Material material in _controlledMaterials)
        {
            if (material == null) continue;
            
            try
            {
                // ● 设置Shader关键字（对应Properties中的Toggle）
                if (_usePointLight)
                {
                    material.EnableKeyword(POINT_LIGHT_KEYWORD);
                }
                else
                {
                    material.DisableKeyword(POINT_LIGHT_KEYWORD);
                }
                
                // ● 设置SpotLight关键字
                if (_useSpotLight)
                {
                    material.EnableKeyword(SPOT_LIGHT_KEYWORD);
                }
                else
                {
                    material.DisableKeyword(SPOT_LIGHT_KEYWORD);
                }
                
                // ● 设置SpotLight Texture关键字
                if (_useSpotTexture)
                {
                    material.EnableKeyword("_USESPOTTEXTURE");
                }
                else
                {
                    material.DisableKeyword("_USESPOTTEXTURE");
                }
                
                // ● 设置浮点参数
                material.SetFloat(ShaderPropertyIDs.PointLightIntensity, _pointLightIntensity);
                material.SetFloat(ShaderPropertyIDs.PointLightRangeMultiplier, _lightRangeMultiplier);
                material.SetFloat(ShaderPropertyIDs.PointLightFalloff, _lightFalloff);
                
                // ● 设置SpotLight浮点参数
                material.SetFloat(ShaderPropertyIDs.SpotLightIntensity, _spotLightIntensity);
                material.SetFloat(ShaderPropertyIDs.SpotLightRangeMultiplier, _spotLightRangeMultiplier);
                material.SetFloat(ShaderPropertyIDs.SpotLightFalloff, _spotLightFalloff);
                material.SetFloat(ShaderPropertyIDs.SpotLightAmount, _spotLightAmount);
                
                // ● 设置光斑纹理浮点参数
                material.SetFloat(ShaderPropertyIDs.SpotTextureContrast, _spotTextureContrast);
                material.SetFloat(ShaderPropertyIDs.SpotTextureSize, _spotTextureSize);
                material.SetFloat(ShaderPropertyIDs.SpotTextureIntensity, _spotTextureIntensity);
                
                updatedCount++;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"更新材质 {material.name} 时出错: {e.Message}");
            }
        }
        
        // ● 只在有材质被更新时才打印日志，避免频繁打印
        // 这个日志现在只在手动调用或参数实际变化时打印一次
    }


    //● 为单个材质设置参数
    //用于精确控制特定材质

    public void SetMaterialParameters(Material material)
    {
        if (material == null) return;
        
        // ● 设置关键字状态
        if (_usePointLight)
        {
            material.EnableKeyword(POINT_LIGHT_KEYWORD);
        }
        else
        {
            material.DisableKeyword(POINT_LIGHT_KEYWORD);
        }
        
        // ● 设置SpotLight关键字状态
        if (_useSpotLight)
        {
            material.EnableKeyword(SPOT_LIGHT_KEYWORD);
        }
        else
        {
            material.DisableKeyword(SPOT_LIGHT_KEYWORD);
        }
        
        // ● 设置SpotLight Texture关键字状态
        if (_useSpotTexture)
        {
            material.EnableKeyword("_USESPOTTEXTURE");
        }
        else
        {
            material.DisableKeyword("_USESPOTTEXTURE");
        }
        
        // ● 设置数值参数
        material.SetFloat(ShaderPropertyIDs.PointLightIntensity, _pointLightIntensity);
        material.SetFloat(ShaderPropertyIDs.PointLightRangeMultiplier, _lightRangeMultiplier);
        material.SetFloat(ShaderPropertyIDs.PointLightFalloff, _lightFalloff);
        
        // ● 设置SpotLight数值参数
        material.SetFloat(ShaderPropertyIDs.SpotLightIntensity, _spotLightIntensity);
        material.SetFloat(ShaderPropertyIDs.SpotLightRangeMultiplier, _spotLightRangeMultiplier);
        material.SetFloat(ShaderPropertyIDs.SpotLightFalloff, _spotLightFalloff);
        material.SetFloat(ShaderPropertyIDs.SpotLightAmount, _spotLightAmount);
        
        // ● 设置光斑纹理数值参数
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
        }
    }

    //● 获取点光照开关状态
    public bool GetPointLightEnabled()
    {
        return _usePointLight;
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


    //● 重置所有材质到默认值
    [ContextMenu("重置材质到默认值")]
    public void ResetMaterialToDefaults()
    {
        // ● 检查是否有参数与默认值不同
        bool changed = false;
        
        // if (_usePointLight != false)
        // {
        //     _usePointLight = false;
        //     changed = true;
        // }
        
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
        bool goingUp = true;
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
    //在编辑器模式下查找场景中使用PBR_Mobile Shader的所有材质
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
                    if (material != null && material.shader.name == "Custom/PBR_Mobile")
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
        
        Debug.Log($"编辑器模式下找到 {materialCount} 个使用PBR_Mobile Shader的材质，已添加到Target Materials列表");
        
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
        
        if (_editorUpdateInitialized)
        {
            UnityEditor.EditorApplication.update -= OnEditorUpdate;
            _editorUpdateInitialized = false;
        }
        
        // ● 释放GraphicsBuffer资源，防止内存泄漏
        if (_lightsBuffer != null)
        {
            _lightsBuffer.Release();
            _lightsBuffer = null;
        }
        
        // 同时释放聚光灯缓冲区
        if (_spotLightsBuffer != null)
        {
            _spotLightsBuffer.Release();
            _spotLightsBuffer = null;
        }
    }
    
    //● 启用时重新初始化编辑器更新
    private void OnEnable()
    {
        // 安全检查：确保对象仍然有效
        if (this == null) return;
        
        if (!Application.isPlaying)
        {
            // ● 确保在编辑器模式下正确初始化
            if (!_editorUpdateInitialized)
            {
                InitializeEditorUpdate();
            }
            
            // ● 强制在编辑器模式下立即初始化Compute Buffer
            if (_lightsBuffer == null && pointLights.Count > 0)
            {
                InitializeComputeBuffer();
                UpdateLightsBuffer(); // 立即更新一次
            }
            
            // ● 标记编辑器模式为激活状态
            _editorModeActive = true;
            Debug.Log("编辑器模式点光灯系统已激活");
        }
    }
    
    //● 对象销毁时清理资源
    //确保GraphicsBuffer被正确释放，防止内存泄漏
    private void OnDestroy()
    {
        // 安全检查：确保对象仍然有效
        if (this == null) return;
        
        // ● 释放GraphicsBuffer资源，防止内存泄漏
        if (_lightsBuffer != null)
        {
            _lightsBuffer.Release();
            _lightsBuffer = null;
        }
        
        // 同时释放聚光灯缓冲区
        if (_spotLightsBuffer != null)
        {
            _spotLightsBuffer.Release();
            _spotLightsBuffer = null;
        }
        
        // 清理编辑器更新事件
        if (_editorUpdateInitialized)
        {
            UnityEditor.EditorApplication.update -= OnEditorUpdate;
            _editorUpdateInitialized = false;
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
        // ● 检查是否需要同步
        bool needsSync = false;
        
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
                needsSync = true;
            }
        }
        
        // ● 移除不在targetMaterials中的材质
        foreach (var material in materialsToRemove)
        {
            _controlledMaterials.Remove(material);
            // ● 对于被移除的材质，禁用点光源效果
            if (material != null)
            {
                material.DisableKeyword(POINT_LIGHT_KEYWORD);
            }
        }
        
        // ● 检查是否有新材质被添加到targetMaterials中
        foreach (var material in targetMaterials)
        {
            if (material != null && !_controlledMaterials.Contains(material))
            {
                _controlledMaterials.Add(material);
                needsSync = true;
                // ● 立即应用当前参数设置到新材质
                SetMaterialParameters(material);
            }
        }
    }
    #endif
}

// ● 光源距离比较器，用于排序
internal class LightDistanceComparer : System.Collections.IComparer
{
    private Vector3 cameraPosition;
    
    public LightDistanceComparer(Vector3 cameraPosition)
    {
        this.cameraPosition = cameraPosition;
    }
    
    public int Compare(object x, object y)
    {
        ComputeBufferLightManager.CustomPointLight a = (ComputeBufferLightManager.CustomPointLight)x;
        ComputeBufferLightManager.CustomPointLight b = (ComputeBufferLightManager.CustomPointLight)y;
        float distA = Vector3.Distance(a.position, cameraPosition);
        float distB = Vector3.Distance(b.position, cameraPosition);
        return distA.CompareTo(distB);
    }
}
