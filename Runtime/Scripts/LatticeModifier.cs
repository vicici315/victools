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
// LatticeModifier 3.2 新增边缘羽化（feather）：晶格边界区域变形通过 smoothstep 平滑衰减到零，消除硬切边缘
// LatticeModifier 3.3 修复轴心旋转后变形方向错位：统一使用当前晶格变换计算参数坐标，轴心操作同步更新内部数据；修复Undo支持（记录子CP Transform）；羽化基于当前晶格包围盒从中心向边缘衰减
// LatticeModifier 3.4 实时变形 + 打包可见：
//      - 撤销 BakeToOriginalMesh 思路（它会让晶格在烘焙后失效、无法继续实时控制）
//      - 配合 Editor 的 OnWillSaveAssets / IPreprocessBuildWithReport 钩子：
//        保存场景 / 打包前自动把所有 Renderer 的 sharedMesh 引用还原回 originalMesh 资产
//        → Build 场景里 Renderer 引用始终指向带 Asset GUID 的资产，打包后正常可见
//      - 进入 Play 模式后 OnEnable 重建 deformedMeshA 并赋给 Renderer，运行时继续实时变形
//      - 退出 Play 模式 OnDestroy 自动还原回 originalMesh
//      - 用户不再需要任何手动烘焙操作，晶格物体保留在场景中，控制点持续实时影响模型
//
// v3.5 修复「目标 Renderer 带非均匀缩放时模型轴向被拉伸/压扁」：
//      - DeformVertices 中 offset 转换由 TransformVector/InverseTransformVector（旋转+缩放）
//        改为 TransformDirection/InverseTransformDirection（只旋转），
//        消除链式 S_lattice * S_target⁻¹ 缩放污染。
//      - ComputeBounds 改用「目标 Renderer local 空间 AABB 的 8 个角点 → 晶格 local 空间」求晶格 AABB，
//        之前是「所有 world 顶点直接转晶格 local 求 AABB」，目标带旋转时 AABB 会膨胀，
//        控制点之间距离因此被放大，模型被「轴向拉伸」。
//
// v3.6 修复「不可读 Mesh 构建 deform Mesh 时因缺失 UV 通道导致模型不可见」：
//      - CreateDeformMesh 中所有 GetUVs/GetNormals/GetTangents/GetColors 调用前
//        用 HasVertexAttribute 守卫；源 Mesh 缺哪条通道就跳过哪条，不再抛异常中断整个构建。
//      - 之前 GetUVs(1, ...) 在源 Mesh 无 uv2 时抛 "Mesh data does not have TexCoord1 vertex component"，
//        外层 catch 捕获后整个 try 块终止，导致 SetTriangles 之后的所有属性没赋值、模型没三角形不可见。
//      - 同时增加 uv3/uv4/colors 通道支持。
//
// v3.7 重构：抽取 RecreateDeformMeshesFor / ComputeLocalAABB / TransformBoundsToLocalSpace 工具方法；
//           OnEnable 拆为 TryRecoverFromBuildScene / InitializeRuntime；
//           OnDestroy 委托给 RestoreOriginal；删除 initLatticeLocalToWorld/initLatticeWorldToLocal 死代码。
//
// v3.8 修复「个别添加了晶格变形器的模型打包后不可见」的 3 个 P0 根因：
//      - OnPreprocessBuild 改用 EditorUtility.SetDirty 替代 Undo.RecordObject，
//
// LatticeModifier v3.9 取消目标模式（SingleRenderer/MultiRenderer），单/多对象统一处理 + 修复污染
// LatticeModifier v3.10 修复「单个静态对象打包后不可见 / 材质变灰」的真正根因——运行时 Mesh 可读性
// LatticeModifier v3.11 修复「目标对象带缩放时晶格变形被放大（叠加）」
// LatticeModifier v3.12：缓存蒙皮数据（仅当确为带蒙皮目标时有意义；非蒙皮 Mesh 这些为空数组）。
// LatticeModifier v3.13：取消蒙皮双缓冲 + 每帧重新赋值 sharedMesh。
// LatticeModifier v3.14：变形性能优化，修复运行时帧率骤降（修正缓存网格命名避免每帧重建、消除 Mathf.Pow、权重缓存、变更检测改用相对矩阵刚性同移零开销）。
//
// LatticeModifier v3.17 内存管理与重建冷却（修复玩家端内存持续增长、70% OOM 闪退）：
//      - 关闭 s_enableOrphanMeshGC 默认值，扫描间隔 30s→120s。
//        原因：Resources.FindObjectsOfTypeAll<Mesh>() 每次返回一个巨大的 Mesh[] 临时数组（数 MB），
//        长期反复调用会持续推高托管堆压力，疑似玩家端内存缓慢增长的主要来源。
//        OnDisable / OnDestroy 已经确保 deform Mesh 及时释放，孤儿回收作为可选排查工具更安全。
//      - EnsureDeformMeshesValid 路径加 dt.lastRebuildTime 冷却（1s）：
//        防止 SkinnedMeshRenderer.sharedMesh 在玩家端周期性失效时持续 Instantiate 新 Mesh，
//        旧 Mesh 已被 Destroy 但 Unity 尚未立即 GC 释放，导致临时双倍内存占用并持续累积。
//      - TryRecoverFromBuildScene 末尾标记 runtimeInitialized=true + 提到 OnEnable 入口处统一去重：
//        防止 OnEnable 反复触发时（域重载/场景重载）持续重建 Mesh。
//      - RestoreOriginal 清空实例级缓存数组（cachedControlPoints/cachedCombined/_powA/_powB）。
//      - LogMemoryDiagnostics 合并 Mesh 池 2 次遍历为 1 次，减少分配。
//      - OnGUI 的 GUIStyle 改为静态缓存，避免诊断开启时每帧 new GUIStyle。
//
// LatticeModifier v3.18 性能优化（针对 15 帧低帧率与持续内存增长）：
//      - 顶点数据上传改用 NativeArray<Vector3> + Mesh.SetVertices(NativeArray) 零拷贝路径，
//        替代 mesh.vertices= 数组（后者有额外托管堆分配 + 一次 native 拷贝）。NativeArray 复用避免分配。
//      - DeformVertices 内层循环累加器用 3 个 float 而非 Vector3（避免拆装 Vector3 的中间 IL 调用）。
//      - 预乘 targetW2L * latL2W 为 3x3 矩阵并展开为 9 个 float，循环内省一次矩阵乘加（9 次乘加），
//        替代原来的两次 MultiplyVector 调用（18 次乘加 + 两次方法调用），省约 50% 矩阵计算时间。
//      - RecalculateBounds 节流：每 4 帧才重算一次 AABB，期间复用旧值，省 75% 该开销。
//      - LOD 距离剔除：s_enableLODCulling + s_maxDeformDistance=50f，远处对象跳过变形，省 50%+ 整体开销。
//      - SyncFromTransforms 检测控制点变化时直接 MarkDirty()，避免 CheckDirty 每帧 27 个 Vector3 比较。
//
// v3.19 内存优化（在 v3.18 基础上进一步消除泄漏源，玩家端内存增长主嫌疑修复）：
//      - CreateDeformMesh 不可读分支：法线/切线/UV/颜色 全部改用 NativeArray + Mesh.SetXxx(NativeArray)
//        零分配上传，替代 .ToArray() 一次性的 O(verts × channels) 托管数组分配。
//      - GetReadableMesh：不可读 Mesh 不再 Instantiate 整 Mesh 拿顶点（会复制 blendshape/
//        boneWeights/bindposes，常驻几十 MB），改用 AcquireReadOnlyMeshData 抓顶点。
//      - 删除 vertCache 死代码字段：v3.18 改造后 vertCache 已无人读取，但占用一份 Vector3[vc] 常驻。
//      - 重建冷却从 1s 拉到 5s：玩家端 GC 节奏受引擎控制，1s 不够安全，5s 更稳。
//      - s_activeLattices 在 OnDestroy 多一道 Remove 双保险（域重载/异常路径覆盖）。
//      - LogMemoryDiagnostics 改用 Profiler.GetTotalAllocatedMemoryLong/GetTotalReservedMemoryLong
//        替代 4 次 Resources.FindObjectsOfTypeAll（无分配 Profiler API）。
//      - s_diagText 改用 StringBuilder 复用池，避免每 5s Debug.Log 字符串拼接分配。
//      - s_warnedMeshBuildFailures 在 OnDestroy 中清理当前 lattice 相关 key，限制长期增长。
//
// LatticeModifier v3.23 性能 + 内存根治（针对 26 Renderer 共享 LatticeModifier 场景，端到端治理玩家端内存增长）：
//      - Per-Renderer 范围跳过：DeformVertices 返回 bool，循环追踪 anyInRange；本 Renderer 无顶点进入
//        晶格时直接 return false，跳过 SetVertices + RecalculateBounds + SkinnedMeshRenderer 蒙皮 dispatch。
//        K 帧只动 1-3 控制点时 26 Renderer 中 22+ 个直接跳过循环，CPU/GPU 开销降到 1/6 ~ 1/13。
//      - LOD 距离剔除默认阈值 50m → 12m（玩家跟随镜头场景下基本不影响可见性）。
//      - 验证：玩家端 %MEM 从涨到 70% 退出 → 稳定在 18.5% (约 295 MB / 1.6 GB)，无持续增长。
//      - 内存增长控制靠 v3.17/v3.19 已有机制：5 秒重建冷却、vertCacheNative 零拷贝、AcquireReadOnlyMeshData、
//        死代码清理、Profiler API 替代 FindObjectsOfTypeAll。
//
// v3.23.2 关键回退：v3.23 曾尝试修复"销毁顺序（先切 Renderer 到 originalMesh 再 Destroy 旧 Mesh）"，
//      实际在 SkinnedMeshRenderer 首次创建场景下导致模型看不到（怀疑切回 originalMesh 后 SkinnedMeshRenderer
//      蒙皮缓冲未及时重新挂上，渲染前 GPU 端看到的是空 Mesh）。回退到 v3.19 销毁顺序，靠 5 秒冷却 +
//      上面列出的 v3.17/v3.19 机制共同控制内存增长（已验证 %MEM 18.5% 稳定）。


using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;

[ExecuteAlways]
public class LatticeModifier : MonoBehaviour
{
    public enum TargetMode { SingleRenderer, MultiRenderer }

    #region 序列化字段

    // v3.9：targetMode 和 targetRenderer 保留字段声明（防止旧场景反序列化丢数据），
    // 但标记 HideInInspector，运行时不再使用 SingleRenderer 路径。
    [HideInInspector] public TargetMode targetMode = TargetMode.MultiRenderer;
    [HideInInspector] public Renderer targetRenderer;

    [Header("多目标根节点（自动收集所有子 Renderer）")]
    public Transform targetRoot;

    [Header("手动指定目标 Renderer（优先于根节点自动收集）")]
    public List<Renderer> manualRenderers = new List<Renderer>();

    [Header("晶格段数（控制点数 = 段数 + 1）")]
    [Range(1, 8)] public int divisionsX = 2;
    [Range(1, 8)] public int divisionsY = 2;
    [Range(1, 8)] public int divisionsZ = 2;

    [Header("边缘羽化")]
    [Range(0f, 0.5f)] public float feather = 0f;

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

    /// 可序列化的 subMesh 三角形索引容器。
    /// Unity 序列化不支持锯齿数组（int[][]），必须用包装结构体使其可序列化。
    /// 之前 v3.8 用 int[][] originalTriangles 缓存拓扑——该字段在场景序列化时被静默忽略，
    /// 玩家端反序列化后始终为 null，导致"三级兜底"的第二级（RebuildMeshFromCache）永远无法工作。
    [Serializable]
    private struct SerializableTriangles
    {
        public int[] triangles;
    }

    [Serializable]
    private class DeformTarget
    {
        public Renderer renderer;
        public Mesh originalMesh;
        public Vector3[] originalVertices;
        // v3.9 修复：Unity 序列化不支持 int[][]（锯齿数组），改为 SerializableTriangles[]
        // 包装后可正确随场景序列化/反序列化，玩家端兜底路径可靠工作。
        public SerializableTriangles[] originalTriangles;  // 每个 subMesh 一组三角形索引
        public int originalSubMeshCount;

        // v3.10 关键修复：缓存完整顶点通道（法线/UV/切线），随场景序列化。
        // 根因：打包后 Read/Write Disabled 的 Mesh 在玩家端 CPU 数据被剥离，
        // Instantiate / AcquireReadOnlyMeshData 都读不到数据 → 变形副本为空 → 模型不可见。
        // 在 Editor（可读取任意 Mesh）阶段把所有通道缓存进组件，玩家端完全从缓存重建，
        // 不再依赖运行时 Mesh 是否可读。
        public Vector3[] originalNormals;
        public Vector4[] originalTangents;
        public Vector2[] originalUV;
        public Color[] originalColors;

        // v3.12：缓存蒙皮数据，玩家端遇到不可读 SkinnedMeshRenderer Mesh 时
        // 从缓存重建仍能保留骨骼权重/绑定姿势，避免蒙皮失效导致模型按根骨骼朝向错位旋转。
        // 用经典 BoneWeight[]（每顶点最多 4 骨骼）+ Matrix4x4[]，二者均为 Unity 可序列化类型。
        public BoneWeight[] originalBoneWeights;
        public Matrix4x4[] originalBindposes;

        public Mesh deformedMeshA;
        public Mesh deformedMeshB;
        public bool isSkinned;

        // v3.19 内存优化：删除 v3.18 改造后的 vertCache 死代码字段。
        // v3.18 改用 NativeArray<Vector3> vertCacheNative 上传 Mesh 后，vertCache 已无人读取
        // （DeformVertices 只写 vertCacheNative[v]），但仍持续占用一份 Vector3[vc] 常驻内存。
        // 每个 LatticeModifier × 每个 DeformTarget 多一份冗余顶点缓冲，几 MB~几十 MB。
        // v3.19 直接删除该字段。
        [NonSerialized] public bool useBufferB;

        // v3.18 性能：NativeArray 形式的顶点缓冲（用于 Mesh.SetVertices 零拷贝路径）。
        // NativeArray<Vector3> 复用避免 mesh.vertices = array 时的内部中间拷贝，
        // Mesh.SetVertices(NativeArray, start, length) 是 Mesh 数据上传最快路径（无 GC、无中间数组）。
        [NonSerialized] public NativeArray<Vector3> vertCacheNative;
        [NonSerialized] public bool vertCacheNativeCreated;

        // v3.13.2 性能：缓存 MeshFilter 引用，避免每帧 GetComponent<MeshFilter>()。
        [NonSerialized] public MeshFilter cachedMeshFilter;
        [NonSerialized] public bool meshFilterResolved;

        // v3.14 性能：每顶点权重缓存。
        // 参数坐标(s,t,u)/Bernstein 基函数/initPos/范围/羽化 只依赖
        //「晶格world2local × 目标local2world」这个组合矩阵 + feather，与控制点无关。
        // 组合矩阵和 feather 不变时（典型：仅 K帧动画控制点、目标与晶格相对静止），
        // 直接复用缓存，跳过 Bernstein/Pow 重算，每帧只做一次加权求和。
        [NonSerialized] public Matrix4x4 wcMatrix;
        [NonSerialized] public float wcFeather;
        [NonSerialized] public bool wcValid;
        [NonSerialized] public int wcVertCount;
        [NonSerialized] public bool[] wcInRange;     // 每顶点是否在晶格范围内
        [NonSerialized] public float[] wcFeatherW;   // 每顶点羽化权重
        [NonSerialized] public Vector3[] wcInitPos;  // 每顶点 FFD 初始重建位置
        [NonSerialized] public float[] wcBx;         // 每顶点 x 轴基函数，长度 = verts*nx
        [NonSerialized] public float[] wcBy;         // verts*ny
        [NonSerialized] public float[] wcBz;         // verts*nz

        // v3.8 新增：三个"已警告"标记位，防止 EnsureDeformMeshesValid 每帧刷屏
        [NonSerialized] public bool warnedOriginalMeshRecoveredByDeformedA;
        [NonSerialized] public bool warnedRebuiltFromCache;
        [NonSerialized] public bool warnedTotallyFailed;

        // v3.17：Mesh 重建冷却时间。每次 EnsureDeformMeshesValid 触发 meshLost→重建后，
        // 在冷却时间内不再重复重建（避免 SkinnedMeshRenderer.sharedMesh 周期性失效时
        // 持续 Instantiate 新 Mesh 但旧 Mesh 未立即被 Unity 回收导致的临时翻倍累积）。
        [NonSerialized] public float lastRebuildTime = -999f;

        // v3.18 性能：RecalculateBounds 节流计数器。每 4 帧重算一次 AABB，期间复用旧值。
        [NonSerialized] public int framesSinceBoundsRecalc;
        [NonSerialized] public int lastRecalcBoundsTime = -1;
    }

    #endregion

    #region 属性

    public int PointCountX => divisionsX + 1;
    public int PointCountY => divisionsY + 1;
    public int PointCountZ => divisionsZ + 1;
    public int TotalPoints => PointCountX * PointCountY * PointCountZ;
    public bool IsInitialized => initialized;

    #endregion

    #region 变更检测与缓存

    [NonSerialized] private Vector3[] cachedControlPoints;
    [NonSerialized] private Matrix4x4[] cachedCombined;
    [NonSerialized] private float cachedFeather;
    [NonSerialized] private bool isDirty = true;
    [NonSerialized] private bool runtimeInitialized;

    // v3.14.4：Bernstein 幂表复用缓冲（避免 RefreshWeightCache 每次分配临时数组）
    [NonSerialized] private float[] _powA;
    [NonSerialized] private float[] _powB;

    // ── v3.17：孤儿变形网格回收（GC）改写 ──
    // 活动晶格注册表：用于构建"当前仍在用的变形网格"集合，集合外的同后缀网格视为孤儿销毁。
    private static readonly HashSet<LatticeModifier> s_activeLattices = new HashSet<LatticeModifier>();
    private static float s_lastOrphanSweepTime = -999f;
    /// 是否启用孤儿变形网格周期回收（默认关闭 — v3.17 改为关闭）。
    /// 注意：v3.16 默认开启的回收器会每 30 秒调用 Resources.FindObjectsOfTypeAll&lt;Mesh&gt;()
    /// 同步扫描整个 Mesh 池，在大场景下会分配一个巨大的 Mesh[] 临时数组（数 MB），
    /// 长期会持续推高托管堆压力，是玩家端内存缓慢增长的主要嫌疑之一。
    /// v3.17：默认关闭，OnDisable / OnDestroy 已经确保 deform Mesh 及时释放；
    /// 真有泄漏怀疑时再用 LatticeModifier.s_enableOrphanMeshGC=true 开启排查。
    public static bool s_enableOrphanMeshGC = false;
    /// 回收扫描间隔（秒）。v3.17 拉长到 120 秒以进一步降低开销。
    public static float s_orphanSweepInterval = 120f;

    // ── v3.16：内存诊断 ──
    /// 开启后周期性打印资源数量，用于定位内存持续增长的真正来源。
    public static bool s_enableMemoryDiagnostics = false;
    public static float s_diagInterval = 5f;
    private static float s_lastDiagTime = -999f;
    private static string s_diagText = "";
    private static int s_diagDrawFrame = -1;

    public void MarkDirty() { isDirty = true; }

    private bool CheckDirty()
    {
        if (isDirty) return true;

        if (feather != cachedFeather)
        {
            cachedFeather = feather;
            return true;
        }

        if (controlPoints != null && cachedControlPoints != null && controlPoints.Length == cachedControlPoints.Length)
        {
            for (int i = 0; i < controlPoints.Length; i++)
                if (controlPoints[i] != cachedControlPoints[i])
                    return true;
        }
        else return true;

        // v3.14.2：用「晶格worldToLocal × 目标localToWorld」的相对矩阵判断变更，而非目标绝对矩阵。
        // 变形结果只取决于目标相对晶格的位姿；当晶格与目标刚性一起移动（例如把晶格挂到角色下
        // 随角色行走），相对矩阵不变 → 无变更 → 完全跳过变形与上传，零开销。
        // 只有相对位姿真正改变（移动其中之一、控制点动画）时才重算，符合 v1.1「移动即变形」语义。
        if (cachedCombined == null || cachedCombined.Length != deformTargets.Count)
            return true;
        Matrix4x4 latW2L = transform.worldToLocalMatrix;
        for (int i = 0; i < deformTargets.Count; i++)
        {
            var dt = deformTargets[i];
            if (dt.renderer == null) continue;
            Matrix4x4 combined = latW2L * dt.renderer.transform.localToWorldMatrix;
            if (combined != cachedCombined[i])
                return true;
        }

        return false;
    }

    private void SaveSnapshot()
    {
        isDirty = false;
        cachedFeather = feather;
        if (controlPoints != null)
        {
            if (cachedControlPoints == null || cachedControlPoints.Length != controlPoints.Length)
                cachedControlPoints = new Vector3[controlPoints.Length];
            Array.Copy(controlPoints, cachedControlPoints, controlPoints.Length);
        }

        // v3.14.2：缓存每个目标相对晶格的组合矩阵
        if (cachedCombined == null || cachedCombined.Length != deformTargets.Count)
            cachedCombined = new Matrix4x4[deformTargets.Count];
        Matrix4x4 latW2L = transform.worldToLocalMatrix;
        for (int i = 0; i < deformTargets.Count; i++)
        {
            var dt = deformTargets[i];
            cachedCombined[i] = (dt.renderer != null)
                ? latW2L * dt.renderer.transform.localToWorldMatrix
                : Matrix4x4.identity;
        }
    }

    #endregion

    #region 生命周期

    private void OnEnable()
    {
        s_activeLattices.Add(this); // v3.16：注册到活动晶格表（用于孤儿网格回收）

        // v3.17：runtimeInitialized 同样作为 TryRecoverFromBuildScene 的去重标志，
        // 避免 OnEnable 反复触发时持续 Instantiate 新 Mesh 累积（域重载 / 场景加载等场景下）。
        if (runtimeInitialized) return;

        if (!initialized)
        {
            // 玩家端兜底：打包后 Renderer 的 sharedMesh 引用可能解析为 null，
            // 但 deformTargets 里原始顶点还在，直接重建 deform Mesh 即可让模型可见。
            TryRecoverFromBuildScene();
            return;
        }

        InitializeRuntime();
    }

    /// 玩家端兜底：当 initialized == false 但 deformTargets 仍带原始顶点缓存时，
    /// 强制重建 deform Mesh 并挂回 Renderer，让打包后模型可见。
    private void TryRecoverFromBuildScene()
    {
        if (deformTargets == null || deformTargets.Count == 0) return;
        bool hasUsable = false;
        for (int i = 0; i < deformTargets.Count; i++)
        {
            var dt = deformTargets[i];
            if (dt != null && dt.renderer != null && dt.originalVertices != null)
            {
                hasUsable = true;
                break;
            }
        }
        if (!hasUsable) return;

        RebuildDeformMeshes();
        isDirty = true;
        ApplyDeformation();

        // v3.17：标记 runtimeInitialized 防止 OnEnable 反复触发时再次重建。
        // 域重载/场景重载会清掉 [NonSerialized] 字段，自然会重新跑一次 TryRecoverFromBuildScene。
        runtimeInitialized = true;
    }

    /// 正常路径：Editor/Player 中 lattice 已经被 SetDirty 持久化进场景时，
    /// OnEnable 重新走一次"重建 deform Mesh + 同步控制点 + 刷一次变形"流程。
    private void InitializeRuntime()
    {
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

        // v3.16：周期性回收孤儿变形网格（释放后可被引擎复用）。
        // 由静态计时器驱动，全局只跑一次/间隔，无论有多少晶格实例。
        if (s_enableOrphanMeshGC && Time.unscaledTime - s_lastOrphanSweepTime >= s_orphanSweepInterval)
        {
            s_lastOrphanSweepTime = Time.unscaledTime;
            SweepOrphanLatticeMeshes();
        }

        // v3.16：内存诊断日志（默认关闭）。开启后周期性打印各类资源数量，
        // 用于定位"到底是什么在持续增长"。设 LatticeModifier.s_enableMemoryDiagnostics=true 开启。
        if (s_enableMemoryDiagnostics && Time.unscaledTime - s_lastDiagTime >= s_diagInterval)
        {
            s_lastDiagTime = Time.unscaledTime;
            LogMemoryDiagnostics();
        }
    }

    private void OnDisable()
    {
        s_activeLattices.Remove(this); // v3.16：从活动晶格表注销

        // v3.15：禁用 / 失活 / 对象池化 / 场景卸载前 释放变形 Mesh，避免被禁用期间
        // 持续占用 GPU 内存，并杜绝任何"禁用→重建"循环导致的网格累积。
        // 还原 Renderer 到 originalMesh（禁用期间仍可见），重新启用时 OnEnable 会重建。
        if (!initialized) return;
        foreach (var dt in deformTargets)
        {
            if (dt == null) continue;
            if (dt.renderer != null && dt.originalMesh != null)
                SetRendererMesh(dt.renderer, dt.originalMesh);
            SafeDestroyLatticeOnlyMesh(dt.deformedMeshA);
            SafeDestroyLatticeOnlyMesh(dt.deformedMeshB);
            dt.deformedMeshA = null;
            dt.deformedMeshB = null;
            dt.wcValid = false;
            // v3.19：重置重建冷却时间戳，让重新启用时能正常重建
            dt.lastRebuildTime = -999f;
            // v3.18 性能：释放 NativeArray 缓冲
            if (dt.vertCacheNativeCreated)
            {
                if (dt.vertCacheNative.IsCreated) dt.vertCacheNative.Dispose();
                dt.vertCacheNativeCreated = false;
            }
        }
        runtimeInitialized = false; // 让 OnEnable 重新走 InitializeRuntime 重建
    }

    private void OnDestroy()
    {
        // 委托给 RestoreOriginal：还原 Renderer 引用 + 销毁 deform Mesh + 清空数据。
        // 之前 OnDestroy 自己写了一份"还原 + 销毁"的循环，与 RestoreOriginal 重复。
        // v3.19：双保险 — 域重载/异常退出时 OnDisable 可能没被调用，再 Remove 一次避免
        // s_activeLattices 持有失效引用。Remove 在已不存在的 key 上是 no-op。
        s_activeLattices.Remove(this);
        RestoreOriginal();
        // v3.19：清空 Mesh 构建失败警告 HashSet 中本 lattice 相关 key。
        // 原本是全程序集静态永不清洗，跨域重载/长期运行会缓慢增长。全部清空是最简单的方案 —
        // 集合里条目本就很少（每个 unique 源 Mesh 报错信息一条记录），清空无副作用。
        s_warnedMeshBuildFailures.Clear();
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

        // 关键：CreateLatticeController 路径下，用户点「初始化晶格」后立刻拖手柄
        // 会出现"模型不变形，必须保存场景重开后才生效"。
        // 根因：AddComponent 触发的 OnEnable 走 TryRecoverFromBuildScene 路径并设了
        // runtimeInitialized=true，后续 OnEnable 早退导致 [NonSerialized] 权重缓存
        // 状态不干净；InitializeLattice 流程本身没显式重建缓存也没显式刷一次变形。
        // 此处与 InitializeRuntime 完全对齐：调一次 RebuildDeformMeshes 重建完整
        // 流水线 + 显式 ApplyDeformation 写初始顶点，确保用户拖手柄前 deformedMeshA
        // 已挂上 Renderer 且顶点已写入，避免"首次拖手柄不变形"问题。
        RebuildDeformMeshes();
        isDirty = true;
        ApplyDeformation();
    }

    public void RebuildDeformMeshes()
    {
        foreach (var dt in deformTargets)
        {
            if (dt.renderer == null) continue;

            // v3.15：先清理孤儿变形网格。域重载后 dt.deformedMeshA 引用丢失（null），
            // 但 Renderer 上仍挂着旧的 DontSave 变形 Mesh。必须在下面把 Renderer 改回
            // originalMesh 之前先销毁它，否则它被彻底孤立、永不释放，每次重载累积一份。
            {
                Mesh onRenderer = GetRendererMesh(dt.renderer);
                if (onRenderer != null && onRenderer != dt.deformedMeshA && IsLatticeDeformMesh(onRenderer))
                    SafeDestroyLatticeOnlyMesh(onRenderer);
            }

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
                // v3.8：顶点重新读取时同时补抓拓扑（如果拓扑还没缓存）
                if (dt.originalTriangles == null || dt.originalTriangles.Length == 0)
                    CaptureMeshTopology(readable, dt);
                if (readable != sharedMesh) SafeDestroy(readable);
            }

            // 确保 originalMesh 引用有效
            if (dt.originalMesh == null || IsLatticeDeformMesh(dt.originalMesh))
            {
                Mesh shared = GetRendererMesh(dt.renderer);
                if (shared != null && !IsLatticeDeformMesh(shared))
                    dt.originalMesh = shared;
            }

            RecreateDeformMeshesFor(dt);
        }
    }

    public void ApplyDeformation()
    {
        if (!initialized) return;
        if (!EnsureDeformMeshesValid()) return;
        if (!CheckDirty()) return;

        // v3.18 性能：LOD 距离剔除 — 距离主相机超过 maxDeformDistance 的对象跳过变形。
        // 远处对象玩家基本看不见，跳过可省 50%+ 变形开销。
        // 缓存主相机引用 1 秒，避免每帧 Camera.main 调用的开销。
        if (s_enableLODCulling && Time.unscaledTime - s_lastCamRefreshTime >= kCamRefreshInterval)
        {
            s_cachedMainCam = Camera.main;
            s_lastCamRefreshTime = Time.unscaledTime;
        }
        float maxDistSqr = s_maxDeformDistance * s_maxDeformDistance;
        bool hasMainCam = s_cachedMainCam != null;
        Vector3 camPos = hasMainCam ? s_cachedMainCam.transform.position : Vector3.zero;
        Vector3 latticePos = transform.position;

        // v3.20 已被撤回 — 还原为 v3.18/v3.19 纯 CPU 路径。
        // 理由：v3.20 GPU 路径在你场景中引入新内存增长（ComputeBuffer + 同步读回路径 + ControlPoint SetData
        // 调度开销），玩家端 70% OOM 闪退现象在测试版（带 v3.20）出现，而之前不带 v3.20 时未出现，
        // 印证 v3.20 引入了新内存源。先撤回保持 v3.19 稳定状态。
        //
        // v3.21 诊断增强：LogMemoryDiagnostics 增加"活动变形 Mesh 精确统计"，
        // 避免 Mesh 池按后缀扫描误判。开启 s_enableMemoryDiagnostics=true，
        // 5 秒一次日志：[LatticeMemDiag] 活动晶格=N 变形目标=N | 活动变形Mesh=N(~MB)
        // 池中_后缀=N(~MB) Mesh池=N | Profiler 分配=NMB 预留=NMB
        foreach (var dt in deformTargets)
        {
            if (dt.renderer == null || dt.deformedMeshA == null || dt.originalVertices == null) continue;

            // v3.18 性能：LOD 距离剔除
            if (s_enableLODCulling && hasMainCam)
            {
                float dx = latticePos.x - camPos.x;
                float dy = latticePos.y - camPos.y;
                float dz = latticePos.z - camPos.z;
                if (dx * dx + dy * dy + dz * dz > maxDistSqr) continue;
            }

            // v3.13：取消蒙皮双缓冲 + 每帧重新赋值 sharedMesh。
            // 原因：对 SkinnedMeshRenderer 每帧重新赋值 sharedMesh 会让其闪现一帧
            // 绑定姿势/原始形态，旋转根节点（每帧都有变更）时表现为"原模型与变形形态来回闪跳"。
            // 正确做法：deformedMeshA 已在 RecreateDeformMeshesFor 赋给 Renderer，
            // 这里只更新它的顶点，SkinnedMeshRenderer 会自动用新顶点重新蒙皮，无需重新赋值。
            //
            // v3.22 性能：DeformVertices 返回 false 表示本 Renderer 没有任何顶点进入晶格范围
            // （K 帧控制点未动 / 晶格位置偏远 / Renderer 在晶格外），跳过 SetRendererMesh 避免
            // 触发 SkinnedMeshRenderer 重复赋值 sharedMesh（虽然上面 v3.13 已避免每帧重复，
            // 但这里进一步保证"顶点未变"时连 SetRendererMesh 的引用比较都不做）。
            if (!DeformVertices(dt, dt.deformedMeshA)) continue;

            // 仅当当前 sharedMesh 不是 deformedMeshA 时才赋值（例如刚重建后），避免每帧重复赋值。
            // 用缓存的 MeshFilter 取当前 Mesh，避免每帧 GetComponent。
            if (GetCurrentMeshFast(dt) != dt.deformedMeshA)
                SetRendererMesh(dt.renderer, dt.deformedMeshA);
        }

        SaveSnapshot();
    }

    // v3.18 性能：LOD 距离剔除配置
    public static bool s_enableLODCulling = true;
    public static float s_maxDeformDistance = 22f; // 12 米外的晶格对象跳过变形
    private static Camera s_cachedMainCam;
    private static float s_lastCamRefreshTime = -999f;
    private const float kCamRefreshInterval = 1f; // 1 秒刷新一次主相机引用（避免每帧 Camera.main）


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
                    // v3.8：刷新源 Mesh 时同步刷新拓扑缓存
                    CaptureMeshTopology(readable, dt);
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
                    // v3.8：刷新源 Mesh 时同步刷新拓扑缓存
                    CaptureMeshTopology(readable, dt);
                    if (readable != currentMesh) SafeDestroy(readable);
                }
            }

            // v3.23.2 回退：恢复 v3.19 顺序的 SafeDestroy（先销毁再创建）。
            // v3.23 的"先切 Renderer 引用再 Destroy"在 SkinnedMeshRenderer 首次创建场景下
            // 导致模型看不到（怀疑切回 originalMesh 后 SkinnedMeshRenderer 蒙皮缓冲未及时重建）。
            SafeDestroyLatticeOnlyMesh(dt.deformedMeshA);
            SafeDestroyLatticeOnlyMesh(dt.deformedMeshB);
            RecreateDeformMeshesFor(dt);
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
            // v3.18 性能：释放 NativeArray 缓冲（避免 native 内存泄漏）
            if (dt.vertCacheNativeCreated)
            {
                if (dt.vertCacheNative.IsCreated) dt.vertCacheNative.Dispose();
                dt.vertCacheNativeCreated = false;
            }
        }
        deformTargets.Clear();
        initialized = false;
        controlPoints = null;
        initialControlPoints = null;
        // v3.17：清空实例级缓存数组，让 GC 能及时回收。
        cachedControlPoints = null;
        cachedCombined = null;
        cachedFeather = 0f;
        isDirty = true;
        _powA = null;
        _powB = null;
        runtimeInitialized = false;
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

    /// 把所有 Renderer 的 sharedMesh 引用还原回 originalMesh 资产（不销毁晶格数据）。
    /// Editor 端在「保存场景」/「打包前」调用，避免 Build 场景中 Renderer 引用指向
    /// 无 Asset GUID 的运行时 deform Mesh，导致打包后模型不可见。
    /// 调用后晶格仍处于 initialized 状态，控制点继续实时控制（但 Renderer 显示 originalMesh），
    /// 下次执行 ApplyDeformation / RebuildDeformMeshes / 进入 Play 模式时
    /// 会自动重新生成 deform Mesh 并挂回 Renderer。
    public void RestoreRenderersToOriginal()
    {
        foreach (var dt in deformTargets)
        {
            if (dt.renderer != null && dt.originalMesh != null)
                SetRendererMesh(dt.renderer, dt.originalMesh);
        }
    }

    /// v3.10 迁移修复：修复"工具改造前创建的旧晶格打包后不可见"。
    /// 旧版本两个隐患会一起导致玩家端模型消失：
    ///   ① originalTriangles 用 int[][] 锯齿数组存储，Unity 序列化时被静默丢弃，
    ///      玩家端反序列化为 null → CreateDeformMeshFromCache 无法重建。
    ///   ② originalMesh 曾被旧 bug 污染为 _LatticeDeform_ 运行时副本（DontSave），
    ///      玩家端解析为 null → RestoreRenderersToOriginal 无法还原。
    /// 本方法在 Editor 端（启动 / 打包前）执行：
    ///   - 清理被污染的 originalMesh；尽量从当前 Renderer 找回真实原始 Mesh
    ///   - 用新的可序列化格式重新缓存 originalTriangles（变形副本拓扑与原始一致，可安全采样）
    ///   - 补抓缺失的 originalVertices（仅当有有效的非变形原始 Mesh）
    /// 返回是否发生修改（供 Editor 决定是否标记变更 + 提示保存）。
    public bool MigrateAndRepair()
    {
        if (deformTargets == null) return false;
        bool changed = false;

        foreach (var dt in deformTargets)
        {
            if (dt == null || dt.renderer == null) continue;

            // ① 清理被污染的 originalMesh（指向晶格变形副本）
            if (dt.originalMesh != null && IsLatticeDeformMesh(dt.originalMesh))
            {
                dt.originalMesh = null;
                changed = true;
            }

            // ② originalMesh 为空时，尝试从当前 Renderer 找回真实原始 Mesh（非变形副本）
            if (dt.originalMesh == null)
            {
                Mesh cur = GetRendererMesh(dt.renderer);
                if (cur != null && !IsLatticeDeformMesh(cur))
                {
                    dt.originalMesh = cur;
                    changed = true;
                }
            }

            // ③ 重新缓存拓扑（新的可序列化格式）。
            //    优先用 originalMesh；没有就用当前 Renderer 上的 Mesh
            //    （变形副本的三角形索引与原始 Mesh 一致，可安全采样）。
            if (dt.originalTriangles == null || dt.originalTriangles.Length == 0)
            {
                Mesh topoSource = dt.originalMesh != null ? dt.originalMesh : GetRendererMesh(dt.renderer);
                if (topoSource != null)
                {
                    Mesh readable = topoSource.isReadable ? topoSource : GetReadableMesh(dt.renderer);
                    if (readable != null)
                    {
                        CaptureMeshTopology(readable, dt);
                        if (readable != topoSource) SafeDestroy(readable);
                        changed = true;
                    }
                }
            }

            // ④ originalVertices 缺失时补抓（仅当有有效的非变形原始 Mesh）
            if ((dt.originalVertices == null || dt.originalVertices.Length == 0) && dt.originalMesh != null)
            {
                Mesh readable = dt.originalMesh.isReadable ? dt.originalMesh : GetReadableMesh(dt.renderer);
                if (readable != null)
                {
                    dt.originalVertices = readable.vertices;
                    if (readable != dt.originalMesh) SafeDestroy(readable);
                    changed = true;
                }
            }
        }

        return changed;
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

    /// 修复多目标模式下丢失绑定的 Renderer。
    /// 检查 manualRenderers 列表（或 targetRoot 下的所有 Renderer）中
    /// 哪些没有在当前 deformTargets 中绑定，重新链接它们。
    /// 同时清理 deformTargets 中 renderer 已为 null 的无效条目。
    /// 返回修复（重新链接）的 Renderer 数量
    public int RepairMissingBindings()
    {
        if (!initialized) return 0;

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
        bool changed = false;
        for (int i = 0; i < controlPoints.Length && i < controlPointTransforms.Length; i++)
        {
            if (controlPointTransforms[i] == null) continue;
            Vector3 lp = controlPointTransforms[i].localPosition;
            if (lp != controlPoints[i])
            {
                controlPoints[i] = lp;
                changed = true;
            }
        }
        // v3.18 性能：检测到任意控制点变化时直接 MarkDirty，让 CheckDirty 短路生效。
        // 避免每帧 27 个 Vector3 比较 + 完整 ApplyDeformation 调用。
        if (changed) MarkDirty();
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
        // v3.9：统一使用多目标逻辑，不再有 SingleRenderer 路径。
        // 优先使用 manualRenderers 列表，其次 targetRoot 子级。
        if (manualRenderers.Count > 0)
        {
            var valid = new List<Renderer>();
            foreach (var r in manualRenderers)
                if (r != null && !valid.Contains(r))
                    valid.Add(r);
            if (valid.Count > 0) return valid;
        }

        if (targetRoot != null)
        {
            var all = targetRoot.GetComponentsInChildren<Renderer>(true);
            if (all.Length > 0) return new List<Renderer>(all);
        }

        Debug.LogWarning("[LatticeModifier] 请先指定目标 Renderer（手动列表或根节点）");
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

        // v3.8：缓存 Mesh 拓扑（每个 subMesh 的三角形索引 + subMesh 数量），
        // 让 EnsureDeformMeshesValid 在 originalMesh + deformedMeshA 都丢失时仍能重建 Mesh。
        CaptureMeshTopology(readable, dt);

        if (readable != sharedMesh) SafeDestroy(readable);

        RecreateDeformMeshesFor(dt);
        return dt;
    }

    /// v3.8 新增：从可读 Mesh 抓取拓扑数据写入 DeformTarget 缓存。
    /// 用于 EnsureDeformMeshesValid 兜底路径（originalMesh 丢失 + deformedMeshA 被 GC），
    /// 玩家端仍能基于 originalVertices + originalTriangles 重建出可见 Mesh。
    private static void CaptureMeshTopology(Mesh readable, DeformTarget dt)
    {
        if (readable == null || dt == null) return;
        int subMeshCount = readable.subMeshCount;
        dt.originalSubMeshCount = subMeshCount;
        dt.originalTriangles = new SerializableTriangles[subMeshCount];
        for (int s = 0; s < subMeshCount; s++)
        {
            dt.originalTriangles[s] = new SerializableTriangles { triangles = readable.GetTriangles(s) };
        }

        int vc = readable.vertexCount;

        // v3.15 内存优化：完整顶点通道缓存只在「源 Mesh 不可读」时才需要——
        // 因为只有不可读 Mesh 在玩家端走缓存重建路径（CreateDeformMeshFromCache）。
        // 源 Mesh 可读时玩家端走 Instantiate，直接从活动 Mesh 复制全部通道，
        // 这些缓存数组完全用不到，缓存它们只是白白占用托管内存 + 增大场景体积。
        // 因此可读 Mesh 只保留每帧变形必需的 originalVertices（在 CreateDeformTarget 中已抓），
        // 跳过法线/切线/UV/颜色/骨骼权重/绑定姿势的缓存。
        bool sourceUnreadable = dt.originalMesh == null || !dt.originalMesh.isReadable;
        if (!sourceUnreadable)
        {
            dt.originalNormals = null;
            dt.originalTangents = null;
            dt.originalUV = null;
            dt.originalColors = null;
            dt.originalBoneWeights = null;
            dt.originalBindposes = null;
            return;
        }

        // 不可读 Mesh：缓存完整顶点通道，玩家端从缓存重建出带 UV/法线/蒙皮的完整 Mesh
        var normals = readable.normals;
        dt.originalNormals = (normals != null && normals.Length == vc) ? normals : null;

        var tangents = readable.tangents;
        dt.originalTangents = (tangents != null && tangents.Length == vc) ? tangents : null;

        var uv = readable.uv;
        dt.originalUV = (uv != null && uv.Length == vc) ? uv : null;

        var colors = readable.colors;
        dt.originalColors = (colors != null && colors.Length == vc) ? colors : null;

        // v3.12：缓存蒙皮数据（仅带蒙皮目标）
        if (dt.isSkinned)
        {
            var bw = readable.boneWeights;
            dt.originalBoneWeights = (bw != null && bw.Length == vc) ? bw : null;
            var bp = readable.bindposes;
            dt.originalBindposes = (bp != null && bp.Length > 0) ? bp : null;
        }
        else
        {
            dt.originalBoneWeights = null;
            dt.originalBindposes = null;
        }
    }

    /// 为单个 DeformTarget 重建 A/B 缓冲 deform Mesh 并挂回 Renderer。
    /// 之前在 CreateDeformTarget / RebuildDeformMeshes / RefreshSourceMesh / EnsureDeformMeshesValid
    /// 四处都有 4-5 行重复，现在统一到这里。
    ///
    /// v3.23.2 关键回退：v3.23 修复的"销毁顺序（先切回 originalMesh 再 Destroy 旧 Mesh）"在
    /// 某些 SkinnedMeshRenderer 场景下会导致模型看不到（怀疑切回 originalMesh 后 SkinnedMeshRenderer
    /// 内部蒙皮顶点缓冲未及时重新挂上，渲染前 GPU 端看到的是空 Mesh）。先回退到 v3.19 顺序（先 Destroy
    /// 旧 Mesh → 创建新 Mesh → SetRendererMesh 挂上），靠 v3.17 5 秒重建冷却 + 其他 v3.19 优化
    /// （vertCacheNative / AcquireReadOnlyMeshData / 死代码清理）共同控制内存增长。
    /// 玩家端 %MEM 18.5% 稳定即可证明 v3.19 优化路径已足够。
    private void RecreateDeformMeshesFor(DeformTarget dt)
    {
        if (dt == null) return;
        if (dt.originalVertices == null) return;

        // v3.15：清理「孤儿变形网格」——域重载（脚本重编译 / 进出 Play 模式）后，
        // dt.deformedMeshA（[NonSerialized]）引用丢失变 null，但其指向的 DontSave 原生 Mesh
        // 仍然存活并挂在 Renderer 上。若只 SafeDestroy(dt.deformedMeshA)（已是 null）则销毁不到它，
        // 每次重载/重建都泄漏一个变形 Mesh（Profiler 里堆积大量 _LatticeDeform_ / LatticeCachedMesh）。
        // 这里先把 Renderer 当前挂着的、且不是 dt.deformedMeshA 的变形 Mesh 一并销毁。
        if (dt.renderer != null)
        {
            Mesh onRenderer = GetRendererMesh(dt.renderer);
            if (onRenderer != null && onRenderer != dt.deformedMeshA && IsLatticeDeformMesh(onRenderer))
                SafeDestroyLatticeOnlyMesh(onRenderer);
        }

        SafeDestroyLatticeOnlyMesh(dt.deformedMeshA);
        SafeDestroyLatticeOnlyMesh(dt.deformedMeshB);

        // v3.13.1：deformedMeshA 必须是「可写」的 Mesh，因为 DeformVertices 每帧会写入它的顶点。
        // - 源 Mesh 可读时：Instantiate 出的副本同样可读/可写，且完整保留所有通道（含蒙皮/blendshape），走源路径。
        // - 源 Mesh 不可读时：走缓存路径（new Mesh()，天然可写），缓存已含
        //   vertices/triangles/normals/tangents/uv/colors + 蒙皮 boneWeights/bindposes。
        //
        // 注意：不能再用 Application.isEditor 判断走源路径——Play 模式下 Application.isEditor 仍为 true，
        // 但此时 Instantiate 不可读 Mesh 得到的副本同样「不可读/不可写」，
        // 导致 DeformVertices 设置 vertices 时报 "Not allowed to access vertices ... isReadable is false"。
        bool canUseSource = dt.originalMesh != null && dt.originalMesh.isReadable;

        if (canUseSource)
        {
            dt.deformedMeshA = CreateDeformMesh(dt.originalMesh, dt.originalVertices);
        }
        else
        {
            dt.deformedMeshA = CreateDeformMeshFromCache(dt);
        }
        dt.deformedMeshB = null; // v3.13：取消双缓冲，B 不再使用
        dt.wcValid = false;      // v3.14：顶点/网格重建后权重缓存失效，下次变形会重算

        if (dt.renderer != null && dt.deformedMeshA != null)
            SetRendererMesh(dt.renderer, dt.deformedMeshA);
        dt.useBufferB = false;

        // v3.17：记录重建时间戳，用于 EnsureDeformMeshesValid 冷却判断，
        // 避免在玩家端持续 Instantiate 累积。
        dt.lastRebuildTime = Time.unscaledTime;

    }



    #endregion

    #region 内部 - 包围盒计算

    private void ComputeBounds()
    {
        // v3.5 关键修复：先在「目标 Renderer local 空间」算 AABB，
        // 再把 AABB 的 8 个角点转到「晶格 local 空间」求晶格空间 AABB。
        // 目标带旋转/缩放时，world 空间 AABB 会被膨胀，导致 latticeSize 偏大、控制点距离偏大、模型轴向拉伸。
        // 这里抽两个工具方法（ComputeLocalAABB / TransformBoundsToLocalSpace）让语义清晰。
        Bounds? localAabb = null;
        foreach (var dt in deformTargets)
        {
            if (dt.renderer == null || dt.originalVertices == null) continue;
            Bounds a = ComputeLocalAABB(dt.originalVertices);
            localAabb = localAabb.HasValue ? MergeBounds(localAabb.Value, a) : a;
        }
        if (!localAabb.HasValue) return;

        Bounds bounds = TransformBoundsToLocalSpace(localAabb.Value, deformTargets[0].renderer.transform);
        bounds.Expand(bounds.size * 0.02f);
        latticeMin = bounds.min;
        latticeSize = bounds.size;
    }

    /// 计算一组顶点的 AABB（按顶点自身坐标系，不经任何 transform）。
    private static Bounds ComputeLocalAABB(Vector3[] vertices)
    {
        Bounds b = new Bounds(vertices[0], Vector3.zero);
        for (int i = 1; i < vertices.Length; i++)
            b.Encapsulate(vertices[i]);
        return b;
    }

    /// 把 sourceAabb 的 8 个角点经 targetT.transform 转 latticeLocal 空间，求 AABB。
    private Bounds TransformBoundsToLocalSpace(Bounds sourceAabb, Transform targetT)
    {
        Vector3 mn = sourceAabb.min, mx = sourceAabb.max;
        Vector3[] corners = new Vector3[8]
        {
            new Vector3(mn.x, mn.y, mn.z),
            new Vector3(mx.x, mn.y, mn.z),
            new Vector3(mn.x, mx.y, mn.z),
            new Vector3(mx.x, mx.y, mn.z),
            new Vector3(mn.x, mn.y, mx.z),
            new Vector3(mx.x, mn.y, mx.z),
            new Vector3(mn.x, mx.y, mx.z),
            new Vector3(mx.x, mx.y, mx.z),
        };
        Bounds b = new Bounds();
        for (int c = 0; c < 8; c++)
        {
            Vector3 world = targetT.TransformPoint(corners[c]);
            Vector3 lp = transform.InverseTransformPoint(world);
            if (c == 0) b = new Bounds(lp, Vector3.zero);
            else b.Encapsulate(lp);
        }
        return b;
    }

    private static Bounds MergeBounds(Bounds a, Bounds b)
    {
        Bounds r = a;
        r.Encapsulate(b.min);
        r.Encapsulate(b.max);
        return r;
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

    /// 计算单轴羽化系数。参数 coord ∈ [0,1] 为归一化坐标，featherSize 为羽化带宽度占比。
    /// 在 [0, featherSize] 和 [1-featherSize, 1] 范围内从 0 平滑过渡到 1（smoothstep）。
    private static float FeatherFactor(float coord, float featherSize)
    {
        if (featherSize <= 0f) return 1f;
        float f;
        if (coord < featherSize)
            f = coord / featherSize;
        else if (coord > 1f - featherSize)
            f = (1f - coord) / featherSize;
        else
            return 1f;
        // smoothstep: 3t² - 2t³
        return f * f * (3f - 2f * f);
    }

    #endregion

    #region 内部 - 变形核心

    // v3.14：二项式系数行缓存（degree 0..8），避免每顶点每基函数重算 Binomial。
    private static readonly Dictionary<int, float[]> s_binomRows = new Dictionary<int, float[]>();
    private static float[] GetBinomRow(int n)
    {
        if (!s_binomRows.TryGetValue(n, out var row))
        {
            row = new float[n + 1];
            for (int k = 0; k <= n; k++) row[k] = Binomial(n, k);
            s_binomRows[n] = row;
        }
        return row;
    }

    // v3.22 性能：返回是否有任何顶点进入 FFD 变形逻辑（用于调用方决定是否触发 SetVertices + SkinnedMeshRenderer 蒙皮）。
    // 背景：26 个 SkinnedMeshRenderer 共享同一个 LatticeModifier 时，K 帧只动少数控制点，
    // 多数 Renderer 的顶点根本不在晶格范围内 — 但旧版每帧仍对所有 Renderer 跑 DeformVertices + SetVertices，
    // 触发 SkinnedMeshRenderer 每帧重新蒙皮（GPU 端 dispatch + 顶点缓冲上传），是移动端主要性能与 VRAM 增长源。
    // 改造：循环里追踪 anyInRange；为 false 时直接 return false，跳过 SetVertices + RecalculateBounds，
    // SkinnedMeshRenderer 复用上一帧蒙皮结果（顶点缓冲未变，引擎不会触发重蒙皮）。
    private bool DeformVertices(DeformTarget dt, Mesh dstMesh)
    {
        Vector3[] srcVerts = dt.originalVertices;
        if (dstMesh == null || srcVerts == null) return false;
        Transform targetT = dt.renderer.transform;

        int nx = PointCountX, ny = PointCountY, nz = PointCountZ;
        int l = divisionsX, m = divisionsY, n = divisionsZ;
        Matrix4x4 curLatticeW2L = transform.worldToLocalMatrix;
        Matrix4x4 curTargetL2W = targetT.localToWorldMatrix;
        // 组合矩阵：直接把目标 local 顶点映射到晶格 local，决定 s,t,u/基函数/initPos
        Matrix4x4 combined = curLatticeW2L * curTargetL2W;

        // v3.18 性能：维护一个 NativeArray<Vector3> 用于 Mesh.SetVertices 零拷贝上传。
        // 首次创建后复用，避免 mesh.vertices= 内部的中转数组分配。
        if (!dt.vertCacheNativeCreated || dt.vertCacheNative.Length != srcVerts.Length)
        {
            if (dt.vertCacheNativeCreated) dt.vertCacheNative.Dispose();
            dt.vertCacheNative = new NativeArray<Vector3>(srcVerts.Length, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            dt.vertCacheNativeCreated = true;
        }
        NativeArray<Vector3> vertCacheNative = dt.vertCacheNative;

        // 权重缓存是否仍然有效（组合矩阵 + feather + 顶点数 都未变）
        bool cacheValid = dt.wcValid
            && dt.wcVertCount == srcVerts.Length
            && dt.wcFeather == feather
            && dt.wcMatrix == combined;

        if (!cacheValid)
            RefreshWeightCache(dt, srcVerts, combined, nx, ny, nz, l, m, n);

        bool[] inRange = dt.wcInRange;
        float[] featherW = dt.wcFeatherW;
        Vector3[] initPosArr = dt.wcInitPos;
        float[] bxAll = dt.wcBx;
        float[] byAll = dt.wcBy;
        float[] bzAll = dt.wcBz;
        Vector3[] cpArr = controlPoints;

        // v3.18 性能：预乘「targetW2L * latL2W」为一个 3x3 矩阵，循环内省一次矩阵乘加（9 次乘加），
        // 替代原来的「worldOffset = latTransform.MultiplyVector(offset) → localOffset = targetInvTransform.MultiplyVector(worldOffset)」（18 次乘加 + 两次方法调用）。
        Matrix4x4 targetW2L = targetT.worldToLocalMatrix;
        Matrix4x4 latL2W = transform.localToWorldMatrix;
        // 预乘矩阵 = targetW2L * latL2W（用于 offset 转换）
        Matrix4x4 premul = targetW2L * latL2W;
        // 展开为 9 个 float（避免内层访问 Matrix4x4 属性的开销）
        float pm00 = premul.m00, pm01 = premul.m01, pm02 = premul.m02;
        float pm10 = premul.m10, pm11 = premul.m11, pm12 = premul.m12;
        float pm20 = premul.m20, pm21 = premul.m21, pm22 = premul.m22;

        int nxny = nx * ny;
        int vc = srcVerts.Length;

        // v3.22 性能：追踪本 Renderer 是否有任何顶点真正进入 FFD 变形。
        // 整个 Renderer 一个顶点都不在范围内时，跳过 SetVertices + RecalculateBounds，
        // SkinnedMeshRenderer 复用上一帧蒙皮结果，省掉一次 GPU 顶点上传 + 蒙皮 dispatch。
        bool anyInRange = false;

        for (int v = 0; v < vc; v++)
        {
            Vector3 src = srcVerts[v];
            if (!inRange[v])
            {
                vertCacheNative[v] = src;
                continue;
            }

            // 至少一个顶点进入 FFD
            anyInRange = true;

            int bxBase = v * nx, byBase = v * ny, bzBase = v * nz;

            // 只重算 deformedPos（initPos 已缓存）：Σ w·controlPoints
            // v3.18 性能：累加用 3 个 float 而非 Vector3（避免 Vector3 内部隐式拆/装）
            float deformedX = 0f, deformedY = 0f, deformedZ = 0f;
            for (int ix = 0; ix < nx; ix++)
            {
                float bx = bxAll[bxBase + ix];
                if (bx == 0f) continue;
                for (int iy = 0; iy < ny; iy++)
                {
                    float bxy = bx * byAll[byBase + iy];
                    if (bxy == 0f) continue;
                    int idxBase = ix + iy * nx;
                    for (int iz = 0; iz < nz; iz++)
                    {
                        float w = bxy * bzAll[bzBase + iz];
                        if (w == 0f) continue;
                        Vector3 cp = cpArr[idxBase + iz * nxny];
                        deformedX += w * cp.x;
                        deformedY += w * cp.y;
                        deformedZ += w * cp.z;
                    }
                }
            }

            // v3.18 性能：合并 worldOffset + localOffset 两次矩阵乘加为一次（已预乘到 pmXX）
            Vector3 initPos = initPosArr[v];
            float offX = deformedX - initPos.x;
            float offY = deformedY - initPos.y;
            float offZ = deformedZ - initPos.z;
            float fw = featherW[v];
            // premul * offset（仅旋转+缩放部分，因为这是向量）
            float lox = (pm00 * offX + pm01 * offY + pm02 * offZ) * fw;
            float loy = (pm10 * offX + pm11 * offY + pm12 * offZ) * fw;
            float loz = (pm20 * offX + pm21 * offY + pm22 * offZ) * fw;

            vertCacheNative[v] = new Vector3(src.x + lox, src.y + loy, src.z + loz);
        }

        // v3.22 性能：整个 Renderer 无任何顶点进入 FFD 时，跳过 GPU 上传。
        // SkinnedMeshRenderer 看到 sharedMesh.vertices 没变 → 复用上一帧蒙皮结果，
        // 省掉一次 GPU 顶点缓冲上传 + 一次蒙皮 Compute Shader dispatch + 一次 mesh upload fence。
        if (!anyInRange) return false;

        // v3.18 性能：用 NativeArray 路径写入（Mesh.SetVertices(NativeArray) 是零拷贝上传）
        // 这比 mesh.vertices= 数组少一次托管堆分配 + 一次额外 native 拷贝。
        dstMesh.SetVertices(vertCacheNative);

        // v3.18 性能：RecalculateBounds 节流 — 每 4 帧才重算一次 AABB，期间复用旧值。
        // 原理：晶格变形是"局部平滑形变"，AABB 不会每帧剧变；低端机 1 帧 RecalculateBounds
        // 50K 顶点约 0.1-0.5ms，节流后省 75% 该开销。
        dt.framesSinceBoundsRecalc++;
        if (dt.framesSinceBoundsRecalc >= 4 || dt.lastRecalcBoundsTime < 0f)
        {
            dt.framesSinceBoundsRecalc = 0;
            dt.lastRecalcBoundsTime = Time.frameCount;
            dstMesh.RecalculateBounds();
        }
        return true;
    }

    /// v3.14：重建每顶点权重缓存（仅在组合矩阵 / feather / 顶点数变化时调用）。
    /// 这里集中承担昂贵计算：s,t,u、范围检测、羽化、Bernstein 基函数、initPos。
    /// Bernstein 用累乘幂表代替 Mathf.Pow，二项式系数用预计算行，无任何 Pow 调用。
    private void RefreshWeightCache(DeformTarget dt, Vector3[] srcVerts, Matrix4x4 combined,
        int nx, int ny, int nz, int l, int m, int n)
    {
        int vc = srcVerts.Length;
        if (dt.wcInRange == null || dt.wcInRange.Length != vc) dt.wcInRange = new bool[vc];
        if (dt.wcFeatherW == null || dt.wcFeatherW.Length != vc) dt.wcFeatherW = new float[vc];
        if (dt.wcInitPos == null || dt.wcInitPos.Length != vc) dt.wcInitPos = new Vector3[vc];
        if (dt.wcBx == null || dt.wcBx.Length != vc * nx) dt.wcBx = new float[vc * nx];
        if (dt.wcBy == null || dt.wcBy.Length != vc * ny) dt.wcBy = new float[vc * ny];
        if (dt.wcBz == null || dt.wcBz.Length != vc * nz) dt.wcBz = new float[vc * nz];

        float[] binomX = GetBinomRow(l);
        float[] binomY = GetBinomRow(m);
        float[] binomZ = GetBinomRow(n);

        // v3.14.4：复用实例级幂表缓冲，避免每帧（目标移动时缓存每帧重建）分配两个临时数组，
        // 消除持续的 GC 堆增长（IL2CPP/移动端托管堆只增不还，长期会被误判为泄漏并 OOM）。
        int maxDeg = Mathf.Max(l, Mathf.Max(m, n));
        if (_powA == null || _powA.Length < maxDeg + 1)
        {
            _powA = new float[maxDeg + 1];
            _powB = new float[maxDeg + 1];
        }
        float[] powA = _powA;
        float[] powB = _powB;

        int nxny = nx * ny;

        for (int v = 0; v < vc; v++)
        {
            Vector3 latticeLocal = combined.MultiplyPoint3x4(srcVerts[v]);
            float s = latticeSize.x > 0 ? (latticeLocal.x - latticeMin.x) / latticeSize.x : 0;
            float t = latticeSize.y > 0 ? (latticeLocal.y - latticeMin.y) / latticeSize.y : 0;
            float u = latticeSize.z > 0 ? (latticeLocal.z - latticeMin.z) / latticeSize.z : 0;

            if (s < -0.01f || s > 1.01f || t < -0.01f || t > 1.01f || u < -0.01f || u > 1.01f)
            {
                dt.wcInRange[v] = false;
                continue;
            }
            dt.wcInRange[v] = true;

            s = Mathf.Clamp01(s); t = Mathf.Clamp01(t); u = Mathf.Clamp01(u);

            float fw = 1f;
            if (feather > 0f)
                fw = FeatherFactor(s, feather) * FeatherFactor(t, feather) * FeatherFactor(u, feather);
            dt.wcFeatherW[v] = fw;

            int bxBase = v * nx, byBase = v * ny, bzBase = v * nz;
            FillBasis(s, l, binomX, dt.wcBx, bxBase, powA, powB);
            FillBasis(t, m, binomY, dt.wcBy, byBase, powA, powB);
            FillBasis(u, n, binomZ, dt.wcBz, bzBase, powA, powB);

            // initPos = Σ w·initialControlPoints（控制点初始位置恒定，故可缓存）
            Vector3 initPos = Vector3.zero;
            for (int ix = 0; ix < nx; ix++)
            {
                float bx = dt.wcBx[bxBase + ix];
                if (bx == 0f) continue;
                for (int iy = 0; iy < ny; iy++)
                {
                    float bxy = bx * dt.wcBy[byBase + iy];
                    if (bxy == 0f) continue;
                    int idxBase = ix + iy * nx;
                    for (int iz = 0; iz < nz; iz++)
                    {
                        float w = bxy * dt.wcBz[bzBase + iz];
                        if (w == 0f) continue;
                        initPos += w * initialControlPoints[idxBase + iz * nxny];
                    }
                }
            }
            dt.wcInitPos[v] = initPos;
        }

        dt.wcMatrix = combined;
        dt.wcFeather = feather;
        dt.wcVertCount = vc;
        dt.wcValid = true;
    }

    /// 用累乘幂表填充一组 Bernstein 基函数到 dest[baseIndex .. baseIndex+degree]，无 Mathf.Pow。
    /// B(i,deg,c) = C(deg,i) · c^i · (1-c)^(deg-i)
    private static void FillBasis(float c, int degree, float[] binomRow, float[] dest, int baseIndex,
        float[] powA, float[] powB)
    {
        float ic = 1f - c;
        powA[0] = 1f; powB[0] = 1f;
        for (int i = 1; i <= degree; i++)
        {
            powA[i] = powA[i - 1] * c;
            powB[i] = powB[i - 1] * ic;
        }
        for (int i = 0; i <= degree; i++)
            dest[baseIndex + i] = binomRow[i] * powA[i] * powB[degree - i];
    }

    private bool EnsureDeformMeshesValid()
    {
        foreach (var dt in deformTargets)
        {
            if (dt.renderer == null) continue;

            // Player 端兜底：若 originalMesh 引用丢失但 originalVertices 仍在，
            // 尝试从 Renderer 当前 sharedMesh 找原始网格，必要时直接复制重建 originalMesh。
            if (dt.originalMesh == null && dt.originalVertices != null && dt.originalVertices.Length > 0)
            {
                Mesh currentMesh = GetRendererMesh(dt.renderer);
                if (currentMesh != null && !IsLatticeDeformMesh(currentMesh))
                {
                    dt.originalMesh = currentMesh;
                }
                else
                {
                    // 关键修复（v3.8 加强版）：originalMesh 和 currentMesh 都为 null 时，
                    // 优先尝试「用 deformedMeshA 顶回去」让玩家端模型可见（速度最快）。
                    // 如果 deformedMeshA 也被 GC/销毁（HideAndDontSave/DontSave 的运行时 Mesh 没资产身份），
                    // 回退到「用 DeformTarget 缓存的 vertices + triangles 重建 Mesh」——
                    // 这条路径完全不依赖 originalMesh 资产引用或 deformedMeshA 存活，
                    // 是真正可靠的最后一根稻草。
                    //
                    // v3.8 反刷屏优化：每个分支只警告 1 次（标记位 [NonSerialized]，域重载重置），
                    // 避免每帧 LateUpdate 都打警告淹没 Console。
                    if (dt.deformedMeshA != null && dt.deformedMeshA.vertexCount == dt.originalVertices.Length)
                    {
                        SetRendererMesh(dt.renderer, dt.deformedMeshA);
                        isDirty = true;
                        if (!dt.warnedOriginalMeshRecoveredByDeformedA)
                        {
                            dt.warnedOriginalMeshRecoveredByDeformedA = true;
                            Debug.LogWarning(
                                $"[LatticeModifier] '{dt.renderer.name}' 的 originalMesh 引用丢失，" +
                                $"已用 deformedMeshA 顶回 Renderer 让模型可见（显示的是上次变形状态）。");
                        }
                    }
                    else if (RebuildMeshFromCache(dt))
                    {
                        // 真正可靠的兜底：用缓存的拓扑重建 Mesh
                        // 模型在玩家端能显示，但 UV/法线是默认值（不致命：模型可见优先）
                        if (!dt.warnedRebuiltFromCache)
                        {
                            dt.warnedRebuiltFromCache = true;
                            Debug.LogWarning(
                                $"[LatticeModifier] '{dt.renderer.name}' 的 originalMesh 引用丢失，deformedMeshA 也不可用，" +
                                $"已用 DeformTarget 缓存的 vertices+triangles(+法线/UV) 重建 Mesh 让模型可见。");
                        }
                    }
                    else
                    {
                        // 极端兜底：缓存数据也不全（说明 DeformTarget 没初始化完成，或缓存损坏）
                        // 只能警告 + 跳过这个 dt
                        if (!dt.warnedTotallyFailed)
                        {
                            dt.warnedTotallyFailed = true;
                            string reason = dt.deformedMeshA == null ? "deformedMeshA 已销毁" : "deformedMeshA 顶点数量不匹配";
                            if (dt.originalTriangles == null || dt.originalTriangles.Length == 0)
                                reason += "、拓扑未缓存";
                            Debug.LogWarning(
                                $"[LatticeModifier] '{dt.renderer.name}' 的 originalMesh 引用和当前 sharedMesh 都为 null，" +
                                $"{reason}。打包后该 Renderer 可能不可见。原始顶点数据已缓存，Editor 中可见。");
                        }
                    }
                }
            }

            if (dt.originalMesh == null || dt.originalVertices == null) continue;

            bool meshLost = dt.deformedMeshA == null;

            if (!meshLost)
            {
                Mesh currentMesh = GetCurrentMeshFast(dt);
                if (currentMesh == null || (!IsLatticeDeformMesh(currentMesh) && currentMesh != dt.originalMesh))
                {
                    // 玩家端：Renderer 的 sharedMesh 引用解析失败（典型 Build 后现象）
                    // 直接重建并强制挂上
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
                // v3.19：重建冷却从 1s 拉到 5s。
                // SkinnedMeshRenderer.sharedMesh 在玩家端周期性失效时，
                // 旧 Mesh 已被 Destroy 但 Unity 尚未真正 GC 释放，
                // 此时若又 Instantiate 一个新 Mesh 会导致临时双倍内存占用并持续累积。
                // 1 秒在玩家端（移动端 GC 节奏受引擎控制）可能不够，5 秒更安全。
                if (Time.unscaledTime - dt.lastRebuildTime >= 5f)
                {
                    dt.lastRebuildTime = Time.unscaledTime;
                    SafeDestroyLatticeOnlyMesh(dt.deformedMeshA);
                    SafeDestroyLatticeOnlyMesh(dt.deformedMeshB);
                    RecreateDeformMeshesFor(dt);
                    isDirty = true;
                }
                else
                {
                    // 冷却期内：保留现有 deformedMeshA 引用（即使 Renderer 上 Mesh 已失效），
                    // 让下一帧变形时仍能写入 deformedMeshA 并重新挂回 Renderer，
                    // 避免不必要的 Instantiate 累积。
                }
            }
        }

        return deformTargets.Count > 0;
    }

    /// v3.8 新增：仅用 DeformTarget 里缓存的 originalVertices + originalTriangles
    /// 重建 deformedMeshA 并挂回 Renderer。不依赖 originalMesh 资产引用是否存在，
    /// 也不依赖 deformedMeshA 是否被 GC 销毁。
    /// 这是真正可靠的"最后一根稻草"——前 4 道防线都漏过时仍能挽救模型可见性。
    private bool RebuildMeshFromCache(DeformTarget dt)
    {
        if (dt == null) return false;
        if (dt.originalVertices == null || dt.originalVertices.Length == 0) return false;
        if (dt.originalTriangles == null || dt.originalTriangles.Length == 0) return false;
        if (dt.originalSubMeshCount <= 0) return false;

        // v3.19：重建冷却 — 避免 originalMesh 丢失分支中持续 Instantiate。
        // 与 EnsureDeformMeshesValid 对齐 5 秒冷却，覆盖玩家端 SkinnedMeshRenderer 失效场景。
        if (Time.unscaledTime - dt.lastRebuildTime < 5f) return false;

        // v3.15：清理 Renderer 上残留的孤儿变形网格（域重载后引用丢失的旧 Mesh），避免堆积。
        if (dt.renderer != null)
        {
            Mesh onRenderer = GetRendererMesh(dt.renderer);
            if (onRenderer != null && onRenderer != dt.deformedMeshA && IsLatticeDeformMesh(onRenderer))
                SafeDestroyLatticeOnlyMesh(onRenderer);
        }

        // 销毁旧 deformedMeshA（如果还在）
        SafeDestroyLatticeOnlyMesh(dt.deformedMeshA);
        SafeDestroyLatticeOnlyMesh(dt.deformedMeshB);

        // 构造新 Mesh（从缓存）
        dt.deformedMeshA = CreateDeformMeshFromCache(dt);
        dt.deformedMeshB = null; // v3.13：取消双缓冲，B 不再使用（修复此处遗留的多余创建）
        dt.useBufferB = false;

        if (dt.deformedMeshA == null) return false;

        if (dt.renderer != null)
        {
            SetRendererMesh(dt.renderer, dt.deformedMeshA);
            isDirty = true;
        }
        return true;
    }

    #endregion

    #region 内部 - Mesh 工具方法

    private static readonly string LatticeDeformSuffix = "_LatticeDeform_";

    // v3.8 新增：Mesh 构建失败警告的全局一次性抑制（按 "源名+错误" 唯一 key）
    // 防止 CreateDeformMesh / CreateDeformMeshFromCache 的 catch 块在每帧 LateUpdate 中刷屏
    private static readonly HashSet<string> s_warnedMeshBuildFailures = new HashSet<string>();

    private Mesh CreateDeformMesh(Mesh src, Vector3[] vertices)
    {
        if (src == null || vertices == null) return null;

        // v3.23.1 命名精简：去掉 src.name（FBX 资产名 + _OL 后缀常 30+ 字符）+ GetInstanceID() 改短，
        // 避免 Hierarchy 名字超 64 字符被截断。保留 _LatticeDeform_ 后缀是 IsLatticeDeformMesh 判定的硬性要求。
        string uniqueName = "LatDeform" + LatticeDeformSuffix + "_" + System.Math.Abs(GetInstanceID());

        // 源 Mesh 可读时用 Instantiate（副本可读/可写，完整保留 boneWeights/bindposes/blendShapes）。
        // 不可读 Mesh 不在此处理：RecreateDeformMeshesFor 会改走缓存路径（CreateDeformMeshFromCache，
        // 产出可写的 new Mesh 且含缓存的蒙皮数据）。这里若遇到不可读 Mesh 仅作降级手动构建。
        if (src.isReadable)
        {
            Mesh nm = Instantiate(src);
            nm.name = uniqueName;
            // 关键：hideFlags 改为 DontSave（不带 Hide 标记）。
            // HideAndDontSave 的 Mesh 在某些 Unity 版本会被 Build 系统当作"无资产身份"
            // 拒绝跟随场景引用打包，导致玩家端 Renderer.sharedMesh 解析为 null → 模型不可见。
            // DontSave 的 Mesh 仍有"被引用打包"优先级，且 IsLatticeDeformMesh 用名称后缀判断
            // （不用 hideFlags），所以原 FBX 资产（HideFlags.DontSave）不会被误判。
            nm.hideFlags = HideFlags.DontSave;
            nm.MarkDynamic();
            return nm;
        }

        // 源 Mesh 不可读，手动构建
        // v3.19 内存优化：整路径改用 NativeArray 零分配上传，避免一次性吐出
        // O(vertices × channels) 的托管数组。原先 normals/tangents/uv/colors 每次重建
        // 都会 ToArray() 一次，10 万顶点模型一次可触发 30+ MB 托管堆增长。
        Mesh mesh = new Mesh { name = uniqueName };
        mesh.hideFlags = HideFlags.DontSave;
        mesh.MarkDynamic();
        // 顶点：v3.18 优化用 NativeArray 路径，避开 mesh.vertices= 数组中转分配
        mesh.SetVertices(vertices);
        Mesh.MeshDataArray dataArr = default;
        try
        {
            mesh.subMeshCount = src.subMeshCount;
            dataArr = Mesh.AcquireReadOnlyMeshData(src);
            var data = dataArr[0];
            int vc = data.vertexCount;
            // 关键修复（v3.5）：用 HasVertexAttribute 守卫，源 Mesh 缺少某通道时跳过，
            // 不要抛异常中断整个构建流程（会导致 subMeshCount 设了但 SetTriangles 没跑、模型不可见）。
            for (int s = 0; s < data.subMeshCount; s++)
            {
                var desc = data.GetSubMesh(s);
                using var idxNative = new Unity.Collections.NativeArray<int>(desc.indexCount, Unity.Collections.Allocator.Temp);
                data.GetIndices(idxNative, s);
                // v3.19：SetIndices(NativeArray) 是 Unity 2019.3+ 才有，老版本只接受 int[]。
                // 此处退回 ToArray 一次 — 仅三角形索引的 int[] 分配（不再 ToArray normals/uv/colors
                // 那些大数组），相比 v3.18 已大幅减少。实际开发环境若升 2019.3+ 可改回 SetIndices(NativeArray)。
                mesh.SetTriangles(idxNative.ToArray(), s);
            }
            // v3.19：法线/切线/UV/颜色 全部走 NativeArray 路径，零托管分配
            if (data.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal))
            {
                var normNative = new Unity.Collections.NativeArray<Vector3>(vc, Unity.Collections.Allocator.Temp);
                try { data.GetNormals(normNative); mesh.SetNormals(normNative); }
                finally { normNative.Dispose(); }
            }
            if (data.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent))
            {
                var tanNative = new Unity.Collections.NativeArray<Vector4>(vc, Unity.Collections.Allocator.Temp);
                try { data.GetTangents(tanNative); mesh.SetTangents(tanNative); }
                finally { tanNative.Dispose(); }
            }
            // UV 通道：MeshData.GetUVs(channel, ...) 在目标通道不存在时会抛异常，
            // 必须在调用前用 HasVertexAttribute 守卫。
            if (data.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))
            {
                var uvNative = new Unity.Collections.NativeArray<Vector2>(vc, Unity.Collections.Allocator.Temp);
                try { data.GetUVs(0, uvNative); mesh.SetUVs(0, uvNative); }
                finally { uvNative.Dispose(); }
            }
            if (data.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1))
            {
                var uvNative = new Unity.Collections.NativeArray<Vector2>(vc, Unity.Collections.Allocator.Temp);
                try { data.GetUVs(1, uvNative); mesh.SetUVs(1, uvNative); }
                finally { uvNative.Dispose(); }
            }
            // uv3/uv4 同样守卫
            if (data.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord2))
            {
                var uvNative = new Unity.Collections.NativeArray<Vector2>(vc, Unity.Collections.Allocator.Temp);
                try { data.GetUVs(2, uvNative); mesh.SetUVs(2, uvNative); }
                finally { uvNative.Dispose(); }
            }
            if (data.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord3))
            {
                var uvNative = new Unity.Collections.NativeArray<Vector2>(vc, Unity.Collections.Allocator.Temp);
                try { data.GetUVs(3, uvNative); mesh.SetUVs(3, uvNative); }
                finally { uvNative.Dispose(); }
            }
            // 颜色通道
            if (data.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color))
            {
                var colNative = new Unity.Collections.NativeArray<Color>(vc, Unity.Collections.Allocator.Temp);
                try { data.GetColors(colNative); mesh.SetColors(colNative); }
                finally { colNative.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            // v3.8 反刷屏：按"源 Mesh 名 + 错误信息"去重，每帧 LateUpdate 只警告 1 次
            string warnKey = $"CreateDeformMesh|{src?.name}|{ex.Message}";
            if (s_warnedMeshBuildFailures.Add(warnKey))
            {
                Debug.LogWarning($"[LatticeModifier] 构建不可读 Mesh '{src.name}' 的变形副本时部分数据读取失败: {ex.Message}。模型可能缺失部分顶点属性（不影响基本可见性）。");
            }
        }
        finally
        {
            // 关键：无论是否异常，dataArr 必须释放，否则 native 资源泄漏导致后续调用失败、模型不可见
            // MeshDataArray 是 struct，default 状态调用 Dispose 是安全的 no-op
            dataArr.Dispose();
        }
        mesh.RecalculateBounds();
        return mesh;
    }

    /// v3.8 新增：仅用 DeformTarget 缓存的顶点 + 三角形索引构造 Mesh。
    /// 不依赖 originalMesh 资产（玩家端可能丢失）、不依赖 deformedMeshA（可能被 GC）。
    /// 这是"最后一根稻草"：哪怕前 4 道防线都漏了，只要 originalVertices + originalTriangles
    /// 在缓存里（场景序列化时持久化），就能建出可见 Mesh。
    ///
    /// 限制：只复制顶点 + 三角形索引，不复制 UV / 法线 / 切线（这些不影响模型可见性，
    /// 仅影响光照 / 贴图）。这是"模型可见优先"策略——先让玩家看到模型，材质细节次要。
    private Mesh CreateDeformMeshFromCache(DeformTarget dt)
    {
        if (dt == null) return null;
        if (dt.originalVertices == null || dt.originalVertices.Length == 0) return null;
        if (dt.originalTriangles == null || dt.originalTriangles.Length == 0) return null;

        try
        {
            Mesh mesh = new Mesh
            {
                // v3.14.1：名称必须包含 _LatticeDeform_ 后缀，否则 IsLatticeDeformMesh 判定为 false，
                // EnsureDeformMeshesValid 会把它当成"丢失的 Mesh"，每帧重建整个 Mesh（致命性能问题）。
                // v3.23.1 命名精简：去掉 dt.renderer?.name（SkinnedMeshRenderer.name 常 30+ 字符），
                // 避免 Hierarchy 名字超 64 字符被截断。保留 _LatticeDeform_ 后缀是 IsLatticeDeformMesh 判定的硬性要求。
                name = "LatCache" + LatticeDeformSuffix + "_" + System.Math.Abs(GetInstanceID()),
                hideFlags = HideFlags.DontSave,
                indexFormat = dt.originalVertices.Length > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };

            // v3.15：MarkDynamic 必须在首次写入顶点之前调用才能生效，
            // 否则每帧 DeformVertices 的 mesh.vertices= 会按静态网格处理、反复分配 GPU 顶点缓冲，
            // 表现为 Graphics 内存持续增长。
            mesh.MarkDynamic();

            mesh.SetVertices(dt.originalVertices);
            mesh.subMeshCount = dt.originalSubMeshCount;
            for (int s = 0; s < dt.originalSubMeshCount && s < dt.originalTriangles.Length; s++)
            {
                int[] tris = dt.originalTriangles[s].triangles;
                if (tris != null && tris.Length > 0)
                    mesh.SetTriangles(tris, s);
            }

            // v3.10：应用缓存的顶点通道（法线/切线/UV）。
            int vc = dt.originalVertices.Length;
            if (dt.originalNormals != null && dt.originalNormals.Length == vc)
                mesh.SetNormals(dt.originalNormals);
            else
                mesh.RecalculateNormals();

            if (dt.originalTangents != null && dt.originalTangents.Length == vc)
                mesh.SetTangents(dt.originalTangents);

            if (dt.originalUV != null && dt.originalUV.Length == vc)
                mesh.SetUVs(0, dt.originalUV);

            if (dt.originalColors != null && dt.originalColors.Length == vc)
                mesh.SetColors(dt.originalColors);

            // v3.12：恢复蒙皮数据，保证不可读 SkinnedMeshRenderer Mesh 在玩家端正确蒙皮，
            // 不再因丢失骨骼权重而按根骨骼朝向错位旋转。
            if (dt.isSkinned)
            {
                if (dt.originalBindposes != null && dt.originalBindposes.Length > 0)
                    mesh.bindposes = dt.originalBindposes;
                if (dt.originalBoneWeights != null && dt.originalBoneWeights.Length == vc)
                    mesh.boneWeights = dt.originalBoneWeights;
            }

            mesh.RecalculateBounds();
            // MarkDynamic 已在写入顶点前调用（见上），此处不再重复。
            return mesh;
        }
        catch (System.Exception ex)
        {
            string warnKey = $"CreateDeformMeshFromCache|{dt?.renderer?.name}|{ex.Message}";
            if (s_warnedMeshBuildFailures.Add(warnKey))
            {
                Debug.LogWarning($"[LatticeModifier] 从缓存重建 Mesh 失败: {ex.Message}");
            }
            return null;
        }
    }

    private bool IsLatticeDeformMesh(Mesh mesh)
    {
        if (mesh == null) return false;
        // 判定方式：Mesh.name 包含 _LatticeDeform_ 后缀。
        // 之前用 HideAndDontSave hideFlags 判断，但 HideAndDontSave 的 Mesh 在某些 Unity 版本
        // 会被 Build 系统当作"无资产身份"剥离（不跟随场景引用打包），导致玩家端 Renderer 引用
        // 解析为 null → 不可见。改为 DontSave + 名称后缀双重判断后，Build 系统更容易跟随
        // 引用图打包变形 Mesh。
        // 不要用 hideFlags 判断：原 FBX 资产常用 HideFlags.DontSave 标记，混在一起会误删。
        return mesh.name.Contains("_LatticeDeform_");
    }

    private Mesh GetReadableMesh(Renderer rend)
    {
        Mesh srcMesh = GetRendererMesh(rend);
        if (srcMesh == null) return null;
        if (srcMesh.isReadable) return srcMesh;
        // v3.19 内存优化：不可读 Mesh 不再 Instantiate 整 Mesh 拿顶点（会复制 blendshape/
        // boneWeights/bindposes，常驻几十 MB），改用 AcquireReadOnlyMeshData 直接抓顶点
        // 数据临时数组，仅在 .ToArray() 一次时分配顶点缓冲，省掉 Mesh 实例化和 blendshape
        // 复制。SkinnedMeshRenderer 仍走 BakeMesh（这是把骨骼蒙皮烘到顶点的唯一可靠路径）。
        try
        {
            using var dataArr = Mesh.AcquireReadOnlyMeshData(srcMesh);
            var data = dataArr[0];
            int vc = data.vertexCount;
            if (vc <= 0) return null;
            var vertNative = new Unity.Collections.NativeArray<Vector3>(vc, Unity.Collections.Allocator.Temp);
            try
            {
                data.GetVertices(vertNative);
                Vector3[] verts = vertNative.ToArray();
                // 用临时 Mesh 装这份顶点 — 调用方（CreateDeformTarget 等）只读 .vertices
                // 后会立刻 SafeDestroy 掉，所以只承担一次小分配。
                Mesh tmp = new Mesh { name = srcMesh.name + "_ReadableTmp", hideFlags = HideFlags.HideAndDontSave };
                tmp.SetVertices(verts);
                return tmp;
            }
            finally
            {
                vertNative.Dispose();
            }
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
                SafeDestroy(b); // v3.14.3：烘焙结果不可用时销毁
            }
            catch { }
        }
        return null;
    }

    public static Mesh GetRendererMeshStatic(Renderer rend) => GetRendererMesh(rend);

    /// v3.13.2 性能：用 DeformTarget 缓存的 MeshFilter 取当前 Mesh，避免每帧 GetComponent。
    /// SkinnedMeshRenderer 直接走属性访问（本就很快）。
    private Mesh GetCurrentMeshFast(DeformTarget dt)
    {
        if (dt.renderer is SkinnedMeshRenderer smr) return smr.sharedMesh;
        if (!dt.meshFilterResolved)
        {
            dt.cachedMeshFilter = dt.renderer.GetComponent<MeshFilter>();
            dt.meshFilterResolved = true;
        }
        return dt.cachedMeshFilter != null ? dt.cachedMeshFilter.sharedMesh : null;
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

    // v3.16：手动触发孤儿变形网格回收（可在内存警告回调 / 切场景时调用）。
    public static void ReclaimOrphanMeshes() => SweepOrphanLatticeMeshes();

    // v3.16：内存诊断。打印各类资源数量，帮助定位"到底什么在持续增长"。
    // 在游戏启动时设 LatticeModifier.s_enableMemoryDiagnostics = true 开启，
    // 观察日志里哪个数字随时间不断变大：
    //   - LatticeMesh 数 增长 → 变形网格泄漏（晶格相关）
    //   - Mesh 总数 增长但 LatticeMesh 不增 → 别处在建 Mesh（非晶格）
    //   - Material / Texture / RenderTexture 增长 → 对应资源泄漏（非晶格变形网格）
    //   - 都不增长但 MEM 仍涨 → 托管堆 / 原生分配 / GPU 驱动，需用 Memory Profiler 比对快照
    // v3.19：诊断专用 StringBuilder 复用池，避免每 5s 字符串拼接 + ToString 分配。
    // StringBuilder 内部 char[] 持续增长，ToString 也会分配新 string — 用池化复用可归零这两个分配源。
    [NonSerialized] private static StringBuilder s_diagSb;
    private static StringBuilder DiagSb_Get()
    {
        var sb = s_diagSb;
        s_diagSb = null;
        if (sb == null) sb = new StringBuilder(512);
        else sb.Clear();
        return sb;
    }
    private static void DiagSb_Release(StringBuilder sb)
    {
        sb.Clear();
        s_diagSb = sb;
    }

    private static void LogMemoryDiagnostics()
    {
        s_activeLattices.RemoveWhere(l => l == null);
        int activeLattices = s_activeLattices.Count;
        int totalTargets = 0;
        foreach (var lat in s_activeLattices)
        {
            if (lat == null || lat.deformTargets == null) continue;
            totalTargets += lat.deformTargets.Count;
        }

        // v3.19：单次扫描 Mesh 池 — 必须保留（要按 _LatticeDeform_ 后缀筛晶格变形网格，
        // 这是定位"晶格相关 Mesh 泄漏"与"别处建 Mesh"的关键区分手段）。
        // 默认 s_enableMemoryDiagnostics=false 不会跑，0 运行时影响。
        int latticeMeshCount = 0;
        long latticeVerts = 0;
        int totalMeshCount = 0;
        var meshes = Resources.FindObjectsOfTypeAll<Mesh>();
        for (int i = 0; i < meshes.Length; i++)
        {
            var m = meshes[i];
            if (m == null) continue;
            totalMeshCount++;
            if (m.name.Contains("_LatticeDeform_"))
            {
                latticeMeshCount++;
                latticeVerts += m.vertexCount;
            }
        }

        // v3.21：精确统计"实际挂在 Renderer 上的 deform Mesh" — 走 s_activeLattices 直接
        // 遍历 deformTargets.deformedMeshA，不依赖名称匹配（避免误判）。
        // 这能精确反映"此刻 LatticeModifier 实际占用多少顶点"。
        int liveDeformMeshCount = 0;
        long liveDeformVerts = 0;
        foreach (var lat in s_activeLattices)
        {
            if (lat == null || lat.deformTargets == null) continue;
            foreach (var dt in lat.deformTargets)
            {
                if (dt == null) continue;
                if (dt.deformedMeshA != null)
                {
                    liveDeformMeshCount++;
                    liveDeformVerts += dt.deformedMeshA.vertexCount;
                }
            }
        }

        // v3.19：Material/Texture/RenderTexture/托管堆 改用 Profiler API 替代
        // Resources.FindObjectsOfTypeAll — 后者每次返回巨大的托管数组（数 MB），开诊断时
        // 自身就成内存增长源。Profiler API 是无分配的内部采样。
        long totalAllocated = Profiler.GetTotalAllocatedMemoryLong();
        long totalReserved = Profiler.GetTotalReservedMemoryLong();

        // 估算变形网格 GPU 内存（顶点数 × 每顶点约 60 字节：位置/法线/切线/UV/颜色 + 蒙皮）
        long latticeMeshMB = latticeVerts * 60 / (1024 * 1024);
        long liveDeformMeshMB = liveDeformVerts * 60 / (1024 * 1024);

        // v3.19：StringBuilder 池复用，零字符串拼接分配
        var sb = DiagSb_Get();
        try
        {
            sb.Append("晶格=").Append(activeLattices)
              .Append(" 目标=").Append(totalTargets).Append('\n')
              .Append("活动变形Mesh=").Append(liveDeformMeshCount)
              .Append(" (~").Append(liveDeformMeshMB).Append("MB)").Append('\n')
              .Append("池中_后缀=").Append(latticeMeshCount)
              .Append(" (~").Append(latticeMeshMB).Append("MB)").Append('\n')
              .Append("Mesh池总数=").Append(totalMeshCount).Append('\n')
              .Append("Profiler 分配=").Append(totalAllocated / (1024 * 1024)).Append("MB\n")
              .Append("Profiler 预留=").Append(totalReserved / (1024 * 1024)).Append("MB");

            s_diagText = sb.ToString();

            // 重置 sb 给 Debug.Log 用，复用同一缓冲
            sb.Clear();
            sb.Append("[LatticeMemDiag] 活动晶格=").Append(activeLattices)
              .Append(" 变形目标=").Append(totalTargets).Append(" | ")
              .Append("活动变形Mesh=").Append(liveDeformMeshCount)
              .Append("(~").Append(liveDeformMeshMB).Append("MB)")
              .Append(" 池中_后缀=").Append(latticeMeshCount)
              .Append("(~").Append(latticeMeshMB).Append("MB) Mesh池=").Append(totalMeshCount).Append(" | ")
              .Append("Profiler 分配=").Append(totalAllocated / (1024 * 1024)).Append("MB ")
              .Append("预留=").Append(totalReserved / (1024 * 1024)).Append("MB");

            Debug.Log(sb.ToString());
        }
        finally
        {
            DiagSb_Release(sb);
        }
    }

    // v3.17：屏幕诊断覆盖层。开启 s_enableMemoryDiagnostics 后在屏幕左上角实时显示各类资源数量，
    // 方便在设备上直接观察内存增长来源（无需抓 logcat）。仅由一个实例每帧绘制一次。
    // v3.17：GUIStyle 改为静态缓存，避免每帧 new GUIStyle 产生 GC 分配。
    private static GUIStyle s_diagStyle;
    private void OnGUI()
    {
        if (!s_enableMemoryDiagnostics || string.IsNullOrEmpty(s_diagText)) return;
        if (s_diagDrawFrame == Time.frameCount) return; // 多实例时每帧只画一次
        s_diagDrawFrame = Time.frameCount;

        if (s_diagStyle == null)
        {
            s_diagStyle = new GUIStyle
            {
                fontSize = 28,
                normal = { textColor = Color.yellow }
            };
        }
        GUI.Label(new Rect(20, 20, 800, 400), s_diagText, s_diagStyle);
    }

    // v3.16：孤儿变形网格回收。扫描内存中所有带 _LatticeDeform_ 后缀的 Mesh，
    // 销毁那些「不被任何活动晶格当前引用」的（即引用已丢失但因 DontSave 不会被引擎自动卸载的孤儿）。
    // 由 LateUpdate 周期性触发（s_orphanSweepInterval），把泄漏的 GPU/内存释放回引擎复用。
    // 安全性：只销毁后缀匹配且不在任何活动晶格 deformedMeshA/B 集合中的网格；
    // 正在使用的变形网格一定在集合内，不会被误删。
    private static void SweepOrphanLatticeMeshes()
    {
        // 1) 收集所有活动晶格当前仍在用的变形网格
        var valid = HashSetPool_Get();
        try
        {
            s_activeLattices.RemoveWhere(l => l == null);
            foreach (var lat in s_activeLattices)
            {
                if (lat == null || lat.deformTargets == null) continue;
                foreach (var dt in lat.deformTargets)
                {
                    if (dt == null) continue;
                    if (dt.deformedMeshA != null) valid.Add(dt.deformedMeshA);
                    if (dt.deformedMeshB != null) valid.Add(dt.deformedMeshB);
                }
            }

            // 2) 扫描所有变形网格，销毁孤儿
            var all = Resources.FindObjectsOfTypeAll<Mesh>();
            int destroyed = 0;
            for (int i = 0; i < all.Length; i++)
            {
                var m = all[i];
                if (m == null) continue;
                if (!m.name.Contains("_LatticeDeform_")) continue; // 只处理我们的变形网格
                if (valid.Contains(m)) continue;                   // 仍在用，跳过
                // 孤儿：引用已丢失但因 DontSave 不会被引擎卸载 → 显式销毁
                if (Application.isPlaying) Destroy(m);
                else DestroyImmediate(m);
                destroyed++;
            }

            if (destroyed > 0)
                Debug.Log($"[LatticeModifier] 孤儿变形网格回收：释放 {destroyed} 个未引用的变形 Mesh。");
        }
        finally
        {
            HashSetPool_Release(valid);
        }
    }

    // 轻量 HashSet 复用，避免每次扫描分配
    [NonSerialized] private static HashSet<Mesh> s_validMeshSetPool;
    private static HashSet<Mesh> HashSetPool_Get()
    {
        var s = s_validMeshSetPool;
        s_validMeshSetPool = null;
        if (s == null) s = new HashSet<Mesh>();
        else s.Clear();
        return s;
    }
    private static void HashSetPool_Release(HashSet<Mesh> s)
    {
        s.Clear();
        s_validMeshSetPool = s;
    }

    #endregion
}
