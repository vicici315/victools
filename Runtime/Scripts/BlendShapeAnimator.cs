// BlendShapeAnimator v1.4 - 新增 ParticleSystem startSpeed 随启用动画线性插值控制
// BlendShapeAnimator v1.5 - 新增 粒子系统控制水花发射速度匹配水柱抛物线；添加 关闭动画手动设置进度参数

//（BlendShaper混合变形顶点定位脚本）
using UnityEngine;

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

    // ── 辅助：统一获取 Mesh 和 Transform ─────────────────────────

    private Mesh GetMesh()
    {
        if (targetRenderer is SkinnedMeshRenderer smr) return smr.sharedMesh;
        var mf = targetRenderer != null ? targetRenderer.GetComponent<MeshFilter>() : null;
        return mf != null ? mf.sharedMesh : null;
    }

    private Transform GetMeshTransform() => targetRenderer != null ? targetRenderer.transform : null;

    // ── Unity 生命周期 ────────────────────────────────────────────

    private void Awake()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();
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

        Vector3[] baseVerts = mesh.vertices;
        if (baseVerts == null || baseVerts.Length == 0)
        {
            Debug.LogWarning($"[BlendShapeAnimator] Mesh '{mesh.name}' 的 Read/Write 未启用，请在模型导入设置中勾选");
            return Vector3.zero;
        }

        if (vertexIndex >= baseVerts.Length) return Vector3.zero;

        Vector3 localPos;

        // SkinnedMeshRenderer 运行时用 BakeMesh 获取蒙皮结果
        if (Application.isPlaying && targetRenderer is SkinnedMeshRenderer smr)
        {
            Mesh baked = new Mesh();
            smr.BakeMesh(baked);
            Vector3[] bakedVerts = baked.vertices;
            localPos = vertexIndex < bakedVerts.Length ? bakedVerts[vertexIndex] : baseVerts[vertexIndex];
            DestroyImmediate(baked);
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
}
