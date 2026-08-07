// LatticeModifierEditor —— LatticeModifier 的 Inspector + SceneView 编辑器。
// 历史版本（v1.0 ~ v3.2）见 git log；当前活跃版本：
//
// v3.3 撤销【烘焙到原 Mesh】思路（它会让晶格烘焙后失效无法继续实时控制）。
//      改为实时变形 + 自动保存时还原：保存场景/打包前自动把所有 Renderer 的 sharedMesh 还原回
//      originalMesh 资产，让 Build 场景里 Renderer 引用指向带 Asset GUID 的资产。
//      进入 Play 模式后 OnEnable 重建 deform Mesh 继续实时变形；退出 Play 模式 OnDestroy 自动还原。
//      用户不再需要任何手动烘焙操作，晶格物体始终保留在场景中，控制点持续实时影响模型。
//      保留【烘焙变形并移除晶格】按钮用于"不再需要晶格、生成新资产"的场景。
//
// v3.4 重构：抽取 LatticeSceneWalker.ForEachInitialized 工具方法，
//           消除 LatticeModifierBuildPreprocessor / LatticeModifierSaveHook 重复的"遍历所有加载场景的 LatticeModifier"循环。

// LatticeModifierEditor v3.5 修复控制点缩放无法与缩放手柄方向一致，newScale 的 x/y/z 是「手柄局部轴」（由 t.rotation 定向，即屏幕上彩色箭头方向）上的缩放分量
// LatticeModifierEditor v3.24 内部点压缩：Inspector 加 surfaceOnly 开关 + 「应用压缩」按钮，
//                              + 「外壳点 / 全部」统计信息 + 「仅表面 vs 全部」效率对比。
// LatticeModifierEditor v3.25 优化晶格点正背面着色判断逻辑，晶格线也加入背面压暗判断。
// LatticeModifierEditor v3.26 解决多 Inspector 窗口下「扩展选择」「取消选择」按钮锁定/失效。
// LatticeModifierEditor v3.27 Esc 定位优化：FindLatticesByName 增加渲染器目标验证，排除同名但无关联的晶格，解决同名模型选中错误晶格体的问题。
// LatticeModifierEditor v3.28 切换晶格对象时「扩展选择」错乱修复：SyncLatticeFromTarget 增加 s_activeLattice 三方比对，解决多 Inspector 锁定场景下 static selectedPoints 属于另一个晶格但当前编辑器仍使用其展开操作的问题。
// LatticeModifierEditor v3.29 选中状态丢失修复：selectedPoints 从 static 改为实例字段，每个 Editor 实例独立维护自己的选中点集合。不再通过 static 共享，彻底避免多 Inspector 窗口间交叉清除选中状态。SyncLatticeFromTarget 简化为只处理 Editor 复用（target 变 lattice 未变）的单一场合。
// LatticeModifierEditor v3.30 多 Inspector 同步修复：selectedPoints 改为属性，底层按 InstanceID 存储在静态 Dictionary 中。多个 Inspector 窗口显示同一晶格时共享同一份选中数据，切换晶格时自动获取对应晶格的独立选中集（切换回来时自动恢复）。不再交叉清除。
// LatticeModifierEditor v3.31 扩展选择支持 Undo/Redo：ExpandSelection 通过 Undo.RecordObject 创建撤销点 + undoRedoPerformed 一次性回调实现双向切换，Ctrl+Z 回退 Ctrl+Y 重做。
// LatticeModifierEditor v3.32 PlayMode 性能优化：进入运行时（Application.isPlaying）后，OnSceneGUI 跳过所有点/线着色计算（背面判断、深度排序、点拖拽、框选等），改为统一浅灰色立方体外壳，大幅降低 Play Mode 下的 Editor 性能开销。
// LatticeModifierEditor v3.33 「对象位置」按钮新增：在重置按钮行最左边加一个新按钮，调 lattice.ResetPositionToTarget() 复位到目标对象当前位置。「位移」按钮恢复原义 —— 复位到初始化时的基准位置。

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

[CustomEditor(typeof(LatticeModifier))]
public class LatticeModifierEditor : Editor
{
    private LatticeModifier lattice;

    // v3.30：按 InstanceID 存储每晶格的独立选中集，多个 Inspector 显示同一晶格时共享选中。
    // 切换晶格时自动获取对应晶格的选中集（若首次选中则创建空集），
    // 每个晶格的选中状态在生命周期内保持独立，不会互相干扰。
    private static Dictionary<int, HashSet<int>> s_latticeSelections = new Dictionary<int, HashSet<int>>();
    private static readonly HashSet<int> s_emptySelection = new HashSet<int>();

    private HashSet<int> selectedPoints
    {
        get
        {
            if (lattice == null) return s_emptySelection;
            int id = lattice.GetInstanceID();
            if (!s_latticeSelections.TryGetValue(id, out var set))
            {
                set = new HashSet<int>();
                s_latticeSelections[id] = set;
            }
            return set;
        }
        set
        {
            if (lattice == null) return;
            int id = lattice.GetInstanceID();
            s_latticeSelections[id] = value;
            if (s_activeLattice == lattice)
                s_activeSelectedPoints = value;
        }
    }

    private static LatticeModifier s_activeLattice;
    private static HashSet<int> s_activeSelectedPoints;
    private static bool s_registered;

    private static bool s_isDragging;
    private static Vector2 s_dragStart;
    private static Vector2 s_dragEnd;

    // 缩放/旋转手柄：记录拖拽开始时的控制点位置和中心，避免变换叠加
    private static Dictionary<int, Vector3> s_handleStartPositions;
    private static Vector3 s_handleStartCenter;
    private static bool s_handleDragging;

    private static bool s_suppressSelectionChanged;

    private static void SyncSelectionToHierarchy()
    {
        if (s_activeLattice == null) return;
        // 选中控制点时，保持焦点在晶格对象上（而非 CP 子物体），确保 Inspector 显示晶格面板
        Selection.activeGameObject = s_activeLattice.gameObject;
        s_suppressSelectionChanged = false;
    }

    // 静态构造函数：类被加载时即注册 SceneView 钩子，确保用户从未点过晶格对象时 Esc 切换也能工作
    static LatticeModifierEditor()
    {
        EnsureHookRegistered();
    }

    /// 显式触发 SceneView 钩子注册。供 LatticeModifierInitOnLoad 在 Editor 启动时
    /// 主动调用以强制加载本类（CustomEditor 属性本身不会触发静态构造函数）。
    /// 内部用 s_registered 防重复。
    internal static void EnsureHookRegistered()
    {
        if (s_registered) return;
        SceneView.duringSceneGui += OnGlobalSceneGUIStatic;
        s_registered = true;
    }

    /// v3.30：仅处理 Unity 复用 Editor 实例但未调用 OnEnable 的场景（target 变了
    /// 但 lattice 仍是旧值）。不再清除选中——selectedPoints 属性按 InstanceID
    /// 自动返回新 lattice 的独立选中集，不会混淆。
    private void SyncLatticeFromTarget()
    {
        var currentTarget = target as LatticeModifier;
        if (currentTarget == null) return;

        // 一致 → 无需同步
        if (currentTarget == lattice)
            return;

        // target 已切换但 lattice 未更新 → Editor 复用，OnEnable 未触发
        lattice = currentTarget;
        s_activeLattice = currentTarget;
        s_activeSelectedPoints = selectedPoints; // 自动获取新 lattice 的选中集
    }

    private void OnEnable()
    {
        lattice = (LatticeModifier)target;

        // v3.30：selectedPoints 按 InstanceID 存储，每个晶格独立维护选中状态。
        // 不再在切换时清除——切换回来时自动恢复之前的选中状态。
        s_activeLattice = lattice;
        s_activeSelectedPoints = selectedPoints;
        EditorApplication.update += EditorUpdate;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Selection.selectionChanged += OnSelectionChanged;
        Undo.undoRedoPerformed += OnUndoRedo;
        if (!s_registered)
        {
            SceneView.duringSceneGui += OnGlobalSceneGUIStatic;
            s_registered = true;
        }
    }

    private void OnDisable()
    {
        Tools.hidden = false;
        Undo.undoRedoPerformed -= OnUndoRedo;
        EditorApplication.update -= EditorUpdate;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        Selection.selectionChanged -= OnSelectionChanged;
    }

    /// Hierarchy 中选中 CP 节点时，反查索引同步到 selectedPoints
    private void OnSelectionChanged()
    {
        // 防止 SyncSelectionToHierarchy 触发的递归回调
        if (s_suppressSelectionChanged) return;

        if (lattice == null || !lattice.HasControlPointTransforms) return;

        var newSel = new HashSet<int>();
        foreach (var go in Selection.gameObjects)
        {
            // 遍历所有控制点找到匹配的 Transform
            int total = lattice.PointCountX * lattice.PointCountY * lattice.PointCountZ;
            for (int i = 0; i < total; i++)
            {
                Transform cp = lattice.GetControlPointTransform(i);
                if (cp != null && cp.gameObject == go)
                {
                    newSel.Add(i);
                    break;
                }
            }
        }

        if (newSel.Count == 0) return; // 不是 CP 节点，不干扰其他选择

        selectedPoints.Clear();
        foreach (int i in newSel) selectedPoints.Add(i);
        s_activeSelectedPoints = selectedPoints;
        SceneView.RepaintAll();
    }

    /// Undo/Redo 后自动恢复变形 Mesh（Undo 可能恢复 Renderer 的 sharedMesh 为 originalMesh）
    private void OnUndoRedo()
    {
        if (lattice == null || !lattice.IsInitialized) return;

        // 延迟一帧执行，确保 Undo 序列化完成
        EditorApplication.delayCall += () =>
        {
            if (lattice == null || !lattice.IsInitialized) return;

            // Undo 后同步控制点 Transform 位置（如果存在），防止缓存不一致
            if (lattice.HasControlPointTransforms)
            {
                lattice.SyncToTransforms();
                // 重置位置快照缓存，避免 EditorUpdate 误判为外部修改
                _lastCpPositions = null;
            }

            lattice.MarkDirty();
            lattice.ApplyDeformation();
            SceneView.RepaintAll();
        };
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (lattice == null || !lattice.IsInitialized) return;

        // 进入 Play 模式后、退出 Play 模式后自动重新初始化
        if (state == PlayModeStateChange.EnteredPlayMode ||
            state == PlayModeStateChange.EnteredEditMode)
        {
            // 延迟一帧执行，确保 Unity 序列化/反序列化完成
            EditorApplication.delayCall += () =>
            {
                if (lattice != null && lattice.IsInitialized)
                {
                    lattice.InitializeLattice();
                    EditorUtility.SetDirty(lattice);
                    SceneView.RepaintAll();
                }
            };
        }
    }

    // 上一帧控制点位置快照，用于检测 CP Transform 是否被外部移动
    private Vector3[] _lastCpPositions;

    private void EditorUpdate()
    {
        if (lattice == null || !lattice.IsInitialized || !lattice.liveUpdate) return;

        if (lattice.HasControlPointTransforms)
        {
            // 检测 CP Transform 是否发生变化（用户在 Hierarchy 中移动了 CP 节点）
            int total = lattice.PointCountX * lattice.PointCountY * lattice.PointCountZ;
            bool changed = false;

            if (_lastCpPositions == null || _lastCpPositions.Length != total)
            {
                _lastCpPositions = new Vector3[total];
                for (int i = 0; i < total; i++)
                {
                    Transform cp = lattice.GetControlPointTransform(i);
                    if (cp != null) _lastCpPositions[i] = cp.localPosition;
                }
            }
            else
            {
                for (int i = 0; i < total; i++)
                {
                    Transform cp = lattice.GetControlPointTransform(i);
                    if (cp != null && cp.localPosition != _lastCpPositions[i])
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (changed)
            {
                Undo.RecordObject(lattice, "移动晶格控制点");
                lattice.SyncFromTransforms();
                for (int i = 0; i < total; i++)
                {
                    Transform cp = lattice.GetControlPointTransform(i);
                    if (cp != null) _lastCpPositions[i] = cp.localPosition;
                }
                EditorUtility.SetDirty(lattice);
            }
        }

        lattice.ApplyDeformation();
    }

    public override void OnInspectorGUI()
    {
        // v3.28：先同步，确保 lattice/selectedPoints 与当前实际 target 一致
        SyncLatticeFromTarget();

        // v3.9：取消目标模式选项，统一使用多目标逻辑
        serializedObject.Update();

        // ── 显示目标字段 ──
        EditorGUILayout.PropertyField(serializedObject.FindProperty("targetRoot"), new GUIContent("多目标根节点"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("manualRenderers"), new GUIContent("手动指定 Renderer"), true);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("divisionsX"), new GUIContent("X 段数"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("divisionsY"), new GUIContent("Y 段数"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("divisionsZ"), new GUIContent("Z 段数"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("feather"), new GUIContent("边缘羽化", "控制晶格边界的变形衰减带宽度。0 = 无羽化（硬切），0.5 = 最大羽化（整个范围平滑过渡）"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("liveUpdate"), new GUIContent("实时更新"));

        // v3.24.1：surfaceOnly 改为 NonSerialized 后不能用 PropertyField，手动 Toggle
        bool prevSurfaceOnly = lattice.surfaceOnly;
        bool currSurfaceOnly = EditorGUILayout.Toggle(
            new GUIContent("忽略内部控制点（v3.24）",
                "开启后控制点只保留 6 个外壳面，去掉立方体内部的点。\n" +
                "内部点对表面顶点影响极小（Bernstein 基函数趋近 0），\n" +
                "可大幅减少 FFD 累加计算量。8x8x8 晶格控制点从 512 减到 296（-42%）。"),
            prevSurfaceOnly);
        if (currSurfaceOnly != prevSurfaceOnly)
            lattice.surfaceOnly = currSurfaceOnly;
        serializedObject.ApplyModifiedProperties();

        if (lattice.IsInitialized)
        {
            int nx = lattice.PointCountX, ny = lattice.PointCountY, nz = lattice.PointCountZ;
            int total = nx * ny * nz;
            int internalCount = (nx - 2) * (ny - 2) * (nz - 2);
            int surfaceCount = total - internalCount;
            float savings = total > 0 ? (1f - (float)surfaceCount / total) * 100f : 0f;

            string mode = currSurfaceOnly ? "已开启（FFD 累加跳过内部点）" : "未开启（FFD 累加全部点）";
            string info = $"v3.24 性能模式：{mode}\n" +
                          $"全部控制点：{total} | 外壳点：{surfaceCount} | 内部点：{internalCount}\n" +
                          $"开启时 FFD 累加减少：{savings:F1}%\n" +
                          $"（数据全部保留，所有 Gizmo/CP Transform 仍按 3D 索引工作）";
            EditorGUILayout.HelpBox(info, MessageType.None);

            if (prevSurfaceOnly != currSurfaceOnly)
            {
                GUI.backgroundColor = new Color(1f, 0.6f, 0.3f);
                if (GUILayout.Button(new GUIContent($"应用压缩模式（{(currSurfaceOnly ? "开启" : "关闭")}）",
                    "切换 surfaceOnly 标志位。\n" +
                    "开启时：DeformVertices 内层累加跳过内部点（性能提升）。\n" +
                    "关闭时：所有点都参与累加（默认行为）。\n" +
                    "已编辑的控制点位置全部保留。"), GUILayout.Height(24)))
                {
                    Undo.RecordObject(lattice, "切换外壳压缩模式");
                    lattice.ApplySurfaceOnlyMode();
                    EditorUtility.SetDirty(lattice);
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = Color.white;
            }
        }

        EditorGUILayout.Space(10);

        if (!lattice.IsInitialized)
        {
            // 检查是否有任何目标配置
            bool hasTarget = lattice.targetRoot != null ||
                             lattice.manualRenderers.Exists(r => r != null);

            if (!hasTarget)
                EditorGUILayout.HelpBox("请指定目标：拖入「目标对象」或「多目标根节点」或添加到「手动指定 Renderer」列表", MessageType.Warning);
            else
            {
                Transform checkT = lattice.targetRoot;
                if (checkT == null && lattice.manualRenderers.Count > 0 && lattice.manualRenderers[0] != null)
                    checkT = lattice.manualRenderers[0].transform;
                if (checkT != null)
                {
                    bool isSameOrChild = lattice.transform == checkT ||
                                         lattice.transform.IsChildOf(checkT) ||
                                         checkT.IsChildOf(lattice.transform);
                    if (isSameOrChild)
                    {
                        EditorGUILayout.HelpBox(
                            "晶格组件不能挂在目标对象上（否则移动目标时晶格会跟着动）。\n" +
                            "点击下方按钮自动创建独立的晶格物体。", MessageType.Warning);

                        GUI.backgroundColor = new Color(1f, 0.9f, 0.3f);
                        if (GUILayout.Button("创建独立晶格物体", GUILayout.Height(30)))
                        {
                            CreateStandaloneLattice();
                            return;
                        }
                        GUI.backgroundColor = Color.white;
                    }
                }
            }

            GUI.backgroundColor = new Color(0.3f, 0.9f, 0.5f);
            if (GUILayout.Button("初始化晶格", GUILayout.Height(30)))
            {
                Undo.RecordObject(lattice, "初始化晶格");
                lattice.InitializeLattice();
                selectedPoints.Clear();
                EditorUtility.SetDirty(lattice);
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = Color.white;
        }
        else
        {
            DrawInitializedUI();
        }
    }

    private void DrawInitializedUI()
    {
        var renderers = lattice.GetActiveRenderers();
        string info = $"晶格：{lattice.PointCountX}×{lattice.PointCountY}×{lattice.PointCountZ} = {lattice.TotalPoints} 个控制点\n" +
                      $"共 {renderers.Count} 个 Renderer\n" +
                      "点击选中 | Ctrl+点击加选 | Shift+拖拽框选 | 拖拽手柄变形";
        EditorGUILayout.HelpBox(info, MessageType.Info);

        // ── 丢失绑定修复按钮 ──
        int missingCount = 0;
        var activeSet = new HashSet<Renderer>(renderers);
        if (lattice.manualRenderers.Count > 0)
        {
            foreach (var r in lattice.manualRenderers)
                if (r != null && !activeSet.Contains(r))
                    missingCount++;
        }
        else if (lattice.targetRoot != null)
        {
            var allChildRenderers = lattice.targetRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var r in allChildRenderers)
                if (!activeSet.Contains(r))
                    missingCount++;
        }

        if (missingCount > 0)
        {
            EditorGUILayout.Space(3);
            GUI.backgroundColor = new Color(1f, 0.6f, 0.2f);
            EditorGUILayout.HelpBox(
                $"检测到 {missingCount} 个目标 Renderer 丢失晶格绑定", MessageType.Warning);
            if (GUILayout.Button(new GUIContent($"修复丢失绑定（{missingCount} 个）",
                "重新链接指定列表中未绑定到晶格的 Renderer。\n\n" +
                "适用场景：\n" +
                "• Prefab 实例化后部分绑定丢失\n" +
                "• 撤销/重做导致部分目标断开连接\n" +
                "• 手动修改了 manualRenderers 列表后同步绑定"),
                GUILayout.Height(26)))
            {
                Undo.RecordObject(lattice, "修复丢失绑定");
                int repaired = lattice.RepairMissingBindings();
                EditorUtility.SetDirty(lattice);
                SceneView.RepaintAll();

                if (repaired > 0)
                    Debug.Log($"[LatticeModifier] 已修复 {repaired} 个丢失绑定");
                else
                    EditorUtility.DisplayDialog("提示", "没有需要修复的绑定", "确定");
            }
            GUI.backgroundColor = Color.white;
        }

        EditorGUILayout.Space(3);
        EditorGUILayout.BeginHorizontal();
        if (selectedPoints.Count > 0)
        {
            EditorGUILayout.LabelField($"已选中 {selectedPoints.Count} 个点", EditorStyles.miniLabel);
            if (GUILayout.Button("取消选择", EditorStyles.miniButton, GUILayout.Width(60)))
            {
                selectedPoints.Clear();
                SceneView.RepaintAll();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(1f, 0.8f, 0.3f);
        if (GUILayout.Button("重置控制点", GUILayout.Height(28)))
        {
            Undo.RecordObject(lattice, "重置晶格控制点");
            lattice.ResetControlPoints();
            if (lattice.HasControlPointTransforms)
                lattice.SyncToTransforms();
            EditorUtility.SetDirty(lattice);
            SceneView.RepaintAll();
        }

        GUI.backgroundColor = new Color(0.3f, 0.8f, 1f);
        if (GUILayout.Button("重新初始化（选中晶格体）", GUILayout.Height(28)))
        {
            Undo.RecordObject(lattice, "重新初始化晶格");
            lattice.InitializeLattice();
            selectedPoints.Clear();
            EditorUtility.SetDirty(lattice);
            SceneView.RepaintAll();
        }

        GUI.backgroundColor = new Color(0.8f, 1f, 0.5f);
        if (GUILayout.Button(new GUIContent("刷新源Mesh",
            "重新读取目标对象的源 Mesh 顶点数据并重建变形管线。\n\n" +
            "适用场景：\n" +
            "• 外部工具修改了模型顶点后，同步最新顶点到晶格\n" +
            "• 手动替换了 Renderer 上的 Mesh 后，让晶格识别新源\n" +
            "• 变形显示异常时，强制重建变形副本\n\n" +
            "注意：当前控制点变形会保留，仅刷新源顶点数据。"),
            GUILayout.Height(28), GUILayout.Width(80)))
        {
            Undo.RecordObject(lattice, "刷新源 Mesh");
            lattice.RefreshSourceMesh();
            EditorUtility.SetDirty(lattice);
            SceneView.RepaintAll();
        }

        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        GUI.enabled = selectedPoints.Count > 0;
        if (GUILayout.Button("扩展选择", GUILayout.Height(28), GUILayout.Width(70)))
        {
            ExpandSelection();
            SceneView.RepaintAll();
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(3);
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.9f, 0.9f, 0.9f);
        if (GUILayout.Button("删除晶格", GUILayout.Height(28)))
        {
            if (EditorUtility.DisplayDialog("删除确认",
                "将还原所有 Mesh 到原始状态，并删除晶格物体。确定？", "确定", "取消"))
            {
                GameObject latticeGO = lattice.gameObject;
                lattice.RestoreOriginal();
                selectedPoints.Clear();
                s_latticeSelections.Remove(lattice.GetInstanceID()); // v3.30 清理字典
                s_activeLattice = null;
                s_activeSelectedPoints = null;
                Undo.DestroyObjectImmediate(latticeGO);
                GUIUtility.ExitGUI();
                return;
            }
        }

        GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
        if (GUILayout.Button("烘焙变形并移除晶格", GUILayout.Height(28)))
        {
            if (EditorUtility.DisplayDialog("烘焙确认",
                "将当前变形烘焙到 Mesh，晶格数据将被清除。确定？", "确定", "取消"))
            {
                BakeDeformationToAsset();
                GUIUtility.ExitGUI();
                return;
            }
        }

        GUI.backgroundColor = new Color(0.6f, 0.9f, 1f);
        if (GUILayout.Button("创建快照", GUILayout.Height(28)))
        {
            CreateDeformSnapshot();
        }
        EditorGUILayout.EndHorizontal();
        GUI.backgroundColor = Color.white;

        // ── 动画控制点 ──
        EditorGUILayout.Space(5);
        if (!lattice.HasControlPointTransforms)
        {
            GUI.backgroundColor = new Color(0.8f, 0.6f, 1f);
            if (GUILayout.Button("创建动画控制点（支持 Timeline K帧）", GUILayout.Height(28)))
            {
                Undo.RecordObject(lattice, "创建动画控制点");
                lattice.CreateControlPointTransforms();
                EditorUtility.SetDirty(lattice);
                SceneView.RepaintAll();
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                "动画控制点已创建，可在 Animation/Timeline 中对子物体 CP_x_y_z 的 Position 做关键帧动画。",
                MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.5f, 0.9f, 1f);
            if (GUILayout.Button(new GUIContent("链接选中对象到晶格",
                "将 Hierarchy 中选中的模型对象追加为当前晶格的变形目标。可以先将晶格对象参数窗口独立出来（Alt + P）\n\n" +
                "操作方法：\n" +
                "1. 在 Hierarchy 中选中要变形的模型（支持 Ctrl+多选）\n" +
                "2. 再 Ctrl+点击选中晶格体（保持多选）\n" +
                "3. 点击此按钮完成链接\n\n" +
                "链接后模型会立即受晶格控制点影响。"),
                GUILayout.Height(24)))
            {
                LinkSelectedObjectsToLattice();
            }
            GUI.backgroundColor = new Color(1f, 0.6f, 0.4f);
            if (GUILayout.Button("清除动画控制点", GUILayout.Height(24)))
            {
                Undo.RecordObject(lattice, "清除动画控制点");
                lattice.DestroyControlPointTransforms();
                EditorUtility.SetDirty(lattice);
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ── 轴心设置 ──
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("轴心设置：", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.9f, 0.85f, 1f);
        if (GUILayout.Button(new GUIContent("轴心居中",
            "将晶格物体的轴心（Transform.position）移到所有控制点的中心位置。\n" +
            "控制点的世界位置不变，仅改变局部坐标系原点。"),
            GUILayout.Height(26)))
        {
            CenterPivot();
        }
        if (GUILayout.Button(new GUIContent("重置轴心旋转",
            "将晶格物体的旋转归零（世界对齐），保持位置不变。\n" +
            "控制点的世界位置不变，仅重新计算局部坐标。"),
            GUILayout.Height(26)))
        {
            ResetPivotRotation();
        }

        GUI.backgroundColor = new Color(0.85f, 1f, 0.9f);
        if (GUILayout.Button(new GUIContent("继承对象轴心",
            "将晶格物体的轴心移到第一个变形目标对象的位置和旋转。\n" +
            "控制点的世界位置不变，晶格坐标系与目标对象对齐。"),
            GUILayout.Height(26)))
        {
            InheritTargetPivot();
        }

        if (GUILayout.Button(new GUIContent("继承对象轴心旋转",
            "将晶格物体的旋转设为第一个变形目标的旋转，位置不变。\n" +
            "控制点的世界位置不变，仅改变局部坐标系朝向。"),
            GUILayout.Height(26)))
        {
            InheritTargetRotation();
        }

        EditorGUILayout.EndHorizontal();

        // v3.26：分别重置晶格体位移/旋转/缩放到初始化时的值。
        EditorGUILayout.Space(5);
        bool hasSaved = lattice.HasInitialTransformSaved;

        if (hasSaved)
        {
            // 已记录：显示四按钮（对象位置/位移/旋转/缩放）+ 右侧「记录当前位置」齿轮按钮
            EditorGUILayout.LabelField("重置晶格体变换：", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(0.8f, 1f, 0.8f);
            if (GUILayout.Button(new GUIContent("对象位置",
                "将晶格体 Position 复位到目标对象的当前位置（targetRoot / targetRenderer / deformTargets[0].renderer）。"), GUILayout.Width(80),
                GUILayout.Height(24)))
            {
                Undo.RecordObject(lattice.transform, "晶格体贴向目标对象位置");
                Undo.RecordObject(lattice, "晶格体贴向目标对象位置");
                if (!lattice.ResetPositionToTarget())
                {
                    EditorUtility.DisplayDialog("提示",
                        "复位失败：未指定目标对象。\n请先在 Inspector 中设置 targetRoot 或 targetRenderer 后再使用「对象位置」按钮。",
                        "确定");
                }
                EditorUtility.SetDirty(lattice);
                EditorUtility.SetDirty(lattice.transform);
                SceneView.RepaintAll();
            }

            if (GUILayout.Button(new GUIContent("位移",
                "将晶格体 Position 复位到初始位置（SaveCurrentAsInitialTransform 保存的基准）。"), GUILayout.Height(24)))
            {
                Undo.RecordObject(lattice.transform, "重置晶格体位移");
                Undo.RecordObject(lattice, "重置晶格体位移");
                lattice.ResetPositionToInitial();
                EditorUtility.SetDirty(lattice);
                EditorUtility.SetDirty(lattice.transform);
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = new Color(0.8f, 0.9f, 1f);
            if (GUILayout.Button(new GUIContent("旋转",
                "将晶格体 Rotation 复位到初始旋转。"), GUILayout.Height(24)))
            {
                Undo.RecordObject(lattice.transform, "重置晶格体旋转");
                Undo.RecordObject(lattice, "重置晶格体旋转");
                lattice.ResetRotationToInitial();
                EditorUtility.SetDirty(lattice);
                EditorUtility.SetDirty(lattice.transform);
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = new Color(1f, 0.9f, 0.75f);
            if (GUILayout.Button(new GUIContent("缩放",
                "将晶格体 Scale 复位到初始缩放。"), GUILayout.Height(24)))
            {
                Undo.RecordObject(lattice.transform, "重置晶格体缩放");
                Undo.RecordObject(lattice, "重置晶格体缩放");
                lattice.ResetScaleToInitial();
                EditorUtility.SetDirty(lattice);
                EditorUtility.SetDirty(lattice.transform);
                SceneView.RepaintAll();
            }

            // 缩放按钮右侧：齿轮图标按钮，点击后将当前 Transform 重新保存为重置基准
            GUI.backgroundColor = new Color(0.25f, 0.22f, 0.25f);
            if (GUILayout.Button(new GUIContent("◆",
                "将当前晶格体的 Position/Rotation/Scale 重新保存为「重置基准」，\n之后三个重置按钮会以这个新位置为参照。"),
                GUILayout.Width(28), GUILayout.Height(24)))
            {
                Undo.RecordObject(lattice, "记录晶格体初始位置");
                lattice.SaveCurrentAsInitialTransform();
                EditorUtility.SetDirty(lattice);
                Debug.Log($"[LatticeModifier] 已记录当前 Transform 为重置基准：" +
                          $"pos={lattice.transform.position}, rot={lattice.transform.rotation.eulerAngles}");
                SceneView.RepaintAll();
            }

            EditorGUILayout.EndHorizontal();
        }
        else
        {
            // 旧场景：未记录时仍用整行宽按钮提示首次建立基准
            GUI.backgroundColor = new Color(0.25f, 0.22f, 0.25f);
            if (GUILayout.Button(new GUIContent("记录当前位置（作为重置基准）",
                "旧场景晶格体尚未记录初始 Transform。\n点击此按钮记录当前位置为「重置基准」，之后可随时复位到此处。"),
                GUILayout.Height(26)))
            {
                Undo.RecordObject(lattice, "记录晶格体初始位置");
                lattice.SaveCurrentAsInitialTransform();
                EditorUtility.SetDirty(lattice);
                Debug.Log($"[LatticeModifier] 已记录当前 Transform 为重置基准：" +
                          $"pos={lattice.transform.position}, rot={lattice.transform.rotation.eulerAngles}");
                SceneView.RepaintAll();
            }
        }

        GUI.backgroundColor = Color.white;
    }

    // ═══════════════════════════════════════════
    //  轴心操作
    // ═══════════════════════════════════════════

    /// 将晶格物体轴心移到所有控制点的中心，控制点世界位置不变。
    private void CenterPivot()
    {
        if (lattice == null || !lattice.IsInitialized || lattice.controlPoints == null) return;

        Transform t = lattice.transform;

        // 计算控制点的局部空间中心
        Vector3 localCenter = Vector3.zero;
        for (int i = 0; i < lattice.controlPoints.Length; i++)
            localCenter += lattice.controlPoints[i];
        localCenter /= lattice.controlPoints.Length;

        // 世界空间中心
        Vector3 worldCenter = t.TransformPoint(localCenter);

        Undo.RecordObject(lattice, "轴心居中");
        Undo.RecordObject(t, "轴心居中");

        // 偏移所有控制点（保持世界位置不变）
        Vector3 offset = localCenter;
        for (int i = 0; i < lattice.controlPoints.Length; i++)
            lattice.controlPoints[i] -= offset;

        // 同步 initialControlPoints
        var initField = typeof(LatticeModifier).GetField("initialControlPoints",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (initField != null)
        {
            var initPts = (Vector3[])initField.GetValue(lattice);
            if (initPts != null && initPts.Length == lattice.controlPoints.Length)
            {
                for (int i = 0; i < initPts.Length; i++)
                    initPts[i] -= offset;
            }
        }

        // 同步 latticeMin
        var minField = typeof(LatticeModifier).GetField("latticeMin",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (minField != null)
        {
            Vector3 oldMin = (Vector3)minField.GetValue(lattice);
            minField.SetValue(lattice, oldMin - offset);
        }

        // 移动 Transform 到新位置
        t.position = worldCenter;

        // initLatticeLocalToWorld/initLatticeWorldToLocal 已在 v3.7 删（死代码，DeformVertices 用当前帧 transform），
        // 这里不再写回这两个字段。控制点和包围盒已在前面重映射过。

        // 同步子物体控制点 Transform
        if (lattice.HasControlPointTransforms)
            lattice.SyncToTransforms();

        lattice.MarkDirty();
        lattice.ApplyDeformation();
        EditorUtility.SetDirty(lattice);
        EditorUtility.SetDirty(t);
        SceneView.RepaintAll();
    }

    /// 将晶格物体的轴心移到第一个变形目标的位置和旋转，控制点世界位置不变。
    private void InheritTargetPivot()
    {
        if (lattice == null || !lattice.IsInitialized || lattice.controlPoints == null) return;

        var renderers = lattice.GetActiveRenderers();
        if (renderers.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "没有变形目标对象", "确定");
            return;
        }

        Transform targetT = renderers[0].transform;
        Transform t = lattice.transform;

        // 目标的世界位置和旋转
        Vector3 newWorldPos = targetT.position;
        Quaternion newWorldRot = targetT.rotation;

        Undo.RecordObject(lattice, "继承变形对象轴心");
        Undo.RecordObject(t, "继承变形对象轴心");

        // 先计算新坐标系下所有控制点的局部坐标
        // 当前控制点世界位置 = t.TransformPoint(cp[i])
        // 新的局部坐标 = 逆变换(newWorldPos, newWorldRot, t.lossyScale) * 世界位置
        Matrix4x4 newLocalToWorld = Matrix4x4.TRS(newWorldPos, newWorldRot, t.lossyScale);
        Matrix4x4 newWorldToLocal = newLocalToWorld.inverse;

        for (int i = 0; i < lattice.controlPoints.Length; i++)
        {
            Vector3 worldPos = t.TransformPoint(lattice.controlPoints[i]);
            lattice.controlPoints[i] = newWorldToLocal.MultiplyPoint3x4(worldPos);
        }

        // 同步 initialControlPoints
        var initField = typeof(LatticeModifier).GetField("initialControlPoints",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (initField != null)
        {
            var initPts = (Vector3[])initField.GetValue(lattice);
            if (initPts != null && initPts.Length == lattice.controlPoints.Length)
            {
                for (int i = 0; i < initPts.Length; i++)
                {
                    Vector3 worldPos = t.TransformPoint(initPts[i]);
                    initPts[i] = newWorldToLocal.MultiplyPoint3x4(worldPos);
                }
            }
        }

        // 同步 latticeMin 和 latticeSize
        var minField = typeof(LatticeModifier).GetField("latticeMin",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var sizeField = typeof(LatticeModifier).GetField("latticeSize",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (minField != null && sizeField != null)
        {
            Vector3 oldMin = (Vector3)minField.GetValue(lattice);
            Vector3 oldSize = (Vector3)sizeField.GetValue(lattice);
            Vector3 oldMax = oldMin + oldSize;

            // 转换包围盒的 8 个角点到新坐标系，重新计算 AABB
            Vector3[] corners = new Vector3[8];
            corners[0] = new Vector3(oldMin.x, oldMin.y, oldMin.z);
            corners[1] = new Vector3(oldMax.x, oldMin.y, oldMin.z);
            corners[2] = new Vector3(oldMin.x, oldMax.y, oldMin.z);
            corners[3] = new Vector3(oldMax.x, oldMax.y, oldMin.z);
            corners[4] = new Vector3(oldMin.x, oldMin.y, oldMax.z);
            corners[5] = new Vector3(oldMax.x, oldMin.y, oldMax.z);
            corners[6] = new Vector3(oldMin.x, oldMax.y, oldMax.z);
            corners[7] = new Vector3(oldMax.x, oldMax.y, oldMax.z);

            Vector3 newMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 newMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                Vector3 wp = t.TransformPoint(corners[i]);
                Vector3 lp = newWorldToLocal.MultiplyPoint3x4(wp);
                newMin = Vector3.Min(newMin, lp);
                newMax = Vector3.Max(newMax, lp);
            }

            minField.SetValue(lattice, newMin);
            sizeField.SetValue(lattice, newMax - newMin);
        }

        // 设置新的 Transform
        t.position = newWorldPos;
        t.rotation = newWorldRot;

        // initLatticeLocalToWorld/initLatticeWorldToLocal 已在 v3.7 删（死代码），
        // 这里不再写回这两个字段。控制点和包围盒已在前面重映射过。

        // 同步子物体控制点 Transform
        if (lattice.HasControlPointTransforms)
            lattice.SyncToTransforms();

        lattice.MarkDirty();
        lattice.ApplyDeformation();
        EditorUtility.SetDirty(lattice);
        EditorUtility.SetDirty(t);
        SceneView.RepaintAll();
    }

    /// 将晶格物体旋转归零（世界对齐），位置不变，控制点世界位置不变。
    private void ResetPivotRotation()
    {
        if (lattice == null || !lattice.IsInitialized || lattice.controlPoints == null) return;

        Transform t = lattice.transform;
        if (t.rotation == Quaternion.identity) return;

        Undo.RecordObject(lattice, "重置轴心旋转");
        Undo.RecordObject(t, "重置轴心旋转");

        SetPivotRotation(Quaternion.identity);
    }

    /// 将晶格物体旋转设为第一个变形目标的旋转，位置不变，控制点世界位置不变。
    private void InheritTargetRotation()
    {
        if (lattice == null || !lattice.IsInitialized || lattice.controlPoints == null) return;

        var renderers = lattice.GetActiveRenderers();
        if (renderers.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "没有变形目标对象", "确定");
            return;
        }

        Quaternion targetRot = renderers[0].transform.rotation;
        Transform t = lattice.transform;
        if (t.rotation == targetRot) return;

        Undo.RecordObject(lattice, "继承对象旋转");
        Undo.RecordObject(t, "继承对象旋转");

        SetPivotRotation(targetRot);
    }

    /// 通用：仅改变晶格旋转，位置不变，控制点世界位置不变。
    private void SetPivotRotation(Quaternion newWorldRot)
    {
        Transform t = lattice.transform;
        Vector3 pos = t.position;

        // 新的局部坐标系矩阵（位置不变，旋转改变）
        Matrix4x4 newLocalToWorld = Matrix4x4.TRS(pos, newWorldRot, t.lossyScale);
        Matrix4x4 newWorldToLocal = newLocalToWorld.inverse;

        // 重新计算控制点在新坐标系下的局部坐标
        for (int i = 0; i < lattice.controlPoints.Length; i++)
        {
            Vector3 worldPos = t.TransformPoint(lattice.controlPoints[i]);
            lattice.controlPoints[i] = newWorldToLocal.MultiplyPoint3x4(worldPos);
        }

        // 同步 initialControlPoints
        var initField = typeof(LatticeModifier).GetField("initialControlPoints",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (initField != null)
        {
            var initPts = (Vector3[])initField.GetValue(lattice);
            if (initPts != null && initPts.Length == lattice.controlPoints.Length)
            {
                for (int i = 0; i < initPts.Length; i++)
                {
                    Vector3 worldPos = t.TransformPoint(initPts[i]);
                    initPts[i] = newWorldToLocal.MultiplyPoint3x4(worldPos);
                }
            }
        }

        // 同步 latticeMin / latticeSize
        var minField = typeof(LatticeModifier).GetField("latticeMin",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var sizeField = typeof(LatticeModifier).GetField("latticeSize",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (minField != null && sizeField != null)
        {
            Vector3 oldMin = (Vector3)minField.GetValue(lattice);
            Vector3 oldSize = (Vector3)sizeField.GetValue(lattice);
            Vector3 oldMax = oldMin + oldSize;

            Vector3[] corners = new Vector3[8];
            corners[0] = new Vector3(oldMin.x, oldMin.y, oldMin.z);
            corners[1] = new Vector3(oldMax.x, oldMin.y, oldMin.z);
            corners[2] = new Vector3(oldMin.x, oldMax.y, oldMin.z);
            corners[3] = new Vector3(oldMax.x, oldMax.y, oldMin.z);
            corners[4] = new Vector3(oldMin.x, oldMin.y, oldMax.z);
            corners[5] = new Vector3(oldMax.x, oldMin.y, oldMax.z);
            corners[6] = new Vector3(oldMin.x, oldMax.y, oldMax.z);
            corners[7] = new Vector3(oldMax.x, oldMax.y, oldMax.z);

            Vector3 newMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 newMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                Vector3 wp = t.TransformPoint(corners[i]);
                Vector3 lp = newWorldToLocal.MultiplyPoint3x4(wp);
                newMin = Vector3.Min(newMin, lp);
                newMax = Vector3.Max(newMax, lp);
            }

            minField.SetValue(lattice, newMin);
            sizeField.SetValue(lattice, newMax - newMin);
        }

        // 应用新旋转
        t.rotation = newWorldRot;

        // initLatticeLocalToWorld/initLatticeWorldToLocal 已在 v3.7 删（死代码），
        // 这里不再写回这两个字段。控制点和包围盒已在前面重映射过。

        // 同步子物体控制点 Transform
        if (lattice.HasControlPointTransforms)
            lattice.SyncToTransforms();

        lattice.MarkDirty();
        lattice.ApplyDeformation();
        EditorUtility.SetDirty(lattice);
        EditorUtility.SetDirty(t);
        SceneView.RepaintAll();
    }

    // ═══════════════════════════════════════════
    //  扩展选择：选中点的相邻点（晶格拓扑上±1）
    //  规则：只扩展到外表面的点，除非扩展源本身是内部点
    // ═══════════════════════════════════════════
    private bool IsOnSurface(int ix, int iy, int iz, int nx, int ny, int nz)
    {
        return ix == 0 || ix == nx - 1 ||
               iy == 0 || iy == ny - 1 ||
               iz == 0 || iz == nz - 1;
    }

    private void ExpandSelection()
    {
        // v3.28：先同步，确保 lattice 与当前 target 一致（防止 Editor 复用导致 stale）
        SyncLatticeFromTarget();

        if (lattice == null || !lattice.IsInitialized || selectedPoints.Count == 0) return;

        // v3.27 防御：清理不属于当前晶格的无效索引（切换晶格时静态选择残留）
        selectedPoints.RemoveWhere(idx => idx < 0 || idx >= lattice.TotalPoints);
        if (selectedPoints.Count == 0) return;

        int nx = lattice.PointCountX, ny = lattice.PointCountY, nz = lattice.PointCountZ;
        var expanded = new HashSet<int>(selectedPoints);

        // 判断选中的源点中是否包含内部点
        bool hasInteriorSource = false;
        foreach (int idx in selectedPoints)
        {
            lattice.GetPointIndex3D(idx, out int ix, out int iy, out int iz);
            if (!IsOnSurface(ix, iy, iz, nx, ny, nz))
            {
                hasInteriorSource = true;
                break;
            }
        }

        foreach (int idx in selectedPoints)
        {
            lattice.GetPointIndex3D(idx, out int ix, out int iy, out int iz);
            bool sourceIsInterior = !IsOnSurface(ix, iy, iz, nx, ny, nz);

            // 6 个相邻方向
            int[][] neighbors = new int[][]
            {
                new[] { ix - 1, iy, iz },
                new[] { ix + 1, iy, iz },
                new[] { ix, iy - 1, iz },
                new[] { ix, iy + 1, iz },
                new[] { ix, iy, iz - 1 },
                new[] { ix, iy, iz + 1 },
            };

            foreach (var nb in neighbors)
            {
                int nix = nb[0], niy = nb[1], niz = nb[2];
                if (nix < 0 || nix >= nx || niy < 0 || niy >= ny || niz < 0 || niz >= nz)
                    continue;

                // 如果源点是内部点，允许扩展到任何相邻点
                // 如果源点是表面点，只扩展到表面点（排除内部点）
                if (!sourceIsInterior && !hasInteriorSource)
                {
                    if (!IsOnSurface(nix, niy, niz, nx, ny, nz))
                        continue;
                }

                expanded.Add(lattice.GetFlatIndex(nix, niy, niz));
            }
        }

        // v3.31：支持 Undo/Redo。selectedPoints 是编辑器数据（存在 s_latticeSelections 中），
        // 通过 RecordObject + undoRedoPerformed 一键回调实现双向切换。
        var prevSelection = new HashSet<int>(selectedPoints);
        var nextSelection = new HashSet<int>(expanded);

        Undo.RecordObject(lattice, "扩展选择");
        selectedPoints = nextSelection;
        s_activeSelectedPoints = selectedPoints;
        SyncSelectionToHierarchy();

        // 注册一次性的 Undo/Redo 回调：根据当前选中状态判断是 undo 还是 redo 方向
        LatticeModifier capturedLattice = lattice;
        Undo.UndoRedoCallback handler = null;
        handler = () =>
        {
            Undo.undoRedoPerformed -= handler;
            if (capturedLattice == null) return;
            // 当前选中 == nextSelection → 用户按了 Undo，恢复 prevSelection
            // 当前选中 == prevSelection → 用户按了 Redo，恢复 nextSelection
            var curr = new HashSet<int>(selectedPoints);
            if (curr.SetEquals(nextSelection))
                selectedPoints = prevSelection;
            else if (curr.SetEquals(prevSelection))
                selectedPoints = nextSelection;
            s_activeSelectedPoints = selectedPoints;
            SyncSelectionToHierarchy();
            SceneView.RepaintAll();
        };
        Undo.undoRedoPerformed += handler;
    }

    // ═══════════════════════════════════════════
    //  SceneView 绘制 & 交互
    // ═══════════════════════════════════════════

    /// 计算四边形面的朝外法线。
    /// 用三角形 (p0→p1→p2) 叉积得原始方向，再基于 outward（晶格本地坐标轴在 world 的投影）
    /// 做方向校正确保法线指向晶格外部。
    /// 关键：使用「晶格本地轴向」而非「四边形中心→晶格形心」，避免哑铃/沙漏型晶格
    /// 中部四边形 outward 接近零向量导致方向校正失败。
    private static Vector3 ComputeOutwardNormal(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 outward)
    {
        Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
        if (n.sqrMagnitude < 0.0001f) return Vector3.zero;
        n.Normalize();
        if (Vector3.Dot(n, outward) < 0) n = -n;
        return n;
    }

    private static void DrawLatticeAndHandles(LatticeModifier lat, HashSet<int> selPts, SceneView sceneView, bool isInstance)
    {
        if (lat == null || !lat.IsInitialized || lat.controlPoints == null) return;

        // v3.32 运行时优化：Play Mode 下不做任何晶格点/线着色计算，
        // 改为绘制一个浅灰色立方体外壳（无点、无选中、无拖拽、无框选）。
        // 单一短路点同时覆盖 OnSceneGUI 与 OnGlobalSceneGUIStatic 两条调用路径。
        if (Application.isPlaying)
        {
            DrawLatticeBoundingBoxSimple(lat);
            return;
        }

        // v3.27 防御：清理选中集中不属于当前晶格的点索引（切换晶格时静态选择残留）
        if (selPts != null && selPts.Count > 0)
            selPts.RemoveWhere(idx => idx < 0 || idx >= lat.controlPoints.Length);

        // 段数被修改后 controlPoints 数组长度与当前 PointCount 不匹配，跳过绘制避免越界
        int expectedTotal = lat.PointCountX * lat.PointCountY * lat.PointCountZ;
        if (lat.controlPoints.Length != expectedTotal) return;

        // 有控制点被选中时隐藏 Unity 内置 Transform Handle，避免出现两个移动工具
        Tools.hidden = selPts != null && selPts.Count > 0;

        Event e = Event.current;
        Transform t = lat.transform;
        int nx = lat.PointCountX, ny = lat.PointCountY, nz = lat.PointCountZ;

        // 相机信息（提前到此处，供后续线和点的背面判断共用）
        Camera cam = sceneView.camera;
        Vector3 camPos = cam.transform.position;
        Vector3 camForward = cam.transform.forward;
        bool isOrtho = cam.orthographic;

        // ── 基于四边形面的可见性预计算 ──
        // 遍历晶格所有外表面四边形（每个面由 4 个控制点组成），
        // 计算每个四边形的实际法线方向并判断是否朝向相机（正面）。
        // 一个控制点只要属于任意一个正面四边形，即为"可见"。
        int totalPts = lat.controlPoints.Length;
        bool[] isPointFrontFacing = new bool[totalPts]; // 默认 false = 背面/不可见

        // 晶格本地坐标轴在世界空间的方向（用于"朝外"参考，避免依赖形心导致
        // 哑铃/沙漏型晶格中部四边形 outward 接近零向量的问题）
        Vector3 axisX = t.TransformDirection(Vector3.right);
        Vector3 axisY = t.TransformDirection(Vector3.up);
        Vector3 axisZ = t.TransformDirection(Vector3.forward);

        // 局部函数：判断四边形面是否朝向相机，若是则标记其 4 个顶点为正面可见
        // outwardWorld: 该表面四边形在世界空间的预期外法线方向（来自晶格本地轴向投影）
        void MarkQuadVisible(int ix0, int iy0, int iz0, int ix1, int iy1, int iz1,
                             int ix2, int iy2, int iz2, int ix3, int iy3, int iz3,
                             Vector3 outwardWorld)
        {
            Vector3 p0 = t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(ix0, iy0, iz0)]);
            Vector3 p1 = t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(ix1, iy1, iz1)]);
            Vector3 p2 = t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(ix2, iy2, iz2)]);
            // p3 仅用于计算四边形中心，不参与法线计算（三角形 p0→p1→p2 已足够）
            Vector3 p3 = t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(ix3, iy3, iz3)]);
            Vector3 quadCenter = (p0 + p1 + p2 + p3) * 0.25f;

            // 朝外法线（内部已基于晶格本地轴向校正）
            Vector3 normal = ComputeOutwardNormal(p0, p1, p2, outwardWorld);
            if (normal == Vector3.zero) return;

            // 从四边形面指向相机的方向
            Vector3 toCam = isOrtho ? -camForward : (camPos - quadCenter).normalized;

            // 实体背面判断：朝外法线与 toCam 同向（Dot>0）= 法线指向相机方向 = 该面朝向观察者（正面）
            if (Vector3.Dot(normal, toCam) > 0)
            {
                isPointFrontFacing[lat.GetFlatIndex(ix0, iy0, iz0)] = true;
                isPointFrontFacing[lat.GetFlatIndex(ix1, iy1, iz1)] = true;
                isPointFrontFacing[lat.GetFlatIndex(ix2, iy2, iz2)] = true;
                isPointFrontFacing[lat.GetFlatIndex(ix3, iy3, iz3)] = true;
            }
        }

        // 遍历四边形并标记正面顶点
        if (nx > 1) // X 面（X=0 和 X=nx-1）
        {
            for (int iy = 0; iy < ny - 1; iy++)
            for (int iz = 0; iz < nz - 1; iz++)
            {
                MarkQuadVisible(0, iy, iz, 0, iy + 1, iz, 0, iy, iz + 1, 0, iy + 1, iz + 1, -axisX);
                MarkQuadVisible(nx - 1, iy, iz, nx - 1, iy + 1, iz, nx - 1, iy, iz + 1, nx - 1, iy + 1, iz + 1, axisX);
            }
        }
        if (ny > 1) // Y 面（Y=0 和 Y=ny-1）
        {
            for (int ix = 0; ix < nx - 1; ix++)
            for (int iz = 0; iz < nz - 1; iz++)
            {
                MarkQuadVisible(ix, 0, iz, ix + 1, 0, iz, ix, 0, iz + 1, ix + 1, 0, iz + 1, -axisY);
                MarkQuadVisible(ix, ny - 1, iz, ix + 1, ny - 1, iz, ix, ny - 1, iz + 1, ix + 1, ny - 1, iz + 1, axisY);
            }
        }
        if (nz > 1) // Z 面（Z=0 和 Z=nz-1）
        {
            for (int ix = 0; ix < nx - 1; ix++)
            for (int iy = 0; iy < ny - 1; iy++)
            {
                MarkQuadVisible(ix, iy, 0, ix + 1, iy, 0, ix, iy + 1, 0, ix + 1, iy + 1, 0, -axisZ);
                MarkQuadVisible(ix, iy, nz - 1, ix + 1, iy, nz - 1, ix, iy + 1, nz - 1, ix + 1, iy + 1, nz - 1, axisZ);
            }
        }

        // ── 绘制晶格线（背面线段压暗）──
        // 在四边形面可见性计算完成后绘制，利用 isPointFrontFacing 判断线段是否处于背面。
        // 背面定义：一条线段两端点都在外壳上，且至少一端所属四边形均不朝向相机。
        // 使用 anyBack 而非 bothBack，使折角处的棱线也能被压暗（角点可能属于正面面片但该棱仍在背面）。
        Color brightLineColor = new Color(0.1f, 0.14f, 0.15f, 0.85f);
        Color dimLineColor = new Color(0.4f , 0.4f , 0.4f, 0.5f);
        // 晶格线宽（屏幕像素）。Handles.DrawLine 是固定 1px，改用 DrawAAPolyLine 才可控。
        const float latticeLineWidth = 4f;
        for (int ix = 0; ix < nx; ix++)
        for (int iy = 0; iy < ny; iy++)
        for (int iz = 0; iz < nz; iz++)
        {
            int idxA = lat.GetFlatIndex(ix, iy, iz);
            Vector3 pA = t.TransformPoint(lat.controlPoints[idxA]);
            bool aOnSurface = (ix == 0 || ix == nx - 1 || iy == 0 || iy == ny - 1 || iz == 0 || iz == nz - 1);

            // X 方向
            if (ix < nx - 1)
            {
                int idxB = lat.GetFlatIndex(ix + 1, iy, iz);
                bool bOnSurface = (ix + 1 == 0 || ix + 1 == nx - 1 || iy == 0 || iy == ny - 1 || iz == 0 || iz == nz - 1);
                if (!lat.surfaceOnly || (aOnSurface && bOnSurface))
                {
                    bool anyBack = aOnSurface && bOnSurface && (!isPointFrontFacing[idxA] || !isPointFrontFacing[idxB]);
                    Handles.color = anyBack ? dimLineColor : brightLineColor;
                    Handles.DrawAAPolyLine(latticeLineWidth, pA, t.TransformPoint(lat.controlPoints[idxB]));
                }
            }
            // Y 方向
            if (iy < ny - 1)
            {
                int idxB = lat.GetFlatIndex(ix, iy + 1, iz);
                bool bOnSurface = (ix == 0 || ix == nx - 1 || iy + 1 == 0 || iy + 1 == ny - 1 || iz == 0 || iz == nz - 1);
                if (!lat.surfaceOnly || (aOnSurface && bOnSurface))
                {
                    bool anyBack = aOnSurface && bOnSurface && (!isPointFrontFacing[idxA] || !isPointFrontFacing[idxB]);
                    Handles.color = anyBack ? dimLineColor : brightLineColor;
                    Handles.DrawAAPolyLine(latticeLineWidth, pA, t.TransformPoint(lat.controlPoints[idxB]));
                }
            }
            // Z 方向
            if (iz < nz - 1)
            {
                int idxB = lat.GetFlatIndex(ix, iy, iz + 1);
                bool bOnSurface = (ix == 0 || ix == nx - 1 || iy == 0 || iy == ny - 1 || iz + 1 == 0 || iz + 1 == nz - 1);
                if (!lat.surfaceOnly || (aOnSurface && bOnSurface))
                {
                    bool anyBack = aOnSurface && bOnSurface && (!isPointFrontFacing[idxA] || !isPointFrontFacing[idxB]);
                    Handles.color = anyBack ? dimLineColor : brightLineColor;
                    Handles.DrawAAPolyLine(latticeLineWidth, pA, t.TransformPoint(lat.controlPoints[idxB]));
                }
            }
        }

        // ── 按深度排序：从远到近绘制，近处控制点覆盖远处（实现遮挡效果） ──

        // 构建索引+深度数组，按深度从远到近排序
        var depthOrder = new int[totalPts];
        var depths = new float[totalPts];
        for (int i = 0; i < totalPts; i++)
        {
            depthOrder[i] = i;
            Vector3 wp = t.TransformPoint(lat.controlPoints[i]);
            // 深度 = 在相机前方向上的投影距离（正交）或到相机距离（透视）
            depths[i] = isOrtho
                ? Vector3.Dot(wp - camPos, camForward)
                : (wp - camPos).sqrMagnitude;
        }
        // 从远到近排序（深度大的先绘制，深度小的后绘制覆盖）
        System.Array.Sort(depths, depthOrder);
        // Sort 默认升序（近→远），需要反转为远→近
        System.Array.Reverse(depthOrder);

        // ── 点击检测：找到屏幕上最近的控制点 ──
        int clickedIndex = -1;
        float closestClickDepth = float.MaxValue;

        for (int order = 0; order < totalPts; order++)
        {
            int i = depthOrder[order];
            Vector3 worldPos = t.TransformPoint(lat.controlPoints[i]);
            float sz = HandleUtility.GetHandleSize(worldPos) * 0.05f;

            bool isSelected = selPts != null && selPts.Contains(i);
            lat.GetPointIndex3D(i, out int pix, out int piy, out int piz);
            bool isCorner = (pix == 0 || pix == nx - 1) && (piy == 0 || piy == ny - 1) && (piz == 0 || piz == nz - 1);

            bool isOnSurface = (pix == 0 || pix == nx - 1) ||
                                (piy == 0 || piy == ny - 1) ||
                                (piz == 0 || piz == nz - 1);

            // v3.24.3：surfaceOnly 开启时，内部点（!isOnSurface）直接 skip：
            //   - 不画 SphereHandle（不显示）
            //   - 不响应 Handles.Button（无法点击选中）
            //   - 不参与 depthOrder 的"被点击"判定
            // 视觉上等价于"内部点不存在"，但 controlPoints 数组里仍有数据用于动画/CP Transform 引用兼容。
            if (lat.surfaceOnly && !isOnSurface)
            {
                // 如果之前选中了内部点（用户已勾选前选中的），现在清掉（避免幽灵选中）
                if (isSelected && selPts != null)
                {
                    selPts.Remove(i);
                    s_activeSelectedPoints = selPts;
                }
                continue;
            }

            // 基于四边形面可见性判断背面：isPointFrontFacing 已在前置遍历中计算完毕，
            // 只要该点所在的任一表面四边形朝向相机（正面），则该点可见。
            // 内部点无表面四边形，视为始终可见（不参与背面判断）。
            bool isBackFacing = isOnSurface && !isPointFrontFacing[i];

            float backDim = isBackFacing ? 0.4f : 1f;

            if (isSelected) Handles.color = isBackFacing ? new Color(0.7f, 0.7f, 0.7f, 0.9f) : Color.white;
            else if (isCorner) Handles.color = new Color(1f * backDim, 0.3f * backDim, 0.3f * backDim, 0.9f);
            else if (!isOnSurface) Handles.color = new Color(0.2f * backDim, 0.2f * backDim, 0.2f * backDim, 0.9f);
            else Handles.color = new Color(0.95f * backDim, 0.65f * backDim, 0.2f * backDim, 0.8f);
            // 设置控制点的显示大小：选中=2.8, 内部=1, 其他未选中=1.8
            float drawSize = isSelected ? sz * 2.8f : (!isOnSurface && !isCorner) ? sz : sz * 1.8f;
            float pickSize = isSelected ? sz * 2.2f : (!isOnSurface && !isCorner) ? sz * 1.5f : sz * 2.5f;

            if (Handles.Button(worldPos, Quaternion.identity, drawSize, pickSize, Handles.SphereHandleCap))
            {
                // 记录点击，但只响应最近的（深度最小的）
                float d = isOrtho
                    ? Vector3.Dot(worldPos - camPos, camForward)
                    : (worldPos - camPos).sqrMagnitude;
                if (d < closestClickDepth)
                {
                    closestClickDepth = d;
                    clickedIndex = i;
                }
            }
        }

        // 处理点击结果（只响应最前面的控制点）
        if (clickedIndex >= 0)
        {
            if (e.control)
            {
                if (selPts.Contains(clickedIndex)) selPts.Remove(clickedIndex);
                else selPts.Add(clickedIndex);
            }
            else
            {
                selPts.Clear();
                selPts.Add(clickedIndex);
            }
            SyncSelectionToHierarchy();
            sceneView.Repaint();
        }

        // ── Esc 取消选择（仅当存在 CP 选中时；模型对象上的 Esc 切换已在 OnGlobalSceneGUIStatic 中处理）──
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && selPts != null && selPts.Count > 0)
        {
            selPts.Clear();
            SyncSelectionToHierarchy();
            sceneView.Repaint();
            e.Use();
        }

        // ── Shift+拖拽框选 ──
        if (e.shift)
        {
            int controlID = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0)
                    {
                        s_isDragging = true;
                        s_dragStart = e.mousePosition;
                        s_dragEnd = e.mousePosition;
                        GUIUtility.hotControl = controlID;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (s_isDragging)
                    {
                        s_dragEnd = e.mousePosition;
                        e.Use();
                        SceneView.RepaintAll();
                    }
                    break;
                case EventType.MouseUp:
                    if (s_isDragging && e.button == 0)
                    {
                        s_isDragging = false;
                        GUIUtility.hotControl = 0;
                        Rect selRect = new Rect(
                            Mathf.Min(s_dragStart.x, s_dragEnd.x),
                            Mathf.Min(s_dragStart.y, s_dragEnd.y),
                            Mathf.Abs(s_dragEnd.x - s_dragStart.x),
                            Mathf.Abs(s_dragEnd.y - s_dragStart.y));
                        bool isSubtract = e.alt; // Shift+Alt = 减选
                        if (!e.control && !isSubtract) selPts.Clear();
                        Camera selCam = sceneView.camera;
                        for (int i = 0; i < lat.controlPoints.Length; i++)
                        {
                            Vector3 worldPos = t.TransformPoint(lat.controlPoints[i]);
                            Vector3 screenPos = selCam.WorldToScreenPoint(worldPos);
                            screenPos.y = selCam.pixelHeight - screenPos.y;
                            if (screenPos.z > 0 && selRect.Contains(new Vector2(screenPos.x, screenPos.y)))
                            {
                                if (isSubtract)
                                    selPts.Remove(i);
                                else
                                    selPts.Add(i);
                            }
                        }
                        e.Use();
                        SyncSelectionToHierarchy();
                        sceneView.Repaint();
                    }
                    break;
            }

            if (s_isDragging)
            {
                Handles.BeginGUI();
                Rect r = new Rect(
                    Mathf.Min(s_dragStart.x, s_dragEnd.x),
                    Mathf.Min(s_dragStart.y, s_dragEnd.y),
                    Mathf.Abs(s_dragEnd.x - s_dragStart.x),
                    Mathf.Abs(s_dragEnd.y - s_dragStart.y));
                EditorGUI.DrawRect(r, new Color(0.2f, 0.6f, 1f, 0.15f));
                Handles.color = new Color(0.2f, 0.6f, 1f, 0.8f);
                Handles.DrawSolidRectangleWithOutline(
                    new Vector3[] {
                        new Vector3(r.xMin, r.yMin, 0),
                        new Vector3(r.xMax, r.yMin, 0),
                        new Vector3(r.xMax, r.yMax, 0),
                        new Vector3(r.xMin, r.yMax, 0)
                    },
                    new Color(0.2f, 0.6f, 1f, 0.1f),
                    new Color(0.2f, 0.6f, 1f, 0.8f));
                Handles.EndGUI();
            }
        }

        // ── 选中点的移动/旋转/缩放手柄 ──
        if (selPts != null && selPts.Count > 0)
        {
            Vector3 center = Vector3.zero;
            foreach (int i in selPts)
                center += t.TransformPoint(lat.controlPoints[i]);
            center /= selPts.Count;

            if (Tools.current == Tool.Scale)
            {
                // 拖拽期间使用锁定的中心点
                Vector3 handleCenter = s_handleDragging ? s_handleStartCenter : center;

                EditorGUI.BeginChangeCheck();
                Vector3 newScale = Handles.ScaleHandle(Vector3.one, handleCenter, t.rotation, HandleUtility.GetHandleSize(handleCenter));
                if (EditorGUI.EndChangeCheck())
                {
                    if (!s_handleDragging)
                    {
                        s_handleDragging = true;
                        s_handleStartCenter = center;
                        handleCenter = center;
                        s_handleStartPositions = new Dictionary<int, Vector3>();
                        foreach (int i in selPts)
                            s_handleStartPositions[i] = t.TransformPoint(lat.controlPoints[i]);
                    }

                    Undo.RecordObject(lat, "缩放晶格控制点");
                    // 关键修复：newScale 的 x/y/z 是「手柄局部轴」（由 t.rotation 定向，
                    // 即屏幕上彩色箭头方向）上的缩放分量，不是世界轴分量。
                    // 必须先把世界偏移转换到手柄旋转坐标系，按轴缩放后再转回世界，
                    // 否则晶格带旋转时，沿某个箭头缩放会作用到错误的世界轴 → 表现为偏移而非缩放。
                    Quaternion handleRot = t.rotation;
                    Quaternion invHandleRot = Quaternion.Inverse(handleRot);
                    foreach (int i in selPts)
                    {
                        Vector3 startWp = s_handleStartPositions[i];
                        Vector3 worldOffset = startWp - handleCenter;
                        Vector3 axisOffset = invHandleRot * worldOffset; // 转入手柄局部轴
                        axisOffset.x *= newScale.x;
                        axisOffset.y *= newScale.y;
                        axisOffset.z *= newScale.z;
                        worldOffset = handleRot * axisOffset;            // 转回世界
                        lat.controlPoints[i] = t.InverseTransformPoint(handleCenter + worldOffset);
                    }
                    if (lat.HasControlPointTransforms)
                        lat.SyncToTransforms();
                    if (lat.liveUpdate)
                        lat.ApplyDeformation();
                    EditorUtility.SetDirty(lat);
                }
                else if (s_handleDragging && Event.current.type == EventType.MouseUp)
                {
                    s_handleDragging = false;
                    s_handleStartPositions = null;
                }
            }
            else if (Tools.current == Tool.Rotate)
            {
                Vector3 handleCenter = s_handleDragging ? s_handleStartCenter : center;

                EditorGUI.BeginChangeCheck();
                Quaternion newRot = Handles.RotationHandle(Quaternion.identity, handleCenter);
                if (EditorGUI.EndChangeCheck())
                {
                    if (!s_handleDragging)
                    {
                        s_handleDragging = true;
                        s_handleStartCenter = center;
                        handleCenter = center;
                        s_handleStartPositions = new Dictionary<int, Vector3>();
                        foreach (int i in selPts)
                            s_handleStartPositions[i] = t.TransformPoint(lat.controlPoints[i]);
                    }

                    Undo.RecordObject(lat, "旋转晶格控制点");
                    // 记录子控制点 Transform 以支持 Undo
                    if (lat.HasControlPointTransforms)
                    {
                        foreach (int i in selPts)
                        {
                            Transform cp = lat.GetControlPointTransform(i);
                            if (cp != null) Undo.RecordObject(cp, "旋转晶格控制点");
                        }
                    }
                    foreach (int i in selPts)
                    {
                        Vector3 startWp = s_handleStartPositions[i];
                        Vector3 offset = startWp - handleCenter;
                        offset = newRot * offset;
                        lat.controlPoints[i] = t.InverseTransformPoint(handleCenter + offset);
                    }
                    if (lat.HasControlPointTransforms)
                        lat.SyncToTransforms();
                    if (lat.liveUpdate)
                        lat.ApplyDeformation();
                    EditorUtility.SetDirty(lat);
                }
                else if (s_handleDragging && Event.current.type == EventType.MouseUp)
                {
                    s_handleDragging = false;
                    s_handleStartPositions = null;
                }
            }
            else
            {
                // 默认移动模式
                EditorGUI.BeginChangeCheck();
                Vector3 newCenter = Handles.PositionHandle(center, t.rotation);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(lat, "移动晶格控制点");
                    // 记录子控制点 Transform 以支持 Undo
                    if (lat.HasControlPointTransforms)
                    {
                        foreach (int i in selPts)
                        {
                            Transform cp = lat.GetControlPointTransform(i);
                            if (cp != null) Undo.RecordObject(cp, "移动晶格控制点");
                        }
                    }
                    Vector3 delta = newCenter - center;
                    foreach (int i in selPts)
                    {
                        Vector3 wp = t.TransformPoint(lat.controlPoints[i]) + delta;
                        lat.controlPoints[i] = t.InverseTransformPoint(wp);
                    }
                    if (lat.HasControlPointTransforms)
                        lat.SyncToTransforms();
                    if (lat.liveUpdate)
                        lat.ApplyDeformation();
                    EditorUtility.SetDirty(lat);
                }
            }
        }
    }

    private static void OnGlobalSceneGUIStatic(SceneView sceneView)
    {
        // ── Esc 切换：选中目标模型对象时按 Esc 自动选中对应晶格对象（按命名规则 Lattice_<模型名>）──
        // 独立于 s_activeLattice，确保从未点过晶格对象时也能生效
        var escEvt = Event.current;
        if (escEvt != null && escEvt.type == EventType.KeyDown && escEvt.keyCode == KeyCode.Escape)
        {
            if (TrySelectLatticeByEsc())
            {
                escEvt.Use();
                sceneView?.Repaint();
                return;
            }
        }

        if (s_activeLattice == null || !s_activeLattice.IsInitialized || s_activeLattice.controlPoints == null)
            return;
        if (Selection.activeGameObject == s_activeLattice.gameObject)
            return;
        DrawLatticeAndHandles(s_activeLattice, s_activeSelectedPoints, sceneView, false);
    }

    /// v3.32 运行时简化绘制：只画 8 个角点 + 12 条边的浅灰色立方体外壳。
    /// 跳过面可见性 / 深度排序 / 选中 / 拖拽 / 框选 等所有重操作，
    /// 用于 Application.isPlaying 期间的 OnSceneGUI，避免 Editor 在 Play Mode 下持续消耗性能。
    private static void DrawLatticeBoundingBoxSimple(LatticeModifier lat)
    {
        Transform t = lat.transform;
        int nx = lat.PointCountX, ny = lat.PointCountY, nz = lat.PointCountZ;
        if (lat.controlPoints.Length != nx * ny * nz) return;

        // 8 个角点（世界空间）
        Vector3 p0 = t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(0,     0,     0)]);
        Vector3 p1 = t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(nx-1, 0,     0)]);
        Vector3 p2 = t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(nx-1, ny-1, 0)]);
        Vector3 p3 = t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(0,     ny-1, 0)]);
        Vector3 p4 = t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(0,     0,     nz-1)]);
        Vector3 p5 = t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(nx-1, 0,     nz-1)]);
        Vector3 p6 = t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(nx-1, ny-1, nz-1)]);
        Vector3 p7 = t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(0,     ny-1, nz-1)]);

        // 浅灰色统一着色
        Color gray = new Color(0.7f, 0.7f, 0.7f, 0.6f);
        Handles.color = gray;
        // 底面四边形
        Handles.DrawAAPolyLine(2f, p0, p1, p2, p3, p0);
        // 顶面四边形
        Handles.DrawAAPolyLine(2f, p4, p5, p6, p7, p4);
        // 4 条垂直棱
        Handles.DrawAAPolyLine(2f, p0, p4);
        Handles.DrawAAPolyLine(2f, p1, p5);
        Handles.DrawAAPolyLine(2f, p2, p6);
        Handles.DrawAAPolyLine(2f, p3, p7);
    }

    /// Esc 切换：按命名规则（Lattice_ + 模型名）查找匹配晶格对象并选中。
    /// 匹配范围：模型对象自身名 + 父链名（覆盖多目标模式父节点命名）。
    /// 返回 true 表示已选中至少一个晶格。
    private static bool TrySelectLatticeByEsc()
    {
        // 文本框/搜索框聚焦时不消费 Esc
        if (EditorGUIUtility.editingTextField) return false;

        var selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0) return false;

        // 晶格对象本身按 Esc 不处理（保持 CP-Esc 取消选择原有行为）
        foreach (var go in selected)
        {
            if (go != null && go.name.StartsWith("Lattice_", System.StringComparison.Ordinal))
                return false;
        }

        var lattices = new List<GameObject>();
        foreach (var go in selected)
        {
            foreach (var lat in LatticeSceneWalker.FindLatticesByName(go))
            {
                if (!lattices.Contains(lat)) lattices.Add(lat);
            }
        }

        if (lattices.Count == 0) return false;

        Selection.objects = lattices.ToArray();
        EditorGUIUtility.PingObject(lattices[0]);
        return true;
    }

    private void OnSceneGUI()
    {
        // v3.28：先同步，确保 lattice/selectedPoints 与当前 target 一致
        SyncLatticeFromTarget();

        if (lattice == null || !lattice.IsInitialized || lattice.controlPoints == null) return;
        DrawLatticeAndHandles(lattice, selectedPoints, SceneView.lastActiveSceneView, true);
    }

    // ═══════════════════════════════════════════
    //  创建变形快照（复制当前变形状态的模型对象）
    // ═══════════════════════════════════════════
    private void CreateDeformSnapshot()
    {
        lattice.ApplyDeformation();

        var renderers = lattice.GetActiveRenderers();
        if (renderers.Count == 0)
        {
            Debug.LogWarning("[LatticeModifier] 没有目标 Renderer，无法创建快照");
            return;
        }

        var createdObjects = new System.Collections.Generic.List<GameObject>();

        foreach (var rend in renderers)
        {
            if (rend == null) continue;

            Mesh currentMesh = LatticeModifier.GetRendererMeshStatic(rend);
            if (currentMesh == null) continue;

            // 创建独立的 Mesh 副本，与原晶格变形 Mesh 完全无关
            Mesh snapshotMesh = Object.Instantiate(currentMesh);
            snapshotMesh.name = rend.name + "_Snapshot";

            // 复制 GameObject
            GameObject snapshot = Object.Instantiate(rend.gameObject);
            snapshot.name = rend.name + "_Snapshot";
            snapshot.transform.SetParent(rend.transform.parent, true);
            snapshot.transform.position = rend.transform.position;
            snapshot.transform.rotation = rend.transform.rotation;
            snapshot.transform.localScale = rend.transform.localScale;

            // 赋予独立的 Mesh 副本
            if (snapshot.TryGetComponent<SkinnedMeshRenderer>(out var smr))
                smr.sharedMesh = snapshotMesh;
            else if (snapshot.TryGetComponent<MeshFilter>(out var mf))
                mf.sharedMesh = snapshotMesh;

            // 移除快照上的 LatticeModifier（如果有的话）
            var latComp = snapshot.GetComponent<LatticeModifier>();
            if (latComp != null) Object.DestroyImmediate(latComp);

            Undo.RegisterCreatedObjectUndo(snapshot, "创建变形快照");
            createdObjects.Add(snapshot);
        }

        if (createdObjects.Count > 0)
        {
            Selection.objects = createdObjects.ToArray();
            Debug.Log($"[LatticeModifier] 已创建 {createdObjects.Count} 个变形快照");
        }
    }

    // ═══════════════════════════════════════════
    //  烘焙变形到 Mesh Asset
    // ═══════════════════════════════════════════
    private void BakeDeformationToAsset()
    {
        // 确保变形已经应用到最新状态
        lattice.ApplyDeformation();

        var renderers = lattice.GetActiveRenderers();
        if (renderers.Count == 0)
        {
            Debug.LogWarning("[LatticeModifier] 没有找到目标 Renderer，烘焙取消");
            return;
        }

        // 选择保存目录
        string savePath = EditorUtility.SaveFolderPanel("选择烘焙 Mesh 保存目录", "Assets", "");
        if (string.IsNullOrEmpty(savePath)) return;

        // 转换为相对路径
        if (!savePath.StartsWith(Application.dataPath))
        {
            EditorUtility.DisplayDialog("路径错误", "请选择 Assets 目录内的文件夹", "确定");
            return;
        }
        string relativeDir = "Assets" + savePath.Substring(Application.dataPath.Length);

        // ── 第一步：保存所有变形 Mesh 为 Asset ──
        bool anyFailed = false;
        var savedMeshes = new List<(Renderer rend, Mesh persistedMesh)>();

        foreach (var rend in renderers)
        {
            if (rend == null) continue;

            Mesh currentMesh = LatticeModifier.GetRendererMeshStatic(rend);
            if (currentMesh == null)
            {
                Debug.LogWarning($"[LatticeModifier] {rend.name} 没有有效 Mesh，跳过");
                anyFailed = true;
                continue;
            }

            string baseName = rend.name + "_Baked";
            Mesh bakedMesh = UnityEngine.Object.Instantiate(currentMesh);
            bakedMesh.name = baseName;

            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{relativeDir}/{baseName}.asset");
            AssetDatabase.CreateAsset(bakedMesh, assetPath);
            // CreateAsset 后 bakedMesh 已经是持久化对象，直接使用
            savedMeshes.Add((rend, bakedMesh));
        }

        // SaveAssets 持久化到磁盘，不调用 Refresh（避免触发重新导入还原 Renderer）
        AssetDatabase.SaveAssets();

        // ── 第二步：把持久化 Mesh 赋给 Renderer ──
        foreach (var (rend, persistedMesh) in savedMeshes)
        {
            if (rend is SkinnedMeshRenderer smr)
            {
                Undo.RecordObject(smr, "烘焙晶格变形");
                smr.sharedMesh = persistedMesh;
                EditorUtility.SetDirty(smr);
            }
            else
            {
                var mf = rend.GetComponent<MeshFilter>();
                if (mf != null)
                {
                    Undo.RecordObject(mf, "烘焙晶格变形");
                    mf.sharedMesh = persistedMesh;
                    EditorUtility.SetDirty(mf);
                }
            }
        }

        // ── 第三步：清理晶格数据（Renderer 已持有持久化 Mesh，此时清理安全）──
        Undo.RecordObject(lattice, "烘焙晶格变形");
        lattice.BakeAndRemove();
        selectedPoints.Clear();
        s_latticeSelections.Remove(lattice.GetInstanceID()); // v3.30 清理字典
        s_activeLattice = null;
        s_activeSelectedPoints = null;
        EditorUtility.SetDirty(lattice);
        SceneView.RepaintAll();

        if (anyFailed)
            Debug.LogWarning("[LatticeModifier] 部分 Renderer 烘焙失败，请检查 Console");
        else
            Debug.Log($"[LatticeModifier] 烘焙完成，共保存 {savedMeshes.Count} 个 Mesh Asset 到 {relativeDir}");
    }

    private void LinkSelectedObjectsToLattice()
    {
        // 收集选中的非晶格对象的所有 Renderer
        var allRenderers = new List<Renderer>();
        foreach (var obj in Selection.gameObjects)
        {
            if (obj == lattice.gameObject) continue;

            Renderer rend = obj.GetComponent<Renderer>();
            if (rend != null && !allRenderers.Contains(rend))
                allRenderers.Add(rend);

            var childRenderers = obj.GetComponentsInChildren<Renderer>(true);
            foreach (var cr in childRenderers)
            {
                if (!allRenderers.Contains(cr))
                    allRenderers.Add(cr);
            }
        }

        if (allRenderers.Count == 0)
        {
            EditorUtility.DisplayDialog("提示",
                "请先在 Hierarchy 中选中要链接的模型对象（可 Ctrl+点击多选），然后再点此按钮。", "确定");
            return;
        }

        Undo.RecordObject(lattice, "链接对象到晶格");
        int linked = lattice.LinkRenderers(allRenderers);

        if (linked > 0)
        {
            EditorUtility.SetDirty(lattice);
            SceneView.RepaintAll();
            Debug.Log($"[LatticeModifier] 已链接 {linked} 个 Renderer 到晶格");
        }
        else
        {
            EditorUtility.DisplayDialog("提示",
                "选中的对象已全部链接或没有有效 Renderer", "确定");
        }

        // 链接后重新选中晶格体
        Selection.activeGameObject = lattice.gameObject;
    }

    private void CreateStandaloneLattice()
    {
        Transform targetRt = lattice.targetRoot;
        int dx = lattice.divisionsX, dy = lattice.divisionsY, dz = lattice.divisionsZ;
        bool live = lattice.liveUpdate;
        var manualRends = new List<Renderer>(lattice.manualRenderers);

        Transform refT = targetRt;

        // 如果 targetRoot 为空，从 manualRenderers 推断
        if (refT == null)
        {
            if (manualRends.Count > 0 && manualRends[0] != null)
                refT = manualRends[0].transform;
        }

        // 确定多目标根节点：使用选中对象自身（含所有子 Renderer）
        Transform autoRoot = refT;
        if (autoRoot != null && autoRoot.parent != null)
            autoRoot = autoRoot.parent;

        string refName = refT != null ? refT.name : "Unknown";

        Undo.DestroyObjectImmediate(lattice);

        GameObject latticeObj = new GameObject("Lattice_" + refName);
        Undo.RegisterCreatedObjectUndo(latticeObj, "创建独立晶格");

        if (refT != null)
        {
            latticeObj.transform.position = refT.position;
            latticeObj.transform.rotation = refT.rotation;
        }

        LatticeModifier newLattice = latticeObj.AddComponent<LatticeModifier>();
        newLattice.targetRoot = autoRoot;
        newLattice.manualRenderers = manualRends;
        newLattice.divisionsX = dx;
        newLattice.divisionsY = dy;
        newLattice.divisionsZ = dz;
        newLattice.liveUpdate = live;

        newLattice.InitializeLattice();
        EditorUtility.SetDirty(newLattice);

        Selection.activeGameObject = latticeObj;
        SceneView.RepaintAll();
    }
}

// ═══════════════════════════════════════════
//  打包前自动还原：扫描所有加载场景中所有 LatticeModifier，
//  把所有 Renderer 的 sharedMesh 引用还原回 originalMesh 资产，
//  保证 Build 场景中 Renderer 引用指向带 Asset GUID 的资产，模型可见。
//  晶格组件本身保留在场景里，进入 Play 模式后 OnEnable 会重建 deform Mesh 继续实时变形。
//  同样的还原也在保存场景时触发（避免 Build 场景里 Renderer 引用指向无 GUID 的运行时 Mesh）。
//  本类不再抛任何异常、不会拦截打包——所有 LatticeModifier 状态都直接放行。
// ═══════════════════════════════════════════

/// 遍历所有加载场景中已初始化的 LatticeModifier 并执行 action。
/// 之前 LatticeModifierBuildPreprocessor / LatticeModifierSaveHook 各写一份"遍历场景"的循环，
/// 抽到这里。
internal static class LatticeSceneWalker
{
    public static int ForEachInitialized(System.Action<LatticeModifier> action)
    {
        if (action == null) return 0;
        int n = 0;
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var scn = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (!scn.isLoaded) continue;
            foreach (var root in scn.GetRootGameObjects())
            {
                foreach (var lm in root.GetComponentsInChildren<LatticeModifier>(true))
                {
                    if (lm == null || !lm.IsInitialized) continue;
                    action(lm);
                    n++;
                }
            }
        }
        return n;
    }

    /// 按命名规则 Lattice_ + 模型名 在所有已加载场景中查找晶格对象。
    /// 匹配范围：自身名 + 父链名（覆盖多目标模式父节点命名）。
    /// v3.27：名称匹配后增加渲染器目标验证，排除同名但无关联的晶格；
    ///        若名称匹配无有效结果，则降级为全场景渲染器目标扫描兜底。
    public static List<GameObject> FindLatticesByName(GameObject go)
    {
        var result = new List<GameObject>();
        if (go == null) return result;

        // 收集待匹配名称：自身 + 父链
        var names = new List<string> { go.name };
        var t = go.transform.parent;
        while (t != null) { names.Add(t.name); t = t.parent; }

        // 第 1 步：名称匹配（快速筛选）
        var nameMatches = new List<GameObject>();
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var scn = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (!scn.isLoaded) continue;
            foreach (var root in scn.GetRootGameObjects())
            {
                foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!tr.name.StartsWith("Lattice_", System.StringComparison.Ordinal)) continue;
                    var baseName = tr.name.Substring("Lattice_".Length);
                    foreach (var n in names)
                    {
                        if (n == baseName && !nameMatches.Contains(tr.gameObject))
                        {
                            nameMatches.Add(tr.gameObject);
                            break;
                        }
                    }
                }
            }
        }

        // 第 2 步：渲染器目标验证（过滤同名但无关联的晶格）
        foreach (var latGo in nameMatches)
        {
            var lm = latGo.GetComponent<LatticeModifier>();
            if (lm != null && IsLatticeTargetingGameObject(lm, go))
                result.Add(latGo);
        }

        // 第 3 步：名称匹配无有效结果时，降级为全场景渲染器目标扫描兜底
        // （例如晶格命名不遵循 Lattice_ 规范但仍引用了该模型）
        if (result.Count == 0 && nameMatches.Count > 0) return result; // 有名称匹配但不关联，不兜底
        if (result.Count == 0)
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scn = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scn.isLoaded) continue;
                foreach (var root in scn.GetRootGameObjects())
                {
                    foreach (var lm in root.GetComponentsInChildren<LatticeModifier>(true))
                    {
                        if (!result.Contains(lm.gameObject) && IsLatticeTargetingGameObject(lm, go))
                            result.Add(lm.gameObject);
                    }
                }
            }
        }

        return result;
    }

    /// 验证晶格是否实际引用了给定 GameObject 的 Renderer。
    /// 检查层级：targetRoot 包含关系 → manualRenderers 包含关系 → deformTargets 中的 renderer。
    private static bool IsLatticeTargetingGameObject(LatticeModifier lattice, GameObject go)
    {
        if (lattice == null || go == null) return false;

        // 1. targetRoot：选中对象是否在目标根节点层级下（含自身）
        if (lattice.targetRoot != null)
        {
            var t = go.transform;
            while (t != null)
            {
                if (t == lattice.targetRoot) return true;
                t = t.parent;
            }
            // 也检查 targetRoot 的下级是否包含 go（go 可能是根节点的直接子节点/后代）
        }

        // 2. manualRenderers：是否包含选中对象自身或其子节点的 Renderer
        foreach (var rend in lattice.manualRenderers)
        {
            if (rend == null) continue;
            if (rend.gameObject == go) return true;
            if (rend.transform.IsChildOf(go.transform))
                return true;
        }

        // 3. deformTargets 中的 renderer（包含初始化时从 targetRoot 自动收集的）
        var activeRenderers = lattice.GetActiveRenderers();
        foreach (var rend in activeRenderers)
        {
            if (rend == null) continue;
            if (rend.gameObject == go) return true;
            if (rend.transform.IsChildOf(go.transform))
                return true;
        }

        // 4. 选中的 go 若带有 Renderer 组件，检查其父 GameObject 是否在 targetRoot 下
        //    （多目标模式中，父节点为 targetRoot，子节点为各模型）
        var goRenderer = go.GetComponent<Renderer>();
        if (goRenderer != null)
        {
            // 检查 lattice 的 active renderers 中是否有 renderer 在 go 层级下
            foreach (var rend in activeRenderers)
            {
                if (rend == null) continue;
                if (go.transform.IsChildOf(rend.transform))
                    return true;
            }
        }

        return false;
    }
}

// 打包前还原（Build 时触发）
internal class LatticeModifierBuildPreprocessor : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        try
        {
            int count = LatticeSceneWalker.ForEachInitialized(lm =>
            {
                // v3.10：先迁移修复（清理污染的 originalMesh + 重新缓存可序列化拓扑），
                // 再还原 Renderer 引用，避免把污染的 deform Mesh 写回 Renderer。
                lm.MigrateAndRepair();

                // 不走 Undo 栈：避免用户在 Build 前后按 Ctrl+Z 把引用撤销回 deformedMeshA
                // （运行时 Mesh，无 Asset GUID，打包后不可见）。
                // 只标脏，让 Build 序列化的就是 originalMesh 资产引用。
                EditorUtility.SetDirty(lm);
                lm.RestoreRenderersToOriginal();

                // v3.9 关键修复：还原的是 Renderer/MeshFilter 上的 sharedMesh 引用，
                // 必须对它们也标脏，否则 Unity 场景序列化可能仍然写出旧引用（deformedMeshA）。
                foreach (var rend in lm.GetActiveRenderers())
                {
                    if (rend == null) continue;
                    EditorUtility.SetDirty(rend);
                    var mf = rend.GetComponent<MeshFilter>();
                    if (mf != null) EditorUtility.SetDirty(mf);
                }
            });
            if (count > 0)
            {
                // 把所有修改过的 Renderer 标记为脏，触发场景重新序列化
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
                Debug.Log($"[LatticeModifier] 打包前已自动还原 {count} 个 Renderer 的 sharedMesh 引用回原 Mesh 资产。");

                // 关键：把内存里的修改强制写入磁盘。
                // 否则 Unity Build 序列化的可能是"磁盘上的旧 .unity + 内存 delta"，
                // 而磁盘上的 .unity 仍然是 deformedMeshA 引用（用户从未 Ctrl+S），
                // 导致 Build 场景里 Renderer 引用还是 deformedMeshA（无 GUID）→ 玩家端不可见。
                // 跳过 untitled / 新建未保存的路径（会弹"另存为"对话框，破坏 Build 流程）。
                // prefab 也跳过：prefab 不能在 build 流程中保存，依赖 OnWillSaveAssets 已经处理。
                int saved = 0;
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                {
                    var scn = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    if (!scn.isLoaded || !scn.isDirty) continue;
                    if (string.IsNullOrEmpty(scn.path)) continue;       // untitled 场景
                    if (scn.path.EndsWith(".prefab")) continue;        // prefab 跳过
                    if (UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scn))
                        saved++;
                }
                if (saved > 0)
                    Debug.Log($"[LatticeModifier] 打包前已强制保存 {saved} 个场景到磁盘，确保 Build 序列化时拿到 originalMesh 引用。");
            }
        }
        catch (System.Exception ex)
        {
            // 任何意外都不应阻断打包流程
            Debug.LogWarning($"[LatticeModifier] 打包前还原 Renderer 引用时出现异常，已忽略: {ex.Message}");
        }
    }
}

// 保存场景或 Prefab 时还原（避免 Build 场景 / Prefab 中 Renderer 引用指向无 GUID 的运行时 Mesh）
internal class LatticeModifierSaveHook : UnityEditor.AssetModificationProcessor
{
    private static string[] OnWillSaveAssets(string[] paths)
    {
        // 介入条件：保存的是场景（.unity）或 Prefab Asset（.prefab）
        // 关键：晶格组件如果挂在 Prefab 里，保存 Prefab Asset 时也要还原
        // 否则 Build 阶段序列化场景时，Prefab 实例的 Renderer 引用会同步为
        // Prefab Asset 里保存的 deformedMeshA（运行时 Mesh，无 GUID）→ 玩家端不可见
        bool anyRelevant = false;
        foreach (var p in paths)
        {
            if (p.EndsWith(".unity") || p.EndsWith(".prefab")) { anyRelevant = true; break; }
        }
        if (!anyRelevant) return paths;

        try
        {
            int n = LatticeSceneWalker.ForEachInitialized(lm =>
            {
                // v3.10：保存前先迁移修复，把可序列化拓扑 + 干净的 originalMesh 写入场景
                lm.MigrateAndRepair();

                // v3.9 修复：不仅记录 LatticeModifier，还需对 Renderer/MeshFilter 标脏，
                // 否则 Unity 保存时仍然序列化旧的 sharedMesh 引用（deformedMeshA）。
                Undo.RecordObject(lm, "保存前还原晶格 Renderer 引用");
                lm.RestoreRenderersToOriginal();
                foreach (var rend in lm.GetActiveRenderers())
                {
                    if (rend == null) continue;
                    EditorUtility.SetDirty(rend);
                    var mf = rend.GetComponent<MeshFilter>();
                    if (mf != null) EditorUtility.SetDirty(mf);
                }
            });
            if (n > 0)
            {
                Debug.Log($"[LatticeModifier] 保存场景/Prefab 时已自动还原 {n} 个 Renderer 的 sharedMesh 引用回原 Mesh 资产。");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[LatticeModifier] 保存场景/Prefab 时还原 Renderer 引用出现异常，已忽略: {ex.Message}");
        }
        return paths;
    }
}

// ═══════════════════════════════════════════
//  LatticeModifierInitOnLoad —— Editor 启动 / 域重载后自动执行
//
// 职责 A：启动时扫描所有已加载场景中的 LatticeModifier，把 Renderer 引用
//         统一还原回 originalMesh 资产 —— 避免"上次编辑时挂在 deformedMeshA"、
//         "打开工程后立刻 Build 漏过 OnPreprocessBuild"等场景下打包后模型不可见。
//
// 职责 B：清理 v3.7 之前已删除的旧字段（initLatticeLocalToWorld / initLatticeWorldToLocal）。
//         Unity 反序列化时遇到序列化数据中存在但当前类已删除的字段会输出警告，
//         且这些数据会持续占用场景文件大小。用 SerializedObject 找到并清除。
//
// 触发时机：[InitializeOnLoad] 静态构造 → EditorApplication.delayCall 延后一帧
//           （避免 InitializeOnLoad 静态构造时场景还没完全加载完，
//            同时和 IPrefabStage 等其他 Editor 启动逻辑错开）。
//
// 防止重复：SessionState 标记本次 Editor 会话已执行过；域重载后会重置 SessionState，
//           域重载后再跑一次（覆盖上次崩溃前未保存的修改）。
// ═══════════════════════════════════════════
[InitializeOnLoad]
internal static class LatticeModifierInitOnLoad
{
    // SessionState key：本次 Editor 会话是否已执行过自动修复
    private const string SessionKey_RanOnce = "VicTools.LatticeModifier.InitOnLoad.RanOnce";

    static LatticeModifierInitOnLoad()
    {
        // 主动触发 LatticeModifierEditor 的静态构造函数，注册 SceneView Esc 钩子
        // 解决"用户从未点过晶格对象 → 类未被加载 → Esc 切换不生效"问题
        LatticeModifierEditor.EnsureHookRegistered();

        // 延后到下一帧执行：此时场景已加载、AssetDatabase 已就绪、IPrefabStage 不会冲突
        EditorApplication.delayCall += RunOnce;
    }

    private static void RunOnce()
    {
        // 防止 Editor 多次调用 delayCall 时重复执行
        if (SessionState.GetBool(SessionKey_RanOnce, false))
        {
            // 域重载会重置 SessionState，所以这里命中"已执行"意味着确实是本次会话重复调用
            return;
        }
        SessionState.SetBool(SessionKey_RanOnce, true);

        try
        {
            int restoredRenderers = FixRendererReferences();
            int cleanedFields = CleanObsoleteFields();

            if (restoredRenderers > 0 || cleanedFields > 0)
            {
                Debug.Log(
                    $"[LatticeModifier] 启动时自动维护完成：" +
                    $"还原 Renderer 引用 {restoredRenderers} 个，清理旧字段 {cleanedFields} 处。" +
                    (restoredRenderers > 0 ? " 建议按 Ctrl+S 保存场景。" : ""));
            }
        }
        catch (System.Exception ex)
        {
            // 任何意外都不应阻塞 Editor 启动
            Debug.LogWarning($"[LatticeModifier] 启动时自动维护出现异常，已忽略: {ex.Message}");
        }
    }

    /// 职责 A：扫描所有已加载场景中已初始化的晶格，把 Renderer.sharedMesh 还原为 originalMesh。
    /// 复用 LatticeSceneWalker.ForEachInitialized，逻辑和 OnPreprocessBuild / OnWillSaveAssets 一致。
    private static int FixRendererReferences()
    {
        int n = LatticeSceneWalker.ForEachInitialized(lm =>
        {
            // v3.10：启动时先迁移修复旧晶格（清理污染 originalMesh + 重新缓存可序列化拓扑）
            lm.MigrateAndRepair();

            // 启动时不走 Undo 栈（和 OnPreprocessBuild 一致）：自动维护动作不应被 Ctrl+Z 撤销
            EditorUtility.SetDirty(lm);
            lm.RestoreRenderersToOriginal();
            // v3.9：对 Renderer/MeshFilter 也标脏，确保场景序列化拿到还原后的引用
            foreach (var rend in lm.GetActiveRenderers())
            {
                if (rend == null) continue;
                EditorUtility.SetDirty(rend);
                var mf = rend.GetComponent<MeshFilter>();
                if (mf != null) EditorUtility.SetDirty(mf);
            }
        });
        return n;
    }

    /// 职责 B：检测并诊断 v3.7 之前旧版本残留 + 异常状态。
    ///
    /// 重要限制：SerializedObject.FindProperty 只能找到"当前类声明的字段"，
    /// 对于"已删除的字段"（如 initLatticeLocalToWorld）它返回 null —— Unity 不会
    /// 通过 SerializedObject API 提供"清除 yaml 里残留但类里没声明的字段"的能力。
    /// 那些残留数据会被 Unity 反序列化时静默忽略并产生 warning（"field X not found"），
    /// 占据场景文件少量字节，不会导致运行错误。
    ///
    /// 因此职责 B 的实际作用是：
    /// 1. 扫描每个晶格，检测其"实际状态"是否健康（已初始化、Renderer 引用正常、控制点数组长度匹配）
    /// 2. 对不健康的晶格打 LogWarning，提示用户在哪个场景的哪个物体需要手动检查
    /// 3. 累计统计，作为 Editor 启动诊断报告的一部分
    private static int CleanObsoleteFields()
    {
        int issueCount = 0;
        var issues = new System.Text.StringBuilder();

        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var scn = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (!scn.isLoaded) continue;

            foreach (var root in scn.GetRootGameObjects())
            {
                var lattices = root.GetComponentsInChildren<LatticeModifier>(true);
                foreach (var lm in lattices)
                {
                    if (lm == null) continue;
                    var issues2 = DiagnoseInstance(lm, scn.name);
                    if (issues2 != null)
                    {
                        issues.AppendLine(issues2);
                        issueCount++;
                    }
                }
            }
        }

        if (issueCount > 0)
        {
            Debug.LogWarning(
                "[LatticeModifier] 启动诊断：检测到以下晶格存在潜在问题，建议在 Inspector 中检查：\n" +
                issues.ToString() +
                "\n（如果模型在 Editor 中正常显示但打包后不显示，请参考 README 中的 v3.8 修复说明。）");
        }

        return issueCount;
    }

    /// 对单个 LatticeModifier 实例做健康检查。返回 null 表示健康；否则返回问题描述文本。
    /// 检查项：
    /// 1. controlPoints 数组长度是否匹配当前 divisions（段数修改后未重新初始化）
    /// 2. deformTargets 里的 renderer 引用是否还有效
    /// 3. 任何 dt 的 originalVertices 是否为空（说明源 Mesh 不可读且 Instantiate/BakeMesh 都失败）
    /// 4. 任何 dt 的 originalMesh 是否指向 _LatticeDeform_ 后缀的运行时 Mesh（说明拍快照时机错误）
    private static string DiagnoseInstance(LatticeModifier lm, string sceneName)
    {
        if (lm == null) return null;
        var problems = new System.Text.StringBuilder();

        try
        {
            // 1. 段数修改后未重新初始化
            if (lm.IsInitialized && lm.controlPoints != null)
            {
                int expected = lm.PointCountX * lm.PointCountY * lm.PointCountZ;
                if (lm.controlPoints.Length != expected)
                {
                    problems.AppendLine(
                        $"  - 场景 [{sceneName}] 中 '{lm.name}' 的 controlPoints 长度 {lm.controlPoints.Length} " +
                        $"与段数配置 {lm.divisionsX}×{lm.divisionsY}×{lm.divisionsZ} (期望 {expected}) 不匹配。" +
                        $"建议：在 Inspector 中点击「重新初始化」按钮。");
                }
            }

            // 通过反射读取 deformTargets（private 字段），不需要为了诊断破坏封装
            var dtField = typeof(LatticeModifier).GetField("deformTargets",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (dtField != null)
            {
                var dts = dtField.GetValue(lm) as System.Collections.IList;
                if (dts != null)
                {
                    for (int i = 0; i < dts.Count; i++)
                    {
                        var dt = dts[i];
                        if (dt == null) continue;

                        // 反射读 renderer / originalMesh / originalVertices
                        var rendField = dt.GetType().GetField("renderer");
                        var origMeshField = dt.GetType().GetField("originalMesh");
                        var origVertsField = dt.GetType().GetField("originalVertices");

                        var rend = rendField?.GetValue(dt) as Renderer;
                        var origMesh = origMeshField?.GetValue(dt) as Mesh;
                        var origVerts = origVertsField?.GetValue(dt) as Vector3[];

                        string targetName = rend != null ? rend.name : "<null>";

                        // 2. renderer 引用丢失
                        if (rend == null)
                        {
                            problems.AppendLine(
                                $"  - 场景 [{sceneName}] 中 '{lm.name}' 的变形目标 #{i} Renderer 引用已丢失（目标对象被删除？）。");
                            continue;
                        }

                        // 3. originalVertices 为空
                        if (origVerts == null || origVerts.Length == 0)
                        {
                            problems.AppendLine(
                                $"  - 场景 [{sceneName}] 中 '{lm.name}' → '{targetName}' 的 originalVertices 为空。" +
                                $"说明源 Mesh 不可读且 Instantiate/BakeMesh 都失败。建议：在 FBX 导入设置中勾选 Read/Write Enabled。");
                        }

                        // 4. originalMesh 指向运行时 deform Mesh（拍快照时机错误）
                        if (origMesh != null && origMesh.name.Contains("_LatticeDeform_"))
                        {
                            problems.AppendLine(
                                $"  - 场景 [{sceneName}] 中 '{lm.name}' → '{targetName}' 的 originalMesh 引用指向运行时变形 Mesh。" +
                                $"说明原始资产引用丢失。建议：选中晶格 → 点击「重新初始化」按钮。");
                        }
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            problems.AppendLine($"  - 场景 [{sceneName}] 中 '{lm.name}' 诊断时异常: {ex.Message}");
        }

        return problems.Length > 0 ? problems.ToString() : null;
    }
}
