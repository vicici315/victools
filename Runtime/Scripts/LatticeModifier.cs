// LatticeModifier 1.0 FFD 晶格变形场，晶格挂在独立空物体上，目标对象拖入 targetRenderer
// LatticeModifier 1.1 移动晶格或模型时，处于晶格范围内的顶点实时变形，离开后恢复原形
// LatticeModifier 1.2 支持子物体控制点（CP_x_y_z），可被 Animation/Timeline K帧驱动变形
// LatticeModifier 1.3 选中晶格点时同步选中 Hierarchy 中对应 CP 节点
// LatticeModifier 1.4 静态 SceneView 回调，选中 CP 后晶格线框持续绘制；修复打包后动画不生效
// LatticeModifier 2.0 支持单个模型或整个预设/带蒙皮角色，新增多目标模式自动收集所有子 Renderer
// LatticeModifier 2.1 添加删除晶格功能（还原 Mesh 并删除晶格物体），添加目标时自动识别带骨骼角色父级
// LatticeModifier 2.2 支持不可读 Mesh（通过 Instantiate/BakeMesh 自动获取可读副本），修复只收集部分 Renderer 的问题
// LatticeModifier 2.3 SkinnedMeshRenderer 双缓冲 Mesh 交替赋值，保留骨骼动画；重新初始化可保留晶格编辑恢复控制
// LatticeModifier 2.4 修复运行时晶格变形失效：OnEnable 自动重建变形 Mesh 管线，保留控制点，动画与晶格叠加生效
// LatticeModifier 2.5 新增手动指定 Renderer 列表（manualRenderers），支持多选对象创建晶格，严格按列表变形不展开子级
// LatticeModifier 2.6 重新初始化保留控制点不再重置；运行/停止游戏自动重建 Mesh 管线；脏标记+顶点缓存优化编辑器性能
// LatticeModifier 2.7 安全 Mesh 销毁机制：只销毁 _LatticeDeform 变形副本，防止共享 Mesh 资源被误删导致模型消失
// LatticeModifier 2.8 重写烘焙晶格变形功能，解决mesh丢失bug
// LatticeModifier 2.9 3D视图选中同步：注册 Selection.selectionChanged，选中 CP 节点时遍历控制点找到对应索引
// LatticeModifier 2.10 添加"扩展选择"按钮，可以扩展选择表面晶格控制点
// LatticeModifier 2.11 修复 Undo 操作可能导致 Renderer 上的 Mesh 引用被恢复为 originalMesh 或 null
// LatticeModifier 3.0 重构：引入 DeformTarget 封装单 Renderer 变形管线，消除 Single/Multi 大量重复逻辑
// LatticeModifier 3.1 新增 RepairMissingBindings()：多目标模式下自动检测并修复 manualRenderers/targetRoot 中丢失晶格绑定的 Renderer

using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class LatticeModifier : MonoBehaviour
{
    public enum TargetMode { SingleRenderer, MultiRenderer }

    #region 序列化字段

    [Header("目标模式")]
    public TargetMode targetMode = TargetMode.SingleRenderer;

    [Header("单目标（拖入要变形的模型）")]
    public Renderer targetRenderer;

    [Header("多目标根节点（自动收集所有子 Renderer）")]
    public Transform targetRoot;

    [Header("手动指定目标 Renderer（优先于根节点自动收集）")]
    public List<Renderer> manualRenderers = new List<Renderer>();

    [Header("晶格段数（控制点数 = 段数 + 1）")]
    [Range(1, 8)] public int divisionsX = 2;
    [Range(1, 8)] public int divisionsY = 2;
    [Range(1, 8)] public int divisionsZ = 2;

    [Header("设置")]
    public bool liveUpdate = true;

    [HideInInspector] public Vector3[] controlPoints;
    [HideInInspector] [SerializeField] private Vector3[] initialControlPoints;
    [HideInInspector] [SerializeField] private Vector3 latticeMin;
    [HideInInspector] [SerializeField] private Vector3 latticeSize;
    [HideInInspector] [SerializeField] private bool initialized;
    [HideInInspector] [SerializeField] private Transform[] controlPointTransforms;

    [HideInInspector] [SerializeField] private List<DeformTarget> deformTargets = new List<DeformTarget>();

    #endregion

    #region DeformTarget - 封装单个 Renderer 变形管线

    [Serializable]
    private class DeformTarget
    {
        public Renderer renderer;
        public Mesh originalMesh;
        public Vector3[] originalVertices;
        public Mesh deformedMeshA;
        public Mesh deformedMeshB;
        public bool isSkinned;

        [NonSerialized] public Vector3[] vertCache;
        [NonSerialized] public bool useBufferB;
    }

    #endregion

    #region 属性

    public int PointCountX => divisionsX + 1;
    public int PointCountY => divisionsY + 1;
    public int PointCountZ => divisionsZ + 1;
    public int TotalPoints => PointCountX * PointCountY * PointCountZ;
    public bool IsInitialized => initialized;

    #endregion

    #region 脏标记与缓存

    [NonSerialized] private Vector3[] cachedControlPoints;
    [NonSerialized] private Matrix4x4 cachedLatticeMatrix;
    [NonSerialized] private Matrix4x4 cachedTargetMatrix;
    [NonSerialized] private bool isDirty = true;
    [NonSerialized] private bool runtimeInitialized;

    public void MarkDirty() { isDirty = true; }

    private bool CheckDirty()
    {
        if (isDirty) return true;

        Matrix4x4 curLattice = transform.localToWorldMatrix;
        if (curLattice != cachedLatticeMatrix)
        {
            cachedLatticeMatrix = curLattice;
            return true;
        }

        if (controlPoints != null && cachedControlPoints != null && controlPoints.Length == cachedControlPoints.Length)
        {
            for (int i = 0; i < controlPoints.Length; i++)
                if (controlPoints[i] != cachedControlPoints[i])
                    return true;
        }
        else return true;

        foreach (var dt in deformTargets)
        {
            if (dt.renderer == null) continue;
            Matrix4x4 curTarget = dt.renderer.transform.localToWorldMatrix;
            if (curTarget != cachedTargetMatrix)
            {
                cachedTargetMatrix = curTarget;
                return true;
            }
        }

        return false;
    }

    private void SaveSnapshot()
    {
        isDirty = false;
        if (controlPoints != null)
        {
            if (cachedControlPoints == null || cachedControlPoints.Length != controlPoints.Length)
                cachedControlPoints = new Vector3[controlPoints.Length];
            Array.Copy(controlPoints, cachedControlPoints, controlPoints.Length);
        }
        cachedLatticeMatrix = transform.localToWorldMatrix;
        if (deformTargets.Count > 0 && deformTargets[0].renderer != null)
            cachedTargetMatrix = deformTargets[0].renderer.transform.localToWorldMatrix;
    }

    #endregion

    #region 生命周期

    private void OnEnable()
    {
        if (!initialized || runtimeInitialized) return;

        RebuildDeformMeshes();
        runtimeInitialized = true;

        if (HasControlPointTransforms)
            SyncFromTransforms();

        isDirty = true;
        ApplyDeformation();
    }

    private void LateUpdate()
    {
        if (!initialized || !liveUpdate) return;
        if (HasControlPointTransforms)
            SyncFromTransforms();
        ApplyDeformation();
    }

    private void OnDestroy()
    {
        if (initialized)
        {
            foreach (var dt in deformTargets)
            {
                if (dt.renderer != null && dt.originalMesh != null)
                    SetRendererMesh(dt.renderer, dt.originalMesh);
            }
        }
        foreach (var dt in deformTargets)
        {
            SafeDestroyLatticeOnlyMesh(dt.deformedMeshA);
            SafeDestroyLatticeOnlyMesh(dt.deformedMeshB);
        }
    }

    #endregion

    #region 公共接口 - 初始化 / 重建 / 应用变形

    public void InitializeLattice()
    {
        if (initialized)
        {
            RebuildDeformMeshes();
            isDirty = true;
            ApplyDeformation();
            return;
        }

        RestoreOriginal();
        var renderers = CollectRenderers();
        if (renderers == null || renderers.Count == 0) return;

        deformTargets.Clear();
        foreach (var rend in renderers)
        {
            var dt = CreateDeformTarget(rend);
            if (dt != null)
                deformTargets.Add(dt);
        }

        if (deformTargets.Count == 0)
        {
            Debug.LogWarning("[LatticeModifier] 未找到有效的 Renderer");
            return;
        }

        ComputeBounds();
        GenerateControlPoints();
        initialized = true;
    }

    public void RebuildDeformMeshes()
    {
        foreach (var dt in deformTargets)
        {
            if (dt.renderer == null) continue;

            // 还原到原始 Mesh
            if (dt.originalMesh != null)
                SetRendererMesh(dt.renderer, dt.originalMesh);

            // 原始顶点丢失时重新读取
            if (dt.originalVertices == null || dt.originalVertices.Length == 0)
            {
                Mesh sharedMesh = GetRendererMesh(dt.renderer);
                if (sharedMesh == null) continue;
                Mesh readable = GetReadableMesh(dt.renderer);
                if (readable == null) continue;
                dt.originalVertices = readable.vertices;
                if (readable != sharedMesh) SafeDestroy(readable);
            }

            // 确保 originalMesh 引用有效
            if (dt.originalMesh == null || IsLatticeDeformMesh(dt.originalMesh))
            {
                Mesh shared = GetRendererMesh(dt.renderer);
                if (shared != null && !IsLatticeDeformMesh(shared))
                    dt.originalMesh = shared;
            }

            // 销毁旧变形 Mesh 并重建
            SafeDestroyLatticeOnlyMesh(dt.deformedMeshA);
            SafeDestroyLatticeOnlyMesh(dt.deformedMeshB);
            dt.deformedMeshA = CreateDeformMesh(dt.originalMesh, dt.originalVertices);
            dt.deformedMeshB = dt.isSkinned ? CreateDeformMesh(dt.originalMesh, dt.originalVertices) : null;
            SetRendererMesh(dt.renderer, dt.deformedMeshA);
            dt.useBufferB = false;
        }
    }

    public void ApplyDeformation()
    {
        if (!initialized) return;
        if (!EnsureDeformMeshesValid()) return;
        if (!CheckDirty()) return;

        foreach (var dt in deformTargets)
        {
            if (dt.renderer == null || dt.deformedMeshA == null || dt.originalVertices == null) continue;

            dt.useBufferB = !dt.useBufferB;
            Mesh dst = (dt.isSkinned && dt.useBufferB && dt.deformedMeshB != null) ? dt.deformedMeshB : dt.deformedMeshA;
            DeformVertices(dt.renderer.transform, dt.originalVertices, dst, ref dt.vertCache);
            if (dt.isSkinned)
                SetRendererMesh(dt.renderer, dst);
        }

        SaveSnapshot();
    }

    public void RefreshSourceMesh()
    {
        if (!initialized) return;

        foreach (var dt in deformTargets)
        {
            if (dt.renderer == null) continue;

            Mesh currentMesh = GetRendererMesh(dt.renderer);
            if (currentMesh == null) continue;

            if (IsLatticeDeformMesh(currentMesh))
            {
                if (dt.originalMesh == null) continue;
                SetRendererMesh(dt.renderer, dt.originalMesh);
                Mesh readable = dt.originalMesh.isReadable ? dt.originalMesh : GetReadableMesh(dt.renderer);
                if (readable != null)
                {
                    dt.originalVertices = readable.vertices;
                    if (readable != dt.originalMesh) SafeDestroy(readable);
                }
            }
            else
            {
                dt.originalMesh = currentMesh;
                Mesh readable = currentMesh.isReadable ? currentMesh : GetReadableMesh(dt.renderer);
                if (readable != null)
                {
                    dt.originalVertices = readable.vertices;
                    if (readable != currentMesh) SafeDestroy(readable);
                }
            }

            SafeDestroyLatticeOnlyMesh(dt.deformedMeshA);
            SafeDestroyLatticeOnlyMesh(dt.deformedMeshB);
            dt.deformedMeshA = CreateDeformMesh(dt.originalMesh, dt.originalVertices);
            dt.deformedMeshB = dt.isSkinned ? CreateDeformMesh(dt.originalMesh, dt.originalVertices) : null;
            SetRendererMesh(dt.renderer, dt.deformedMeshA);
            dt.useBufferB = false;
        }

        isDirty = true;
        ApplyDeformation();
        Debug.Log("[LatticeModifier] 源 Mesh 已刷新");
    }

    #endregion

    #region 公共接口 - 重置 / 还原 / 烘焙

    public void ResetControlPoints()
    {
        if (initialControlPoints == null) return;
        Array.Copy(initialControlPoints, controlPoints, controlPoints.Length);
        ApplyDeformation();
    }

    public void RestoreOriginal()
    {
        foreach (var dt in deformTargets)
        {
            if (dt.renderer != null && dt.originalMesh != null)
                SetRendererMesh(dt.renderer, dt.originalMesh);
            SafeDestroyLatticeOnlyMesh(dt.deformedMeshA);
            SafeDestroyLatticeOnlyMesh(dt.deformedMeshB);
        }
        deformTargets.Clear();
        initialized = false;
        controlPoints = null;
        initialControlPoints = null;
    }

    public void BakeAndRemove()
    {
        foreach (var dt in deformTargets)
        {
            // 只销毁 B 缓冲，A 由编辑器侧接管
            if (dt.deformedMeshB != null && IsLatticeDeformMesh(dt.deformedMeshB))
                DestroyImmediate(dt.deformedMeshB);
        }
        deformTargets.Clear();
        initialized = false;
        controlPoints = null;
        initialControlPoints = null;
    }

    #endregion

    #region 公共接口 - 获取 Renderer 列表

    public List<Renderer> GetActiveRenderers()
    {
        var list = new List<Renderer>();
        foreach (var dt in deformTargets)
        {
            if (dt.renderer != null)
                list.Add(dt.renderer);
        }
        return list;
    }

    /// 将新的 Renderer 链接到当前晶格（已初始化状态下追加目标）
    public bool LinkRenderer(Renderer rend)
    {
        if (!initialized || rend == null) return false;

        // 检查是否已存在
        foreach (var dt in deformTargets)
            if (dt.renderer == rend) return false;

        var newDt = CreateDeformTarget(rend);
        if (newDt == null) return false;

        deformTargets.Add(newDt);
        isDirty = true;
        ApplyDeformation();
        return true;
    }

    /// 批量链接 Renderer 列表到当前晶格
    public int LinkRenderers(IEnumerable<Renderer> renderers)
    {
        int count = 0;
        foreach (var rend in renderers)
        {
            if (LinkRenderer(rend))
                count++;
        }
        return count;
    }

    /// <summary>
    /// 修复多目标模式下丢失绑定的 Renderer。
    /// 检查 manualRenderers 列表（或 targetRoot 下的所有 Renderer）中
    /// 哪些没有在当前 deformTargets 中绑定，重新链接它们。
    /// 同时清理 deformTargets 中 renderer 已为 null 的无效条目。
    /// </summary>
    /// <returns>修复（重新链接）的 Renderer 数量</returns>
    public int RepairMissingBindings()
    {
        if (!initialized || targetMode != TargetMode.MultiRenderer) return 0;

        // 清理无效条目（renderer 已被删除的 DeformTarget）
        int removed = deformTargets.RemoveAll(dt => dt.renderer == null);
        if (removed > 0)
            Debug.Log($"[LatticeModifier] 已清理 {removed} 条无效绑定条目");

        // 收集应绑定的 Renderer 列表
        var expectedRenderers = new List<Renderer>();
        if (manualRenderers.Count > 0)
        {
            foreach (var r in manualRenderers)
                if (r != null && !expectedRenderers.Contains(r))
                    expectedRenderers.Add(r);
        }
        else if (targetRoot != null)
        {
            var all = targetRoot.GetComponentsInChildren<Renderer>(true);
            expectedRenderers.AddRange(all);
        }

        if (expectedRenderers.Count == 0) return 0;

        // 收集当前已绑定的 Renderer
        var boundRenderers = new HashSet<Renderer>();
        foreach (var dt in deformTargets)
        {
            if (dt.renderer != null)
                boundRenderers.Add(dt.renderer);
        }

        // 找到丢失绑定的并重新链接
        int repaired = 0;
        foreach (var rend in expectedRenderers)
        {
            if (!boundRenderers.Contains(rend))
            {
                if (LinkRenderer(rend))
                    repaired++;
            }
        }

        return repaired;
    }

    #endregion

    #region 动画控制点

    public bool HasControlPointTransforms =>
        controlPointTransforms != null && controlPointTransforms.Length > 0 && controlPointTransforms[0] != null;

    public Transform GetControlPointTransform(int index)
    {
        if (controlPointTransforms == null || index < 0 || index >= controlPointTransforms.Length) return null;
        return controlPointTransforms[index];
    }

    public void CreateControlPointTransforms()
    {
        if (!initialized || controlPoints == null) return;
        DestroyControlPointTransforms();
        controlPointTransforms = new Transform[controlPoints.Length];
        for (int i = 0; i < controlPoints.Length; i++)
        {
            GetPointIndex3D(i, out int ix, out int iy, out int iz);
            var go = new GameObject($"CP_{ix}_{iy}_{iz}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = controlPoints[i];
            controlPointTransforms[i] = go.transform;
        }
    }

    public void DestroyControlPointTransforms()
    {
        if (controlPointTransforms != null)
        {
            foreach (var t in controlPointTransforms)
                if (t != null) DestroyImmediate(t.gameObject);
            controlPointTransforms = null;
        }
    }

    public void SyncFromTransforms()
    {
        if (controlPointTransforms == null || controlPoints == null) return;
        for (int i = 0; i < controlPoints.Length && i < controlPointTransforms.Length; i++)
            if (controlPointTransforms[i] != null)
                controlPoints[i] = controlPointTransforms[i].localPosition;
    }

    public void SyncToTransforms()
    {
        if (controlPointTransforms == null || controlPoints == null) return;
        for (int i = 0; i < controlPoints.Length && i < controlPointTransforms.Length; i++)
            if (controlPointTransforms[i] != null)
                controlPointTransforms[i].localPosition = controlPoints[i];
    }

    #endregion

    #region 索引工具

    public void GetPointIndex3D(int flat, out int ix, out int iy, out int iz)
    {
        int nx = PointCountX;
        iz = flat / (nx * PointCountY);
        iy = (flat % (nx * PointCountY)) / nx;
        ix = flat % nx;
    }

    public int GetFlatIndex(int ix, int iy, int iz)
    {
        return ix + iy * PointCountX + iz * PointCountX * PointCountY;
    }

    #endregion

    #region 内部 - 收集 Renderer

    private List<Renderer> CollectRenderers()
    {
        if (targetMode == TargetMode.SingleRenderer)
        {
            if (targetRenderer == null)
            {
                Debug.LogWarning("[LatticeModifier] 请先指定目标对象");
                return null;
            }
            return new List<Renderer> { targetRenderer };
        }

        // 多目标模式
        if (manualRenderers.Count > 0)        {
            var valid = new List<Renderer>();
            foreach (var r in manualRenderers)
                if (r != null && !valid.Contains(r))
                    valid.Add(r);
            return valid.Count > 0 ? valid : null;
        }

        if (targetRoot != null)
        {
            var all = targetRoot.GetComponentsInChildren<Renderer>(true);
            return all.Length > 0 ? new List<Renderer>(all) : null;
        }

        Debug.LogWarning("[LatticeModifier] 请先指定多目标根节点或手动添加 Renderer");
        return null;
    }

    #endregion

    #region 内部 - DeformTarget 工厂

    private DeformTarget CreateDeformTarget(Renderer rend)
    {
        Mesh sharedMesh = GetRendererMesh(rend);
        if (sharedMesh == null) return null;

        Mesh readable = GetReadableMesh(rend);
        if (readable == null)
        {
            Debug.LogWarning($"[LatticeModifier] Mesh on '{rend.name}' 无法读取，已跳过");
            return null;
        }

        var dt = new DeformTarget
        {
            renderer = rend,
            originalMesh = sharedMesh,
            originalVertices = readable.vertices,
            isSkinned = rend is SkinnedMeshRenderer
        };

        if (readable != sharedMesh) SafeDestroy(readable);

        dt.deformedMeshA = CreateDeformMesh(dt.originalMesh, dt.originalVertices);
        dt.deformedMeshB = dt.isSkinned ? CreateDeformMesh(dt.originalMesh, dt.originalVertices) : null;
        SetRendererMesh(rend, dt.deformedMeshA);

        return dt;
    }

    #endregion

    #region 内部 - 包围盒计算

    private void ComputeBounds()
    {
        Bounds bounds = new Bounds();
        bool first = true;
        foreach (var dt in deformTargets)
        {
            if (dt.renderer == null || dt.originalVertices == null) continue;
            Transform targetT = dt.renderer.transform;
            foreach (var v in dt.originalVertices)
            {
                Vector3 lp = transform.InverseTransformPoint(targetT.TransformPoint(v));
                if (first) { bounds = new Bounds(lp, Vector3.zero); first = false; }
                else bounds.Encapsulate(lp);
            }
        }
        bounds.Expand(bounds.size * 0.02f);
        latticeMin = bounds.min;
        latticeSize = bounds.size;
    }

    #endregion

    #region 内部 - 控制点生成与数学

    private void GenerateControlPoints()
    {
        int total = TotalPoints;
        controlPoints = new Vector3[total];
        initialControlPoints = new Vector3[total];
        for (int ix = 0; ix < PointCountX; ix++)
        for (int iy = 0; iy < PointCountY; iy++)
        for (int iz = 0; iz < PointCountZ; iz++)
        {
            int idx = GetFlatIndex(ix, iy, iz);
            Vector3 p = new Vector3(
                latticeMin.x + latticeSize.x * ix / divisionsX,
                latticeMin.y + latticeSize.y * iy / divisionsY,
                latticeMin.z + latticeSize.z * iz / divisionsZ);
            controlPoints[idx] = p;
            initialControlPoints[idx] = p;
        }
    }

    private static int Binomial(int n, int k)
    {
        if (k < 0 || k > n) return 0;
        if (k == 0 || k == n) return 1;
        int r = 1;
        for (int i = 0; i < k; i++) r = r * (n - i) / (i + 1);
        return r;
    }

    private static float Bernstein(int i, int n, float t)
    {
        return Binomial(n, i) * Mathf.Pow(t, i) * Mathf.Pow(1f - t, n - i);
    }

    #endregion

    #region 内部 - 变形核心

    private void DeformVertices(Transform targetT, Vector3[] srcVerts, Mesh dstMesh, ref Vector3[] vertCache)
    {
        if (dstMesh == null || srcVerts == null) return;

        int nx = PointCountX, ny = PointCountY, nz = PointCountZ;
        int l = divisionsX, m = divisionsY, n = divisionsZ;
        Matrix4x4 curLatticeW2L = transform.worldToLocalMatrix;
        Matrix4x4 curTargetL2W = targetT.localToWorldMatrix;

        if (vertCache == null || vertCache.Length != srcVerts.Length)
            vertCache = new Vector3[srcVerts.Length];

        float[] bxArr = new float[nx];
        float[] byArr = new float[ny];
        float[] bzArr = new float[nz];

        for (int v = 0; v < srcVerts.Length; v++)
        {
            Vector3 worldPos = curTargetL2W.MultiplyPoint3x4(srcVerts[v]);
            Vector3 latticeLocal = curLatticeW2L.MultiplyPoint3x4(worldPos);

            float s = latticeSize.x > 0 ? (latticeLocal.x - latticeMin.x) / latticeSize.x : 0;
            float t = latticeSize.y > 0 ? (latticeLocal.y - latticeMin.y) / latticeSize.y : 0;
            float u = latticeSize.z > 0 ? (latticeLocal.z - latticeMin.z) / latticeSize.z : 0;

            if (s < -0.01f || s > 1.01f || t < -0.01f || t > 1.01f || u < -0.01f || u > 1.01f)
            {
                vertCache[v] = srcVerts[v];
                continue;
            }

            s = Mathf.Clamp01(s);
            t = Mathf.Clamp01(t);
            u = Mathf.Clamp01(u);

            for (int ix = 0; ix < nx; ix++) bxArr[ix] = Bernstein(ix, l, s);
            for (int iy = 0; iy < ny; iy++) byArr[iy] = Bernstein(iy, m, t);
            for (int iz = 0; iz < nz; iz++) bzArr[iz] = Bernstein(iz, n, u);

            Vector3 initPos = Vector3.zero;
            Vector3 deformedPos = Vector3.zero;

            for (int ix = 0; ix < nx; ix++)
            {
                float bx = bxArr[ix];
                for (int iy = 0; iy < ny; iy++)
                {
                    float bxy = bx * byArr[iy];
                    for (int iz = 0; iz < nz; iz++)
                    {
                        float w = bxy * bzArr[iz];
                        int idx = GetFlatIndex(ix, iy, iz);
                        initPos += w * initialControlPoints[idx];
                        deformedPos += w * controlPoints[idx];
                    }
                }
            }

            Vector3 offset = deformedPos - initPos;
            Vector3 worldOffset = transform.TransformVector(offset);
            Vector3 localOffset = targetT.InverseTransformVector(worldOffset);
            vertCache[v] = srcVerts[v] + localOffset;
        }

        dstMesh.vertices = vertCache;
        dstMesh.RecalculateBounds();
    }

    private bool EnsureDeformMeshesValid()
    {
        foreach (var dt in deformTargets)
        {
            if (dt.renderer == null || dt.originalMesh == null || dt.originalVertices == null) continue;

            bool meshLost = dt.deformedMeshA == null;

            if (!meshLost)
            {
                Mesh currentMesh = GetRendererMesh(dt.renderer);
                if (currentMesh == null || (!IsLatticeDeformMesh(currentMesh) && currentMesh != dt.originalMesh))
                {
                    meshLost = true;
                }
                else if (currentMesh == dt.originalMesh)
                {
                    SetRendererMesh(dt.renderer, dt.deformedMeshA);
                    isDirty = true;
                }
            }

            if (meshLost)
            {
                SafeDestroyLatticeOnlyMesh(dt.deformedMeshA);
                SafeDestroyLatticeOnlyMesh(dt.deformedMeshB);
                dt.deformedMeshA = CreateDeformMesh(dt.originalMesh, dt.originalVertices);
                dt.deformedMeshB = dt.isSkinned ? CreateDeformMesh(dt.originalMesh, dt.originalVertices) : null;
                SetRendererMesh(dt.renderer, dt.deformedMeshA);
                dt.useBufferB = false;
                isDirty = true;
            }
        }

        return deformTargets.Count > 0;
    }

    #endregion

    #region 内部 - Mesh 工具方法

    private static readonly string LatticeDeformSuffix = "_LatticeDeform_";

    private Mesh CreateDeformMesh(Mesh src, Vector3[] vertices)
    {
        if (src == null || vertices == null) return null;

        string uniqueName = src.name + LatticeDeformSuffix + GetInstanceID();

        if (src.isReadable)
        {
            Mesh nm = Instantiate(src);
            nm.name = uniqueName;
            nm.hideFlags = HideFlags.HideAndDontSave;
            nm.MarkDynamic();
            return nm;
        }

        // 源 Mesh 不可读，手动构建
        Mesh mesh = new Mesh { name = uniqueName };        mesh.hideFlags = HideFlags.HideAndDontSave;
        mesh.vertices = vertices;
        try
        {
            mesh.subMeshCount = src.subMeshCount;
            var dataArr = Mesh.AcquireReadOnlyMeshData(src);
            var data = dataArr[0];
            for (int s = 0; s < data.subMeshCount; s++)
            {
                var desc = data.GetSubMesh(s);
                using var idxNative = new Unity.Collections.NativeArray<int>(desc.indexCount, Unity.Collections.Allocator.Temp);
                data.GetIndices(idxNative, s);
                mesh.SetTriangles(idxNative.ToArray(), s);
            }
            var normNative = new Unity.Collections.NativeArray<Vector3>(data.vertexCount, Unity.Collections.Allocator.Temp);
            data.GetNormals(normNative);
            mesh.normals = normNative.ToArray(); normNative.Dispose();
            var tanNative = new Unity.Collections.NativeArray<Vector4>(data.vertexCount, Unity.Collections.Allocator.Temp);
            data.GetTangents(tanNative);
            mesh.tangents = tanNative.ToArray(); tanNative.Dispose();
            var uvNative = new Unity.Collections.NativeArray<Vector2>(data.vertexCount, Unity.Collections.Allocator.Temp);
            data.GetUVs(0, uvNative); mesh.uv = uvNative.ToArray();
            data.GetUVs(1, uvNative); mesh.uv2 = uvNative.ToArray();
            uvNative.Dispose();
            dataArr.Dispose();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LatticeModifier] 构建不可读 Mesh '{src.name}' 的变形副本时部分数据读取失败: {ex.Message}");
        }
        mesh.RecalculateBounds();
        mesh.MarkDynamic();
        return mesh;
    }

    private bool IsLatticeDeformMesh(Mesh mesh)
    {
        if (mesh == null) return false;
        return mesh.name.Contains(LatticeDeformSuffix + GetInstanceID());
    }

    private Mesh GetReadableMesh(Renderer rend)
    {
        Mesh srcMesh = GetRendererMesh(rend);
        if (srcMesh == null) return null;
        if (srcMesh.isReadable) return srcMesh;
        try
        {
            var c = Instantiate(srcMesh);
            c.name = srcMesh.name;
            if (c.vertexCount > 0) return c;
        }
        catch { }
        if (rend is SkinnedMeshRenderer smr)
        {
            try
            {
                Mesh b = new Mesh();
                smr.BakeMesh(b);
                b.name = srcMesh.name + "_Baked";
                if (b.vertexCount > 0) return b;
            }
            catch { }
        }
        return null;
    }

    public static Mesh GetRendererMeshStatic(Renderer rend) => GetRendererMesh(rend);

    private static Mesh GetRendererMesh(Renderer rend)
    {
        if (rend is SkinnedMeshRenderer smr) return smr.sharedMesh;
        var mf = rend.GetComponent<MeshFilter>();
        return mf != null ? mf.sharedMesh : null;
    }

    private static void SetRendererMesh(Renderer rend, Mesh mesh)
    {
        if (rend is SkinnedMeshRenderer smr) { smr.sharedMesh = mesh; return; }
        var mf = rend.GetComponent<MeshFilter>();
        if (mf != null) mf.sharedMesh = mesh;
    }

    private void SafeDestroyLatticeOnlyMesh(Mesh mesh)
    {
        if (mesh == null || !IsLatticeDeformMesh(mesh)) return;
        if (Application.isPlaying) Destroy(mesh);
        else DestroyImmediate(mesh);
    }

    private static void SafeDestroy(UnityEngine.Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    #endregion
}
