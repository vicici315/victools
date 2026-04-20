// LatticeModifier 1.0 FFD 晶格变形场，晶格挂在独立空物体上，目标对象拖入 targetRenderer
// LatticeModifier 1.1 移动晶格或模型时，处于晶格范围内的顶点实时变形，离开后恢复原形
// LatticeModifier 1.2 支持子物体控制点（CP_x_y_z），可被 Animation/Timeline K帧驱动变形
// LatticeModifier 1.3 选中晶格点时同步选中 Hierarchy 中对应 CP 节点
// LatticeModifier 1.4 静态 SceneView 回调，选中 CP 后晶格线框持续绘制；修复打包后动画不生效
// LatticeModifier 2.0 支持单个模型或整个预设/带蒙皮角色，新增多目标模式自动收集所有子 Renderer
// LatticeModifier 2.1 添加删除晶格功能（还原 Mesh 并删除晶格物体），添加目标时自动识别带骨骼角色父级
// LatticeModifier 2.2 支持不可读 Mesh（通过 Instantiate/BakeMesh 自动获取可读副本），修复只收集部分 Renderer 的问题
// LatticeModifier 2.3 SkinnedMeshRenderer 双缓冲 Mesh 交替赋值，保留骨骼动画；重新初始化可保留晶格编辑恢复控制
using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class LatticeModifier : MonoBehaviour
{
    public enum TargetMode { SingleRenderer, MultiRenderer }

    [Header("目标模式")]
    public TargetMode targetMode = TargetMode.SingleRenderer;

    [Header("单目标（拖入要变形的模型）")]
    public Renderer targetRenderer;

    [Header("多目标根节点（自动收集所有子 Renderer）")]
    public Transform targetRoot;

    [Header("晶格段数（控制点数 = 段数 + 1）")]
    [Range(1, 8)] public int divisionsX = 2;
    [Range(1, 8)] public int divisionsY = 2;
    [Range(1, 8)] public int divisionsZ = 2;

    [Header("设置")]
    public bool liveUpdate = true;
    [Tooltip("使用子物体作为控制点（支持 Animation/Timeline K帧）")]
    public bool useTransformHandles = false;

    [HideInInspector] public Vector3[] controlPoints;
    [HideInInspector] [SerializeField] private Vector3[] initialControlPoints;
    [HideInInspector] [SerializeField] private Vector3 latticeMin;
    [HideInInspector] [SerializeField] private Vector3 latticeSize;
    [HideInInspector] [SerializeField] private bool initialized;
    [HideInInspector] [SerializeField] private Transform[] controlPointTransforms;

    // ── 单目标数据 ──
    [HideInInspector] [SerializeField] private Vector3[] originalVertices;
    [HideInInspector] [SerializeField] private Mesh originalMesh;

    // ── 多目标数据 ──
    [HideInInspector] [SerializeField] private List<Renderer> targetRenderers = new List<Renderer>();
    [HideInInspector] [SerializeField] private List<Mesh> originalMeshes = new List<Mesh>();
    [HideInInspector] [SerializeField] private List<Vector3[]> originalVerticesList = new List<Vector3[]>();

    // ── 变形 Mesh（序列化，跨 Play 模式保持）──
    // 对 SMR 使用双缓冲：交替赋值两个 Mesh 强制 GPU 刷新
    [HideInInspector] [SerializeField] private Mesh deformedMeshA;
    [HideInInspector] [SerializeField] private List<Mesh> deformedMeshesA = new List<Mesh>();
    [HideInInspector] [SerializeField] private Mesh deformedMeshB;
    [HideInInspector] [SerializeField] private List<Mesh> deformedMeshesB = new List<Mesh>();
    [HideInInspector] [SerializeField] private List<bool> isSkinned = new List<bool>();
    [HideInInspector] [SerializeField] private bool singleIsSkinned;

    [NonSerialized] private bool useBufferB;
    [NonSerialized] private bool runtimeInitialized;

    // ── 优化：脏标记 & 缓存 ──
    [NonSerialized] private Vector3[] cachedControlPoints;
    [NonSerialized] private Matrix4x4 cachedLatticeMatrix;
    [NonSerialized] private Matrix4x4 cachedTargetMatrix;
    [NonSerialized] private bool isDirty = true;
    [NonSerialized] private Vector3[] singleVertCache;
    [NonSerialized] private List<Vector3[]> multiVertCaches = new List<Vector3[]>();

    public int PointCountX => divisionsX + 1;
    public int PointCountY => divisionsY + 1;
    public int PointCountZ => divisionsZ + 1;
    public int TotalPoints => PointCountX * PointCountY * PointCountZ;
    public bool IsInitialized => initialized;

    /// 标记需要重新计算变形
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

        if (targetMode == TargetMode.SingleRenderer && targetRenderer != null)
        {
            Matrix4x4 curTarget = targetRenderer.transform.localToWorldMatrix;
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
        if (targetMode == TargetMode.SingleRenderer && targetRenderer != null)
            cachedTargetMatrix = targetRenderer.transform.localToWorldMatrix;
    }

    public List<Renderer> GetActiveRenderers()
    {
        if (targetMode == TargetMode.SingleRenderer)
        {
            var list = new List<Renderer>();
            if (targetRenderer != null) list.Add(targetRenderer);
            return list;
        }
        return new List<Renderer>(targetRenderers);
    }

    // ═══════════════════════════════════════════
    //  OnEnable
    // ═══════════════════════════════════════════
    private void OnEnable()
    {
        if (!initialized) return;
        if (runtimeInitialized) return;

        if (targetMode == TargetMode.SingleRenderer)
        {
            if (targetRenderer != null && deformedMeshA != null)
                SetRendererMesh(targetRenderer, deformedMeshA);
        }
        else
        {
            for (int i = 0; i < targetRenderers.Count; i++)
            {
                if (targetRenderers[i] == null) continue;
                if (i < deformedMeshesA.Count && deformedMeshesA[i] != null)
                    SetRendererMesh(targetRenderers[i], deformedMeshesA[i]);
            }
        }

        runtimeInitialized = true;

        if (useTransformHandles && HasControlPointTransforms)
            SyncFromTransforms();

        ApplyDeformation();
    }

    // ═══════════════════════════════════════════
    //  初始化
    // ═══════════════════════════════════════════
    public void InitializeLattice()
    {
        if (targetMode == TargetMode.SingleRenderer) InitSingle();
        else InitMulti();
    }

    private void InitSingle()
    {
        if (targetRenderer == null) { Debug.LogWarning("请先指定目标对象"); return; }
        RestoreOriginal();

        Mesh sharedMesh = GetRendererMesh(targetRenderer);
        if (sharedMesh == null) { Debug.LogWarning("目标对象没有有效的 Mesh"); return; }

        singleIsSkinned = targetRenderer is SkinnedMeshRenderer;

        Mesh readableMesh = sharedMesh.isReadable ? sharedMesh : GetReadableMesh(targetRenderer);
        if (readableMesh == null)
        {
            Debug.LogError($"Mesh '{sharedMesh.name}' 无法读取顶点数据");
            return;
        }

        originalMesh = sharedMesh;
        originalVertices = readableMesh.vertices;
        if (readableMesh != sharedMesh) DestroyImmediate(readableMesh);

        deformedMeshA = CreateDeformMesh(originalMesh, originalVertices);
        if (singleIsSkinned)
            deformedMeshB = CreateDeformMesh(originalMesh, originalVertices);
        SetRendererMesh(targetRenderer, deformedMeshA);

        ComputeBoundsFromVertices(targetRenderer.transform, originalVertices);
        GenerateControlPoints();
        initialized = true;
    }

    private void InitMulti()
    {
        if (targetRoot == null) { Debug.LogWarning("请先指定多目标根节点"); return; }
        RestoreOriginal();

        var renderers = targetRoot.GetComponentsInChildren<Renderer>(true);
        var vR = new List<Renderer>();
        var vM = new List<Mesh>();
        var vV = new List<Vector3[]>();
        var vS = new List<bool>();

        foreach (var rend in renderers)
        {
            Mesh sharedMesh = GetRendererMesh(rend);
            if (sharedMesh == null) continue;
            Mesh readableMesh = GetReadableMesh(rend);
            if (readableMesh == null)
            {
                Debug.LogWarning($"Mesh on '{rend.name}' 无法读取，已跳过");
                continue;
            }
            vR.Add(rend);
            vM.Add(sharedMesh);
            vV.Add(readableMesh.vertices);
            vS.Add(rend is SkinnedMeshRenderer);
            if (readableMesh != sharedMesh) DestroyImmediate(readableMesh);
        }

        if (vR.Count == 0) { Debug.LogWarning("根节点下没有找到有效的 Renderer"); return; }

        targetRenderers = vR;
        originalMeshes = vM;
        originalVerticesList = vV;
        isSkinned = vS;

        deformedMeshesA.Clear();
        deformedMeshesB.Clear();
        for (int i = 0; i < targetRenderers.Count; i++)
        {
            var dmA = CreateDeformMesh(originalMeshes[i], originalVerticesList[i]);
            deformedMeshesA.Add(dmA);
            deformedMeshesB.Add(isSkinned[i] ? CreateDeformMesh(originalMeshes[i], originalVerticesList[i]) : null);
            SetRendererMesh(targetRenderers[i], dmA);
        }

        ComputeBoundsFromAllRenderers();
        GenerateControlPoints();
        initialized = true;
    }

    // ═══════════════════════════════════════════
    //  包围盒
    // ═══════════════════════════════════════════
    private void ComputeBoundsFromVertices(Transform targetT, Vector3[] verts)
    {
        Bounds bounds = new Bounds();
        bool first = true;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 lp = transform.InverseTransformPoint(targetT.TransformPoint(verts[i]));
            if (first) { bounds = new Bounds(lp, Vector3.zero); first = false; }
            else bounds.Encapsulate(lp);
        }
        bounds.Expand(bounds.size * 0.02f);
        latticeMin = bounds.min;
        latticeSize = bounds.size;
    }

    private void ComputeBoundsFromAllRenderers()
    {
        Bounds bounds = new Bounds();
        bool first = true;
        for (int ri = 0; ri < targetRenderers.Count; ri++)
        {
            Transform targetT = targetRenderers[ri].transform;
            Vector3[] verts = originalVerticesList[ri];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 lp = transform.InverseTransformPoint(targetT.TransformPoint(verts[i]));
                if (first) { bounds = new Bounds(lp, Vector3.zero); first = false; }
                else bounds.Encapsulate(lp);
            }
        }
        bounds.Expand(bounds.size * 0.02f);
        latticeMin = bounds.min;
        latticeSize = bounds.size;
    }

    // ═══════════════════════════════════════════
    //  Mesh 工具
    // ═══════════════════════════════════════════
    private Mesh CreateDeformMesh(Mesh src, Vector3[] vertices)
    {
        try
        {
            var m = Instantiate(src);
            m.name = src.name + "_LatticeDeform";
            m.MarkDynamic();
            var _ = m.vertices;
            return m;
        }
        catch { }

        var nm = new Mesh { name = src.name + "_LatticeDeform" };
        nm.vertices = vertices;
        try { nm.subMeshCount = src.subMeshCount; for (int s = 0; s < src.subMeshCount; s++) nm.SetTriangles(src.GetTriangles(s), s); } catch { }
        try
        {
            if (src.normals?.Length > 0) nm.normals = src.normals;
            if (src.tangents?.Length > 0) nm.tangents = src.tangents;
            if (src.uv?.Length > 0) nm.uv = src.uv;
            if (src.uv2?.Length > 0) nm.uv2 = src.uv2;
            if (src.colors?.Length > 0) nm.colors = src.colors;
            if (src.boneWeights?.Length > 0) nm.boneWeights = src.boneWeights;
            if (src.bindposes?.Length > 0) nm.bindposes = src.bindposes;
        }
        catch { }
        nm.RecalculateBounds();
        nm.MarkDynamic();
        return nm;
    }

    private Mesh GetReadableMesh(Renderer rend)
    {
        Mesh srcMesh = GetRendererMesh(rend);
        if (srcMesh == null) return null;
        if (srcMesh.isReadable) return srcMesh;
        try { var c = Instantiate(srcMesh); c.name = srcMesh.name; if (c.vertexCount > 0) { var _ = c.vertices; return c; } } catch { }
        if (rend is SkinnedMeshRenderer smr)
        {
            try { Mesh b = new Mesh(); smr.BakeMesh(b); b.name = srcMesh.name + "_Baked"; if (b.vertexCount > 0) return b; } catch { }
        }
        return null;
    }

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

    // ═══════════════════════════════════════════
    //  控制点 & 数学
    // ═══════════════════════════════════════════
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

    static int Binomial(int n, int k)
    {
        if (k < 0 || k > n) return 0;
        if (k == 0 || k == n) return 1;
        int r = 1;
        for (int i = 0; i < k; i++) r = r * (n - i) / (i + 1);
        return r;
    }

    static float Bernstein(int i, int n, float t)
    {
        return Binomial(n, i) * Mathf.Pow(t, i) * Mathf.Pow(1f - t, n - i);
    }

    // ═══════════════════════════════════════════
    //  变形核心
    // ═══════════════════════════════════════════
    private void DeformVertices(Transform targetT, Vector3[] srcVerts, Mesh dstMesh, ref Vector3[] vertCache)
    {
        if (dstMesh == null || srcVerts == null) return;

        int nx = PointCountX, ny = PointCountY, nz = PointCountZ;
        int l = divisionsX, m = divisionsY, n = divisionsZ;
        Transform latticeT = transform;
        Matrix4x4 curLatticeW2L = latticeT.worldToLocalMatrix;
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
                        initPos     += w * initialControlPoints[idx];
                        deformedPos += w * controlPoints[idx];
                    }
                }
            }

            Vector3 offset = deformedPos - initPos;
            Vector3 worldOffset = latticeT.TransformVector(offset);
            Vector3 localOffset = targetT.InverseTransformVector(worldOffset);
            vertCache[v] = srcVerts[v] + localOffset;
        }

        dstMesh.vertices = vertCache;
        dstMesh.RecalculateBounds();
    }

    public void ApplyDeformation()
    {
        if (!initialized) return;
        if (!CheckDirty()) return;

        useBufferB = !useBufferB;

        if (targetMode == TargetMode.SingleRenderer)
        {
            if (targetRenderer == null) return;
            Mesh dst = (singleIsSkinned && useBufferB && deformedMeshB != null) ? deformedMeshB : deformedMeshA;
            if (dst == null) return;
            DeformVertices(targetRenderer.transform, originalVertices, dst, ref singleVertCache);
            if (singleIsSkinned)
                SetRendererMesh(targetRenderer, dst);
        }
        else
        {
            while (multiVertCaches.Count < targetRenderers.Count) multiVertCaches.Add(null);

            for (int i = 0; i < targetRenderers.Count; i++)
            {
                if (targetRenderers[i] == null) continue;
                if (i >= deformedMeshesA.Count || deformedMeshesA[i] == null) continue;
                if (i >= originalVerticesList.Count) continue;

                bool skinned = i < isSkinned.Count && isSkinned[i];
                Mesh dst = (skinned && useBufferB && i < deformedMeshesB.Count && deformedMeshesB[i] != null)
                    ? deformedMeshesB[i] : deformedMeshesA[i];

                var cache = multiVertCaches[i];
                DeformVertices(targetRenderers[i].transform, originalVerticesList[i], dst, ref cache);
                multiVertCaches[i] = cache;
                if (skinned)
                    SetRendererMesh(targetRenderers[i], dst);
            }
        }

        SaveSnapshot();
    }

    // ═══════════════════════════════════════════
    //  重置 / 还原 / 烘焙
    // ═══════════════════════════════════════════
    public void ResetControlPoints()
    {
        if (initialControlPoints == null) return;
        Array.Copy(initialControlPoints, controlPoints, controlPoints.Length);
        ApplyDeformation();
    }

    public void RestoreOriginal()
    {
        if (targetMode == TargetMode.SingleRenderer)
        {
            if (originalMesh != null && targetRenderer != null)
                SetRendererMesh(targetRenderer, originalMesh);
            SafeDestroyMesh(ref deformedMeshA);
            SafeDestroyMesh(ref deformedMeshB);
            originalVertices = null;
            originalMesh = null;
            singleIsSkinned = false;
        }
        else
        {
            for (int i = 0; i < targetRenderers.Count; i++)
            {
                if (targetRenderers[i] != null && i < originalMeshes.Count && originalMeshes[i] != null)
                    SetRendererMesh(targetRenderers[i], originalMeshes[i]);
            }
            foreach (var dm in deformedMeshesA) { if (dm != null) DestroyImmediate(dm); }
            foreach (var dm in deformedMeshesB) { if (dm != null) DestroyImmediate(dm); }
            deformedMeshesA.Clear();
            deformedMeshesB.Clear();
            targetRenderers.Clear();
            originalMeshes.Clear();
            originalVerticesList.Clear();
            isSkinned.Clear();
        }

        initialized = false;
        controlPoints = null;
        initialControlPoints = null;
    }

    public void BakeAndRemove()
    {
        initialized = false;
        controlPoints = null;
        initialControlPoints = null;
        if (targetMode == TargetMode.SingleRenderer)
        {
            originalVertices = null;
            originalMesh = null;
            deformedMeshA = null;
            deformedMeshB = null;
        }
        else
        {
            targetRenderers.Clear();
            originalMeshes.Clear();
            originalVerticesList.Clear();
            deformedMeshesA.Clear();
            deformedMeshesB.Clear();
            isSkinned.Clear();
        }
    }

    private void SafeDestroyMesh(ref Mesh mesh)
    {
        if (mesh != null) DestroyImmediate(mesh);
        mesh = null;
    }

    // ═══════════════════════════════════════════
    //  索引
    // ═══════════════════════════════════════════
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

    // ═══════════════════════════════════════════
    //  动画控制点
    // ═══════════════════════════════════════════
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
        useTransformHandles = true;
    }

    public void DestroyControlPointTransforms()
    {
        if (controlPointTransforms != null)
        {
            foreach (var t in controlPointTransforms)
                if (t != null) DestroyImmediate(t.gameObject);
            controlPointTransforms = null;
        }
        useTransformHandles = false;
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

    public bool HasControlPointTransforms =>
        controlPointTransforms != null && controlPointTransforms.Length > 0 && controlPointTransforms[0] != null;

    public Transform GetControlPointTransform(int index)
    {
        if (controlPointTransforms == null || index < 0 || index >= controlPointTransforms.Length) return null;
        return controlPointTransforms[index];
    }

    // ═══════════════════════════════════════════
    //  LateUpdate & OnDestroy
    // ═══════════════════════════════════════════
    private void LateUpdate()
    {
        if (!initialized || !liveUpdate) return;
        if (useTransformHandles && HasControlPointTransforms)
            SyncFromTransforms();
        ApplyDeformation();
    }

    private void OnDestroy()
    {
        void SD(Mesh m) { if (m == null) return; if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); }
        SD(deformedMeshA); SD(deformedMeshB);
        foreach (var dm in deformedMeshesA) SD(dm);
        foreach (var dm in deformedMeshesB) SD(dm);
    }
}
