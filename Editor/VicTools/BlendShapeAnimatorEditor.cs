// BlendShapeAnimatorEditor - BlendShapeAnimator 的自定义 Inspector
// 支持 SkinnedMeshRenderer 和普通 MeshFilter 模型
// v1.1 "删除跟踪对象"操作时保留里面的子对象

// 1. 实时刷新 Scene 视图 通过 EditorApplication.update 每帧调用 SceneView.RepaintAll()，让 Scene 视图持续重绘，顶点位置标记才能实时更新。
// 2. 顶点总数显示 在 Inspector 中显示当前 Mesh 的顶点总数和有效索引范围，方便填写 trackVertexIndex。
// 3. Read/Write 检查提示 检测 Mesh 是否开启了 Read/Write，未开启时在 Inspector 显示红色错误提示，引导用户去导入设置中勾选。
// 4. trackVertexIndex 变化时 Debug 输出 修改顶点索引时自动在 Console 打印该顶点的世界坐标。
// 5. Scene 视图可视化标记（OnSceneGUI） 在 Scene 视图中用黄色小球 + 十字线 + 坐标标签标记当前追踪顶点的位置，实时更新。
// 6. "创建顶点追踪空对象"按钮 在 Inspector 中提供按钮，一键在顶点位置创建空 GameObject 并绑定为 vertexTracker。

using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(BlendShapeAnimator))]
public class BlendShapeAnimatorEditor : Editor
{
    private int _lastVertexIndex = -1;

    private void OnEnable()  => EditorApplication.update += OnEditorUpdate;
    private void OnDisable() => EditorApplication.update -= OnEditorUpdate;

    private void OnEditorUpdate()
    {
        if (!Application.isPlaying) SceneView.RepaintAll();
    }

    // 统一获取 Mesh（兼容 SkinnedMeshRenderer 和 MeshFilter）
    private static Mesh GetMesh(BlendShapeAnimator anim)
    {
        if (anim.targetRenderer == null) return null;
        if (anim.targetRenderer is SkinnedMeshRenderer smr) return smr.sharedMesh;
        var mf = anim.targetRenderer.GetComponent<MeshFilter>();
        return mf != null ? mf.sharedMesh : null;
    }
// 属性栏UI显示
    public override void OnInspectorGUI()
    {
        BlendShapeAnimator anim = (BlendShapeAnimator)target;
        SerializedObject so = serializedObject;
        so.Update();

        EditorGUI.BeginChangeCheck();

        // 目标
        EditorGUILayout.PropertyField(so.FindProperty("targetRenderer"));

        using(new GUILayout.VerticalScope(EditorStyles.helpBox)){
        // 动画参数
            EditorGUILayout.PropertyField(so.FindProperty("enableAnimation"), new GUIContent("启用动画"));

            if (so.FindProperty("enableAnimation").boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(so.FindProperty("blendShapeIndex"), new GUIContent("BlendShape 索引"));
                EditorGUILayout.PropertyField(so.FindProperty("duration"), new GUIContent("单程时长"));
                EditorGUILayout.PropertyField(so.FindProperty("playOnAwake"), new GUIContent("自动播放"));
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(so.FindProperty("blendShapeIndex"), new GUIContent("BlendShape 索引"));
                EditorGUILayout.Slider(so.FindProperty("manualProgress"), 0f, 1f, new GUIContent("手动进度", "手动控制 0→1 过程，驱动 BlendShape 与粒子速度"));
                EditorGUI.indentLevel--;
            }
        }

        // 粒子速度控制
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("粒子速度控制", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty("targetParticle"), new GUIContent("粒子系统"));
            if (so.FindProperty("targetParticle").objectReferenceValue != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(so.FindProperty("particleSpeedMin"), new GUIContent("起始速度 (t=0)"));
                EditorGUILayout.PropertyField(so.FindProperty("particleSpeedMax"), new GUIContent("结束速度 (t=1)"));
                EditorGUILayout.PropertyField(so.FindProperty("particleSpeedEaseForward"), new GUIContent("起始段 EaseOut 指数", "forward阶段曲线陡度，1=线性，值越大越急促"));
                EditorGUILayout.PropertyField(so.FindProperty("particleSpeedEaseBack"),    new GUIContent("返回段 EaseOut 指数", "back阶段曲线陡度，1=线性，值越大越急促"));
                EditorGUI.indentLevel--;
            }
        }
        // 顶点追踪
        Mesh mesh = GetMesh(anim);
        if (mesh != null)
        {
            int vertCount = mesh.vertexCount;

            if (vertCount > 0 && mesh.vertices.Length == 0)
                EditorGUILayout.HelpBox(
                    $"Mesh '{mesh.name}' 的 Read/Write 未启用，请在模型导入设置中勾选 Read/Write Enabled。",
                    MessageType.Error);
        }

        EditorGUILayout.PropertyField(so.FindProperty("trackVertexIndex"));

        if (mesh != null)
            EditorGUILayout.HelpBox($"顶点总数：{mesh.vertexCount}（有效索引 0 ~ {mesh.vertexCount - 1}）", MessageType.None);

        EditorGUILayout.PropertyField(so.FindProperty("vertexTracker"));
        EditorGUILayout.PropertyField(so.FindProperty("ignoreTrackerZ"));

        bool changed = EditorGUI.EndChangeCheck();
        so.ApplyModifiedProperties();

        // trackVertexIndex 变化时 Debug 输出
        if (changed && mesh != null && anim.trackVertexIndex != _lastVertexIndex)
        {
            _lastVertexIndex = anim.trackVertexIndex;
            if (anim.trackVertexIndex >= 0 && anim.trackVertexIndex < mesh.vertexCount)
                Debug.Log($"[BlendShapeAnimator] 顶点 {anim.trackVertexIndex} 世界坐标：{anim.GetVertexWorldPosition(anim.trackVertexIndex)}");
            else
                Debug.LogWarning($"[BlendShapeAnimator] 顶点索引 {anim.trackVertexIndex} 无效");
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        EditorGUI.BeginDisabledGroup(anim.vertexTracker == null);
        if (GUILayout.Button("删除跟踪对象", GUILayout.Height(28)))
        {
            Transform tracker = anim.vertexTracker;
            // 先将所有子对象移到场景根级，保持子对象不丢失
            for (int i = tracker.childCount - 1; i >= 0; i--)
            {
                Transform child = tracker.GetChild(i);
                Undo.SetTransformParent(child, null, "移出子对象");
            }
            Undo.DestroyObjectImmediate(tracker.gameObject);
            anim.vertexTracker = null;
            EditorUtility.SetDirty(anim);
        }
        EditorGUI.EndDisabledGroup();

        GUI.backgroundColor = new Color(0.4f, 0.9f, 0.6f);
        if (GUILayout.Button("创建顶点追踪空对象", GUILayout.Height(28)))
        {
            Undo.RecordObject(anim, "创建顶点追踪器");
            anim.CreateVertexTracker();
            if (anim.vertexTracker != null)
                Undo.RegisterCreatedObjectUndo(anim.vertexTracker.gameObject, "创建顶点追踪器");
            EditorUtility.SetDirty(anim);
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndHorizontal();
    }
// 3D视窗屏幕显示
    private void OnSceneGUI()
    {
        BlendShapeAnimator anim = (BlendShapeAnimator)target;
        Mesh mesh = GetMesh(anim);
        if (mesh == null || anim.trackVertexIndex < 0 || anim.trackVertexIndex >= mesh.vertexCount) return;

        Vector3 worldPos = anim.GetVertexWorldPosition(anim.trackVertexIndex);
        float size = HandleUtility.GetHandleSize(worldPos) * 0.08f;

        Handles.color = Color.yellow;
        Handles.SphereHandleCap(0, worldPos, Quaternion.identity, size, EventType.Repaint);

        Handles.color = new Color(1f, 1f, 0f, 0.6f);
        Handles.DrawLine(worldPos - Vector3.right   * size * 6, worldPos + Vector3.right   * size * 4);
        Handles.DrawLine(worldPos - Vector3.up      * size * 6, worldPos + Vector3.up      * size * 4);
        Handles.DrawLine(worldPos - Vector3.forward * size * 6, worldPos + Vector3.forward * size * 4);

        Handles.color = Color.white;
        Handles.Label(worldPos + Vector3.up * size * 3,
            $"V{anim.trackVertexIndex}\n{worldPos:F3}", EditorStyles.miniLabel);
    }
}
