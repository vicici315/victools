// LatticeModifierEditor 1.0 晶格变形器编辑器，Inspector 面板与 SceneView 控制点交互
// LatticeModifierEditor 1.1 支持晶格单独移动，模型经过晶格区域产生变形，离开后恢复
// LatticeModifierEditor 1.2 添加晶格点动画控制（子物体 CP 节点，支持 Animation/Timeline K帧）
// LatticeModifierEditor 1.3 选中晶格点时同步选中 Hierarchy 中对应 CP 节点（蓝色高亮，不含父对象）
// LatticeModifierEditor 1.4 静态 SceneView 回调：选中 CP 后晶格线框持续绘制，可继续点击/框选其他晶格点
// LatticeModifierEditor 1.5 不重算法线，保持原始 mesh 的法线数据，变形只改顶点位置
// LatticeModifierEditor 2.0 支持单目标/多目标（整个预设/带蒙皮角色）两种模式，SceneView 绘制逻辑合并
// LatticeModifierEditor 2.1 添加删除晶格按钮（还原 Mesh 并删除晶格物体），单目标模式自动识别带骨骼角色父级切换多目标
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

    private static void SyncSelectionToHierarchy()
    {
        if (s_activeLattice == null || !s_activeLattice.HasControlPointTransforms) return;
        if (s_activeSelectedPoints == null || s_activeSelectedPoints.Count == 0) return;

        var objects = new List<UnityEngine.Object>();
        foreach (int i in s_activeSelectedPoints)
        {
            Transform cpT = s_activeLattice.GetControlPointTransform(i);
            if (cpT != null) objects.Add(cpT.gameObject);
        }
        if (objects.Count > 0)
            Selection.objects = objects.ToArray();
    }

    private void OnEnable()
    {
        lattice = (LatticeModifier)target;
        s_activeLattice = lattice;
        s_activeSelectedPoints = selectedPoints;
        EditorApplication.update += EditorUpdate;
        if (!s_registered)
        {
            SceneView.duringSceneGui += OnGlobalSceneGUIStatic;
            s_registered = true;
        }
    }

    private void OnDisable()
    {
        EditorApplication.update -= EditorUpdate;
    }

    private void EditorUpdate()
    {
        if (lattice == null || !lattice.IsInitialized || !lattice.liveUpdate) return;
        if (lattice.useTransformHandles && lattice.HasControlPointTransforms)
            lattice.SyncFromTransforms();
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
        }

        // ── 添加目标按钮（统一入口，智能判断） ──
        GUI.backgroundColor = new Color(0.5f, 0.9f, 1f);
        if (GUILayout.Button("添加目标（将场景中的对象拖入上方字段，或先选中对象再点此按钮）", GUILayout.Height(26)))
        {
            GameObject sel = null;
            foreach (var obj in Selection.gameObjects)
            {
                if (obj != lattice.gameObject)
                {
                    sel = obj;
                    break;
                }
            }

            if (sel != null)
            {
                Undo.RecordObject(lattice, "设置目标");

                Renderer rend = sel.GetComponent<Renderer>();
                if (rend != null && isSingle)
                {
                    lattice.targetRenderer = rend;
                }
                else
                {
                    var childRenderers = sel.GetComponentsInChildren<Renderer>(true);
                    if (childRenderers.Length > 0)
                    {
                        lattice.targetMode = LatticeModifier.TargetMode.MultiRenderer;
                        lattice.targetRoot = sel.transform;
                    }
                    else if (rend != null)
                    {
                        lattice.targetMode = LatticeModifier.TargetMode.SingleRenderer;
                        lattice.targetRenderer = rend;
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("提示", "选中的对象及其子物体都没有 Renderer 组件", "确定");
                    }
                }

                serializedObject.Update();
                EditorUtility.SetDirty(lattice);
            }
            else
            {
                EditorUtility.DisplayDialog("提示",
                    "请先在 Hierarchy 中选中目标对象（可 Ctrl+点击同时选中晶格和目标），或直接将对象拖入上方字段", "确定");
            }
        }
        GUI.backgroundColor = Color.white;

        // 模式可能在按钮中被自动切换，重新读取
        isSingle = lattice.targetMode == LatticeModifier.TargetMode.SingleRenderer;

        EditorGUILayout.PropertyField(serializedObject.FindProperty("divisionsX"), new GUIContent("X 段数"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("divisionsY"), new GUIContent("Y 段数"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("divisionsZ"), new GUIContent("Z 段数"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("liveUpdate"), new GUIContent("实时更新"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("useTransformHandles"), new GUIContent("使用子物体控制点"));
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10);

        if (!lattice.IsInitialized)
        {
            if (isSingle && lattice.targetRenderer == null)
                EditorGUILayout.HelpBox("请将要变形的模型拖入「目标对象」字段", MessageType.Warning);
            else if (!isSingle && lattice.targetRoot == null)
                EditorGUILayout.HelpBox("请将要变形的根节点拖入「多目标根节点」字段（会自动收集所有子 Renderer）", MessageType.Warning);
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
        if (GUILayout.Button("重新初始化", GUILayout.Height(28)))
        {
            Undo.RecordObject(lattice, "重新初始化晶格");
            lattice.InitializeLattice();
            selectedPoints.Clear();
            EditorUtility.SetDirty(lattice);
            SceneView.RepaintAll();
        }
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
                Undo.RecordObject(lattice, "烘焙晶格变形");
                lattice.BakeAndRemove();
                selectedPoints.Clear();
                EditorUtility.SetDirty(lattice);
                SceneView.RepaintAll();
            }
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
            GUI.backgroundColor = new Color(1f, 0.6f, 0.4f);
            if (GUILayout.Button("清除动画控制点", GUILayout.Height(24)))
            {
                Undo.RecordObject(lattice, "清除动画控制点");
                lattice.DestroyControlPointTransforms();
                EditorUtility.SetDirty(lattice);
                SceneView.RepaintAll();
            }
        }
        GUI.backgroundColor = Color.white;
    }

    // ═══════════════════════════════════════════
    //  SceneView 绘制 & 交互
    // ═══════════════════════════════════════════
    private static void DrawLatticeAndHandles(LatticeModifier lat, HashSet<int> selPts, SceneView sceneView, bool isInstance)
    {
        if (lat == null || !lat.IsInitialized || lat.controlPoints == null) return;

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

        for (int i = 0; i < lat.controlPoints.Length; i++)
        {
            Vector3 worldPos = t.TransformPoint(lat.controlPoints[i]);
            float sz = HandleUtility.GetHandleSize(worldPos) * 0.05f;

            bool isSelected = selPts != null && selPts.Contains(i);
            lat.GetPointIndex3D(i, out int pix, out int piy, out int piz);
            bool isCorner = (pix == 0 || pix == nx - 1) && (piy == 0 || piy == ny - 1) && (piz == 0 || piz == nz - 1);

            if (isSelected) Handles.color = Color.white;
            else if (isCorner) Handles.color = new Color(1f, 0.3f, 0.3f, 0.9f);
            else Handles.color = new Color(1f, 0.9f, 0.2f, 0.8f);

            if (Handles.Button(worldPos, Quaternion.identity, sz, sz * 1.5f, Handles.SphereHandleCap))
            {
                if (e.control)
                {
                    if (selPts.Contains(i)) selPts.Remove(i);
                    else selPts.Add(i);
                }
                else
                {
                    selPts.Clear();
                    selPts.Add(i);
                }
                SyncSelectionToHierarchy();
                sceneView.Repaint();
            }
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
                        if (!e.control) selPts.Clear();
                        Camera cam = sceneView.camera;
                        for (int i = 0; i < lat.controlPoints.Length; i++)
                        {
                            Vector3 worldPos = t.TransformPoint(lat.controlPoints[i]);
                            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                            screenPos.y = cam.pixelHeight - screenPos.y;
                            if (screenPos.z > 0 && selRect.Contains(new Vector2(screenPos.x, screenPos.y)))
                                selPts.Add(i);
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

        // ── 选中点的移动手柄 ──
        if (selPts != null && selPts.Count > 0)
        {
            Vector3 center = Vector3.zero;
            foreach (int i in selPts)
                center += t.TransformPoint(lat.controlPoints[i]);
            center /= selPts.Count;

            EditorGUI.BeginChangeCheck();
            Vector3 newCenter = Handles.PositionHandle(center, t.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(lat, "移动晶格控制点");
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
    //  创建独立晶格物体
    // ═══════════════════════════════════════════
    private void CreateStandaloneLattice()
    {
        bool isSingle = lattice.targetMode == LatticeModifier.TargetMode.SingleRenderer;
        Renderer targetRend = lattice.targetRenderer;
        Transform targetRt = lattice.targetRoot;
        int dx = lattice.divisionsX, dy = lattice.divisionsY, dz = lattice.divisionsZ;
        bool live = lattice.liveUpdate;
        var mode = lattice.targetMode;

        Transform refT = isSingle ? targetRend?.transform : targetRt;
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
        newLattice.targetMode = mode;
        newLattice.targetRenderer = targetRend;
        newLattice.targetRoot = targetRt;
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
