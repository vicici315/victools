// BlendShapeAnimator v1.4 - 新增 ParticleSystem startSpeed 随启用动画线性插值控制
// BlendShapeAnimator v1.5 - 新增 粒子系统控制水花发射速度匹配水柱抛物线；添加 关闭动画手动设置进度参数
// BlendShapeAnimator v1.6 - 性能优化：BakeMesh 缓存复用，避免每帧 new Mesh 产生 GC
// BlendShapeAnimator v1.7 - 添加 SkinnedMeshRenderer 包围盒扩展，防止轴心偏移导致视锥剔除（修复轴心偏移问题）
// BlendShapeAnimator v1.8 - 自动设置混合变形模型文件启用 Read/Write 选项

//（BlendShaper混合变形顶点定位脚本）
using UnityEngine;

namespace Vic.Runtime
{

[ExecuteAlways]
public class BlendShapeAnimator : MonoBehaviour
{
    [Header("目标（支持 SkinnedMeshRenderer 或 MeshFilter 对象）")]
    public Renderer targetRenderer;

    [Header("BlendShape 设置（仅 SkinnedMeshRenderer 有效）")]
    [Tooltip("要驱动的 BlendShape 索引（-1 = 驱动全部）")]
    public int blendShapeIndex = 0;

    // [Header("动画参数")]
    [Tooltip("关闭时跳过所有动画计算")]
    public bool enableAnimation = false;
    [Tooltip("手动控制进度（仅在禁用动画时生效），0=起始，1=结束")]
    [Range(0f, 1f)]
    public float manualProgress = 0f;
    public float duration = 1.5f;
    public bool playOnAwake = true;

    [Header("顶点追踪")]
    [Tooltip("要追踪的顶点索引")]
    public int trackVertexIndex = 0;
    [Tooltip("跟随该顶点的空对象（由按钮创建或手动指定）")]
    public Transform vertexTracker;
    [Tooltip("勾选时只跟随 XZ 平面，Y 轴（高度）保持不变")]
    public bool ignoreTrackerZ = false;

    // [Header("包围盒扩展（防止视锥剔除）")]
    [Tooltip("扩展 SkinnedMeshRenderer 的 localBounds，防止轴心偏移时模型被提前剔除。0 = 不扩展")]
    public float boundsExpand = 0f;
    
    // 缓存原始 localBounds，用于还原和以原始中心对称扩展
    [HideInInspector] [SerializeField] private Bounds _originalBounds;
    [HideInInspector] [SerializeField] private bool _originalBoundsSaved = false;

    // [Header("粒子速度控制")]
    [Tooltip("要控制 startSpeed 的粒子系统")]
    public ParticleSystem targetParticle;
    [Tooltip("动画开始时（t=0）的 startSpeed 值")]
    public float particleSpeedMin = 0f;
    [Tooltip("动画结束时（t=1）的 startSpeed 值")]
    public float particleSpeedMax = 5f;
    [Tooltip("起始阶段 EaseOut 指数（_forward=true，Min→Max）：1=线性，值越大起步越急促")]
    public float particleSpeedEaseForward = 2f;
    [Tooltip("返回阶段 EaseOut 指数（_forward=false，Max→Min）：1=线性，值越大起步越急促")]
    public float particleSpeedEaseBack = 2f;

    private float _timer;
    private bool  _forward;
    private bool  _playing;
    private float _particleSpeedCurrent;  // 粒子速度当前值，用于平滑过渡
    private float _particleSpeedFrom;     // 每段动画起始速度
    private float _particleSpeedTarget;   // 每段动画目标速度

    // BakeMesh 缓存：避免每帧 new Mesh 产生 GC 分配
    [System.NonSerialized] private Mesh _bakedMeshCache;

    // ── 辅助：统一获取 Mesh 和 Transform ─────────────────────────

    private Mesh GetMesh()
    {
        if (targetRenderer is SkinnedMeshRenderer smr) return smr.sharedMesh;
        var mf = targetRenderer != null ? targetRenderer.GetComponent<MeshFilter>() : null;
        return mf != null ? mf.sharedMesh : null;
    }

    private Transform GetMeshTransform() => targetRenderer != null ? targetRenderer.transform : null;

#if UNITY_EDITOR
    // 在编辑器模式下自动修复 Read/Write 设置（无需用户确认）
    private void TryFixReadWriteEnabled(Mesh mesh)
    {
        // 使用反射调用编辑器功能，避免编译错误
        System.Type assetDatabaseType = System.Type.GetType("UnityEditor.AssetDatabase, UnityEditor");
        System.Type assetImporterType = System.Type.GetType("UnityEditor.AssetImporter, UnityEditor");
        
        if (assetDatabaseType == null || assetImporterType == null)
        {
            Debug.LogWarning($"[BlendShapeAnimator] Mesh '{mesh.name}' 的 Read/Write 未启用。");
            return;
        }

        // 获取资源路径
        System.Reflection.MethodInfo getAssetPathMethod = assetDatabaseType.GetMethod("GetAssetPath", new System.Type[] { typeof(UnityEngine.Object) });
        if (getAssetPathMethod == null) return;
        
        string assetPath = getAssetPathMethod.Invoke(null, new object[] { mesh }) as string;
        if (string.IsNullOrEmpty(assetPath))
        {
            Debug.LogWarning($"[BlendShapeAnimator] Mesh '{mesh.name}' 不是资产文件，无法修改导入设置。");
            return;
        }

        // 获取 AssetImporter
        System.Reflection.MethodInfo getAtPathMethod = assetImporterType.GetMethod("GetAtPath");
        if (getAtPathMethod == null) return;
        
        object importer = getAtPathMethod.Invoke(null, new object[] { assetPath });
        if (importer == null) return;

        // 检查是否是 ModelImporter
        System.Type modelImporterType = System.Type.GetType("UnityEditor.ModelImporter, UnityEditor");
        if (modelImporterType == null || !modelImporterType.IsInstanceOfType(importer))
        {
            Debug.LogWarning($"[BlendShapeAnimator] 无法修改 '{assetPath}' 的导入设置。");
            return;
        }

        // 检查是否已经启用
        System.Reflection.PropertyInfo isReadableProperty = modelImporterType.GetProperty("isReadable");
        if (isReadableProperty == null) return;
        
        bool isReadable = (bool)isReadableProperty.GetValue(importer);
        if (isReadable)
        {
            Debug.Log($"[BlendShapeAnimator] Mesh '{mesh.name}' 已经启用了 Read/Write。");
            return;
        }

        // 直接修改导入设置，无需用户确认
        Debug.Log($"[BlendShapeAnimator] ⚙️ 正在为 Mesh '{mesh.name}' 自动启用 Read/Write...");
        isReadableProperty.SetValue(importer, true);
        
        System.Reflection.MethodInfo saveAndReimportMethod = modelImporterType.GetMethod("SaveAndReimport");
        if (saveAndReimportMethod != null)
        {
            saveAndReimportMethod.Invoke(importer, null);
            Debug.Log($"[BlendShapeAnimator] ✅ 已成功为 Mesh '{mesh.name}' 启用 Read/Write！模型已重新导入。");
        }

        // 刷新资源数据库
        System.Reflection.MethodInfo refreshMethod = assetDatabaseType.GetMethod("Refresh");
        refreshMethod?.Invoke(null, null);
    }
#endif

    // ── Unity 生命周期 ────────────────────────────────────────────

    private void Awake()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();
        ApplyBoundsExpand();
        if (playOnAwake) Play();
    }

    private void Update()
    {
        if (targetRenderer == null) return;

        // 手动模式：编辑器和运行时都响应，不依赖 _playing
        if (!enableAnimation)
        {
            float mt = Mathf.Clamp01(manualProgress);
            SetBlendShape(mt * 100f);
            _particleSpeedCurrent = Mathf.Lerp(particleSpeedMin, particleSpeedMax, mt);
            if (targetParticle != null)
            {
                var main = targetParticle.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(_particleSpeedCurrent);
            }
            UpdateVertexTracker();
            return;
        }

        if (!Application.isPlaying)
        {
            UpdateVertexTracker();
            return;
        }

        if (!_playing) return;

        _timer += Time.deltaTime;
        float t      = Mathf.Clamp01(_timer / Mathf.Max(duration, 0.001f));
        float smooth = t * t * (3f - 2f * t);
        float value  = _forward ? smooth * 100f : (1f - smooth) * 100f;

        SetBlendShape(value);
        SetParticleStartSpeed(t, _forward);
        UpdateVertexTracker();

        if (_timer >= duration)
        {
            _timer = 0f;
            _forward = !_forward;
            // 记录当前速度作为下一段起点，保证连续不跳变
            _particleSpeedFrom = _particleSpeedCurrent;
            _particleSpeedTarget = _forward ? particleSpeedMax : particleSpeedMin;
        }
    }

    // ── 包围盒扩展 ──────────────────────────────────────────────

    /// 保存原始 localBounds（首次调用时缓存，后续不覆盖）
    private void SaveOriginalBounds()
    {
        if (_originalBoundsSaved || targetRenderer == null) return;
        if (targetRenderer is SkinnedMeshRenderer smr)
        {
            _originalBounds = smr.localBounds;
            _originalBoundsSaved = true;
        }
    }

    /// 基于原始 bounds center 对称扩展 extents，不偏移中心点
    public void ApplyBoundsExpand()
    {
        if (boundsExpand <= 0f || targetRenderer == null) return;
        if (targetRenderer is SkinnedMeshRenderer smr)
        {
            SaveOriginalBounds();
            // 始终从原始 bounds 出发扩展，避免多次调用累积偏移
            var b = _originalBounds;
            b.extents += Vector3.one * (boundsExpand * 0.5f);
            // center 保持原始值不变，不会偏移坐标轴
            smr.localBounds = b;
        }
    }

    /// 还原 localBounds 到原始值
    public void ResetBounds()
    {
        if (!_originalBoundsSaved || targetRenderer == null) return;
        if (targetRenderer is SkinnedMeshRenderer smr)
        {
            smr.localBounds = _originalBounds;
        }
    }

    /// 从 sharedMesh 重新读取 bounds 并还原（注意：mesh.bounds 的 center 是相对于 mesh 原点的，
    /// 可能与 SMR 的 localBounds 坐标空间不同，优先使用 Editor 的 FBX 重置功能）
    public void ResetBoundsFromMesh()
    {
        if (targetRenderer == null) return;
        if (targetRenderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
        {
            // 重新计算：将 mesh bounds 转换到以 rootBone 为参考的本地空间
            Bounds meshBounds = smr.sharedMesh.bounds;
            if (smr.rootBone != null)
            {
                // rootBone 存在时，localBounds 是相对于 rootBone 的
                // mesh.bounds 是相对于 mesh 原点的，需要考虑 rootBone 与 mesh transform 的偏移
                Vector3 rootLocalPos = smr.transform.InverseTransformPoint(smr.rootBone.position);
                meshBounds.center -= rootLocalPos;
            }
            smr.localBounds = meshBounds;
            _originalBounds = meshBounds;
            _originalBoundsSaved = true;
        }
    }

    /// 用指定的 Bounds 还原 localBounds（由 Editor 从 FBX 原始资产读取后传入）
    public void ResetBoundsTo(Bounds bounds)
    {
        if (targetRenderer == null) return;
        if (targetRenderer is SkinnedMeshRenderer smr)
        {
            smr.localBounds = bounds;
            _originalBounds = bounds;
            _originalBoundsSaved = true;
        }
    }

    // ── 顶点追踪 ──────────────────────────────────────────────────

    public Vector3 GetVertexWorldPosition(int vertexIndex)
    {
        Mesh mesh = GetMesh();
        Transform meshTransform = GetMeshTransform();
        if (mesh == null || meshTransform == null) return Vector3.zero;

        if (vertexIndex < 0 || vertexIndex >= mesh.vertexCount)
        {
            Debug.LogWarning($"[BlendShapeAnimator] 顶点索引 {vertexIndex} 超出范围（共 {mesh.vertexCount} 个顶点）");
            return Vector3.zero;
        }

        // 在访问 vertices 之前先检查 Read/Write 是否启用
#if UNITY_EDITOR
        // 通过尝试访问来检测 isReadable 状态
        Vector3[] testVerts = null;
        try
        {
            testVerts = mesh.vertices;
        }
        catch (System.Exception)
        {
            // 如果访问失败，说明 Read/Write 未启用，尝试自动修复
            TryFixReadWriteEnabled(mesh);
            return Vector3.zero;
        }

        if (testVerts == null || testVerts.Length == 0)
        {
            TryFixReadWriteEnabled(mesh);
            return Vector3.zero;
        }
#else
        // 运行时直接检查
        if (!mesh.isReadable)
        {
            Debug.LogError($"[BlendShapeAnimator] Mesh '{mesh.name}' 的 Read/Write 未启用，请在模型导入设置中勾选 Read/Write Enabled。");
            return Vector3.zero;
        }
#endif

        Vector3[] baseVerts = mesh.vertices;

        if (vertexIndex >= baseVerts.Length) return Vector3.zero;

        Vector3 localPos;

        // SkinnedMeshRenderer 运行时用 BakeMesh 获取蒙皮结果（缓存复用）
        if (Application.isPlaying && targetRenderer is SkinnedMeshRenderer smr)
        {
            if (_bakedMeshCache == null)
                _bakedMeshCache = new Mesh();
            smr.BakeMesh(_bakedMeshCache);
            Vector3[] bakedVerts = _bakedMeshCache.vertices;
            localPos = vertexIndex < bakedVerts.Length ? bakedVerts[vertexIndex] : baseVerts[vertexIndex];
        }
        else
        {
            // 普通 MeshFilter 或编辑器模式：基础顶点 + BlendShape 偏移
            localPos = baseVerts[vertexIndex];

            if (targetRenderer is SkinnedMeshRenderer smrEdit)
            {
                int bsCount = mesh.blendShapeCount;
                for (int si = 0; si < bsCount; si++)
                {
                    float weight = smrEdit.GetBlendShapeWeight(si) / 100f;
                    if (weight <= 0f) continue;
                    int frameIndex = mesh.GetBlendShapeFrameCount(si) - 1;
                    var dv = new Vector3[baseVerts.Length];
                    var dn = new Vector3[baseVerts.Length];
                    var dt = new Vector3[baseVerts.Length];
                    mesh.GetBlendShapeFrameVertices(si, frameIndex, dv, dn, dt);
                    localPos += dv[vertexIndex] * weight;
                }
            }
        }

        return meshTransform.TransformPoint(localPos);
    }

    private void UpdateVertexTracker()
    {
        if (vertexTracker == null) return;
        try
        {
            Vector3 newPos = GetVertexWorldPosition(trackVertexIndex);
            if (ignoreTrackerZ)
                newPos.y = vertexTracker.position.y;
            vertexTracker.position = newPos;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BlendShapeAnimator] UpdateVertexTracker 异常：{e.Message}");
        }
    }

    public void CreateVertexTracker()
    {
        GameObject go = new GameObject($"VertexTracker_{trackVertexIndex}");
        go.transform.position = GetVertexWorldPosition(trackVertexIndex);
        vertexTracker = go.transform;
        Debug.Log($"[BlendShapeAnimator] 已创建追踪器：{go.name}，位置：{go.transform.position}");
    }

    // ── 公开控制接口 ──────────────────────────────────────────────

    public void Play()
    {
        _playing = true; _timer = 0f; _forward = true;
        _particleSpeedCurrent = particleSpeedMin;
        _particleSpeedFrom    = particleSpeedMin;
        _particleSpeedTarget  = particleSpeedMax;
    }
    public void Stop()   => _playing = false;
    public void Pause()  => _playing = false;
    public void Resume() => _playing = true;

    // ── BlendShape 驱动（仅 SkinnedMeshRenderer）─────────────────

    private void SetBlendShape(float value)
    {
        if (!(targetRenderer is SkinnedMeshRenderer smr)) return;
        int count = smr.sharedMesh.blendShapeCount;
        if (count == 0) return;
        if (blendShapeIndex < 0)
            for (int i = 0; i < count; i++) smr.SetBlendShapeWeight(i, value);
        else if (blendShapeIndex < count)
            smr.SetBlendShapeWeight(blendShapeIndex, value);
    }

    // 每段动画独立 EaseOut：从上一段结束时的实际速度平滑过渡到目标值，两个方向都先快后慢
    private void SetParticleStartSpeed(float t, bool forward)
    {
        if (targetParticle == null) return;
        float ease = Mathf.Max(forward ? particleSpeedEaseForward : particleSpeedEaseBack, 0.1f);
        float eased = 1f - Mathf.Pow(1f - t, ease);
        _particleSpeedCurrent = Mathf.Lerp(_particleSpeedFrom, _particleSpeedTarget, eased);
        var main = targetParticle.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(_particleSpeedCurrent);
    }

    private void OnDestroy()
    {
        if (_bakedMeshCache != null)
        {
            if (Application.isPlaying)
                Destroy(_bakedMeshCache);
            else
                DestroyImmediate(_bakedMeshCache);
            _bakedMeshCache = null;
        }
    }
}
} // namespace Vic.Runtime
