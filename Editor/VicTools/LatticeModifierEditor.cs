// LatticeModifierEditor 1.1 - 晶格变形器编辑器，支持晶格单独移动模型随之变形
// LatticeModifierEditor 1.2 添加晶格点动画控制
// LatticeModifierEditor 1.3 优化晶格控制点选择，可以直接设置动画
// 支持：点击选中、Ctrl+点击加选、Shift+拖拽框选
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(LatticeModifier))]
public class LatticeModifierEditor : Editor
{
    private LatticeModifier lattice;
    private HashSet<int> selectedPoints = new HashSet<int>();

    // 框选状态
    private bool isDragging;
    private Vector2 dragStart;
    private Vector2 dragEnd;

    /// <summary>
    /// 将 3D 视窗中选中的控制点同步到 Hierarchy（选中对应的 CP 子物体）
    /// 只选中 CP 子物体，不包含父对象，这样 Animation/Timeline 能正确录制关键帧
    /// </summary>
    private void SyncSelectionToHierarchy()
    {
        if (!lattice.HasControlPointTransforms || selectedPoints.Count == 0) return;

        var objects = new List<UnityEngine.Object>();
        foreach (int i in selectedPoints)
        {
            Transform cpT = lattice.GetControlPointTransform(i);
            if (cpT != null) objects.Add(cpT.gameObject);
        }
        if (objects.Count > 0)
            Selection.objects = objects.ToArray();
    }

    private void OnEnable()
    {
        lattice = (LatticeModifier)target;
        EditorApplication.update += EditorUpdate;
        SceneView.duringSceneGui += OnGlobalSceneGUI;
    }

    private void OnDisable()
    {
        EditorApplication.update -= EditorUpdate;
        SceneView.duringSceneGui -= OnGlobalSceneGUI;
    }

    // 编辑器持续更新：确保移动晶格时目标对象实时变形
    private void EditorUpdate()
    {
        if (lattice == null || !lattice.IsInitialized || !lattice.liveUpdate) return;
        // 如果有子物体控制点，先从 Transform 同步到数组
        if (lattice.useTransformHandles && lattice.HasControlPointTransforms)
            lattice.SyncFromTransforms();
        lattice.ApplyDeformation();
        // 强制 Scene 视图重绘
        SceneView.RepaintAll();
    }

    public override void OnInspectorGUI()
    {
        
        // "添加目标"按钮：从场景选中对象获取 Renderer
        GUI.backgroundColor = new Color(0.5f, 0.9f, 1f);
        if (GUILayout.Button("添加目标（从场景选中对象）", GUILayout.Height(26)))
        {
            GameObject sel = Selection.activeGameObject;
            if (sel != null && sel != lattice.gameObject)
            {
                Renderer rend = sel.GetComponent<Renderer>();
                if (rend != null)
                {
                    Undo.RecordObject(lattice, "设置目标对象");
                    lattice.targetRenderer = rend;
                    EditorUtility.SetDirty(lattice);
                }
                else
                {
                    EditorUtility.DisplayDialog("提示", "选中的对象没有 Renderer 组件", "确定");
                }
            }
            else
            {
                EditorUtility.DisplayDialog("提示", "请先在场景中选中一个模型对象（不能是晶格自身）", "确定");
            }
        }
        GUI.backgroundColor = Color.white;

        DrawDefaultInspector();
        EditorGUILayout.Space(10);

        if (!lattice.IsInitialized)
        {
            if (lattice.targetRenderer == null)
            {
                EditorGUILayout.HelpBox("请将要变形的模型拖入「目标对象」字段", MessageType.Warning);
            }
            else
            {
                // 检查晶格是否挂在目标对象上，提示用户
                bool isSameOrChild = lattice.transform == lattice.targetRenderer.transform ||
                                     lattice.transform.IsChildOf(lattice.targetRenderer.transform) ||
                                     lattice.targetRenderer.transform.IsChildOf(lattice.transform);
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
            EditorGUILayout.HelpBox(
                $"晶格：{lattice.PointCountX}×{lattice.PointCountY}×{lattice.PointCountZ} = {lattice.TotalPoints} 个控制点\n" +
                "点击选中 | Ctrl+点击加选 | Shift+拖拽框选 | 拖拽手柄变形",
                MessageType.Info);

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
            GUI.backgroundColor = Color.white;

            // ── 动画控制点（子物体）──
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
    }

    /// <summary>
    /// 全局 SceneView 回调：即使选中了 CP 子物体（Inspector 切走），也持续绘制晶格线框和控制点
    /// </summary>
    private void OnGlobalSceneGUI(SceneView sceneView)
    {
        if (lattice == null || !lattice.IsInitialized || lattice.controlPoints == null)
            return;

        // 如果当前选中的就是晶格物体本身，OnSceneGUI 会处理绘制，这里不重复
        if (Selection.activeGameObject == lattice.gameObject)
            return;

        Transform t = lattice.transform;
        int nx = lattice.PointCountX, ny = lattice.PointCountY, nz = lattice.PointCountZ;

        // 绘制晶格线框
        Handles.color = new Color(0.2f, 0.8f, 1f, 0.5f);
        for (int ix = 0; ix < nx; ix++)
        for (int iy = 0; iy < ny; iy++)
        for (int iz = 0; iz < nz; iz++)
        {
            int idx = lattice.GetFlatIndex(ix, iy, iz);
            Vector3 p = t.TransformPoint(lattice.controlPoints[idx]);
            if (ix < nx - 1) Handles.DrawLine(p, t.TransformPoint(lattice.controlPoints[lattice.GetFlatIndex(ix + 1, iy, iz)]));
            if (iy < ny - 1) Handles.DrawLine(p, t.TransformPoint(lattice.controlPoints[lattice.GetFlatIndex(ix, iy + 1, iz)]));
            if (iz < nz - 1) Handles.DrawLine(p, t.TransformPoint(lattice.controlPoints[lattice.GetFlatIndex(ix, iy, iz + 1)]));
        }

        // 绘制控制点球
        for (int i = 0; i < lattice.controlPoints.Length; i++)
        {
            Vector3 worldPos = t.TransformPoint(lattice.controlPoints[i]);
            float sz = HandleUtility.GetHandleSize(worldPos) * 0.05f;

            bool isSelected = selectedPoints.Contains(i);
            lattice.GetPointIndex3D(i, out int pix, out int piy, out int piz);
            bool isCorner = (pix == 0 || pix == nx - 1) && (piy == 0 || piy == ny - 1) && (piz == 0 || piz == nz - 1);

            if (isSelected)
                Handles.color = Color.white;
            else if (isCorner)
                Handles.color = new Color(1f, 0.3f, 0.3f, 0.9f);
            else
                Handles.color = new Color(1f, 0.9f, 0.2f, 0.8f);

            Handles.SphereHandleCap(0, worldPos, Quaternion.identity, sz * 2f, EventType.Repaint);
        }
    }

    private void OnSceneGUI()
    {
        if (lattice == null || !lattice.IsInitialized || lattice.controlPoints == null)
            return;

        Event e = Event.current;
        Transform t = lattice.transform;
        int nx = lattice.PointCountX, ny = lattice.PointCountY, nz = lattice.PointCountZ;

        // ── 绘制晶格线框 ──
        Handles.color = new Color(0.2f, 0.8f, 1f, 0.5f);
        for (int ix = 0; ix < nx; ix++)
        for (int iy = 0; iy < ny; iy++)
        for (int iz = 0; iz < nz; iz++)
        {
            int idx = lattice.GetFlatIndex(ix, iy, iz);
            Vector3 p = t.TransformPoint(lattice.controlPoints[idx]);
            if (ix < nx - 1) Handles.DrawLine(p, t.TransformPoint(lattice.controlPoints[lattice.GetFlatIndex(ix + 1, iy, iz)]));
            if (iy < ny - 1) Handles.DrawLine(p, t.TransformPoint(lattice.controlPoints[lattice.GetFlatIndex(ix, iy + 1, iz)]));
            if (iz < nz - 1) Handles.DrawLine(p, t.TransformPoint(lattice.controlPoints[lattice.GetFlatIndex(ix, iy, iz + 1)]));
        }

        // ── 绘制控制点 ──
        for (int i = 0; i < lattice.controlPoints.Length; i++)
        {
            Vector3 worldPos = t.TransformPoint(lattice.controlPoints[i]);
            float sz = HandleUtility.GetHandleSize(worldPos) * 0.05f;

            bool isSelected = selectedPoints.Contains(i);
            lattice.GetPointIndex3D(i, out int ix, out int iy, out int iz);
            bool isCorner = (ix == 0 || ix == nx - 1) && (iy == 0 || iy == ny - 1) && (iz == 0 || iz == nz - 1);

            if (isSelected)
                Handles.color = Color.white;
            else if (isCorner)
                Handles.color = new Color(1f, 0.3f, 0.3f, 0.9f);
            else
                Handles.color = new Color(1f, 0.9f, 0.2f, 0.8f);

            // 点击选中 / Ctrl 加选
            if (Handles.Button(worldPos, Quaternion.identity, sz, sz * 1.5f, Handles.SphereHandleCap))
            {
                if (e.control)
                {
                    // Ctrl+点击：切换选中状态
                    if (selectedPoints.Contains(i)) selectedPoints.Remove(i);
                    else selectedPoints.Add(i);
                }
                else
                {
                    selectedPoints.Clear();
                    selectedPoints.Add(i);
                }
                SyncSelectionToHierarchy();
                Repaint();
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
                        isDragging = true;
                        dragStart = e.mousePosition;
                        dragEnd = e.mousePosition;
                        GUIUtility.hotControl = controlID;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (isDragging)
                    {
                        dragEnd = e.mousePosition;
                        e.Use();
                        SceneView.RepaintAll();
                    }
                    break;

                case EventType.MouseUp:
                    if (isDragging && e.button == 0)
                    {
                        isDragging = false;
                        GUIUtility.hotControl = 0;

                        // 计算框选矩形
                        Rect selRect = new Rect(
                            Mathf.Min(dragStart.x, dragEnd.x),
                            Mathf.Min(dragStart.y, dragEnd.y),
                            Mathf.Abs(dragEnd.x - dragStart.x),
                            Mathf.Abs(dragEnd.y - dragStart.y)
                        );

                        // 如果没按 Ctrl 则清除之前的选择
                        if (!e.control) selectedPoints.Clear();

                        // 将每个控制点投影到屏幕空间，判断是否在框内
                        Camera cam = SceneView.lastActiveSceneView.camera;
                        for (int i = 0; i < lattice.controlPoints.Length; i++)
                        {
                            Vector3 worldPos = t.TransformPoint(lattice.controlPoints[i]);
                            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                            // Unity GUI 坐标 Y 轴翻转
                            screenPos.y = cam.pixelHeight - screenPos.y;

                            if (screenPos.z > 0 && selRect.Contains(new Vector2(screenPos.x, screenPos.y)))
                            {
                                selectedPoints.Add(i);
                            }
                        }

                        e.Use();
                        SyncSelectionToHierarchy();
                        Repaint();
                    }
                    break;
            }

            // 绘制框选矩形
            if (isDragging)
            {
                Handles.BeginGUI();
                Rect r = new Rect(
                    Mathf.Min(dragStart.x, dragEnd.x),
                    Mathf.Min(dragStart.y, dragEnd.y),
                    Mathf.Abs(dragEnd.x - dragStart.x),
                    Mathf.Abs(dragEnd.y - dragStart.y)
                );
                EditorGUI.DrawRect(r, new Color(0.2f, 0.6f, 1f, 0.15f));
                // 边框
                Handles.color = new Color(0.2f, 0.6f, 1f, 0.8f);
                Handles.DrawSolidRectangleWithOutline(
                    new Vector3[] {
                        new Vector3(r.xMin, r.yMin, 0),
                        new Vector3(r.xMax, r.yMin, 0),
                        new Vector3(r.xMax, r.yMax, 0),
                        new Vector3(r.xMin, r.yMax, 0)
                    },
                    new Color(0.2f, 0.6f, 1f, 0.1f),
                    new Color(0.2f, 0.6f, 1f, 0.8f)
                );
                Handles.EndGUI();
            }
        }

        // ── 选中点的移动手柄 ──
        if (selectedPoints.Count > 0)
        {
            // 计算选中点的中心
            Vector3 center = Vector3.zero;
            foreach (int i in selectedPoints)
                center += t.TransformPoint(lattice.controlPoints[i]);
            center /= selectedPoints.Count;

            EditorGUI.BeginChangeCheck();
            Vector3 newCenter = Handles.PositionHandle(center, t.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(lattice, "移动晶格控制点");
                Vector3 delta = newCenter - center;
                foreach (int i in selectedPoints)
                {
                    Vector3 wp = t.TransformPoint(lattice.controlPoints[i]) + delta;
                    lattice.controlPoints[i] = t.InverseTransformPoint(wp);
                }
                // 同步到子物体 Transform（如果有）
                if (lattice.HasControlPointTransforms)
                    lattice.SyncToTransforms();
                if (lattice.liveUpdate)
                    lattice.ApplyDeformation();
                EditorUtility.SetDirty(lattice);
            }
        }
    }

    /// <summary>
    /// 自动创建独立的晶格物体：从目标对象上移除 LatticeModifier，在场景根级创建新物体并初始化
    /// </summary>
    private void CreateStandaloneLattice()
    {
        Renderer targetRend = lattice.targetRenderer;
        int dx = lattice.divisionsX, dy = lattice.divisionsY, dz = lattice.divisionsZ;
        bool live = lattice.liveUpdate;

        // 移除当前组件
        Undo.DestroyObjectImmediate(lattice);

        // 创建独立的晶格物体（场景根级，不是任何对象的子物体）
        GameObject latticeObj = new GameObject("Lattice_" + targetRend.name);
        Undo.RegisterCreatedObjectUndo(latticeObj, "创建独立晶格");

        // 放在目标对象旁边
        latticeObj.transform.position = targetRend.transform.position;
        latticeObj.transform.rotation = targetRend.transform.rotation;

        // 添加 LatticeModifier 并配置
        LatticeModifier newLattice = latticeObj.AddComponent<LatticeModifier>();
        newLattice.targetRenderer = targetRend;
        newLattice.divisionsX = dx;
        newLattice.divisionsY = dy;
        newLattice.divisionsZ = dz;
        newLattice.liveUpdate = live;

        // 初始化
        newLattice.InitializeLattice();
        EditorUtility.SetDirty(newLattice);

        // 选中新创建的晶格物体
        Selection.activeGameObject = latticeObj;
        SceneView.RepaintAll();
    }
}
