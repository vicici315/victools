using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 圆锥形风力控制器
/// 用于控制吹风机对毛发的影响效果
/// 可以附加到吹风机模型上，自动设置圆锥形风力参数
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class WindConeController : MonoBehaviour
{
    [Header("圆锥形风力参数")]
    [Tooltip("启用圆锥形风力影响")]
    public bool enableWindCone = true;
    
    [Tooltip("圆锥中心位置偏移（相对于吹风机位置）")]
    public Vector3 positionOffset = Vector3.zero;
    
    [Tooltip("圆锥方向（使用物体的前向方向）")]
    public bool useForwardDirection = true;
    
    [Tooltip("自定义圆锥方向（当useForwardDirection为false时使用）")]
    public Vector3 customDirection = Vector3.forward;
    
    [Tooltip("圆锥角度（度）")]
    [Range(0.0f, 90.0f)]
    public float coneAngle = 30.0f;
    
    [Tooltip("圆锥范围")]
    public float coneRange = 5.0f;
    
    [Tooltip("圆锥强度倍增")]
    public float coneIntensity = 2.0f;
    
    [Tooltip("圆锥内风频率加大值")]
    [Range(0.0f, 10.0f)]
    public float frequencyBoost = 2.0f;
    
    [Header("缓冲过渡参数")]
    [Tooltip("启用缓冲过渡，平滑风源移动")]
    public bool enableSmoothing = true;
    
    [Tooltip("位置缓冲速度（值越大过渡越快）")]
    [Range(0.1f, 20.0f)]
    public float positionSmoothSpeed = 5.0f;
    
    [Tooltip("方向缓冲速度（值越大过渡越快）")]
    [Range(0.1f, 20.0f)]
    public float directionSmoothSpeed = 8.0f;
    
    [Tooltip("强度缓冲速度（值越大过渡越快）")]
    [Range(0.1f, 20.0f)]
    public float intensitySmoothSpeed = 10.0f;
    
[Header("目标毛发渲染器")]
[Tooltip("目标毛发渲染器（如果为空，将查找场景中所有使用FurShell材质的渲染器）")]
public Renderer targetFurRenderer;

[Tooltip("影响所有使用FurShell材质的渲染器")]
public bool affectAllFurRenderers = true;

[Header("动画控制")]
[Tooltip("启用动画暂停/继续功能")]
public bool enableAnimationControl = true;

[Tooltip("检测半径（用于查找范围内的动画模型）")]
public float detectionRadius = 10.0f;

[Tooltip("检测间隔（秒）")]
[Range(0.1f, 2.0f)]
public float detectionInterval = 0.5f;

[Tooltip("动画暂停速度（0=完全暂停，1=正常播放）")]
[Range(0.0f, 1.0f)]
public float pauseAnimationSpeed = 0.0f;

[Tooltip("离开范围后恢复的动画速度")]
[Range(0.0f, 2.0f)]
public float resumeAnimationSpeed = 1.0f;
    
    [Header("调试")]
    [Tooltip("在Scene视图中显示圆锥范围")]
    public bool showGizmos = true;
    
    [Tooltip("圆锥Gizmos颜色")]
    public Color gizmoColor = new Color(0.2f, 0.8f, 1.0f, 0.3f);
    
    [Tooltip("在Scene视图中显示检测范围")]
    public bool showDetectionRadius = true;
    
    [Tooltip("检测范围Gizmos颜色")]
    public Color detectionRadiusColor = new Color(1.0f, 0.5f, 0.0f, 0.2f);
    
    // 私有变量
    private Renderer[] targetRenderers;
    private MaterialPropertyBlock propertyBlock;
    
    // 缓冲变量
    private Vector3 smoothedConePosition;
    private Vector3 smoothedConeDirection;
    private float smoothedConeIntensity;
    
    // 动画控制相关变量
    private float detectionTimer = 0.0f;
    private System.Collections.Generic.List<Animator> animatorsInRange = new System.Collections.Generic.List<Animator>();
    private System.Collections.Generic.Dictionary<Animator, float> originalAnimationSpeeds = new System.Collections.Generic.Dictionary<Animator, float>();
    
    // 缓存优化变量
    private static Renderer[] cachedAllFurRenderers;
    private static float lastFurRendererCacheTime = 0f;
    private static readonly float furRendererCacheInterval = 5f; // 每5秒重新缓存一次
    private static Animator[] cachedAllAnimators;
    private static float lastAnimatorCacheTime = 0f;
    private static readonly float animatorCacheInterval = 2f; // 每2秒重新缓存一次
    private bool needRefreshFurCache = true;
    private bool needRefreshAnimatorCache = true;
    
    // 着色器属性ID（缓存以提高性能）
    private static readonly int UseWindConeID = Shader.PropertyToID("_UseWindCone");
    private static readonly int WindConePositionID = Shader.PropertyToID("_WindConePosition");
    private static readonly int WindConeDirectionID = Shader.PropertyToID("_WindConeDirection");
    private static readonly int WindConeAngleID = Shader.PropertyToID("_WindConeAngle");
    private static readonly int WindConeRangeID = Shader.PropertyToID("_WindConeRange");
    private static readonly int WindConeFrequencyBoostID = Shader.PropertyToID("_WindConeFrequencyBoost");
    
    void Start()
    {
        Initialize();
    }
    
    void OnEnable()
    {
        Initialize();
    }
    
    void OnDisable()
    {
        // 只在播放模式下禁用时关闭圆锥影响
        if (Application.isPlaying)
        {
            SetWindConeEnabled(false);
            
            // 恢复所有动画器
            if (enableAnimationControl)
            {
                DisableAnimationControl();
            }
        }
    }
    
    void OnDestroy()
    {
        // 清理资源
        targetRenderers = null;
        propertyBlock = null;
    }
    
    void Update()
    {
        UpdateWindConeParameters();
        
        // 更新动画控制检测
        if (enableAnimationControl && Application.isPlaying)
        {
            UpdateAnimationControl();
        }
    }
    
    /// <summary>
    /// 初始化控制器
    /// </summary>
    private void Initialize()
    {
        // 创建MaterialPropertyBlock用于高效设置材质属性
        propertyBlock = new MaterialPropertyBlock();
        
        // 查找目标渲染器
        FindTargetRenderers();
        
        // 初始化参数
        UpdateWindConeParameters();
    }
    
    /// <summary>
    /// 查找目标渲染器（使用缓存优化）
    /// </summary>
    private void FindTargetRenderers()
    {
        if (affectAllFurRenderers)
        {
            // 使用缓存获取所有毛发渲染器
            targetRenderers = GetCachedFurRenderers();
            
            if (targetRenderers.Length == 0)
            {
                Debug.LogWarning("WindConeController: 未找到使用FurShell材质的渲染器");
            }
        }
        else if (targetFurRenderer != null)
        {
            targetRenderers = new Renderer[] { targetFurRenderer };
        }
        else
        {
            // 如果没有指定目标，尝试查找当前物体或子物体中的渲染器
            targetRenderers = GetComponentsInChildren<Renderer>();
            
            if (targetRenderers.Length == 0)
            {
                Debug.LogWarning("WindConeController: 未找到目标渲染器");
            }
        }
    }
    
    /// <summary>
    /// 获取缓存的毛发渲染器（减少FindObjectsOfType调用）
    /// </summary>
    private Renderer[] GetCachedFurRenderers()
    {
        // 检查是否需要刷新缓存
        bool shouldRefreshCache = needRefreshFurCache || 
                                 cachedAllFurRenderers == null || 
                                 Time.time - lastFurRendererCacheTime > furRendererCacheInterval;
        
        if (shouldRefreshCache)
        {
            // 查找场景中所有渲染器
            Renderer[] allRenderers = FindObjectsOfType<Renderer>();
            System.Collections.Generic.List<Renderer> furRenderers = new System.Collections.Generic.List<Renderer>();
            
            foreach (Renderer renderer in allRenderers)
            {
                if (renderer.sharedMaterial != null && 
                    renderer.sharedMaterial.shader.name.Contains("FurShell"))
                {
                    furRenderers.Add(renderer);
                }
            }
            
            cachedAllFurRenderers = furRenderers.ToArray();
            lastFurRendererCacheTime = Time.time;
            needRefreshFurCache = false;
            
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"WindConeController: 缓存了 {cachedAllFurRenderers.Length} 个毛发渲染器");
            #endif
        }
        
        return cachedAllFurRenderers;
    }
    
    /// <summary>
    /// 获取缓存的动画器（减少FindObjectsOfType调用）
    /// </summary>
    private Animator[] GetCachedAnimators()
    {
        // 检查是否需要刷新缓存
        bool shouldRefreshCache = needRefreshAnimatorCache || 
                                 cachedAllAnimators == null || 
                                 Time.time - lastAnimatorCacheTime > animatorCacheInterval;
        
        if (shouldRefreshCache)
        {
            cachedAllAnimators = FindObjectsOfType<Animator>();
            lastAnimatorCacheTime = Time.time;
            needRefreshAnimatorCache = false;
            
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"WindConeController: 缓存了 {cachedAllAnimators.Length} 个动画器");
            #endif
        }
        
        return cachedAllAnimators;
    }
    
    /// <summary>
    /// 标记需要刷新毛发渲染器缓存
    /// </summary>
    public void MarkFurCacheDirty()
    {
        needRefreshFurCache = true;
    }
    
    /// <summary>
    /// 标记需要刷新动画器缓存
    /// </summary>
    public void MarkAnimatorCacheDirty()
    {
        needRefreshAnimatorCache = true;
    }
    
    /// <summary>
    /// 更新圆锥形风力参数
    /// </summary>
    private void UpdateWindConeParameters()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            return;
        
        // 计算目标圆锥位置和方向
        Vector3 targetConePosition = transform.position + transform.TransformDirection(positionOffset);
        Vector3 targetConeDirection = useForwardDirection ? transform.forward : customDirection.normalized;
        
        // 初始化缓冲变量（如果是第一次调用）
        if (smoothedConePosition == Vector3.zero && targetConePosition != Vector3.zero)
        {
            smoothedConePosition = targetConePosition;
            smoothedConeDirection = targetConeDirection;
            smoothedConeIntensity = coneIntensity;
        }
        
        // 应用缓冲过渡
        if (enableSmoothing && Application.isPlaying)
        {
            // 使用Lerp平滑过渡位置
            smoothedConePosition = Vector3.Lerp(
                smoothedConePosition, 
                targetConePosition, 
                positionSmoothSpeed * Time.deltaTime
            );
            
            // 使用Lerp平滑过渡方向（使用Slerp处理方向向量）
            smoothedConeDirection = Vector3.Slerp(
                smoothedConeDirection, 
                targetConeDirection, 
                directionSmoothSpeed * Time.deltaTime
            ).normalized;
            
            // 使用Lerp平滑过渡强度
            smoothedConeIntensity = Mathf.Lerp(
                smoothedConeIntensity, 
                coneIntensity, 
                intensitySmoothSpeed * Time.deltaTime
            );
        }
        else
        {
            // 不启用缓冲或不在播放模式，直接使用目标值
            smoothedConePosition = targetConePosition;
            smoothedConeDirection = targetConeDirection;
            smoothedConeIntensity = coneIntensity;
        }
        
        // 更新所有目标渲染器
        foreach (Renderer renderer in targetRenderers)
        {
            if (renderer == null)
                continue;
                
            try
            {
                // 获取当前的MaterialPropertyBlock
                renderer.GetPropertyBlock(propertyBlock);
                
                // 设置圆锥形风力参数
                propertyBlock.SetFloat(UseWindConeID, enableWindCone ? 1.0f : 0.0f);
                
                // _WindConePosition: xyz为圆锥中心位置, w为强度倍增
                Vector4 conePosWithIntensity = new Vector4(
                    smoothedConePosition.x, 
                    smoothedConePosition.y, 
                    smoothedConePosition.z, 
                    smoothedConeIntensity
                );
                propertyBlock.SetVector(WindConePositionID, conePosWithIntensity);
                
                // _WindConeDirection: xyz为圆锥方向, w未使用
                Vector4 coneDir = new Vector4(
                    smoothedConeDirection.x,
                    smoothedConeDirection.y,
                    smoothedConeDirection.z,
                    0.0f
                );
                propertyBlock.SetVector(WindConeDirectionID, coneDir);
                
                propertyBlock.SetFloat(WindConeAngleID, coneAngle);
                propertyBlock.SetFloat(WindConeRangeID, coneRange);
                propertyBlock.SetFloat(WindConeFrequencyBoostID, frequencyBoost);
                
                // 应用属性
                renderer.SetPropertyBlock(propertyBlock);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"WindConeController: 更新渲染器 {renderer.name} 时发生错误: {e.Message}");
            }
        }
    }
    
    /// <summary>
    /// 更新动画控制
    /// </summary>
    private void UpdateAnimationControl()
    {
        detectionTimer -= Time.deltaTime;
        
        if (detectionTimer <= 0.0f)
        {
            detectionTimer = detectionInterval;
            DetectAnimatorsInCone();
        }
    }
    
    /// <summary>
    /// 检测圆锥范围内的动画器（使用缓存优化）
    /// </summary>
    private void DetectAnimatorsInCone()
    {
        // 计算当前圆锥位置和方向
        Vector3 currentConePosition = smoothedConePosition;
        Vector3 currentConeDirection = smoothedConeDirection;
        
        // 使用缓存获取所有动画器
        Animator[] allAnimators = GetCachedAnimators();
        System.Collections.Generic.List<Animator> currentAnimators = new System.Collections.Generic.List<Animator>();
        
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"WindConeController: 场景中找到 {allAnimators.Length} 个动画器（使用缓存）");
        #endif
        
        foreach (Animator animator in allAnimators)
        {
            if (animator == null || !animator.enabled || !animator.gameObject.activeInHierarchy)
                continue;
            
            // 方法1：使用Animator的根位置（对于角色动画更准确）
            Vector3 animatorPosition = animator.transform.position;
            
            // 方法2：尝试获取更精确的位置（对于蒙皮网格渲染器）
            SkinnedMeshRenderer skinnedRenderer = animator.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinnedRenderer != null && skinnedRenderer.enabled)
            {
                // 使用蒙皮网格渲染器的边界中心作为位置
                animatorPosition = skinnedRenderer.bounds.center;
                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"WindConeController: 动画器 {animator.name} 使用蒙皮网格边界中心 (位置: {animatorPosition})");
                #endif
            }
            
            // 检查距离
            float distance = Vector3.Distance(animatorPosition, currentConePosition);
            if (distance <= detectionRadius)
            {
                // 检查是否在圆锥范围内
                bool inCone = IsPointInCone(animatorPosition, currentConePosition, currentConeDirection, coneAngle, coneRange);
                
                if (inCone)
                {
                    currentAnimators.Add(animator);
                    #if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"WindConeController: 动画器 {animator.name} 在圆锥范围内 (距离: {distance:F2}, 位置: {animatorPosition})");
                    #endif
                }
            }
        }
        
        // 处理新进入范围的动画器
        foreach (Animator animator in currentAnimators)
        {
            if (!animatorsInRange.Contains(animator))
            {
                // 新进入范围，暂停动画
                PauseAnimator(animator);
                animatorsInRange.Add(animator);
            }
        }
        
        // 处理离开范围的动画器
        for (int i = animatorsInRange.Count - 1; i >= 0; i--)
        {
            Animator animator = animatorsInRange[i];
            
            if (!currentAnimators.Contains(animator))
            {
                // 离开范围，恢复动画
                ResumeAnimator(animator);
                animatorsInRange.RemoveAt(i);
            }
        }
        
        // 调试信息：显示当前在范围内的动画器数量
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (currentAnimators.Count > 0)
        {
            Debug.Log($"WindConeController: 当前有 {currentAnimators.Count} 个动画器在圆锥范围内");
        }
        else if (allAnimators.Length > 0)
        {
            Debug.Log($"WindConeController: 没有动画器在圆锥范围内。最近动画器距离: {GetNearestAnimatorDistance(allAnimators, currentConePosition):F2}");
        }
        #endif
    }
    
    /// <summary>
    /// 获取最近动画器的距离（用于调试）
    /// </summary>
    private float GetNearestAnimatorDistance(Animator[] animators, Vector3 conePosition)
    {
        if (animators == null || animators.Length == 0)
            return float.MaxValue;
            
        float minDistance = float.MaxValue;
        bool foundValidAnimator = false;
        
        foreach (Animator animator in animators)
        {
            if (animator == null || !animator.enabled || animator.transform == null)
                continue;
                
            float distance = Vector3.Distance(animator.transform.position, conePosition);
            if (distance < minDistance)
            {
                minDistance = distance;
                foundValidAnimator = true;
            }
        }
        
        return foundValidAnimator ? minDistance : float.MaxValue;
    }
    
    /// <summary>
    /// 检查点是否在圆锥范围内
    /// </summary>
    private bool IsPointInCone(Vector3 point, Vector3 conePosition, Vector3 coneDirection, float coneAngle, float coneRange)
    {
        // 计算点到圆锥顶点的向量
        Vector3 pointToCone = point - conePosition;
        float distanceToCone = pointToCone.magnitude;
        
        // 如果距离超过范围，不在圆锥内
        if (distanceToCone > coneRange)
            return false;
        
        // 计算点与圆锥方向的夹角
        float angle = Vector3.Angle(coneDirection, pointToCone.normalized);
        
        // 如果夹角小于圆锥角度的一半，则在圆锥内
        return angle <= coneAngle * 0.5f;
    }
    
    // ReSharper disable Unity.PerformanceAnalysis
    /// <summary>
    /// 暂停动画器
    /// </summary>
    private void PauseAnimator(Animator animator)
    {
        if (!animator)
            return;
            
        // 保存原始速度
        if (!originalAnimationSpeeds.ContainsKey(animator))
        {
            originalAnimationSpeeds[animator] = animator.speed;
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"WindConeController: 保存动画器 {animator.name} 的原始速度: {animator.speed}");
            #endif
        }
        
        // 设置暂停速度
        animator.speed = pauseAnimationSpeed;
        
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"WindConeController: 暂停动画器 {animator.name} (位置: {animator.transform.position}, 新速度: {animator.speed})");
        #endif
    }
    
    /// <summary>
    /// 恢复动画器
    /// </summary>
    private void ResumeAnimator(Animator animator)
    {
        if (!animator)
            return;
            
        // 恢复原始速度或使用配置的恢复速度
        if (originalAnimationSpeeds.ContainsKey(animator))
        {
            float originalSpeed = originalAnimationSpeeds[animator];
            animator.speed = originalSpeed;
            originalAnimationSpeeds.Remove(animator);
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"WindConeController: 恢复动画器 {animator.name} 到原始速度: {originalSpeed}");
            #endif
        }
        else
        {
            animator.speed = resumeAnimationSpeed;
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"WindConeController: 恢复动画器 {animator.name} 到配置速度: {resumeAnimationSpeed}");
            #endif
        }
    }
    
    /// <summary>
    /// 设置圆锥形风力启用状态
    /// </summary>
    public void SetWindConeEnabled(bool enabled)
    {
        enableWindCone = enabled;
        UpdateWindConeParameters();
    }
    
    /// <summary>
    /// 设置圆锥位置偏移
    /// </summary>
    public void SetPositionOffset(Vector3 offset)
    {
        positionOffset = offset;
        UpdateWindConeParameters();
    }
    
    /// <summary>
    /// 设置圆锥方向
    /// </summary>
    public void SetConeDirection(Vector3 direction)
    {
        customDirection = direction.normalized;
        useForwardDirection = false;
        UpdateWindConeParameters();
    }
    
    /// <summary>
    /// 使用物体前向方向作为圆锥方向
    /// </summary>
    public void UseForwardDirection()
    {
        useForwardDirection = true;
        UpdateWindConeParameters();
    }
    
    /// <summary>
    /// 设置圆锥角度
    /// </summary>
    public void SetConeAngle(float angle)
    {
        coneAngle = Mathf.Clamp(angle, 0.0f, 90.0f);
        UpdateWindConeParameters();
    }
    
    /// <summary>
    /// 设置圆锥范围
    /// </summary>
    public void SetConeRange(float range)
    {
        coneRange = Mathf.Max(0.1f, range);
        UpdateWindConeParameters();
    }
    
    /// <summary>
    /// 设置圆锥强度
    /// </summary>
    public void SetConeIntensity(float intensity)
    {
        coneIntensity = Mathf.Max(0.0f, intensity);
        UpdateWindConeParameters();
    }
    
    /// <summary>
    /// 设置频率增强值
    /// </summary>
    public void SetFrequencyBoost(float boost)
    {
        frequencyBoost = Mathf.Clamp(boost, 0.0f, 10.0f);
        UpdateWindConeParameters();
    }
    
    /// <summary>
    /// 设置目标渲染器
    /// </summary>
    public void SetTargetRenderer(Renderer renderer)
    {
        targetFurRenderer = renderer;
        affectAllFurRenderers = false;
        FindTargetRenderers();
        UpdateWindConeParameters();
    }
    
    /// <summary>
    /// 影响所有使用FurShell材质的渲染器
    /// </summary>
    public void AffectAllFurRenderers()
    {
        affectAllFurRenderers = true;
        targetFurRenderer = null;
        FindTargetRenderers();
        UpdateWindConeParameters();
    }
    
    /// <summary>
    /// 启用动画控制
    /// </summary>
    public void EnableAnimationControl()
    {
        enableAnimationControl = true;
        detectionTimer = 0.0f; // 立即开始检测
    }
    
    /// <summary>
    /// 禁用动画控制
    /// </summary>
    public void DisableAnimationControl()
    {
        enableAnimationControl = false;
        
        // 恢复所有在范围内的动画器
        foreach (Animator animator in animatorsInRange)
        {
            ResumeAnimator(animator);
        }
        
        animatorsInRange.Clear();
        originalAnimationSpeeds.Clear();
    }
    
    /// <summary>
    /// 设置检测半径
    /// </summary>
    public void SetDetectionRadius(float radius)
    {
        detectionRadius = Mathf.Max(0.1f, radius);
    }
    
    /// <summary>
    /// 设置检测间隔
    /// </summary>
    public void SetDetectionInterval(float interval)
    {
        detectionInterval = Mathf.Clamp(interval, 0.1f, 2.0f);
    }
    
    /// <summary>
    /// 设置动画暂停速度
    /// </summary>
    public void SetPauseAnimationSpeed(float speed)
    {
        pauseAnimationSpeed = Mathf.Clamp(speed, 0.0f, 1.0f);
        
        // 更新当前在范围内的所有动画器
        foreach (Animator animator in animatorsInRange)
        {
            if (animator != null)
            {
                animator.speed = pauseAnimationSpeed;
            }
        }
    }
    
    /// <summary>
    /// 设置恢复动画速度
    /// </summary>
    public void SetResumeAnimationSpeed(float speed)
    {
        resumeAnimationSpeed = Mathf.Clamp(speed, 0.0f, 2.0f);
    }
    
    /// <summary>
    /// 在Scene视图中绘制圆锥范围和检测范围
    /// </summary>
    void OnDrawGizmosSelected()
    {
        // 计算圆锥位置和方向
        Vector3 conePosition = transform.position + transform.TransformDirection(positionOffset);
        Vector3 coneDirection = useForwardDirection ? transform.forward : customDirection.normalized;
        
        // 保存原始Gizmos颜色
        Color originalColor = Gizmos.color;
        
        // 绘制圆锥范围
        if (showGizmos)
        {
            // 设置圆锥Gizmos颜色
            Gizmos.color = gizmoColor;
            
            // 绘制圆锥中心点
            Gizmos.DrawSphere(conePosition, 0.1f);
            
            // 绘制圆锥方向
            Gizmos.DrawLine(conePosition, conePosition + coneDirection * coneRange * 0.5f);
            
            // 绘制圆锥范围
            DrawConeGizmo(conePosition, coneDirection, coneAngle, coneRange);
        }
        
        // 绘制检测范围
        if (showDetectionRadius && enableAnimationControl)
        {
            // 设置检测范围Gizmos颜色
            Gizmos.color = detectionRadiusColor;
            
            // 绘制检测范围球体（透明）
            Gizmos.DrawWireSphere(conePosition, detectionRadius);
            
            // 绘制检测范围球体（半透明填充）
            Color fillColor = detectionRadiusColor;
            fillColor.a *= 0.1f; // 更透明的填充
            Gizmos.color = fillColor;
            Gizmos.DrawSphere(conePosition, detectionRadius);
            
            // 绘制检测范围与圆锥中心的关系线
            Gizmos.color = new Color(detectionRadiusColor.r, detectionRadiusColor.g, detectionRadiusColor.b, 0.5f);
            Gizmos.DrawLine(conePosition, conePosition + Vector3.right * detectionRadius);
            Gizmos.DrawLine(conePosition, conePosition - Vector3.right * detectionRadius);
            Gizmos.DrawLine(conePosition, conePosition + Vector3.up * detectionRadius);
            Gizmos.DrawLine(conePosition, conePosition - Vector3.up * detectionRadius);
            Gizmos.DrawLine(conePosition, conePosition + Vector3.forward * detectionRadius);
            Gizmos.DrawLine(conePosition, conePosition - Vector3.forward * detectionRadius);
        }
        
        // 恢复原始Gizmos颜色
        Gizmos.color = originalColor;
    }
    
    /// <summary>
    /// 绘制圆锥Gizmo
    /// </summary>
    private void DrawConeGizmo(Vector3 position, Vector3 direction, float angle, float range)
    {
        float angleRad = Mathf.Deg2Rad * angle;
        float radius = Mathf.Tan(angleRad) * range;
        
        // 计算圆锥底面中心
        Vector3 baseCenter = position + direction * range;
        
        // 计算两个垂直向量
        Vector3 up = Vector3.up;
        Vector3 right = Vector3.right;
        
        // 如果方向不是垂直的，调整up和right向量
        if (Mathf.Abs(Vector3.Dot(direction, Vector3.up)) < 0.99f)
        {
            right = Vector3.Cross(direction, Vector3.up).normalized;
            up = Vector3.Cross(right, direction).normalized;
        }
        
        // 绘制圆锥侧面
        int segments = 16;
        for (int i = 0; i < segments; i++)
        {
            float angle1 = 2 * Mathf.PI * i / segments;
            float angle2 = 2 * Mathf.PI * (i + 1) / segments;
            
            Vector3 point1 = baseCenter + (Mathf.Cos(angle1) * right + Mathf.Sin(angle1) * up) * radius;
            Vector3 point2 = baseCenter + (Mathf.Cos(angle2) * right + Mathf.Sin(angle2) * up) * radius;
            
            // 绘制从顶点到底面边缘的线
            Gizmos.DrawLine(position, point1);
            Gizmos.DrawLine(position, point2);
            
            // 绘制底面边缘
            Gizmos.DrawLine(point1, point2);
        }
        
        // 绘制范围球体（透明）
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, gizmoColor.a * 0.2f);
        Gizmos.DrawWireSphere(position, coneRange);
    }
    
    /// <summary>
    /// 在Scene视图中始终绘制检测范围（当选中物体时）
    /// </summary>
    void OnDrawGizmos()
    {
        // 只在选中物体时绘制检测范围
        if (showDetectionRadius && enableAnimationControl)
        {
            #if UNITY_EDITOR
            // 检查是否选中当前物体（仅在编辑器中）
            if (Selection.activeGameObject != gameObject)
                return;
            #endif
            
            // 计算圆锥位置
            Vector3 conePosition = transform.position + transform.TransformDirection(positionOffset);
            
            // 保存原始Gizmos颜色
            Color originalColor = Gizmos.color;
            
            // 设置检测范围Gizmos颜色（更明显的颜色）
            Gizmos.color = new Color(detectionRadiusColor.r, detectionRadiusColor.g, detectionRadiusColor.b, 0.3f);
            
            // 绘制检测范围球体
            Gizmos.DrawWireSphere(conePosition, detectionRadius);
            
            // 恢复原始Gizmos颜色
            Gizmos.color = originalColor;
        }
    }
    
    /// <summary>
    /// 验证参数
    /// </summary>
    void OnValidate()
    {
        // 确保参数在合理范围内
        coneAngle = Mathf.Clamp(coneAngle, 0.0f, 90.0f);
        coneRange = Mathf.Max(0.1f, coneRange);
        coneIntensity = Mathf.Max(0.0f, coneIntensity);
        frequencyBoost = Mathf.Clamp(frequencyBoost, 0.0f, 10.0f);
        
        // 动画控制参数验证
        detectionRadius = Mathf.Max(0.1f, detectionRadius);
        detectionInterval = Mathf.Clamp(detectionInterval, 0.1f, 2.0f);
        pauseAnimationSpeed = Mathf.Clamp(pauseAnimationSpeed, 0.0f, 1.0f);
        resumeAnimationSpeed = Mathf.Clamp(resumeAnimationSpeed, 0.0f, 2.0f);
        
        // 如果正在运行，更新参数
        if (Application.isPlaying && enabled)
        {
            UpdateWindConeParameters();
        }
    }
    
}
