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
using Vic.Runtime;

namespace Vic.Editor
{

[CustomEditor(typeof(BlendShapeAnimator))]
public class BlendShapeAnimatorEditor : UnityEditor.Editor
{
    private int _lastVertexIndex = -1;

    private void OnEnable()  => EditorApplication.update += OnEditorUpdate;
    private void OnDisable() => EditorApplication.update -= OnEditorUpdate;

    private void OnEditorUpdate()
    {
        if (!Application.isPlaying) SceneView.RepaintAll();
    }

    /// 从 FBX 原始资产中加载 SkinnedMeshRenderer 的原始 localBounds
    /// 注意：不能用 mesh.bounds 替代，因为 SMR 的 localBounds 是相对于 rootBone 的，
    /// 而 mesh.bounds 是相对于 mesh 原点的，两者 center 不同
    private static Bounds? GetOriginalFBXBounds(SkinnedMeshRenderer currentSMR)
    {
        Mesh currentMesh = currentSMR.sharedMesh;
        if (currentMesh == null) return null;
        
        string assetPath = AssetDatabase.GetAssetPath(currentMesh);
        if (string.IsNullOrEmpty(assetPath)) return null;
        
        // 从 FBX 资产实例化临时对象，读取其 SkinnedMeshRenderer 的原始 localBounds
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null) return null;
        
        // 在 FBX 的层级中查找同名的 SkinnedMeshRenderer
        var allSMRs = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in allSMRs)
        {
            if (smr.sharedMesh == currentMesh || smr.sharedMesh.name == currentMesh.name)
            {
                return smr.localBounds;
            }
        }
        
        // 如果名字匹配不到，返回第一个 SMR 的 localBounds
        if (allSMRs.Length > 0)
        {
            return allSMRs[0].localBounds;
        }
        
        return null;
    }

    /// 从 FBX 原始资产还原 sharedMesh 和 localBounds
    /// 修复 LatticeModifier.BakeAndRemove 后 mesh 被替换为运行时副本导致轴心偏移的问题
    private static void RestoreMeshFromFBX(SkinnedMeshRenderer smr)
    {
        if (smr == null || smr.sharedMesh == null) return;
        
        Mesh currentMesh = smr.sharedMesh;
        string assetPath = AssetDatabase.GetAssetPath(currentMesh);
        
        // 如果当前 mesh 没有资产路径（运行时创建的），尝试通过名字找原始 FBX
        if (string.IsNullOrEmpty(assetPath))
        {
            // 去掉 _LatticeDeform 后缀尝试搜索
            string meshName = currentMesh.name;
            if (meshName.EndsWith("_LatticeDeform"))
                meshName = meshName.Substring(0, meshName.Length - "_LatticeDeform".Length);
            
            // 在项目中搜索同名 mesh
            string[] guids = AssetDatabase.FindAssets($"{meshName} t:Mesh");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // 只查找 FBX/模型文件
                string ext = System.IO.Path.GetExtension(path).ToLower();
                if (ext == ".fbx" || ext == ".obj" || ext == ".blend" || ext == ".dae")
                {
                    var allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                    foreach (var asset in allAssets)
                    {
                        if (asset is Mesh fbxMesh && fbxMesh.name == meshName)
                        {
                            Undo.RecordObject(smr, "还原Mesh到FBX原始资产");
                            smr.sharedMesh = fbxMesh;
                            // 同时从 FBX 还原 localBounds
                            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                            if (prefab != null)
                            {
                                var fbxSMRs = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                                foreach (var fbxSMR in fbxSMRs)
                                {
                                    if (fbxSMR.sharedMesh == fbxMesh || fbxSMR.sharedMesh.name == meshName)
                                    {
                                        smr.localBounds = fbxSMR.localBounds;
                                        break;
                                    }
                                }
                            }
                            EditorUtility.SetDirty(smr);
                            Debug.Log($"[BlendShapeAnimator] 已从 '{path}' 还原原始 Mesh '{meshName}'，轴心已恢复");
                            return;
                        }
                    }
                }
            }
            
            Debug.LogError($"[BlendShapeAnimator] 无法找到原始 FBX 资产，当前 Mesh 名称: '{currentMesh.name}'。请手动将原始 FBX 的 Mesh 拖入 SkinnedMeshRenderer");
            return;
        }
        
        // 当前 mesh 有资产路径，说明它本身就是 FBX 资产中的 mesh，直接从资产重新加载
        GameObject prefabFromPath = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefabFromPath != null)
        {
            var fbxSMRs = prefabFromPath.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var fbxSMR in fbxSMRs)
            {
                if (fbxSMR.sharedMesh != null && fbxSMR.sharedMesh.name == currentMesh.name)
                {
                    Undo.RecordObject(smr, "还原Mesh到FBX原始资产");
                    smr.sharedMesh = fbxSMR.sharedMesh;
                    smr.localBounds = fbxSMR.localBounds;
                    EditorUtility.SetDirty(smr);
                    Debug.Log($"[BlendShapeAnimator] 已从 '{assetPath}' 还原原始 Mesh 和 localBounds");
                    return;
                }
            }
        }
        
        Debug.LogWarning($"[BlendShapeAnimator] 资产路径 '{assetPath}' 中未找到匹配的 SkinnedMeshRenderer");
    }

    // 统一获取 Mesh（兼容 SkinnedMeshRenderer 和 MeshFilter）
    private static Mesh GetMesh(BlendShapeAnimator anim)
    {
        if (anim.targetRenderer == null) return null;
        if (anim.targetRenderer is SkinnedMeshRenderer smr) return smr.sharedMesh;
        var mf = anim.targetRenderer.GetComponent<MeshFilter>();
        return mf != null ? mf.sharedMesh : null;
    }

    // 自动启用 Mesh 的 Read/Write 功能
    private static void EnableMeshReadWrite(Mesh mesh)
    {
        if (mesh == null)
        {
            Debug.LogWarning("[BlendShapeAnimator] 未找到有效的 Mesh！");
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(mesh);
        if (string.IsNullOrEmpty(assetPath))
        {
            Debug.LogWarning($"[BlendShapeAnimator] Mesh '{mesh.name}' 不是资产文件，无法修改导入设置。");
            return;
        }

        ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[BlendShapeAnimator] 无法获取 '{assetPath}' 的模型导入器。");
            return;
        }

        if (importer.isReadable)
        {
            Debug.Log($"[BlendShapeAnimator] Mesh '{mesh.name}' 已经启用了 Read/Write，无需修改。");
            return;
        }

        // 弹出确认对话框
        bool confirm = EditorUtility.DisplayDialog(
            "启用 Read/Write",
            $"即将为 Mesh '{mesh.name}' 启用 Read/Write 功能。\n\n"
            + "注意：此操作会重新导入模型，可能导致场景中的引用暂时丢失。\n\n"
            + "是否继续？",
            "确定",
            "取消");

        if (!confirm) return;

        // 修改导入设置
        importer.isReadable = true;
        importer.SaveAndReimport();

        Debug.Log($"[BlendShapeAnimator] ✅ 已成功为 Mesh '{mesh.name}' 启用 Read/Write！请重新选择对象以刷新 Inspector。");
        
        // 刷新资源数据库
        AssetDatabase.Refresh();
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

            // 检查 Read/Write 是否启用
            if (vertCount > 0 && mesh.vertices.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    $"Mesh '{mesh.name}' 的 Read/Write 未启用，请在模型导入设置中勾选 Read/Write Enabled。",
                    MessageType.Error);
                
                // 提供一键修复按钮
                GUI.backgroundColor = new Color(1f, 0.7f, 0.3f);
                if (GUILayout.Button("自动启用 Read/Write", GUILayout.Height(28)))
                {
                    EnableMeshReadWrite(mesh);
                }
                GUI.backgroundColor = Color.white;
            }
        }

        EditorGUILayout.PropertyField(so.FindProperty("trackVertexIndex"));

        if (mesh != null)
            EditorGUILayout.HelpBox($"顶点总数：{mesh.vertexCount}（有效索引 0 ~ {mesh.vertexCount - 1}）", MessageType.None);

        EditorGUILayout.PropertyField(so.FindProperty("vertexTracker"));
        EditorGUILayout.PropertyField(so.FindProperty("ignoreTrackerZ"));

        // 包围盒扩展
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("包围盒扩展（防止视锥剔除）", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty("boundsExpand"), new GUIContent("扩展量", "基于原始 bounds center 对称扩展 extents，不偏移坐标轴。0 = 不扩展"));
            if (so.FindProperty("boundsExpand").floatValue > 0f && anim.targetRenderer is SkinnedMeshRenderer)
            {
                EditorGUILayout.HelpBox("Awake 时将自动扩展包围盒（以原始中心对称扩展，不偏移坐标轴）。", MessageType.Info);
                if (GUILayout.Button("立即应用包围盒扩展"))
                {
                    Undo.RecordObject(anim.targetRenderer, "应用包围盒扩展");
                    Undo.RecordObject(anim, "保存原始包围盒");
                    anim.ApplyBoundsExpand();
                    EditorUtility.SetDirty(anim.targetRenderer);
                    EditorUtility.SetDirty(anim);
                }
            }
            else if (so.FindProperty("boundsExpand").floatValue > 0f && anim.targetRenderer != null)
            {
                EditorGUILayout.HelpBox("包围盒扩展仅对 SkinnedMeshRenderer 有效。", MessageType.Warning);
            }
            
            // 还原按钮（始终显示，只要是 SkinnedMeshRenderer）
            if (anim.targetRenderer is SkinnedMeshRenderer smrForReset)
            {
                EditorGUILayout.Space(2);
                GUI.backgroundColor = new Color(1f, 0.7f, 0.4f);
                if (GUILayout.Button("还原 Mesh 与包围盒到 FBX 原始状态", GUILayout.Height(26)))
                {
                    RestoreMeshFromFBX(smrForReset);
                    Bounds? fbxBounds = GetOriginalFBXBounds(smrForReset);
                    if (fbxBounds.HasValue)
                    {
                        Undo.RecordObject(anim, "重置原始包围盒缓存");
                        anim.ResetBoundsTo(fbxBounds.Value);
                        EditorUtility.SetDirty(anim);
                    }
                }
                GUI.backgroundColor = Color.white;
            }
        }

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
} // namespace Vic.Editor
