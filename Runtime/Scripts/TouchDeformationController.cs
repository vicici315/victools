using UnityEngine;

/// 触摸挤压效果控制器
/// 将触摸参数（位置、半径、强度）传递给毛发着色器
[DisallowMultipleComponent]
[ExecuteAlways]
public class TouchDeformationController : MonoBehaviour
{
    [Header("触摸参数")]
    [Tooltip("触摸点物体对象列表（优先使用）")]
    public GameObject[] touchObjects = new GameObject[0];
    
    [Tooltip("触摸点世界空间位置（当touchObjects为空时使用）")]
    public Vector3 touchPosition = Vector3.zero;
    
    [Tooltip("触摸影响半径")]
    public float touchRadius = 0.3f;
    
    [Tooltip("触摸强度")]
    public float touchStrength = 2.7f;
    
    [Tooltip("最大凹陷度 (0.0-2.0)")]
    [Range(0.0f, 2.0f)]
    public float maxDepression = 0.8f;

    private Renderer targetRenderer;
    private MaterialPropertyBlock propertyBlock;
    
    // 销毁状态标志
    private bool isBeingDestroyed = false;
    
    // 着色器属性ID（缓存以提高性能）
    // 支持最多4个触摸点
    private static readonly int TouchPosition1ID = Shader.PropertyToID("_TouchPosition");
    private static readonly int TouchPosition2ID = Shader.PropertyToID("_TouchPosition2");
    private static readonly int TouchPosition3ID = Shader.PropertyToID("_TouchPosition3");
    private static readonly int TouchPosition4ID = Shader.PropertyToID("_TouchPosition4");
    private static readonly int TouchRadiusID = Shader.PropertyToID("_TouchRadius");
    private static readonly int MaxDepressionID = Shader.PropertyToID("_MaxDepression");
    
    void OnEnable()
    {
        // 确保在启用时初始化
        #if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // 在编辑模式下，延迟初始化以避免编辑器GUI问题
            EditorInit();
        }
        #endif
    }
    
    void Start()
    {
        // 获取当前GameObject的Renderer组件
        targetRenderer = GetComponent<Renderer>();
        if (targetRenderer == null)
        {
            Debug.LogWarning("TouchDeformationController: 没有找到Renderer组件，将尝试查找子物体中的Renderer");
            targetRenderer = GetComponentInChildren<Renderer>();
        }
        
        if (targetRenderer == null)
        {
            Debug.LogError("TouchDeformationController: 没有找到任何Renderer组件！");
            enabled = false;
            return;
        }
        
        // 创建MaterialPropertyBlock用于高效设置材质属性
        propertyBlock = new MaterialPropertyBlock();
        
        // 初始化触摸参数
        UpdateShaderProperties();
    }
    
    #if UNITY_EDITOR
    /// 编辑器初始化方法
    /// 避免在OnValidate中直接初始化导致的编辑器GUI问题
    private void EditorInit()
    {
        // 安全检查：如果组件已被销毁，直接返回
        if (IsBeingDestroyedOrNull()) return;
        
        // 延迟初始化targetRenderer
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
            if (targetRenderer == null)
            {
                targetRenderer = GetComponentInChildren<Renderer>();
            }
        }
        
        // 初始化propertyBlock
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }
    }
    #endif
    
    void OnDestroy()
    {
        // 标记组件正在被销毁
        isBeingDestroyed = true;
        
        // 在编辑模式下，避免访问可能已被销毁的对象
        #if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // 在编辑模式下，不尝试修改已销毁对象的属性
            // 直接返回，让Unity处理剩余的销毁逻辑
            // 清理引用，帮助垃圾回收
            targetRenderer = null;
            propertyBlock = null;
            touchObjects = null;
            return;
        }
        #endif
        
        // 清理资源，避免内存泄漏
        // 重置着色器参数，确保效果被禁用
        if (!IsBeingDestroyedOrNull())
        {
            // 将触摸位置设置为远离物体的位置（例如地下1000单位）
            touchPosition = new Vector3(0, -1000, 0);
            // touchStrength = 0f;
            UpdateShaderProperties();
        }
        
        // 清理引用，帮助垃圾回收
        targetRenderer = null;
        propertyBlock = null;
        touchObjects = null;
    }
    
    void OnDisable()
    {
        // 当组件被禁用时，重置触摸效果
        if (!IsBeingDestroyedOrNull())
        {
            // 将触摸位置设置为远离物体的位置（例如地下1000单位）
            touchPosition = new Vector3(0, -1000, 0);
            // touchStrength = 0f;
            UpdateShaderProperties();
        }
    }
    
    void Update()
    {
        // 如果组件正在被销毁，直接返回
        if (IsBeingDestroyedOrNull()) return;
        
        // 如果设置了touchObjects，使用它们的位置
        // 使用安全的属性访问方法来避免MissingReferenceException
        if (touchObjects != null && touchObjects.Length > 0)
        {
            // 只使用第一个有效的touchObject作为主触摸点
            for (int i = 0; i < touchObjects.Length; i++)
            {
                if (TryGetTouchObjectPosition(touchObjects[i], out Vector3 newPosition))
                {
                    touchPosition = newPosition;
                    break;
                }
            }
        }
        
        // 更新触摸参数到着色器
        UpdateShaderProperties();
    }
    
    /// 更新着色器属性
    private void UpdateShaderProperties()
    {
        // 安全检查：如果组件本身已被销毁或正在被销毁，直接返回
        if (IsBeingDestroyedOrNull()) return;
        
        // 在编辑模式下，尝试获取targetRenderer
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
            if (targetRenderer == null)
            {
                targetRenderer = GetComponentInChildren<Renderer>();
            }
        }
        
        // 使用安全的null检查来避免MissingReferenceException
        if (IsUnityObjectNull(targetRenderer)) return;
        
        // 确保propertyBlock已初始化
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }
        
        try
        {
            // 获取当前的MaterialPropertyBlock
            targetRenderer.GetPropertyBlock(propertyBlock);
            
            // 设置多个触摸点参数（最多4个）
            Vector4[] touchPositions = new Vector4[4];
            
            // 初始化所有触摸点为无效位置（地下1000单位）
            for (int i = 0; i < 4; i++)
            {
                touchPositions[i] = new Vector4(0, -1000, 0, 0);
            }
            
            // 如果有touchObjects，使用它们的位置
            if (touchObjects != null && touchObjects.Length > 0)
            {
                int validCount = 0;
                for (int i = 0; i < touchObjects.Length && validCount < 4; i++)
                {
                    if (TryGetTouchObjectPosition(touchObjects[i], out Vector3 pos))
                    {
                        touchPositions[validCount] = new Vector4(pos.x, pos.y, pos.z, touchStrength);
                        validCount++;
                    }
                }
            }
            else
            {
                // 使用单个touchPosition
                touchPositions[0] = new Vector4(touchPosition.x, touchPosition.y, touchPosition.z, touchStrength);
            }
            
            // 设置触摸参数到着色器
            propertyBlock.SetVector(TouchPosition1ID, touchPositions[0]);
            propertyBlock.SetVector(TouchPosition2ID, touchPositions[1]);
            propertyBlock.SetVector(TouchPosition3ID, touchPositions[2]);
            propertyBlock.SetVector(TouchPosition4ID, touchPositions[3]);
            propertyBlock.SetFloat(TouchRadiusID, touchRadius);
            propertyBlock.SetFloat(MaxDepressionID, maxDepression);
            
            // 应用属性
            targetRenderer.SetPropertyBlock(propertyBlock);
        }
        catch (System.Exception)
        {
            // 如果发生异常（例如对象正在被销毁），静默处理
            // 在编辑模式下不记录错误以避免控制台污染
            #if UNITY_EDITOR
            if (Application.isPlaying)
            {
                // 仅在运行模式下记录警告
                Debug.LogWarning("TouchDeformationController: 更新着色器属性时发生异常，组件可能正在被销毁");
            }
            #endif
        }
    }
    
    /// 设置触摸位置（世界空间）
    public void SetTouchPosition(Vector3 position)
    {
        // 安全检查：如果组件已被销毁或正在被销毁，直接返回
        if (IsBeingDestroyedOrNull()) return;
        
        touchPosition = position;
        UpdateShaderProperties();
    }
    
    /// 安全的null检查辅助方法
    /// 正确处理UnityEngine.Object的销毁状态
    private bool IsUnityObjectNull(UnityEngine.Object obj)
    {
        // 在Unity中，销毁的对象不是真正的null，但应该被视为null
        // 使用System.Object转换来确保正确的null检查
        return obj == null || !obj;
    }
    
    /// 检查组件是否正在被销毁或已销毁
    private bool IsBeingDestroyedOrNull()
    {
        return isBeingDestroyed || IsUnityObjectNull(this);
    }
    
    /// 安全的属性访问辅助方法
    /// 避免访问已被销毁的Unity对象的属性
    private bool TryGetTouchObjectPosition(GameObject touchObj, out Vector3 position)
    {
        position = Vector3.zero;
        
        if (IsUnityObjectNull(touchObj))
            return false;
            
        try
        {
            position = touchObj.transform.position;
            return true;
        }
        catch (System.Exception)
        {
            // 如果访问属性时发生异常（例如对象正在被销毁）
            return false;
        }
    }
    
    /// 设置触摸半径
    private void SetTouchRadius(float radius)
    {
        // 安全检查：如果组件已被销毁或正在被销毁，直接返回
        if (IsBeingDestroyedOrNull()) return;
        
        touchRadius = Mathf.Max(0.01f, radius);
        UpdateShaderProperties();
    }
    
    /// 设置触摸强度
    private void SetTouchStrength(float strength)
    {
        // 安全检查：如果组件已被销毁或正在被销毁，直接返回
        if (IsBeingDestroyedOrNull()) return;
        
        touchStrength = Mathf.Max(0f, strength);
        UpdateShaderProperties();
    }
    
    /// 设置最大凹陷度
    public void SetMaxDepression(float depression)
    {
        // 安全检查：如果组件已被销毁或正在被销毁，直接返回
        if (IsBeingDestroyedOrNull()) return;
        UpdateShaderProperties();
    }
    
    /// 处理触摸输入
    public void HandleTouchInput()
    {
        if (IsBeingDestroyedOrNull()) return;
        
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            
            // 转换触摸位置到世界坐标
            if (Camera.main != null)
            {
                Ray ray = Camera.main.ScreenPointToRay(touch.position);
                RaycastHit hit;
                
                if (Physics.Raycast(ray, out hit))
                {
                    SetTouchPosition(hit.point);
                }
                else
                {
                    SetTouchPosition(ray.origin + ray.direction * 10f);
                }
            }
            
            // 根据触摸压力调整强度（如果设备支持）
            if (Input.touchPressureSupported)
            {
                SetTouchStrength(touch.pressure * 2f);
            }
            else
            {
                SetTouchStrength(1f);
            }
        }
        else
        {
            // 没有触摸时，重置触摸位置到远离物体的位置
            if (!IsBeingDestroyedOrNull())
            {
                // 将触摸位置设置为远离物体的位置（例如地下1000单位）
                touchPosition = new Vector3(0, -1000, 0);
                // touchStrength = 0f;
                UpdateShaderProperties();
            }
        }
    }
    
    void OnDrawGizmosSelected()
    {
        // 安全检查：避免在对象被销毁时绘制Gizmos
        if (IsBeingDestroyedOrNull())
            return;
        
        // 绘制所有touchObjects的影响区域
        if (touchObjects != null && touchObjects.Length > 0)
        {
            for (int i = 0; i < touchObjects.Length && i < 4; i++)
            {
                if (TryGetTouchObjectPosition(touchObjects[i], out Vector3 pos))
                {
                    // 在Scene视图中绘制触摸影响区域
                    Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
                    Gizmos.DrawWireSphere(pos, touchRadius);
                    
                    // 绘制强度指示器
                    Gizmos.color = new Color(1f, 0f, 0f, 0.8f);
                    Gizmos.DrawLine(pos, pos + Vector3.up * (touchStrength * 0.2f));
                    
                    // 绘制编号标签
                    #if UNITY_EDITOR
                    UnityEditor.Handles.Label(pos + Vector3.up * 0.3f, $"Touch {i + 1}");
                    #endif
                }
            }
        }
        else
        {
            // 绘制单个touchPosition的影响区域
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            Gizmos.DrawWireSphere(touchPosition, touchRadius);
            
            // 绘制强度指示器
            Gizmos.color = new Color(1f, 0f, 0f, 0.8f);
            Gizmos.DrawLine(touchPosition, touchPosition + Vector3.up * (touchStrength * 0.2f));
        }
    }
    
    /// 编辑器验证方法
    /// 在Inspector中修改值时调用
    void OnValidate()
    {
        // 在编辑模式下，完全禁用OnValidate功能以避免MissingReferenceException
        #if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // 在编辑模式下，直接返回，不执行任何操作
            // 这样可以避免编辑器在组件销毁后仍然尝试访问它
            return;
        }
        #endif
        
        // 仅在运行模式下执行验证
        if (IsBeingDestroyedOrNull()) return;
        
        // 确保参数在合理范围内
        touchRadius = Mathf.Max(0.01f, touchRadius);
        touchStrength = Mathf.Max(0f, touchStrength);
    }
}
