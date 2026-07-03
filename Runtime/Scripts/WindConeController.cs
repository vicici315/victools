// WindConeController2.0 优化控制参数，修复targetFurRenderer相关参数控制实时生效
/// 圆锥形风力控制器 v2.0 (2026.05.27)
/// - 精简参数：移除positionOffset/customDirection/独立平滑速度/检测间隔/动画速度控制/Gizmo颜色等
/// - 统一smoothSpeed控制过渡，方向固定forward，动画暂停=0/恢复=原始速度
/// - 修复：targetFurRenderer实时生效、affectAll关闭后残留清除、空目标不影响子物体

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// 圆锥形风力控制器
/// 用于控制吹风机对毛发的影响效果
/// 可以附加到吹风机模型上，自动设置圆锥形风力参数
[DisallowMultipleComponent]
[ExecuteAlways]
public class WindConeController : MonoBehaviour
{
    [Header("圆锥形风力参数")]
    [Tooltip("启用圆锥形风力影响")]
    public bool enableWindCone = true;
    
    [Tooltip("圆锥角度（度）")]
    [Range(0.0f, 90.0f)]
    public float coneAngle = 30.0f;
    
    [Tooltip("圆锥范围")]
    public float coneRange = 5.0f;
    
    [Tooltip("圆锥风力强度")]
    public float coneIntensity = 5.0f;
    
    [Tooltip("圆锥内风频率加大值")]
    [Range(0.0f, 10.0f)]
    public float frequencyBoost = 5.0f;
    
    [Tooltip("平滑过渡速度（值越大过渡越快，0=无平滑）")]
    [Range(0.0f, 20.0f)]
    public float smoothSpeed = 8.0f;

    [Header("目标毛发渲染器")]
    [Tooltip("目标毛发渲染器（如果为空，将查找场景中所有使用FurShell材质的渲染器）")]
    public Renderer targetFurRenderer;

    [Tooltip("影响所有使用FurShell材质的渲染器")]
    public bool affectAllFurRenderers = true;

    [Header("动画控制")]
    [Tooltip("启用动画暂停/继续功能（圆锥范围内的动画器将被暂停）")]
    public bool enableAnimationControl = true;
    
    [Header("调试")]
    [Tooltip("在Scene视图中显示圆锥范围")]
    public bool showGizmos = true;
    
    // 私有变量
    private Renderer[] targetRenderers;
    private MaterialPropertyBlock propertyBlock;
    
    // 缓冲变量
    private Vector3 smoothedConePosition;
    private Vector3 smoothedConeDirection;
    private float smoothedConeIntensity;
    
    // 动画控制相关变量
    private const float DetectionInterval = 0.5f;
    private float detectionTimer = 0.0f;
    private readonly System.Collections.Generic.List<Animator> animatorsInRange = new System.Collections.Generic.List<Animator>();
    private readonly System.Collections.Generic.Dictionary<Animator, float> originalAnimationSpeeds = new System.Collections.Generic.Dictionary<Animator, float>();
    
    // 缓存优化变量
    private static Renderer[] cachedAllFurRenderers;
    private static float lastFurRendererCacheTime = 0f;
    private static readonly float furRendererCacheInterval = 5f;
    private static Animator[] cachedAllAnimators;
    private static float lastAnimatorCacheTime = 0f;
    private static readonly float animatorCacheInterval = 2f;
    private bool needRefreshFurCache = true;
    private bool needRefreshAnimatorCache = true;
    
    // 着色器属性ID（缓存以提高性能）
    private static readonly int UseWindConeID = Shader.PropertyToID("_UseWindCone");
    private static readonly int WindConePositionID = Shader.PropertyToID("_WindConePosition");
    private static readonly int WindConeDirectionID = Shader.PropertyToID("_WindConeDirection");
    private static readonly int WindConeAngleID = Shader.PropertyToID("_WindConeAngle");
    private static readonly int WindConeRangeID = Shader.PropertyToID("_WindConeRange");
    private static readonly int WindConeFrequencyBoostID = Shader.PropertyToID("_WindConeFrequencyBoost");
    
    // Gizmo颜色常量
    private static readonly Color ConeGizmoColor = new Color(0.0f, 0.0f, 0.5f, 0.8f);
    private static readonly Color DetectionGizmoColor = new Color(1.0f, 0.5f, 0.0f, 0.2f);
    
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
        ClearAllPropertyBlocks();
        
        if (enableAnimationControl && Application.isPlaying)
        {
            DisableAnimationControl();
        }
    }
    
    private void ClearAllPropertyBlocks()
    {
        if (targetRenderers == null) return;
        
        foreach (Renderer renderer in targetRenderers)
        {
            if (renderer == null) continue;
            renderer.SetPropertyBlock(null); // 清除覆盖，不分配新的 MaterialPropertyBlock
        }
    }
    
    void OnDestroy()
    {
        targetRenderers = null;
        propertyBlock = null;
    }
    
    void Update()
    {
        UpdateWindConeParameters();
        
        if (enableAnimationControl && Application.isPlaying)
        {
            UpdateAnimationControl();
        }
    }
    
    private void Initialize()
    {
        propertyBlock = new MaterialPropertyBlock();
        FindTargetRenderers();
        UpdateWindConeParameters();
    }
    
    private void FindTargetRenderers()
    {
        // 先清除旧目标上的残留PropertyBlock
        ClearAllPropertyBlocks();
        
        if (affectAllFurRenderers)
        {
            targetRenderers = GetCachedFurRenderers();
        }
        else if (targetFurRenderer != null)
        {
            targetRenderers = new Renderer[] { targetFurRenderer };
        }
        else
        {
            // targetFurRenderer为空且未勾选affectAll时，不影响任何渲染器
            targetRenderers = new Renderer[0];
        }
    }
    
    private Renderer[] GetCachedFurRenderers()
    {
        bool shouldRefreshCache = needRefreshFurCache || 
                                 cachedAllFurRenderers == null || 
                                 Time.time - lastFurRendererCacheTime > furRendererCacheInterval;
        
        if (shouldRefreshCache)
        {
            Renderer[] allRenderers = FindObjectsOfType<Renderer>();
            var furRenderers = new System.Collections.Generic.List<Renderer>();
            
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
        }
        
        return cachedAllFurRenderers;
    }
    
    private Animator[] GetCachedAnimators()
    {
        bool shouldRefreshCache = needRefreshAnimatorCache || 
                                 cachedAllAnimators == null || 
                                 Time.time - lastAnimatorCacheTime > animatorCacheInterval;
        
        if (shouldRefreshCache)
        {
            cachedAllAnimators = FindObjectsOfType<Animator>();
            lastAnimatorCacheTime = Time.time;
            needRefreshAnimatorCache = false;
        }
        
        return cachedAllAnimators;
    }
    
    public void MarkFurCacheDirty()
    {
        needRefreshFurCache = true;
    }
    
    public void MarkAnimatorCacheDirty()
    {
        needRefreshAnimatorCache = true;
    }
    
    private void UpdateWindConeParameters()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            return;
        
        // 始终使用物体前向方向
        Vector3 targetConePosition = transform.position;
        Vector3 targetConeDirection = transform.forward;
        
        // 初始化缓冲变量
        if (smoothedConePosition == Vector3.zero && targetConePosition != Vector3.zero)
        {
            smoothedConePosition = targetConePosition;
            smoothedConeDirection = targetConeDirection;
            smoothedConeIntensity = coneIntensity;
        }
        
        // 应用平滑过渡
        if (smoothSpeed > 0f && Application.isPlaying)
        {
            float dt = smoothSpeed * Time.deltaTime;
            smoothedConePosition = Vector3.Lerp(smoothedConePosition, targetConePosition, dt);
            smoothedConeDirection = Vector3.Slerp(smoothedConeDirection, targetConeDirection, dt).normalized;
            smoothedConeIntensity = Mathf.Lerp(smoothedConeIntensity, coneIntensity, dt);
        }
        else
        {
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
                renderer.GetPropertyBlock(propertyBlock);
                
                if (!enableWindCone)
                {
                    renderer.SetPropertyBlock(null); // 每帧调用，用 null 清除，避免每帧分配 MaterialPropertyBlock 泄漏原生内存
                    continue;
                }
                
                propertyBlock.SetFloat(UseWindConeID, 1.0f);
                
                Vector4 conePosWithIntensity = new Vector4(
                    smoothedConePosition.x, 
                    smoothedConePosition.y, 
                    smoothedConePosition.z, 
                    smoothedConeIntensity
                );
                propertyBlock.SetVector(WindConePositionID, conePosWithIntensity);
                
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
                
                renderer.SetPropertyBlock(propertyBlock);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"WindConeController: 更新渲染器 {renderer.name} 时发生错误: {e.Message}");
            }
        }
    }
    
    private void UpdateAnimationControl()
    {
        detectionTimer -= Time.deltaTime;
        
        if (detectionTimer <= 0.0f)
        {
            detectionTimer = DetectionInterval;
            DetectAnimatorsInCone();
        }
    }
    
    private void DetectAnimatorsInCone()
    {
        Vector3 currentConePosition = smoothedConePosition;
        Vector3 currentConeDirection = smoothedConeDirection;
        
        Animator[] allAnimators = GetCachedAnimators();
        var currentAnimators = new System.Collections.Generic.List<Animator>();
        
        foreach (Animator animator in allAnimators)
        {
            if (animator == null || !animator.enabled || !animator.gameObject.activeInHierarchy)
                continue;
            
            Vector3 animatorPosition = animator.transform.position;
            
            SkinnedMeshRenderer skinnedRenderer = animator.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinnedRenderer != null && skinnedRenderer.enabled)
            {
                animatorPosition = skinnedRenderer.bounds.center;
            }
            
            if (IsPointInCone(animatorPosition, currentConePosition, currentConeDirection, coneAngle, coneRange))
            {
                currentAnimators.Add(animator);
            }
        }
        
        // 处理新进入范围的动画器
        foreach (Animator animator in currentAnimators)
        {
            if (!animatorsInRange.Contains(animator))
            {
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
                ResumeAnimator(animator);
                animatorsInRange.RemoveAt(i);
            }
        }
    }
    
    private bool IsPointInCone(Vector3 point, Vector3 conePosition, Vector3 coneDirection, float angle, float range)
    {
        Vector3 pointToCone = point - conePosition;
        float distanceToCone = pointToCone.magnitude;
        
        if (distanceToCone > range)
            return false;
        
        float pointAngle = Vector3.Angle(coneDirection, pointToCone.normalized);
        return pointAngle <= angle * 0.5f;
    }
    
    private void PauseAnimator(Animator animator)
    {
        if (!animator) return;
            
        if (!originalAnimationSpeeds.ContainsKey(animator))
        {
            originalAnimationSpeeds[animator] = animator.speed;
        }
        
        animator.speed = 0f;
    }
    
    private void ResumeAnimator(Animator animator)
    {
        if (!animator) return;
            
        if (originalAnimationSpeeds.ContainsKey(animator))
        {
            animator.speed = originalAnimationSpeeds[animator];
            originalAnimationSpeeds.Remove(animator);
        }
        else
        {
            animator.speed = 1f;
        }
    }
    
    // === 公共API ===
    
    public void SetWindConeEnabled(bool enabled)
    {
        enableWindCone = enabled;
        UpdateWindConeParameters();
    }
    
    public void SetWindEnabled(bool enabled)
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            return;
            
        foreach (Renderer renderer in targetRenderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(Shader.PropertyToID("_UseWind"), enabled ? 1.0f : 0.0f);
            renderer.SetPropertyBlock(propertyBlock);
        }
    }
    
    public void SetConeAngle(float angle)
    {
        coneAngle = Mathf.Clamp(angle, 0.0f, 90.0f);
        UpdateWindConeParameters();
    }
    
    public void SetConeRange(float range)
    {
        coneRange = Mathf.Max(0.1f, range);
        UpdateWindConeParameters();
    }
    
    public void SetConeIntensity(float intensity)
    {
        coneIntensity = Mathf.Max(0.0f, intensity);
        UpdateWindConeParameters();
    }
    
    public void SetFrequencyBoost(float boost)
    {
        frequencyBoost = Mathf.Clamp(boost, 0.0f, 10.0f);
        UpdateWindConeParameters();
    }
    
    public void SetTargetRenderer(Renderer renderer)
    {
        targetFurRenderer = renderer;
        affectAllFurRenderers = false;
        FindTargetRenderers();
        UpdateWindConeParameters();
    }
    
    public void AffectAllFurRenderers()
    {
        affectAllFurRenderers = true;
        targetFurRenderer = null;
        FindTargetRenderers();
        UpdateWindConeParameters();
    }
    
    public void EnableAnimationControl()
    {
        enableAnimationControl = true;
        detectionTimer = 0.0f;
    }
    
    public void DisableAnimationControl()
    {
        enableAnimationControl = false;
        
        foreach (Animator animator in animatorsInRange)
        {
            ResumeAnimator(animator);
        }
        
        animatorsInRange.Clear();
        originalAnimationSpeeds.Clear();
    }
    
    // === Gizmos ===
    
    void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;
        
        Vector3 conePosition = transform.position;
        Vector3 coneDirection = transform.forward;
        
        Color originalColor = Gizmos.color;
        
        // 绘制圆锥
        Gizmos.color = ConeGizmoColor;
        Gizmos.DrawSphere(conePosition, 0.1f);
        Gizmos.DrawLine(conePosition, conePosition + coneDirection * (coneRange * 0.5f));
        DrawConeGizmo(conePosition, coneDirection, coneAngle, coneRange);
        
        // 绘制动画检测范围（使用圆锥范围作为检测范围）
        if (enableAnimationControl)
        {
            Gizmos.color = DetectionGizmoColor;
            Gizmos.DrawWireSphere(conePosition, coneRange);
        }
        
        Gizmos.color = originalColor;
    }
    
    private void DrawConeGizmo(Vector3 position, Vector3 direction, float angle, float range)
    {
        float angleRad = Mathf.Deg2Rad * angle;
        float radius = Mathf.Tan(angleRad) * range;
        
        Vector3 baseCenter = position + direction * range;
        
        Vector3 up;
        Vector3 right;
        
        float dotWithUp = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up));
        
        if (dotWithUp > 0.99f)
        {
            right = Vector3.Cross(direction, Vector3.forward).normalized;
            up = Vector3.Cross(right, direction).normalized;
        }
        else
        {
            right = Vector3.Cross(direction, Vector3.up).normalized;
            up = Vector3.Cross(right, direction).normalized;
        }
        
        int segments = 16;
        for (int i = 0; i < segments; i++)
        {
            float angle1 = 2 * Mathf.PI * i / segments;
            float angle2 = 2 * Mathf.PI * (i + 1) / segments;
            
            Vector3 point1 = baseCenter + (Mathf.Cos(angle1) * right + Mathf.Sin(angle1) * up) * radius;
            Vector3 point2 = baseCenter + (Mathf.Cos(angle2) * right + Mathf.Sin(angle2) * up) * radius;
            
            Gizmos.DrawLine(position, point1);
            Gizmos.DrawLine(position, point2);
            Gizmos.DrawLine(point1, point2);
        }
    }
    
    void OnValidate()
    {
        coneAngle = Mathf.Clamp(coneAngle, 0.0f, 90.0f);
        coneRange = Mathf.Max(0.1f, coneRange);
        coneIntensity = Mathf.Max(0.0f, coneIntensity);
        frequencyBoost = Mathf.Clamp(frequencyBoost, 0.0f, 10.0f);
        
        if (enabled)
        {
            FindTargetRenderers();
            UpdateWindConeParameters();
        }
    }
}
