// LatticeModifierEditor 1.0 晶格变形器编辑器，Inspector 面板与 SceneView 控制点交互
// LatticeModifierEditor 1.1 支持晶格单独移动，模型经过晶格区域产生变形，离开后恢复
// LatticeModifierEditor 1.2 添加晶格点动画控制（子物体 CP 节点，支持 Animation/Timeline K帧）
// LatticeModifierEditor 1.3 选中晶格点时同步选中 Hierarchy 中对应 CP 节点（蓝色高亮，不含父对象）
// LatticeModifierEditor 1.4 静态 SceneView 回调：选中 CP 后晶格线框持续绘制，可继续点击/框选其他晶格点
// LatticeModifierEditor 1.5 不重算法线，保持原始 mesh 的法线数据，变形只改顶点位置
// LatticeModifierEditor 2.0 支持单目标/多目标（整个预设/带蒙皮角色）两种模式，SceneView 绘制逻辑合并
// LatticeModifierEditor 2.1 添加删除晶格按钮（还原 Mesh 并删除晶格物体），单目标模式自动识别带骨骼角色父级切换多目标
// LatticeModifierEditor 2.2 添加目标按钮支持多选，显示手动 Renderer 列表字段，运行/停止游戏自动重建晶格，移除每帧 RepaintAll 优化性能
// LatticeModifierEditor 2.3 支持缩放旋转等工具手柄操作控制点；优化缩放旋转工具操作
// LatticeModifierEditor 2.4 3D视图选中同步：注册 Selection.selectionChanged，选中 CP 节点时遍历控制点找到对应索引，写入 selectedPoints 并触发 SceneView.RepaintAll()，Scene 视图里对应控制点会高亮显示。
// LatticeModifierEditor 2.5 设置选中控制点显示大小。
// LatticeModifierEditor 2.6 选中晶格点焦点落在晶格体，添加【创建快照】按钮
//      Shift + 拖拽：框选添加控制点
//      Shift + Alt + 拖拽：框选减去控制点
//      Shift + Ctrl + 拖拽：框选追加（不清除已有选择）。
// LatticeModifierEditor 2.7 优化控制点显示 // 内部控制点：蓝色
// LatticeModifierEditor 2.8 晶格体背面控制点压暗显示
// LatticeModifierEditor 2.9 优化晶格体背面控制点压暗显示（支持透视/正交）；优化控制点显示顺序
// LatticeModifierEditor 3.0 配合 Runtime v3.0 重构验证通过，公共 API 完全兼容无需修改
// LatticeModifierEditor 3.1 SingleRenderer模式【修复晶格链接】按钮（无需选中对象直接修复）；轴心操作同步initLattice矩阵；晶格点背面压暗改为基于控制点实际位置计算面法线；Undo记录子CP Transform防止撤销失效
// LatticeModifierEditor 3.1 多目标模式新增"修复丢失绑定"按钮，自动检测并重新链接列表中未绑定到晶格的 Renderer
// LatticeModifierEditor 3.2 Inspector 暴露边缘羽化参数（feather），实时调整晶格边界变形衰减；添加晶格轴心设置；Esc 取消选择晶格点。

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(LatticeModifier))]
public class LatticeModifierEditor : Editor
{
    private LatticeModifier lattice;
    private HashSet<int> selectedPoints = new HashSet<int>();

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

    private void OnEnable()
    {
        lattice = (LatticeModifier)target;
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
        // ── 模式切换 ──
        serializedObject.Update();
        EditorGUI.BeginChangeCheck();
        var modeProp = serializedObject.FindProperty("targetMode");
        EditorGUILayout.PropertyField(modeProp, new GUIContent("目标模式"));
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
        }

        bool isSingle = lattice.targetMode == LatticeModifier.TargetMode.SingleRenderer;

        // ── 根据模式显示对应字段 ──
        if (isSingle)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetRenderer"), new GUIContent("目标对象"));
        }
        else
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetRoot"), new GUIContent("多目标根节点"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("manualRenderers"), new GUIContent("手动指定 Renderer"), true);
        }

        // ── 添加目标按钮（支持多选） ──
        // GUI.backgroundColor = new Color(0.5f, 0.9f, 1f);
        // if (GUILayout.Button("添加目标（选中对象后点此按钮，支持多选）", GUILayout.Height(26)))
        // {
        //     // 收集所有选中的非晶格对象
        //     var selectedObjects = new List<GameObject>();
        //     foreach (var obj in Selection.gameObjects)
        //     {
        //         if (obj != lattice.gameObject)
        //             selectedObjects.Add(obj);
        //     }

        //     if (selectedObjects.Count == 0)
        //     {
        //         EditorUtility.DisplayDialog("提示",
        //             "请先在 Hierarchy 中选中目标对象（可 Ctrl+点击多选），或直接将对象拖入上方字段", "确定");
        //     }
        //     else
        //     {
        //         Undo.RecordObject(lattice, "设置目标");

        //         // 收集所有选中对象的 Renderer（含子物体）
        //         var allRenderers = new List<Renderer>();
        //         foreach (var sel in selectedObjects)
        //         {
        //             Renderer rend = sel.GetComponent<Renderer>();
        //             if (rend != null) allRenderers.Add(rend);
        //             var childRenderers = sel.GetComponentsInChildren<Renderer>(true);
        //             foreach (var cr in childRenderers)
        //             {
        //                 if (!allRenderers.Contains(cr))
        //                     allRenderers.Add(cr);
        //             }
        //         }

        //         if (allRenderers.Count == 0)
        //         {
        //             EditorUtility.DisplayDialog("提示", "选中的对象及其子物体都没有 Renderer 组件", "确定");
        //         }
        //         else if (allRenderers.Count == 1 && isSingle)
        //         {
        //             // 单个 Renderer，单目标模式
        //             lattice.targetRenderer = allRenderers[0];
        //         }
        //         else
        //         {
        //             // 多个 Renderer 或已是多目标模式 → 切换多目标，添加到手动列表
        //             lattice.targetMode = LatticeModifier.TargetMode.MultiRenderer;
        //             // 合并到 manualRenderers（不重复）
        //             foreach (var r in allRenderers)
        //             {
        //                 if (!lattice.manualRenderers.Contains(r))
        //                     lattice.manualRenderers.Add(r);
        //             }
        //         }

        //         serializedObject.Update();
        //         EditorUtility.SetDirty(lattice);
        //     }
        // }
        // GUI.backgroundColor = Color.white;

        // 模式可能在按钮中被自动切换，重新读取
        isSingle = lattice.targetMode == LatticeModifier.TargetMode.SingleRenderer;

        EditorGUILayout.PropertyField(serializedObject.FindProperty("divisionsX"), new GUIContent("X 段数"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("divisionsY"), new GUIContent("Y 段数"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("divisionsZ"), new GUIContent("Z 段数"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("feather"), new GUIContent("边缘羽化", "控制晶格边界的变形衰减带宽度。0 = 无羽化（硬切），0.5 = 最大羽化（整个范围平滑过渡）"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("liveUpdate"), new GUIContent("实时更新"));
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10);

        if (!lattice.IsInitialized)
        {
            if (isSingle && lattice.targetRenderer == null)
                EditorGUILayout.HelpBox("请将要变形的模型拖入「目标对象」字段", MessageType.Warning);
            else if (!isSingle && lattice.targetRoot == null)
                EditorGUILayout.HelpBox("请将要变形的根节点拖入「多目标根节点」（会自动收集所有子 Renderer）", MessageType.Warning);
            else
            {
                Transform checkT = isSingle ? lattice.targetRenderer?.transform : lattice.targetRoot;
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
            DrawInitializedUI(isSingle);
        }
    }

    private void DrawInitializedUI(bool isSingle)
    {
        string info;
        if (isSingle)
        {
            info = $"晶格：{lattice.PointCountX}×{lattice.PointCountY}×{lattice.PointCountZ} = {lattice.TotalPoints} 个控制点\n" +
                   "点击选中 | Ctrl+点击加选 | Shift+拖拽框选 | 拖拽手柄变形";
        }
        else
        {
            var renderers = lattice.GetActiveRenderers();
            info = $"晶格：{lattice.PointCountX}×{lattice.PointCountY}×{lattice.PointCountZ} = {lattice.TotalPoints} 个控制点\n" +
                   $"多目标模式：共 {renderers.Count} 个 Renderer\n" +
                   "点击选中 | Ctrl+点击加选 | Shift+拖拽框选 | 拖拽手柄变形";
        }
        EditorGUILayout.HelpBox(info, MessageType.Info);

        if (!isSingle)
        {
            var renderers = lattice.GetActiveRenderers();
            if (renderers.Count > 0)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("目标 Renderer 列表：", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                for (int i = 0; i < renderers.Count; i++)
                {
                    string typeName = renderers[i] is SkinnedMeshRenderer ? "[Skinned]" : "[Mesh]";
                    EditorGUILayout.LabelField($"{i + 1}. {typeName} {renderers[i].name}", EditorStyles.miniLabel);
                }
                EditorGUI.indentLevel--;
            }

            // ── 丢失绑定修复按钮 ──
            // 检测是否有丢失绑定（manualRenderers 或 targetRoot 子对象中未在 deformTargets 中绑定的）
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
            if (isSingle)
            {
                if (GUILayout.Button(new GUIContent("修复晶格链接",
                    "重新将「目标对象」字段中的 Renderer 链接到当前晶格。\n\n" +
                    "适用场景：\n" +
                    "• Prefab 实例化后绑定丢失\n" +
                    "• 撤销/重做导致目标断开连接\n" +
                    "• 更换了目标对象后同步绑定"),
                    GUILayout.Height(24)))
                {
                    RepairSingleRendererLink();
                }
            }
            else
            {
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
        EditorGUILayout.LabelField("轴心设置", EditorStyles.boldLabel);
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

        // 更新初始化矩阵（因为控制点和包围盒已经重新映射到新坐标系）
        var initL2WField = typeof(LatticeModifier).GetField("initLatticeLocalToWorld",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var initW2LField = typeof(LatticeModifier).GetField("initLatticeWorldToLocal",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (initL2WField != null && initW2LField != null)
        {
            initL2WField.SetValue(lattice, t.localToWorldMatrix);
            initW2LField.SetValue(lattice, t.worldToLocalMatrix);
        }

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

        // 更新初始化矩阵（因为控制点和包围盒已经重新映射到新坐标系）
        var initL2WField = typeof(LatticeModifier).GetField("initLatticeLocalToWorld",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var initW2LField = typeof(LatticeModifier).GetField("initLatticeWorldToLocal",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (initL2WField != null && initW2LField != null)
        {
            initL2WField.SetValue(lattice, t.localToWorldMatrix);
            initW2LField.SetValue(lattice, t.worldToLocalMatrix);
        }

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

        // 更新初始化矩阵（因为控制点和包围盒已经重新映射到新坐标系）
        var initL2WField = typeof(LatticeModifier).GetField("initLatticeLocalToWorld",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var initW2LField = typeof(LatticeModifier).GetField("initLatticeWorldToLocal",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (initL2WField != null && initW2LField != null)
        {
            initL2WField.SetValue(lattice, t.localToWorldMatrix);
            initW2LField.SetValue(lattice, t.worldToLocalMatrix);
        }

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
        if (lattice == null || !lattice.IsInitialized || selectedPoints.Count == 0) return;

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

        selectedPoints = expanded;
        s_activeSelectedPoints = selectedPoints;
        SyncSelectionToHierarchy();
    }

    // ═══════════════════════════════════════════
    //  SceneView 绘制 & 交互
    // ═══════════════════════════════════════════

    /// <summary>
    /// 根据控制点实际位置计算指定面的法线方向。
    /// faceAxis 指明面的朝向轴：(-1,0,0)=X轴负方向面，(1,0,0)=X轴正方向面 等。
    /// 通过该点与同面相邻控制点的叉积得到实际法线，不依赖晶格 Transform 轴向。
    /// </summary>
    private static Vector3 ComputeFaceNormal(LatticeModifier lat, Transform t, int pix, int piy, int piz, int nx, int ny, int nz, int faceAxisX, int faceAxisY, int faceAxisZ)
    {
        Vector3 center = t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(pix, piy, piz)]);

        // 确定面上两个切线方向的邻居索引
        int t1ix = pix, t1iy = piy, t1iz = piz;
        int t2ix = pix, t2iy = piy, t2iz = piz;

        if (faceAxisX != 0)
        {
            // YZ 面：沿 Y 和 Z 方向取邻居
            t1iy = Mathf.Clamp(piy + 1, 0, ny - 1);
            if (t1iy == piy) t1iy = Mathf.Clamp(piy - 1, 0, ny - 1);
            t2iz = Mathf.Clamp(piz + 1, 0, nz - 1);
            if (t2iz == piz) t2iz = Mathf.Clamp(piz - 1, 0, nz - 1);
        }
        else if (faceAxisY != 0)
        {
            // XZ 面：沿 X 和 Z 方向取邻居
            t1ix = Mathf.Clamp(pix + 1, 0, nx - 1);
            if (t1ix == pix) t1ix = Mathf.Clamp(pix - 1, 0, nx - 1);
            t2iz = Mathf.Clamp(piz + 1, 0, nz - 1);
            if (t2iz == piz) t2iz = Mathf.Clamp(piz - 1, 0, nz - 1);
        }
        else
        {
            // XY 面：沿 X 和 Y 方向取邻居
            t1ix = Mathf.Clamp(pix + 1, 0, nx - 1);
            if (t1ix == pix) t1ix = Mathf.Clamp(pix - 1, 0, nx - 1);
            t2iy = Mathf.Clamp(piy + 1, 0, ny - 1);
            if (t2iy == piy) t2iy = Mathf.Clamp(piy - 1, 0, ny - 1);
        }

        Vector3 neighbor1 = t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(t1ix, t1iy, t1iz)]);
        Vector3 neighbor2 = t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(t2ix, t2iy, t2iz)]);

        Vector3 edge1 = neighbor1 - center;
        Vector3 edge2 = neighbor2 - center;
        Vector3 normal = Vector3.Cross(edge1, edge2).normalized;

        // 确保法线朝向面的外侧：用 faceAxis 的预期方向做参考
        // 取初始控制点的中心作为内部参考点
        Vector3 latticeCenter = Vector3.zero;
        for (int i = 0; i < lat.controlPoints.Length; i++)
            latticeCenter += lat.controlPoints[i];
        latticeCenter = t.TransformPoint(latticeCenter / lat.controlPoints.Length);

        Vector3 outward = center - latticeCenter;
        if (Vector3.Dot(normal, outward) < 0)
            normal = -normal;

        return normal;
    }

    private static void DrawLatticeAndHandles(LatticeModifier lat, HashSet<int> selPts, SceneView sceneView, bool isInstance)
    {
        if (lat == null || !lat.IsInitialized || lat.controlPoints == null) return;

        // 段数被修改后 controlPoints 数组长度与当前 PointCount 不匹配，跳过绘制避免越界
        int expectedTotal = lat.PointCountX * lat.PointCountY * lat.PointCountZ;
        if (lat.controlPoints.Length != expectedTotal) return;

        // 有控制点被选中时隐藏 Unity 内置 Transform Handle，避免出现两个移动工具
        Tools.hidden = selPts != null && selPts.Count > 0;

        Event e = Event.current;
        Transform t = lat.transform;
        int nx = lat.PointCountX, ny = lat.PointCountY, nz = lat.PointCountZ;

        Handles.color = new Color(0.2f, 0.8f, 1f, 0.5f);
        for (int ix = 0; ix < nx; ix++)
        for (int iy = 0; iy < ny; iy++)
        for (int iz = 0; iz < nz; iz++)
        {
            int idx = lat.GetFlatIndex(ix, iy, iz);
            Vector3 p = t.TransformPoint(lat.controlPoints[idx]);
            if (ix < nx - 1) Handles.DrawLine(p, t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(ix + 1, iy, iz)]));
            if (iy < ny - 1) Handles.DrawLine(p, t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(ix, iy + 1, iz)]));
            if (iz < nz - 1) Handles.DrawLine(p, t.TransformPoint(lat.controlPoints[lat.GetFlatIndex(ix, iy, iz + 1)]));
        }

        // 相机信息，用于判断控制点是否在晶格体背面（支持透视/正交）
        Camera cam = sceneView.camera;
        Vector3 camPos = cam.transform.position;
        Vector3 camForward = cam.transform.forward;
        bool isOrtho = cam.orthographic;

        // ── 按深度排序：从远到近绘制，近处控制点覆盖远处（实现遮挡效果） ──
        int totalPts = lat.controlPoints.Length;

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

            // 透视模式下使用逐点视线方向，正交模式下使用统一相机朝向
            Vector3 viewDir = isOrtho ? camForward : (worldPos - camPos).normalized;

            // 基于控制点实际位置计算面法线判断是否为背面
            // 不依赖晶格 Transform 的方向，而是从相邻控制点推导表面朝向
            bool isBackFacing = false;
            if (isOnSurface)
            {
                bool anyFaceFront = false;
                // 对该点所在的每个外表面，用相邻控制点叉积计算实际法线方向
                if (pix == 0)
                {
                    Vector3 faceNormal = ComputeFaceNormal(lat, t, pix, piy, piz, nx, ny, nz, -1, 0, 0);
                    if (Vector3.Dot(faceNormal, viewDir) < 0) anyFaceFront = true;
                }
                if (pix == nx - 1)
                {
                    Vector3 faceNormal = ComputeFaceNormal(lat, t, pix, piy, piz, nx, ny, nz, 1, 0, 0);
                    if (Vector3.Dot(faceNormal, viewDir) < 0) anyFaceFront = true;
                }
                if (piy == 0)
                {
                    Vector3 faceNormal = ComputeFaceNormal(lat, t, pix, piy, piz, nx, ny, nz, 0, -1, 0);
                    if (Vector3.Dot(faceNormal, viewDir) < 0) anyFaceFront = true;
                }
                if (piy == ny - 1)
                {
                    Vector3 faceNormal = ComputeFaceNormal(lat, t, pix, piy, piz, nx, ny, nz, 0, 1, 0);
                    if (Vector3.Dot(faceNormal, viewDir) < 0) anyFaceFront = true;
                }
                if (piz == 0)
                {
                    Vector3 faceNormal = ComputeFaceNormal(lat, t, pix, piy, piz, nx, ny, nz, 0, 0, -1);
                    if (Vector3.Dot(faceNormal, viewDir) < 0) anyFaceFront = true;
                }
                if (piz == nz - 1)
                {
                    Vector3 faceNormal = ComputeFaceNormal(lat, t, pix, piy, piz, nx, ny, nz, 0, 0, 1);
                    if (Vector3.Dot(faceNormal, viewDir) < 0) anyFaceFront = true;
                }
                isBackFacing = !anyFaceFront;
            }

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

        // ── Esc 取消选择 ──
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
                    foreach (int i in selPts)
                    {
                        Vector3 startWp = s_handleStartPositions[i];
                        Vector3 offset = startWp - handleCenter;
                        offset.x *= newScale.x;
                        offset.y *= newScale.y;
                        offset.z *= newScale.z;
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
        if (s_activeLattice == null || !s_activeLattice.IsInitialized || s_activeLattice.controlPoints == null)
            return;
        if (Selection.activeGameObject == s_activeLattice.gameObject)
            return;
        DrawLatticeAndHandles(s_activeLattice, s_activeSelectedPoints, sceneView, false);
    }

    private void OnSceneGUI()
    {
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

    private void RepairSingleRendererLink()
    {
        if (lattice.targetRenderer == null)
        {
            EditorUtility.DisplayDialog("提示",
                "「目标对象」字段为空，请先将要变形的模型拖入该字段。", "确定");
            return;
        }

        Undo.RecordObject(lattice, "修复晶格链接");
        bool linked = lattice.LinkRenderer(lattice.targetRenderer);

        if (linked)
        {
            EditorUtility.SetDirty(lattice);
            SceneView.RepaintAll();
            Debug.Log($"[LatticeModifier] 已修复链接：{lattice.targetRenderer.name}");
        }
        else
        {
            EditorUtility.DisplayDialog("提示",
                "目标对象已处于链接状态，无需修复。", "确定");
        }
    }

    private void CreateStandaloneLattice()
    {
        bool isSingle = lattice.targetMode == LatticeModifier.TargetMode.SingleRenderer;
        Renderer targetRend = lattice.targetRenderer;
        Transform targetRt = lattice.targetRoot;
        int dx = lattice.divisionsX, dy = lattice.divisionsY, dz = lattice.divisionsZ;
        bool live = lattice.liveUpdate;
        var manualRends = new List<Renderer>(lattice.manualRenderers);

        Transform refT = isSingle ? targetRend?.transform : targetRt;

        // 如果 targetRoot 为空，从选中对象或 targetRenderer 推断根节点
        if (refT == null)
        {
            if (targetRend != null)
                refT = targetRend.transform;
            else if (manualRends.Count > 0 && manualRends[0] != null)
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
        newLattice.targetMode = LatticeModifier.TargetMode.MultiRenderer;
        newLattice.targetRoot = autoRoot;
        newLattice.targetRenderer = targetRend;
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
