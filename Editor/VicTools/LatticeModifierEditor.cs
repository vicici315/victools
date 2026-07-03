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

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

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
        serializedObject.ApplyModifiedProperties();

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

        // 目标 Renderer 列表
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

    /// 根据控制点实际位置计算指定面的法线方向。
    /// faceAxis 指明面的朝向轴：(-1,0,0)=X轴负方向面，(1,0,0)=X轴正方向面 等。
    /// 通过该点与同面相邻控制点的叉积得到实际法线，不依赖晶格 Transform 轴向。
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
