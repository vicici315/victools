// LatticeModifier 1.4 - FFD 晶格变形场
// 晶格挂在独立空物体上，目标对象拖入 targetRenderer
// 移动晶格或模型时，处于晶格范围内的顶点实时变形，离开后恢复原形
// 支持子物体控制点（CP_x_y_z），可被 Animation/Timeline K帧驱动变形
using System;
using UnityEngine;

[ExecuteAlways]
public class LatticeModifier : MonoBehaviour
{
    [Header("目标对象（拖入要变形的模型）")]
    public Renderer targetRenderer;

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
    [HideInInspector] [SerializeField] private Vector3[] originalVertices;
    [HideInInspector] [SerializeField] private Mesh originalMesh;
    [HideInInspector] [SerializeField] private Vector3 latticeMin;
    [HideInInspector] [SerializeField] private Vector3 latticeSize;
    [HideInInspector] [SerializeField] private bool initialized;
    [HideInInspector] [SerializeField] private Transform[] controlPointTransforms;

    [NonSerialized] private Mesh deformedMesh;

    public int PointCountX => divisionsX + 1;
    public int PointCountY => divisionsY + 1;
    public int PointCountZ => divisionsZ + 1;
    public int TotalPoints => PointCountX * PointCountY * PointCountZ;
    public bool IsInitialized => initialized;

    public void InitializeLattice()
    {
        if (targetRenderer == null) { Debug.LogWarning("请先指定目标对象"); return; }

        // 还原之前的状态
        if (originalMesh != null)
        {
            SetTargetMesh(originalMesh);
            if (deformedMesh != null) { DestroyImmediate(deformedMesh); deformedMesh = null; }
        }

        Mesh srcMesh = originalMesh != null ? originalMesh : GetTargetMesh();
        if (srcMesh == null) { Debug.LogWarning("目标对象没有有效的 Mesh"); return; }

        if (!srcMesh.isReadable)
        {
            Debug.LogError($"Mesh '{srcMesh.name}' 的 Read/Write 未启用，请在模型导入设置中勾选");
            return;
        }

        if (originalMesh == null) originalMesh = srcMesh;
        originalVertices = srcMesh.vertices;

        // 创建可读写的副本
        deformedMesh = CreateReadableCopy(srcMesh);
        SetTargetMesh(deformedMesh);

        // 计算包围盒（晶格本地空间）
        Bounds bounds = new Bounds();
        bool first = true;
        Transform targetT = targetRenderer.transform;
        for (int i = 0; i < originalVertices.Length; i++)
        {
            Vector3 wp = targetT.TransformPoint(originalVertices[i]);
            Vector3 lp = transform.InverseTransformPoint(wp);
            if (first) { bounds = new Bounds(lp, Vector3.zero); first = false; }
            else bounds.Encapsulate(lp);
        }
        bounds.Expand(bounds.size * 0.02f);
        latticeMin = bounds.min;
        latticeSize = bounds.size;

        GenerateControlPoints();
        initialized = true;
    }

    private Mesh CreateReadableCopy(Mesh src)
    {
        var m = new Mesh();
        m.name = src.name + "_LatticeDeform";
        m.vertices = src.vertices;
        m.normals = src.normals;
        m.tangents = src.tangents;
        m.uv = src.uv;
        m.uv2 = src.uv2;
        m.colors = src.colors;
        m.boneWeights = src.boneWeights;
        m.bindposes = src.bindposes;
        m.subMeshCount = src.subMeshCount;
        for (int i = 0; i < src.subMeshCount; i++)
            m.SetTriangles(src.GetTriangles(i), i);
        m.RecalculateBounds();
        return m;
    }

    private Mesh GetTargetMesh()
    {
        if (targetRenderer is SkinnedMeshRenderer smr) return smr.sharedMesh;
        var mf = targetRenderer.GetComponent<MeshFilter>();
        return mf != null ? mf.sharedMesh : null;
    }

    private void SetTargetMesh(Mesh mesh)
    {
        if (targetRenderer is SkinnedMeshRenderer smr) { smr.sharedMesh = mesh; return; }
        var mf = targetRenderer.GetComponent<MeshFilter>();
        if (mf != null) mf.sharedMesh = mesh;
    }

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

    /// <summary>
    /// 确保 deformedMesh 存在（脚本重载后 NonSerialized 字段会丢失）
    /// </summary>
    private void EnsureDeformedMesh()
    {
        if (deformedMesh != null) return;
        if (!initialized || originalMesh == null || targetRenderer == null) return;

        // 检查当前 Mesh 是否已经是我们的副本
        Mesh current = GetTargetMesh();
        if (current != null && current != originalMesh && current.isReadable)
        {
            deformedMesh = current;
            return;
        }

        // 重新创建
        deformedMesh = CreateReadableCopy(originalMesh);
        SetTargetMesh(deformedMesh);
    }

    public void ApplyDeformation()
    {
        if (!initialized || targetRenderer == null) return;
        EnsureDeformedMesh();
        if (deformedMesh == null || !deformedMesh.isReadable) return;

        int nx = PointCountX, ny = PointCountY, nz = PointCountZ;
        int l = divisionsX, m = divisionsY, n = divisionsZ;
        Transform targetT = targetRenderer.transform;
        Transform latticeT = transform;

        // 当前实时的变换矩阵：用于判断顶点是否在晶格范围内
        Matrix4x4 curLatticeW2L = latticeT.worldToLocalMatrix;
        Matrix4x4 curTargetL2W = targetT.localToWorldMatrix;

        Vector3[] newVerts = new Vector3[originalVertices.Length];

        for (int v = 0; v < originalVertices.Length; v++)
        {
            // 用当前实时变换计算顶点在晶格本地空间的位置
            Vector3 worldPos = curTargetL2W.MultiplyPoint3x4(originalVertices[v]);
            Vector3 latticeLocal = curLatticeW2L.MultiplyPoint3x4(worldPos);

            float s = latticeSize.x > 0 ? (latticeLocal.x - latticeMin.x) / latticeSize.x : 0;
            float t = latticeSize.y > 0 ? (latticeLocal.y - latticeMin.y) / latticeSize.y : 0;
            float u = latticeSize.z > 0 ? (latticeLocal.z - latticeMin.z) / latticeSize.z : 0;

            // 范围外恢复原样（离开晶格区域 = 不变形）
            if (s < -0.01f || s > 1.01f || t < -0.01f || t > 1.01f || u < -0.01f || u > 1.01f)
            {
                newVerts[v] = originalVertices[v];
                continue;
            }

            s = Mathf.Clamp01(s);
            t = Mathf.Clamp01(t);
            u = Mathf.Clamp01(u);

            Vector3 initPos = Vector3.zero;
            Vector3 deformedPos = Vector3.zero;

            for (int ix = 0; ix < nx; ix++)
            {
                float bx = Bernstein(ix, l, s);
                for (int iy = 0; iy < ny; iy++)
                {
                    float by = Bernstein(iy, m, t);
                    for (int iz = 0; iz < nz; iz++)
                    {
                        float bz = Bernstein(iz, n, u);
                        int idx = GetFlatIndex(ix, iy, iz);
                        float w = bx * by * bz;
                        initPos     += w * initialControlPoints[idx];
                        deformedPos += w * controlPoints[idx];
                    }
                }
            }

            // 控制点偏移用当前晶格变换转到世界空间，再转到目标本地空间
            Vector3 offset = deformedPos - initPos;
            Vector3 worldOffset = latticeT.TransformVector(offset);
            Vector3 localOffset = targetT.InverseTransformVector(worldOffset);
            newVerts[v] = originalVertices[v] + localOffset;
        }

        deformedMesh.vertices = newVerts;
        deformedMesh.RecalculateNormals();
        deformedMesh.RecalculateBounds();
    }

    public void ResetControlPoints()
    {
        if (initialControlPoints == null) return;
        Array.Copy(initialControlPoints, controlPoints, controlPoints.Length);
        ApplyDeformation();
    }

    public void RestoreOriginal()
    {
        if (originalMesh != null && targetRenderer != null)
            SetTargetMesh(originalMesh);
        if (deformedMesh != null) DestroyImmediate(deformedMesh);
        deformedMesh = null;
        initialized = false;
        controlPoints = null;
        initialControlPoints = null;
        originalVertices = null;
        originalMesh = null;
    }

    public void BakeAndRemove()
    {
        initialized = false;
        controlPoints = null;
        initialControlPoints = null;
        originalVertices = null;
        originalMesh = null;
    }

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

    /// <summary>
    /// 创建子物体控制点，每个控制点对应一个子 GameObject，可被 Animation/Timeline K帧
    /// </summary>
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

    /// <summary>
    /// 清除子物体控制点
    /// </summary>
    public void DestroyControlPointTransforms()
    {
        if (controlPointTransforms != null)
        {
            foreach (var t in controlPointTransforms)
            {
                if (t != null) DestroyImmediate(t.gameObject);
            }
            controlPointTransforms = null;
        }
        useTransformHandles = false;
    }

    /// <summary>
    /// 从子物体 Transform 同步位置到 controlPoints 数组
    /// </summary>
    public void SyncFromTransforms()
    {
        if (controlPointTransforms == null || controlPoints == null) return;
        for (int i = 0; i < controlPoints.Length && i < controlPointTransforms.Length; i++)
        {
            if (controlPointTransforms[i] != null)
                controlPoints[i] = controlPointTransforms[i].localPosition;
        }
    }

    /// <summary>
    /// 从 controlPoints 数组同步位置到子物体 Transform
    /// </summary>
    public void SyncToTransforms()
    {
        if (controlPointTransforms == null || controlPoints == null) return;
        for (int i = 0; i < controlPoints.Length && i < controlPointTransforms.Length; i++)
        {
            if (controlPointTransforms[i] != null)
                controlPointTransforms[i].localPosition = controlPoints[i];
        }
    }

    /// <summary>
    /// 子物体控制点是否已创建
    /// </summary>
    public bool HasControlPointTransforms =>
        controlPointTransforms != null && controlPointTransforms.Length > 0 && controlPointTransforms[0] != null;

    /// <summary>
    /// 获取指定索引的控制点 Transform
    /// </summary>
    public Transform GetControlPointTransform(int index)
    {
        if (controlPointTransforms == null || index < 0 || index >= controlPointTransforms.Length)
            return null;
        return controlPointTransforms[index];
    }

    private void LateUpdate()
    {
        if (!initialized || !liveUpdate) return;

        if (useTransformHandles && HasControlPointTransforms)
        {
            SyncFromTransforms();
        }

        ApplyDeformation();
    }

    private void OnDestroy()
    {
        if (deformedMesh != null)
        {
            if (Application.isPlaying)
                Destroy(deformedMesh);
            else
                DestroyImmediate(deformedMesh);
        }
    }
}
