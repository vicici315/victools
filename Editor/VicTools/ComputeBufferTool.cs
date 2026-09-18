// Compute Buffer Tool v4.4 修复合享材质剔除 / 添加 材质灯光选项不生效 -
//   1. 工具"剔除材质"按钮恢复关闭材质上的 _UsePointlight / _UseSpotlight / _UseSpotTexture 开关，
//      走 ComputeBufferLightManager.RemoveMaterial() 集中路径，避免与启动期自愈清理逻辑分叉
//   2. 工具"添加材质"按钮改为在循环结束后走 ComputeBufferLightManager.SetMaterialParameters 集中路径：
//      - 仅写 4 个开关的旧代码只下发 _UsePointlight / _UseSpotlight / _UseSpotTexture / _SpotTexture，
//        不下发数值参数（强度 / 范围 / 衰减 / 数量等），新增材质与已存在材质参数分叉
//      - OnValidate 的脏检查闸门命中"参数未变"时 UpdateAllMaterials 会被跳过，新增材质既不拿到开关也不拿到数值
//      - 现在改为调用 SetMaterialParameters(material)：开关 + 数值 + 纹理一次性按管理器当前状态全量下发
//      - 单点维护：未来扩展任何下发字段只需修改 SetMaterialParameters 一处，避免工具与运行时分叉
//   3. 保留 v4.3 的"非显式路径不覆盖用户主动勾选"原则：OnValidate 的反向同步逻辑、共享材质在跨场景
//      共享时的清理时机仍由 ComputeBuffer 4.4 的启动期 CleanupUnmanagedSceneMaterials 兜底
//   4. 工具按钮是用户的显式意图：剔除按下 = "我不再希望这个材质被本管理器接管"，必须关关键字；
//      添加按下 = "我需要这个材质按管理器当前配置立即参与自定义灯光"，必须立即套用管理器状态
// Compute Buffer Tool v4.3 修复合享材质"剔除材质"被擅自修改 - 剔除材质时不再强制关闭材质资产上的
//   _UsePointlight / _UseSpotlight / _UseSpotTexture 开关。理由：1) 材质资产是跨场景共享的；
//   2) Shader 端的 _CustomLightSystemActive 安全位（v8.5 + 4.1）已能防止残留关键字导致变黑；
//   3) 用户对材质做的主动勾选不应被工具反向覆盖。
// Compute Buffer Tool v4.2 材质按钮自动识别选中内容 - 选中灯光时按钮切换为"添加/剔除点灯·射灯"，其余情况仍为材质增删
// Compute Buffer Tool v4.0 同步 ComputeBuffer 4.0 重构 - 依赖的反射字段与接口均未变动，工具侧无需改动
// Compute Buffer Tool v3.7 修复工具管理器修改保存机制
// Compute Buffer Tool v3.6 匹配PBR_Mobile_NEW材质
// Compute Buffer Tool v3.0 支持最高2盏SpotLight，优化UI界面
// Compute Buffer Tool v2.0.3 编辑器模式实时更新优化 - 增强Compute Buffer系统与编辑器集成，支持非运行模式下点光效果预览

// Compute Buffer Tool v2.0
// 场景中使用PBR_Mobile_NEW材质的材质列表与管理器材质列表分开

// Compute Buffer Tool v1.1
// 无需管理器可使用PBR_Mobile_NEW材质收集
// 主要功能：
// 1. Compute Buffer系统管理 - 管理GPU端的点光源数据缓冲区
// 2. 材质管理 - 自动查找和管理使用PBR_Mobile_NEW着色器的材质
// 3. 点光源收集 - 自动收集场景中的点光源并转换为Compute Buffer格式
// 4. 材质选择工具 - 根据材质快速选择场景中使用该材质的模型
// 5. 缓冲区清理 - 完全重置Compute Buffer系统，释放GPU资源
// 6. 实时参数更新 - 动态更新材质参数和光源数据
//
// 使用场景：
// - 动态点光源管理
// - 材质批量操作
// - 场景对象快速选择
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.SceneManagement;

[UnityEditor.InitializeOnLoad]
public class ComputeBufferTool : EditorWindow
{
    private static readonly int CustomPointLightCount = Shader.PropertyToID("_CustomPointLightCount");
    private Vector2 _scrollPosition;
    private ComputeBufferLightManager _manager;
    private bool _computeBufferFileExists;
    private List<Material> _targetMaterials = new List<Material>();
    private Material _tempMaterial; // 临时存储用户手动选择的材质
    private Material _selectedMaterial; // 用于存储用户手动选择的材质
    private bool _extractMaterial; // 获取材质选项参数 - 默认不勾选
    private int _selectedObjectsCount; // 存储选择的对象数量

    private readonly List<Material> _toolTargetMaterials = new List<Material>();

    // `false` 表示这是一个普通的菜单项，点击后会执行对应的方法，如果设为 `true`，则表示这是一个验证函数，用于检查菜单项是否可用（启用/禁用状态）
    // 优先级 100，默认优先级是1000，设为100会让菜单项显示在较靠前的位置
    [MenuItem("Tools/VicTools(YD)/CustomLightTool: ComputeBufferTool", false, 2000)]
    public static void ShowWindow()
    {
        // 设置窗口宽度和高度
        var window = EditorWindow.GetWindow<ComputeBufferTool>("Compute Buffer Tool v4.4");
        window.minSize = new Vector2(400, 600);  // 最小宽度，最小高度
        window.maxSize = new Vector2(1000, 1200); // 最大宽度1200，最大高度1000
        
        // 优化：从 EditorPrefs 恢复上次窗口位置，如果没有则使用默认居中位置
        // LoadWindowPosition(window);
    }

    // private static void LoadWindowPosition(EditorWindow window)
    // {
    //     // 使用唯一键名保存窗口位置
    //     string keyPrefix = "ComputeBufferTool_Window_";
        
    //     // 尝试读取保存的位置
    //     float x = EditorPrefs.GetFloat(keyPrefix + "x", -1);
    //     float y = EditorPrefs.GetFloat(keyPrefix + "y", -1);
    //     float width = EditorPrefs.GetFloat(keyPrefix + "width", 443);
    //     float height = EditorPrefs.GetFloat(keyPrefix + "height", 717);
        
    //     // 如果保存的位置有效，则使用保存的位置
    //     if (x >= 0 && y >= 0)
    //     {
    //         window.position = new Rect(x, y, width, height);
    //     }
    //     else
    //     {
    //         // 否则使用默认居中位置
    //         Vector2 center = new Vector2(Screen.currentResolution.width / 2, Screen.currentResolution.height / 2);
    //         Vector2 size = new Vector2(443, 717);
    //         window.position = new Rect(center - size / 2, size);
    //     }
    // }

    /// 窗口关闭时保存当前位置到 EditorPrefs
    private void OnDestroy()
    {
        // ● 取消订阅，避免窗口销毁后仍收到选择变化回调
        Selection.selectionChanged -= Repaint;
        SaveWindowPosition();
    }

    /// 保存窗口位置到 EditorPrefs
    private void SaveWindowPosition()
    {
        const string keyPrefix = "ComputeBufferTool_Window_";
        EditorPrefs.SetFloat(keyPrefix + "x", position.x);
        EditorPrefs.SetFloat(keyPrefix + "y", position.y);
        EditorPrefs.SetFloat(keyPrefix + "width", position.width);
        EditorPrefs.SetFloat(keyPrefix + "height", position.height);
    }

    private void OnEnable()
    {
        // 检查ComputeBuffer.cs文件是否存在 - 使用 Unity 的包虚拟路径
        string packageRelativePath = "Runtime/Scripts/Shader/ComputeBuffer.cs";
        string computeBufferPath = Path.GetFullPath(
            Path.Combine("Packages/com.youdoo.victools", packageRelativePath)   //名字取package.json中的name:
        );
        _computeBufferFileExists = File.Exists(computeBufferPath);

        if (_computeBufferFileExists)
        {
            // 查找场景中的ComputeBufferLightManager实例
            FindManager();
        }
        else
        {
            Debug.LogWarning($"ComputeBuffer.cs文件不存在于路径: {computeBufferPath}");
        }

        // ● 选中对象变化时重绘窗口，使"材质 / 灯光"按钮的自动识别文案即时切换
        Selection.selectionChanged -= Repaint;
        Selection.selectionChanged += Repaint;

        // 工具启动时执行指定函数
        OnToolStartup();
    }

    private void OnGUI()
    {
        // 使用垂直布局，将内容分为两部分：可滚动区域和底部固定区域
        EditorGUILayout.BeginVertical();
        
        // GUILayout.Label("Compute Buffer Tool", EditorStyles.boldLabel);

        // [SEARCH: 管理器状态] - 管理器状态显示区域
        EditorGUILayout.LabelField("管理器状态", EditorStyles.boldLabel != null ? EditorStyles.boldLabel : new GUIStyle());

        if (!_computeBufferFileExists)
        {
            EditorGUILayout.HelpBox("ComputeBuffer.cs 文件不存在，Compute Buffer 功能不可用。", MessageType.Warning);
        }
        else
        {
            // 检查manager是否有效，避免访问已销毁的对象
            var hasManager = _manager && !_manager.Equals(null);

            if (!hasManager)
            {
                EditorGUILayout.HelpBox("未找到ComputeBufferLightManager实例。请确保场景中存在该组件。", MessageType.Warning);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("查找管理器", GUILayout.Height(28)))
                {
                    FindManager();
                }

                if (GUILayout.Button("创建管理器对象", GUILayout.Height(28)))
                {
                    CreateManagerObject();
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                // 管理器名称作为可点击按钮 - 添加额外的null检查
                var managerButtonContent = new GUIContent($"当前管理器(点击选择管理器载体): {_manager.name} ←", "点击选择场景中挂载ComputeBuffer组件的游戏对象");
                if (GUILayout.Button(managerButtonContent, EditorStyles.boldLabel != null ? EditorStyles.boldLabel : new GUIStyle()))
                {
                    Selection.activeObject = _manager.gameObject;
                    EditorGUIUtility.PingObject(_manager.gameObject);
                }

                // 添加额外的null检查，确保manager对象仍然有效
                if (_manager && !_manager.Equals(null))
                {
                    EditorGUILayout.LabelField($"控制材质数量: {_manager.GetControlledMaterialCount()}");
                    
                    // 计算总活动光源数量（点光源 + 聚光灯）
                    int pointLightCount = _manager.UpdateLightsBuffer();
                    int spotLightCount = _manager.UpdateSpotLightsBuffer();
                    int totalLightCount = pointLightCount + spotLightCount;
                    EditorGUILayout.LabelField($"活动光源数量: {totalLightCount} (点光: {pointLightCount}, 聚光: {spotLightCount})");

                    // 添加其他编辑器工具按钮 - 并排排列
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("更新材质参数"))
                    {
                        if (_manager && !_manager.Equals(null))
                        {
                            RecordManagerUndo("ComputeBuffer 更新材质参数");
                            _manager.UpdateAllMaterials();
                            MarkManagerModified(_manager.targetMaterials);
                        }
                    }

                    if (GUILayout.Button("重置到默认值"))
                    {
                        if (_manager && !_manager.Equals(null))
                        {
                            RecordManagerUndo("ComputeBuffer 重置材质默认值");
                            _manager.ResetMaterialToDefaults();
                            MarkManagerModified(_manager.targetMaterials);
                        }
                    }

                    if (GUILayout.Button("删除管理器载体（仅对象）"))
                    {
                        DeleteManagerObject();

                    }
                    EditorGUILayout.EndHorizontal();

                    // [SEARCH: 编辑器工具] - 编辑器工具按钮区域
                    if (_computeBufferFileExists)
                    {
                        EditorGUILayout.Space();

                        EditorGUILayout.BeginHorizontal();
                        if (GUILayout.Button(new GUIContent("获取材质列表", "获取管理器材质列表"), GUILayout.Height(30)))
                        {
                            try
                            {

                                if (_manager && !_manager.Equals(null))
                                {
                                    // manager.FindPBRMobileMaterials();
                                    RefreshTargetMaterials();
                                }
                                // else
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"刷新材质列表时出错: {e.Message}");
                                Debug.LogException(e);
                                EditorUtility.DisplayDialog("错误", $"刷新材质列表时出错: {e.Message}", "确定");
                            }
                        }
                        // 添加查找PBR_Mobile_NEW材质的按钮
                        if (GUILayout.Button(new GUIContent("●收集材质", "收集场景中所有PBR_Mobile_NEW材质到管理器"), GUILayout.Height(30)))
                        {
                            try
                            {
                                // 如果管理器不存在，自动创建
                                if (!_manager || _manager.Equals(null))
                                {
                                    CreateManagerObject();
                                }
                                if (_manager != null && !_manager.Equals(null))
                                {
                                    RecordManagerUndo("ComputeBuffer 收集材质");
                                    _manager.EditorFindPBRMobileMaterials();
                                    MarkManagerModified(_manager.targetMaterials);
                                    RefreshTargetMaterials();
                                }
                                // else
                                // {
                                //     EditorUtility.DisplayDialog("错误", "无法创建或找到ComputeBufferLightManager实例，无法查找PBR_Mobile_NEW材质。", "确定");
                                // }
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"查找PBR_Mobile_NEW材质时出错: {e.Message}");
                                Debug.LogException(e);
                                EditorUtility.DisplayDialog("错误", $"查找PBR_Mobile_NEW材质时出错: {e.Message}", "确定");
                            }
                        }

                        if (GUILayout.Button(new GUIContent("●收集点光源", "收集场景中所有点光源到管理器"), GUILayout.Height(30)))
                        {
                            try
                            {
                                // 如果管理器不存在，自动创建
                                if (!_manager || _manager.Equals(null))
                                {
                                    CreateManagerObject();
                                }

                                if (_manager && !_manager.Equals(null))
                                {
                                    RecordManagerUndo("ComputeBuffer 收集点光源");
                                    _manager.CollectScenePointLights();
                                    MarkManagerModified();
                                    // 强制刷新UI以更新活动光源数量显示
                                    Repaint();
                                }
                                else
                                {
                                    EditorUtility.DisplayDialog("错误", "无法创建或找到ComputeBufferLightManager实例，无法收集场景点光源。", "确定");
                                }
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"收集场景点光源时出错: {e.Message}");
                                Debug.LogException(e);
                                EditorUtility.DisplayDialog("错误", $"收集场景点光源时出错: {e.Message}", "确定");
                            }
                        }
                        
                        if (GUILayout.Button(new GUIContent("●收集聚光灯", "收集场景中所有聚光灯到管理器"), GUILayout.Height(30)))
                        {
                            try
                            {
                                // 如果管理器不存在，自动创建
                                if (!_manager || _manager.Equals(null))
                                {
                                    CreateManagerObject();
                                }

                                if (_manager && !_manager.Equals(null))
                                {
                                    RecordManagerUndo("ComputeBuffer 收集聚光灯");
                                    _manager.CollectSceneSpotLights();
                                    MarkManagerModified();
                                    // 强制刷新UI以更新活动光源数量显示
                                    Repaint();
                                }
                                else
                                {
                                    EditorUtility.DisplayDialog("错误", "无法创建或找到ComputeBufferLightManager实例，无法收集场景聚光灯。", "确定");
                                }
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"收集场景聚光灯时出错: {e.Message}");
                                Debug.LogException(e);
                                EditorUtility.DisplayDialog("错误", $"收集场景聚光灯时出错: {e.Message}", "确定");
                            }
                        }
                        EditorGUILayout.EndHorizontal();
//选择按钮行（自动识别选中内容：选中灯光 -> 灯光增删，其余 -> 材质增删）
                        bool lightsSelected = HasSelectedLights();
                        EditorGUILayout.BeginHorizontal();
                        GUI.backgroundColor = new Color(0.98f,0.3f,0.5f);
                        var removeButtonContent = lightsSelected
                            ? new GUIContent("剔除点灯/射灯 ↑", "从管理器 剔除 场景中选中 的点光源 / 聚光灯")
                            : new GUIContent("剔除材质 ↑", "从管理器 剔除 场景中选中 模型的材质 或 Project中选择的材质球");
                        if (GUILayout.Button(removeButtonContent, GUILayout.Height(22)))
                        {
                            if (lightsSelected)
                            {
                                DelLightsFromManager();
                            }
                            else
                            {
                                DelMaterialsFromManager();
                            }
                        }
                        GUI.backgroundColor = Color.magenta;
                        var addButtonContent = lightsSelected
                            ? new GUIContent("添加点灯/射灯 ↓", "向管理器 添加 场景中选中 的点光源 / 聚光灯")
                            : new GUIContent("添加材质 ↓", "向管理器 添加 场景中选中 模型的材质 或 Project中选择的材质球");
                        if (GUILayout.Button(addButtonContent, GUILayout.Height(22)))
                        {
                            if (lightsSelected)
                            {
                                AddLightsToManager();
                            }
                            else
                            {
                                AddMaterialsToManager();
                            }
                        }
                        GUI.backgroundColor = Color.cyan;
                        if (GUILayout.Button(new GUIContent("选择材质", "选择管理器中收集的所有材质球"), GUILayout.Height(22)))
                        {
                            SelectAllMaterialsInManager();
                        }
                        GUI.backgroundColor = Color.white;
                        EditorGUILayout.EndHorizontal();

                        EditorGUILayout.LabelField("管理器材质列表", EditorStyles.miniBoldLabel != null ? EditorStyles.miniBoldLabel : new GUIStyle());
                        // [SEARCH: 材质列表显示] - 材质列表显示和选择区域
                        // 显示材质列表并添加选择按钮，选择场景中使用了该材质的模型
                        if (_targetMaterials != null && _targetMaterials.Count > 0)
                        {
                            EditorGUILayout.HelpBox($"管理器有 {_targetMaterials.Count} 个 PBR_Mobile_NEW 材质在列表中。选择一个材质来查找使用它的模型：", MessageType.Info);

        // 第一部分：可滚动区域
                            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));
                            for (var i = 0; i < _targetMaterials.Count; i++)
                            {
                                var material = _targetMaterials[i];
                                if (!material) continue;
                                EditorGUILayout.BeginHorizontal();
                                // 保存当前标签宽度
                                float originalLabelWidth = EditorGUIUtility.labelWidth;
                                // 设置标签宽度像素
                                EditorGUIUtility.labelWidth = 80;
                                EditorGUILayout.ObjectField($"材质: {i+1}", material, typeof(Material), false);
                                // 恢复原始标签宽度
                                EditorGUIUtility.labelWidth = originalLabelWidth;
                                GUI.backgroundColor = Color.cyan;
                                if (GUILayout.Button("选择模型", GUILayout.Width(80)))
                                {
                                    SelectObjectsUsingMaterial(material);
                                }
                                GUI.backgroundColor = Color.white;
                                EditorGUILayout.EndHorizontal();
                            }
        // 结束滚动视图
                            EditorGUILayout.EndScrollView();
                        }
                        else
                        {
                            EditorGUILayout.HelpBox("材质列表为空，请先点击【查找PBR_Mobile_NEW材质】按钮来填充列表。", MessageType.Warning);
                        }
                    }

                    // 清除计算缓冲区功能
                    EditorGUILayout.LabelField("缓冲区及管理器", EditorStyles.boldLabel != null ? EditorStyles.boldLabel : new GUIStyle());
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("清除所有计算缓冲区"))
                    {
                        ClearAllComputeBuffers();
                    }
                    if (GUILayout.Button("删除场景管理器（全面）"))
                    {
                        DeleteCurrentManager();
                    }
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    // 如果manager变为null，重新查找
                    FindManager();
                }
            }
        }
        
        // 第二部分：底部固定区域 - 自定义材质选择工具
        // 这个区域始终显示在窗口底部，不会被_toolTargetMaterials列表顶没
        EditorGUILayout.Space();
        
        // 添加分隔线，区分可滚动区域和固定区域
        GUIStyle separatorStyle = new GUIStyle();
        separatorStyle.normal.background = CreateColorTexture(1, 1, new Color(0.7f, 0.5f, 0.0f)); // 黄色分隔线
        separatorStyle.normal.background.hideFlags = HideFlags.HideAndDontSave;
        GUILayout.Box("", separatorStyle, GUILayout.Height(2), GUILayout.ExpandWidth(true));
        
        // [SEARCH: 自定义材质选择工具] - 自定义材质选择工具区域
        // 自定义材质选择功能 - 始终显示，不依赖于管理器状态
        EditorGUILayout.BeginVertical(GUI.skin.box);
        
        EditorGUILayout.LabelField("自定义材质选择工具", EditorStyles.boldLabel != null ? EditorStyles.boldLabel : new GUIStyle());

        EditorGUILayout.BeginHorizontal();
        // 获取材质选项参数 - 默认不勾选
        // 是否提取选中对象材质选项
        bool previousExtractMaterial = _extractMaterial;
        _extractMaterial = EditorGUILayout.Toggle("获取选中对象材质", _extractMaterial, GUILayout.Width(170));
        _tempMaterial = EditorGUILayout.ObjectField("", _tempMaterial, typeof(Material), false) as Material;
        EditorGUILayout.EndHorizontal();

        // 添加事件处理：当extractMaterial从false变为true时，清除selectedMaterial
        if (!_extractMaterial && previousExtractMaterial)
        {
            _selectedMaterial = null;
        }

        EditorGUILayout.BeginHorizontal();
        _selectedMaterial = EditorGUILayout.ObjectField("指定材质:", _selectedMaterial, typeof(Material), false) as Material;
        GUIContent selectButtonContent = new GUIContent("选择相同材质对象", "选择使用指定材质的所有对象\n如果未指定材质，则自动获取场景中选择物体的材质");
        if (GUILayout.Button(selectButtonContent, GUILayout.Width(110)))
        {
            // 添加安全检查，确保不会在无效对象上操作
            if (_selectedMaterial && !_selectedMaterial.Equals(null))
            {
                SelectObjectsUsingMaterial(_selectedMaterial);
            }
            else
            {
                //选择场景中与选择物体相同材质的物体
                SelectObjectsUsingSelectedObjectMaterial();
            }
        }
        EditorGUILayout.EndHorizontal();

        // 显示选择的对象数量
        EditorGUILayout.LabelField($"选中对象数量: 【 {_selectedObjectsCount} 】", EditorStyles.miniLabel != null ? EditorStyles.miniLabel : new GUIStyle());
        
        EditorGUILayout.EndVertical();
        
        // 结束整个垂直布局
        EditorGUILayout.EndVertical();
    }

    // 删除管理器载体
    // ReSharper disable Unity.PerformanceAnalysis
    private void DeleteManagerObject()
    {
        if (!_computeBufferFileExists)
        {
            Debug.LogWarning("ComputeBuffer.cs文件不存在，无法删除管理器载体");
            return;
        }

        try
        {
            if (!_manager || _manager.Equals(null))
            {
                Debug.LogWarning("无法删除管理器载体：当前没有活动的ComputeBufferLightManager实例");
                return;
            }

            var targetObject = _manager.gameObject;
            if (targetObject)
            {
                var scene = targetObject.scene;

                // ● 使用Undo删除，使其可撤销，并让场景被标记为"未保存"
                Undo.DestroyObjectImmediate(targetObject);
                MarkSceneDirty(scene);
                _manager = null;
                _targetMaterials.Clear();
                Repaint();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"删除对象出错: {e.Message}");
            Debug.LogException(e);
            EditorUtility.DisplayDialog("错误", $"删除管理器载体对象时出错: {e.Message}", "确定");
        }
    }

    // ReSharper disable Unity.PerformanceAnalysis
    /// 创建管理器对象 - 自动创建空物体并挂载ComputeBufferLightManager组件
    private void CreateManagerObject()
    {
        if (!_computeBufferFileExists)
        {
            Debug.LogWarning("ComputeBuffer.cs文件不存在，无法创建管理器对象");
            EditorUtility.DisplayDialog("错误", "ComputeBuffer.cs文件不存在，无法创建ComputeBufferLightManager组件。", "确定");
            return;
        }

        try
        {
            // 检查场景中是否已存在ComputeBufferLightManager对象
            var existingManagers = FindObjectsByType<ComputeBufferLightManager>(FindObjectsSortMode.None);

            if (existingManagers is { Length: > 0 })
            {
                // 如果已存在管理器，选择第一个并提示用户
                _manager = existingManagers[0];

                // 确保manager对象有效
                if (_manager && _manager.gameObject)
                {
                    // 选择并聚焦到现有对象
                    Selection.activeObject = _manager.gameObject;
                    EditorGUIUtility.PingObject(_manager.gameObject);

                    // 刷新材质列表
                    RefreshTargetMaterials();

                    Debug.Log($"场景中已存在ComputeBufferLightManager对象: {_manager.name}");
                    Debug.Log("已自动选择现有管理器对象，无需重复创建。");

                    // 显示提示对话框
                    EditorUtility.DisplayDialog(
                        "管理器已存在",
                        $"场景中已存在ComputeBufferLightManager对象 '{_manager.name}'。\n\n已自动选择现有对象，无需重复创建。",
                        "确定"
                    );
                }
                else
                {
                    Debug.LogWarning("找到的管理器对象无效，将继续创建新对象");
                    // 继续执行创建新对象的逻辑
                }

                return; // 不创建新对象，直接返回
            }

            // 创建新的游戏对象
            var managerObject = new GameObject("ComputeBufferLightManager");

            // ● 登记创建操作到撤销栈，使新建对象可撤销且场景被标记为"未保存"
            Undo.RegisterCreatedObjectUndo(managerObject, "创建 ComputeBuffer 管理器");

            // 添加ComputeBufferLightManager组件
            _manager = managerObject.AddComponent<ComputeBufferLightManager>();

            // 选择并聚焦到新创建的对象
            Selection.activeObject = managerObject;
            EditorGUIUtility.PingObject(managerObject);

            // ● 标记新对象与场景为已修改
            EditorUtility.SetDirty(_manager);
            MarkSceneDirty(managerObject.scene);

            // 刷新材质列表
            RefreshTargetMaterials();

            Debug.Log($"已成功创建ComputeBufferLightManager对象: {managerObject.name}");
            Debug.Log("管理器对象已创建并挂载ComputeBufferLightManager组件，现在可以使用所有功能。");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"创建管理器对象时出错: {e.Message}");
            Debug.LogException(e);
            EditorUtility.DisplayDialog("错误", $"创建管理器对象时出错: {e.Message}", "确定");
        }
    }

    // [SEARCH: 缓冲区管理功能] - 缓冲区管理功能区域
    // ReSharper disable Unity.PerformanceAnalysis
    /// 清除所有计算缓冲区 - 完全重置Compute Buffer系统
    /// 这个功能用于：
    /// 1. 释放现有的GraphicsBuffer资源，避免内存泄漏
    /// 2. 重置光源计数为0，清空所有光源数据
    /// 3. 重新初始化缓冲区，恢复到初始状态
    /// 4. 清理Shader全局属性，确保GPU端数据同步
    /// 5. 停止所有动画协程，防止残留效果
    /// 
    /// 使用场景：
    /// - 调试时重置系统状态
    /// - 切换场景前清理资源
    /// - 解决GPU端数据不一致问题
    /// - 性能优化和内存管理
    private void ClearAllComputeBuffers()
    {
        if (!_computeBufferFileExists)
        {
            Debug.LogWarning("ComputeBuffer.cs文件不存在，无法清除计算缓冲区");
            return;
        }

        if (!_manager || _manager.Equals(null))
        {
            Debug.LogWarning("无法清除计算缓冲区：未找到ComputeBufferLightManager实例");
            return;
        }

        // ● 反射写入私有字段前登记撤销点
        RecordManagerUndo("ComputeBuffer 清除计算缓冲区");

        try
        {
            // 1. 停止所有动画协程，防止残留效果影响新状态
            // 这包括回弹动画、闪烁效果等所有正在运行的协程
            if (_manager && !_manager.Equals(null))
            {
                _manager.StopAllCoroutines();
                Debug.Log("已停止所有动画协程");
            }

            // 2. 释放现有的GraphicsBuffer资源，避免内存泄漏
            // GraphicsBuffer是GPU资源，必须显式释放
            var lightsBufferField = typeof(ComputeBufferLightManager).GetField("_lightsBuffer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (lightsBufferField != null && _manager && !_manager.Equals(null))
            {
                if (lightsBufferField.GetValue(_manager) is GraphicsBuffer currentBuffer)
                {
                    currentBuffer.Release();
                    lightsBufferField.SetValue(_manager, null);
                    Debug.Log("已释放GraphicsBuffer资源");
                }
            }

            // 3. 重置光源计数和数据结构
            // 将当前光源数量重置为0，清空所有光源数据
            var currentLightCountField = typeof(ComputeBufferLightManager).GetField("_currentLightCount",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (currentLightCountField != null && _manager && !_manager.Equals(null))
            {
                currentLightCountField.SetValue(_manager, 0);
            }

            // 4. 清空光源数据数组，确保没有残留数据
            var lightsDataField = typeof(ComputeBufferLightManager).GetField("_lightsData",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (lightsDataField != null && _manager && !_manager.Equals(null))
            {
                if (lightsDataField.GetValue(_manager) is ComputeBufferLightManager.CustomPointLight[] lightsData)
                {
                    System.Array.Clear(lightsData, 0, lightsData.Length);
                }
            }

            // 5. 更新Shader全局属性，通知GPU端数据已清空
            // 将全局光源数量设置为0，Shader将不会处理任何光源
            Shader.SetGlobalInt(CustomPointLightCount, 0);

            // 6. 重新初始化Compute Buffer系统
            // 调用私有方法重新创建GraphicsBuffer和数据结构
            var initializeMethod = typeof(ComputeBufferLightManager).GetMethod("InitializeComputeBuffer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (initializeMethod != null && _manager && !_manager.Equals(null))
            {
                initializeMethod.Invoke(_manager, null);
                Debug.Log("已重新初始化Compute Buffer系统");
            }

            // 7. 强制更新材质参数，确保所有材质状态同步
            if (_manager && !_manager.Equals(null))
            {
                _manager.UpdateAllMaterials();
            }

            // ● 标记管理器与场景为已修改
            MarkManagerModified(_manager ? _manager.targetMaterials : null);

            Debug.Log("▲ 所有计算缓冲区已成功清除并重置！系统已恢复到初始状态。");

            // 刷新编辑器显示，确保UI状态更新
            RefreshTargetMaterials();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"清除计算缓冲区时出错: {e.Message}");
            Debug.LogException(e);
            EditorUtility.DisplayDialog("错误", $"清除计算缓冲区时出错: {e.Message}", "确定");
        }
    }

    // ReSharper disable Unity.PerformanceAnalysis
    /// 选择场景中与选择物体相同材质的物体
    /// 自动获取当前场景中选择的物体的材质，并选择所有使用相同材质的物体
    private void SelectObjectsUsingSelectedObjectMaterial()
    {
        // 检查场景中是否选择了物体
        if (!Selection.activeGameObject)
        {
            Debug.LogWarning("无法选择物体：场景中没有选择任何物体");
            EditorUtility.DisplayDialog("选择错误", "请先在场景中选择一个物体，然后点击此按钮。", "确定");
            return;
        }

        // 获取选择物体的Renderer组件
        Renderer selectedRenderer = Selection.activeGameObject.GetComponent<Renderer>();
        if (!selectedRenderer)
        {
            Debug.LogWarning($"无法获取材质：选择的物体 '{Selection.activeGameObject.name}' 没有Renderer组件");
            EditorUtility.DisplayDialog("选择错误", $"选择的物体 '{Selection.activeGameObject.name}' 没有Renderer组件，无法获取材质。", "确定");
            return;
        }

        // 获取选择物体的材质
        Material[] selectedMaterials = selectedRenderer.sharedMaterials;
        if (selectedMaterials == null || selectedMaterials.Length == 0)
        {
            Debug.LogWarning($"无法获取材质：选择的物体 '{Selection.activeGameObject.name}' 没有材质");
            EditorUtility.DisplayDialog("选择错误", $"选择的物体 '{Selection.activeGameObject.name}' 没有材质。", "确定");
            return;
        }

        // 使用第一个材质作为目标材质
        Material targetMaterial = selectedMaterials[0];
        if (!targetMaterial)
        {
            Debug.LogWarning($"无法获取材质：选择的物体 '{Selection.activeGameObject.name}' 的材质为空");
            EditorUtility.DisplayDialog("选择错误", $"选择的物体 '{Selection.activeGameObject.name}' 的材质为空。", "确定");
            return;
        }

        // 调用现有的材质选择方法
        SelectObjectsUsingMaterial(targetMaterial);
        _tempMaterial = targetMaterial;
        if (_extractMaterial)
        {
            _selectedMaterial = targetMaterial; // 更新选中的材质
        }
    }

    // ReSharper disable Unity.PerformanceAnalysis
    private void FindManager()
    {
        if (!_computeBufferFileExists)
        {
            Debug.LogWarning("ComputeBuffer.cs文件不存在，无法查找管理器");
            return;
        }

        try
        {
            _manager = FindFirstObjectByType<ComputeBufferLightManager>();
            if (_manager)
            {
                RefreshTargetMaterials();
            }
            else
            {
                Debug.LogWarning("未找到ComputeBufferLightManager实例。请确保场景中存在该组件。");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"查找ComputeBufferLightManager时出错: {e.Message}");
            Debug.LogException(e);
        }
    }

    // 只查找PBR Mobile材质到选择工具中
    // ReSharper disable Unity.PerformanceAnalysis
    private void ToolFindPbrMobileMaterials()
    {
        _toolTargetMaterials.Clear();

        // ● 查找所有Renderer（包含未激活的物体）
        var allRenderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var renderer in allRenderers)
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (!material || material.shader.name != "Custom/PBR_Mobile_NEW") continue;
                // ● 同时添加到targetMaterials列表，方便在Inspector中查看
                if (!_toolTargetMaterials.Contains(material))
                {
                    _toolTargetMaterials.Add(material);
                }
            }
        }
        Debug.Log($"找到 {_toolTargetMaterials.Count} 个使用PBR_Mobile_NEW Shader的材质");
    }

    private void RefreshTargetMaterials()
    {
        if (!_computeBufferFileExists)
        {
            _targetMaterials.Clear();
            return;
        }

        if (_manager)
        {
            _targetMaterials = new List<Material>(_manager.targetMaterials);
        }
    }

    // ==========================================================
    // ● 编辑器脏标记 / 保存机制
    /// 在修改管理器序列化字段【之前】调用，登记撤销点
    /// Undo.RecordObject 会在撤销栈中记录当前状态，同时把管理器所在场景标记为已修改
    private void RecordManagerUndo(string undoName)
    {
        if (!_manager || _manager.Equals(null)) return;
        Undo.RecordObject(_manager, undoName);
    }

    /// 在修改管理器序列化字段【之后】调用
    /// 1. EditorUtility.SetDirty：让管理器组件本身的修改被识别
    /// 2. MarkSceneDirty：让场景出现"未保存"标记（标题栏 *），可通过 File/Save 保存
    /// 3. 若同时改动了材质资源，则标记材质为脏并写盘
    private void MarkManagerModified(IEnumerable<Material> dirtyMaterials = null)
    {
        if (!_manager || _manager.Equals(null)) return;

        EditorUtility.SetDirty(_manager);
        MarkSceneDirty(_manager.gameObject.scene);
        MarkMaterialsDirtyAndSave(dirtyMaterials);
    }

    /// 安全地把场景标记为"已修改/未保存"
    /// EditorSceneManager.MarkSceneDirty 对未保存过的新场景会报错，这里做保护
    private static void MarkSceneDirty(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        if (string.IsNullOrEmpty(scene.path)) return; // 新建但尚未保存到磁盘的场景无法标记

        try
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"标记场景为已修改时出错: {e.Message}");
        }
    }

    /// 标记材质资源为脏并立即写盘，避免材质球参数修改在关闭编辑器后丢失
    private static void MarkMaterialsDirtyAndSave(IEnumerable<Material> materials)
    {
        if (materials == null) return;

        var hasPersistentAsset = false;
        foreach (var material in materials)
        {
            if (!material || material.Equals(null)) continue;

            EditorUtility.SetDirty(material);
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material)))
            {
                hasPersistentAsset = true;
            }
        }

        if (hasPersistentAsset)
        {
            AssetDatabase.SaveAssets();
        }
    }

    // ReSharper disable Unity.PerformanceAnalysis
    /// 删除当前场景中的ComputeBufferLightManager管理器对象
    /// 安全地删除管理器对象，包括清理GPU资源和重置系统状态
    private void DeleteCurrentManager()
    {
        if (!_computeBufferFileExists)
        {
            Debug.LogWarning("ComputeBuffer.cs文件不存在，无法删除管理器");
            EditorUtility.DisplayDialog("删除失败", "ComputeBuffer.cs文件不存在，无法删除ComputeBufferLightManager管理器。", "确定");
            return;
        }

        if (!_manager || _manager.Equals(null))
        {
            Debug.LogWarning("无法删除管理器：当前没有活动的ComputeBufferLightManager实例");
            EditorUtility.DisplayDialog("删除失败", "当前没有活动的ComputeBufferLightManager实例可以删除。", "确定");
            return;
        }

        try
        {
            // 获取管理器对象的名称用于日志记录
            var managerName = _manager.name;
            var managerObject = _manager.gameObject;

            // 首先清理计算缓冲区资源
            ClearAllComputeBuffers();

            // 确认删除操作
            var confirmDelete = EditorUtility.DisplayDialog(
                "确认删除管理器",
                $"确定要删除管理器对象 '{managerName}' 吗？\n\n" +
                "此操作将：\n" +
                "• 删除场景中的管理器对象\n" +
                "• 清理所有GPU计算缓冲区资源\n" +
                "• 重置材质参数到默认状态\n" +
                "• 停止所有动画效果",
                "确定删除",
                "取消"
            );

            if (!confirmDelete)
            {
                Debug.Log("用户取消了管理器删除操作");
                return;
            }

            // 销毁管理器对象
            if (managerObject)
            {
                var scene = managerObject.scene;

                // 使用Undo.DestroyObjectImmediate在编辑器模式下立即销毁对象，保证可撤销且场景被标记为"未保存"
                Undo.DestroyObjectImmediate(managerObject);
                MarkSceneDirty(scene);
                Debug.Log($"▲ 已成功删除ComputeBufferLightManager对象: {managerName}");
            }

            // 重置管理器引用
            _manager = null;
            _targetMaterials.Clear();

            // 重置Shader全局属性，确保GPU端数据同步
            Shader.SetGlobalInt(CustomPointLightCount, 0);

            // 显示成功消息
            EditorUtility.DisplayDialog(
                "删除成功",
                $"ComputeBufferLightManager对象 '{managerName}' 已成功删除。\n\n" +
                "所有计算缓冲区资源已清理，系统已重置。",
                "确定"
            );

            // 刷新UI状态
            Repaint();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"删除管理器对象时出错: {e.Message}");
            Debug.LogException(e);
            EditorUtility.DisplayDialog("错误", $"删除管理器对象时出错: {e.Message}", "确定");
        }
    }

    // [SEARCH: 材质选择方法] - 材质选择方法区域
    // private void SelectObjectsUsingMaterial(Material targetMaterial)
    // {
    //     if (targetMaterial == null)
    //     {
    //         Debug.LogWarning("无法选择模型：材质为空");
    //         selectedObjectsCount = 0;
    //         return;
    //     }

    //     // 获取场景中的所有根对象
    //     var allGameObjects = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().GetRootGameObjects();
    //     List<GameObject> objectsUsingMaterial = new List<GameObject>();

    //     // 遍历所有对象查找使用指定材质的Renderer
    //     foreach (var rootObject in allGameObjects)
    //     {
    //         var renderers = rootObject.GetComponentsInChildren<Renderer>(true);

    //         foreach (Renderer renderer in renderers)
    //         {
    //             foreach (Material material in renderer.sharedMaterials)
    //             {
    //                 if (material == targetMaterial)
    //                 {
    //                     objectsUsingMaterial.Add(renderer.gameObject);
    //                     break; // 找到匹配后跳出内层循环
    //                 }
    //             }
    //         }
    //     }

    //     // 更新选择的对象数量
    //     selectedObjectsCount = objectsUsingMaterial.Count;

    //     if (objectsUsingMaterial.Count > 0)
    //     {
    //         // 选择所有使用该材质的对象
    //         Selection.objects = objectsUsingMaterial.ToArray();
    //         Debug.Log($"已选择 {objectsUsingMaterial.Count} 个使用材质 '{targetMaterial.name}' 的模型");

    //         // 如果只有一个对象，聚焦到该对象
    //         if (objectsUsingMaterial.Count == 1)
    //         {
    //             EditorGUIUtility.PingObject(objectsUsingMaterial[0]);
    //         }
    //     }
    //     else
    //     {
    //         Debug.LogWarning($"场景中没有找到使用材质 '{targetMaterial.name}' 的模型");
    //     }
    // }
    // [SEARCH: 材质选择方法] - 材质选择方法区域
    // ReSharper disable Unity.PerformanceAnalysis
    /// 查找并选择场景中使用指定材质的所有模型
    /// <param name="targetMaterial">要查找的材质</param>
    private void SelectObjectsUsingMaterial(Material targetMaterial)
    {
        if (!targetMaterial)
        {
            Debug.LogWarning("无法选择模型：材质为空");
            _selectedObjectsCount = 0;
            return;
        }

        // 获取场景中的所有根对象
        var allGameObjects = SceneManager.GetActiveScene().GetRootGameObjects();
        var objectsUsingMaterial = new List<GameObject>();

        // 遍历所有对象查找使用指定材质的Renderer
        foreach (var rootObject in allGameObjects)
        {
            var renderers = rootObject.GetComponentsInChildren<Renderer>(true);

            objectsUsingMaterial.AddRange(from renderer in renderers where renderer.sharedMaterials.Any(material => material == targetMaterial) select renderer.gameObject);
        }

        // 检查是否按住Ctrl键（Windows）或Command键（Mac）
        bool isAdditive = Event.current != null && (Event.current.control || Event.current.command);
        
        if (isAdditive && objectsUsingMaterial.Count > 0)
        {
            // 加选模式：将新找到的对象添加到当前选择中
            var currentSelection = new List<Object>(Selection.objects);
            foreach (var obj in objectsUsingMaterial)
            {
                if (!currentSelection.Contains(obj))
                {
                    currentSelection.Add(obj);
                }
            }
            Selection.objects = currentSelection.ToArray();
            _selectedObjectsCount = currentSelection.Count;
            Debug.Log($"已加选 {objectsUsingMaterial.Count} 个使用材质 '{targetMaterial.name}' 的模型，当前共选中 {_selectedObjectsCount} 个对象");
        }
        else if (objectsUsingMaterial.Count > 0)
        {
            // 普通模式：替换当前选择
            Selection.objects = objectsUsingMaterial.ToArray();
            _selectedObjectsCount = objectsUsingMaterial.Count;
            Debug.Log($"已选择 {_selectedObjectsCount} 个使用材质 '{targetMaterial.name}' 的模型");

            // 如果只有一个对象，聚焦到该对象
            if (_selectedObjectsCount == 1)
            {
                EditorGUIUtility.PingObject(objectsUsingMaterial[0]);
            }
        }
        else
        {
            _selectedObjectsCount = 0;
            Debug.LogWarning($"场景中没有找到使用材质 '{targetMaterial.name}' 的模型");
        }
    }

    /// 工具启动时执行的函数。重写此方法以添加自定义启动逻辑。
    protected virtual void OnToolStartup()
    {
        // 默认实现为空，子类可以重写此方法以在工具启动时执行自定义逻辑
        ToolFindPbrMobileMaterials();
    }
    
    private void DelMaterialsFromManager()
    {
        if (!_computeBufferFileExists)
        {
            Debug.LogWarning("ComputeBuffer.cs文件不存在，无法剔除材质");
            EditorUtility.DisplayDialog("错误", "ComputeBuffer.cs文件不存在，无法剔除材质。", "确定");
            return;
        }

        if (!_manager || _manager.Equals(null))
        {
            Debug.LogWarning("无法剔除材质：未找到ComputeBufferLightManager实例");
            EditorUtility.DisplayDialog("错误", "未找到ComputeBufferLightManager实例，请先创建或查找管理器。", "确定");
            return;
        }

        // 收集要剔除的材质
        List<Material> materialsToRemove = new List<Material>();

        // 1. 从场景中选中的模型获取材质
        GameObject[] selectedGameObjects = Selection.gameObjects;
        if (selectedGameObjects != null && selectedGameObjects.Length > 0)
        {
            foreach (var go in selectedGameObjects)
            {
                Renderer renderer = go.GetComponent<Renderer>();
                if (renderer && renderer.sharedMaterials != null)
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material && !materialsToRemove.Contains(material))
                        {
                            materialsToRemove.Add(material);
                        }
                    }
                }
            }
        }

        // 2. 从Project中选择的材质球
        Object[] selectedObjects = Selection.objects;
        if (selectedObjects != null && selectedObjects.Length > 0)
        {
            foreach (var obj in selectedObjects)
            {
                if (obj is Material material && !materialsToRemove.Contains(material))
                {
                    materialsToRemove.Add(material);
                }
            }
        }

        // 检查是否有材质需要剔除
        if (materialsToRemove.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请先在场景中选择包含材质的模型，或在Project窗口中选择材质球。", "确定");
            return;
        }

        // 从管理器中剔除材质
        List<Material> removedMaterials = new List<Material>();
        List<Material> notFoundMaterials = new List<Material>();

        // ● 修改管理器序列化字段前登记撤销点，保证场景被标记为"未保存"且可 Ctrl+Z 撤销
        RecordManagerUndo("ComputeBuffer 剔除材质");

        foreach (var material in materialsToRemove)
        {
            if (_manager.targetMaterials.Contains(material))
            {
                // ● 走管理器集中路径 RemoveMaterial()：内部会同时从 targetMaterials /
                //   _controlledMaterials 移除材质，并关闭 _USEPOINTLIGHT / _USESPOTLIGHT /
                //   _USESPOTTEXTURE 三个关键字 + 对应浮点属性，避免该材质在剔除后被全局
                //   点光 / 聚光缓冲区错误点亮。
                _manager.RemoveMaterial(material);
                removedMaterials.Add(material);
            }
            else
            {
                notFoundMaterials.Add(material);
            }
        }

        // 显示结果
        string message = "";
        if (removedMaterials.Count > 0)
        {
            message += $"成功从管理器剔除 {removedMaterials.Count} 个材质。\n";
        }
        
        if (notFoundMaterials.Count > 0)
        {
            message += $"\n以下 {notFoundMaterials.Count} 个材质不在管理器中：\n";
            foreach (var mat in notFoundMaterials)
            {
                message += $"  • {mat.name}\n";
            }
        }

        if (removedMaterials.Count > 0 || notFoundMaterials.Count > 0)
        {
            EditorUtility.DisplayDialog("剔除材质结果", message, "确定");
            
            // 刷新材质列表显示
            if (removedMaterials.Count > 0)
            {
                // ● 标记管理器与场景为已修改（触发场景"未保存"标记），并保存被改动的材质资源
                MarkManagerModified(removedMaterials);
                RefreshTargetMaterials();
            }
        }
    }
    private void AddMaterialsToManager()
    {
        if (!_computeBufferFileExists)
        {
            Debug.LogWarning("ComputeBuffer.cs文件不存在，无法添加材质");
            EditorUtility.DisplayDialog("错误", "ComputeBuffer.cs文件不存在，无法添加材质。", "确定");
            return;
        }

        if (!_manager || _manager.Equals(null))
        {
            Debug.LogWarning("无法添加材质：未找到ComputeBufferLightManager实例");
            EditorUtility.DisplayDialog("错误", "未找到ComputeBufferLightManager实例，请先创建或查找管理器。", "确定");
            return;
        }

        // 收集要添加的材质
        List<Material> materialsToAdd = new List<Material>();

        // 1. 从场景中选中的模型获取材质
        GameObject[] selectedGameObjects = Selection.gameObjects;
        if (selectedGameObjects != null && selectedGameObjects.Length > 0)
        {
            foreach (var go in selectedGameObjects)
            {
                Renderer renderer = go.GetComponent<Renderer>();
                if (renderer && renderer.sharedMaterials != null)
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material && !materialsToAdd.Contains(material))
                        {
                            materialsToAdd.Add(material);
                        }
                    }
                }
            }
        }

        // 2. 从Project中选择的材质球
        Object[] selectedObjects = Selection.objects;
        if (selectedObjects != null && selectedObjects.Length > 0)
        {
            foreach (var obj in selectedObjects)
            {
                if (obj is Material material && !materialsToAdd.Contains(material))
                {
                    materialsToAdd.Add(material);
                }
            }
        }

        // 检查是否有材质需要添加
        if (materialsToAdd.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请先在场景中选择包含材质的模型，或在Project窗口中选择材质球。", "确定");
            return;
        }

        // 检查并添加材质
        List<Material> addedMaterials = new List<Material>();
        List<Material> existingMaterials = new List<Material>();

        // ● 修改管理器序列化字段前登记撤销点，保证场景被标记为"未保存"且可 Ctrl+Z 撤销
        RecordManagerUndo("ComputeBuffer 添加材质");

        foreach (var material in materialsToAdd)
        {
            if (_manager.targetMaterials.Contains(material))
            {
                existingMaterials.Add(material);
            }
            else
            {
                _manager.targetMaterials.Add(material);
                addedMaterials.Add(material);
            }
        }

        // ● 走管理器集中路径：每条新增材质立即套用管理器当前状态
        //   1. 之前手动写死的 SetFloat/EnableKeyword 与 SetMaterialParameters 行为重复，但只下发开关不传数值参数，
        //      新增材质的 _PointLightIntensity / _SpotLightIntensity / _SpotTexture 等仍保持材质资产旧值，
        //      在 OnValidate 的脏检查闸门命中"无变化"路径时尤其明显：targetMaterials 列表变更不会触发 hasDirtyParameter，
        //      UpdateAllMaterials 会被跳过，新增材质既不拿到开关也不拿到数值。
        //   2. 现在循环结束后统一调用 SetMaterialParameters / UpdateAllMaterials：
        //      - 开关（_USEPOINTLIGHT / _USESPOTLIGHT / _USESPOTTEXTURE）按管理器当前 _usePointLight 等下发
        //      - 强度 / 范围 / 衰减 / 数量 / 纹理 / 对比等数值参数同步下发，避免新增材质与已存在材质参数分叉
        //      - 单点维护：未来扩展任何下发字段只需修改 SetMaterialParameters 一处
        //   3. 显式按钮路径不受 v4.3"非显式路径不覆盖用户主动勾选"原则约束：
        //      OnValidate 的反向同步已被该原则收敛；本路径是用户显式意图，统一刷新即可。
        foreach (var material in addedMaterials)
        {
            _manager.SetMaterialParameters(material);
        }

        // 显示结果
        string message = "";
        if (addedMaterials.Count > 0)
        {
            message += $"成功添加 {addedMaterials.Count} 个材质到管理器。\n";
        }
        
        if (existingMaterials.Count > 0)
        {
            message += $"\n以下 {existingMaterials.Count} 个材质已存在于管理器中：\n";
            foreach (var mat in existingMaterials)
            {
                message += $"  • {mat.name}\n";
            }
        }

        if (addedMaterials.Count > 0 || existingMaterials.Count > 0)
        {
            EditorUtility.DisplayDialog("添加材质结果", message, "确定");
            
            // 刷新材质列表显示
            if (addedMaterials.Count > 0)
            {
                // ● 标记管理器与场景为已修改（触发场景"未保存"标记），并保存被改动的材质资源
                MarkManagerModified(addedMaterials);
                RefreshTargetMaterials();
            }
        }
    }

    // ==========================================================
    // ● 灯光自动识别与增删（"添加点灯/射灯 ↓"、"剔除点灯/射灯 ↑"）

    /// 当前选择中是否包含灯源 - 用于决定按钮显示材质操作还是灯光操作
    /// 仅识别系统支持的点光源与聚光灯，方向光 / 面光源仍走材质模式
    private static bool HasSelectedLights()
    {
        GameObject[] selectedGameObjects = Selection.gameObjects;
        if (selectedGameObjects == null || selectedGameObjects.Length == 0) return false;

        foreach (var go in selectedGameObjects)
        {
            if (!go) continue;

            foreach (var light in go.GetComponentsInChildren<Light>(true))
            {
                if (light && (light.type == LightType.Point || light.type == LightType.Spot)) return true;
            }
        }
        return false;
    }

    /// 收集场景中选中的灯光并按类型分组
    /// 支持选中父物体：会递归查找其子级中的灯光
    private static bool CollectSelectedLights(out List<Light> selectedPointLights, out List<Light> selectedSpotLights)
    {
        selectedPointLights = new List<Light>();
        selectedSpotLights = new List<Light>();

        GameObject[] selectedGameObjects = Selection.gameObjects;
        if (selectedGameObjects == null || selectedGameObjects.Length == 0) return false;

        foreach (var go in selectedGameObjects)
        {
            if (!go) continue;

            foreach (var light in go.GetComponentsInChildren<Light>(true))
            {
                if (!light) continue;

                if (light.type == LightType.Point)
                {
                    if (!selectedPointLights.Contains(light)) selectedPointLights.Add(light);
                }
                else if (light.type == LightType.Spot)
                {
                    if (!selectedSpotLights.Contains(light)) selectedSpotLights.Add(light);
                }
            }
        }

        return selectedPointLights.Count > 0 || selectedSpotLights.Count > 0;
    }

    /// 读取管理器的最大聚光灯数量
    /// _spotLightAmount 为私有序列化字段，沿用本工具既有的反射访问方式
    private int GetManagerSpotLightAmount()
    {
        if (!_manager || _manager.Equals(null)) return 0;

        var field = typeof(ComputeBufferLightManager).GetField("_spotLightAmount",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        return field != null && field.GetValue(_manager) is int amount ? amount : 0;
    }

    /// 把场景中选中的点光源 / 聚光灯添加到管理器的对应列表
    private void AddLightsToManager()
    {
        if (!_computeBufferFileExists)
        {
            Debug.LogWarning("ComputeBuffer.cs文件不存在，无法添加灯光");
            EditorUtility.DisplayDialog("错误", "ComputeBuffer.cs文件不存在，无法添加灯光。", "确定");
            return;
        }

        if (!_manager || _manager.Equals(null))
        {
            Debug.LogWarning("无法添加灯光：未找到ComputeBufferLightManager实例");
            EditorUtility.DisplayDialog("错误", "未找到ComputeBufferLightManager实例，请先创建或查找管理器。", "确定");
            return;
        }

        if (!CollectSelectedLights(out var selectedPointLights, out var selectedSpotLights))
        {
            EditorUtility.DisplayDialog("提示", "请先在场景中选择点光源或聚光灯，再点击此按钮。", "确定");
            return;
        }

        List<Light> addedPointLights = new List<Light>();
        List<Light> addedSpotLights = new List<Light>();
        List<Light> existingLights = new List<Light>();

        // ● 修改管理器序列化字段前登记撤销点，保证场景被标记为"未保存"且可 Ctrl+Z 撤销
        RecordManagerUndo("ComputeBuffer 添加灯光");

        foreach (var light in selectedPointLights)
        {
            if (_manager.pointLights.Contains(light))
            {
                existingLights.Add(light);
            }
            else
            {
                _manager.pointLights.Add(light);
                addedPointLights.Add(light);
            }
        }

        foreach (var light in selectedSpotLights)
        {
            if (_manager.spotLights.Contains(light))
            {
                existingLights.Add(light);
            }
            else
            {
                _manager.spotLights.Add(light);
                addedSpotLights.Add(light);
            }
        }

        if (addedPointLights.Count == 0 && addedSpotLights.Count == 0)
        {
            EditorUtility.DisplayDialog("添加灯光结果", "选中的灯光已全部存在于管理器中，未做任何改动。", "确定");
            return;
        }

        // ● 立即刷新缓冲区，使新增灯光在场景中生效
        if (addedPointLights.Count > 0) _manager.UpdateLightsBuffer();
        if (addedSpotLights.Count > 0) _manager.UpdateSpotLightsBuffer();

        // ● 标记管理器与场景为已修改
        MarkManagerModified();
        Repaint();

        string message = "";
        if (addedPointLights.Count > 0) message += $"成功添加 {addedPointLights.Count} 个点光源。\n";
        if (addedSpotLights.Count > 0) message += $"成功添加 {addedSpotLights.Count} 个聚光灯。\n";

        if (existingLights.Count > 0)
        {
            message += $"\n以下 {existingLights.Count} 个灯光已存在于管理器中：\n";
            foreach (var light in existingLights) message += $"  • {light.name}\n";
        }

        int spotLightLimit = GetManagerSpotLightAmount();
        if (spotLightLimit > 0 && _manager.spotLights.Count > spotLightLimit)
        {
            message += $"\n注意：管理器最大聚光灯数量为 {spotLightLimit}，当前列表已有 {_manager.spotLights.Count} 个，超出部分不会参与渲染。";
        }

        EditorUtility.DisplayDialog("添加灯光结果", message, "确定");
        Debug.Log(message);
    }

    /// 从管理器的点光源 / 聚光灯列表中剔除场景中选中的灯光
    private void DelLightsFromManager()
    {
        if (!_computeBufferFileExists)
        {
            Debug.LogWarning("ComputeBuffer.cs文件不存在，无法剔除灯光");
            EditorUtility.DisplayDialog("错误", "ComputeBuffer.cs文件不存在，无法剔除灯光。", "确定");
            return;
        }

        if (!_manager || _manager.Equals(null))
        {
            Debug.LogWarning("无法剔除灯光：未找到ComputeBufferLightManager实例");
            EditorUtility.DisplayDialog("错误", "未找到ComputeBufferLightManager实例，请先创建或查找管理器。", "确定");
            return;
        }

        if (!CollectSelectedLights(out var selectedPointLights, out var selectedSpotLights))
        {
            EditorUtility.DisplayDialog("提示", "请先在场景中选择点光源或聚光灯，再点击此按钮。", "确定");
            return;
        }

        List<Light> removedPointLights = new List<Light>();
        List<Light> removedSpotLights = new List<Light>();
        List<Light> notFoundLights = new List<Light>();

        // ● 修改管理器序列化字段前登记撤销点，保证场景被标记为"未保存"且可 Ctrl+Z 撤销
        RecordManagerUndo("ComputeBuffer 剔除灯光");

        foreach (var light in selectedPointLights)
        {
            if (_manager.pointLights.Remove(light)) removedPointLights.Add(light);
            else notFoundLights.Add(light);
        }

        foreach (var light in selectedSpotLights)
        {
            if (_manager.spotLights.Remove(light)) removedSpotLights.Add(light);
            else notFoundLights.Add(light);
        }

        if (removedPointLights.Count == 0 && removedSpotLights.Count == 0)
        {
            string emptyMessage = "选中的灯光均不在管理器中，未做任何改动。\n";
            foreach (var light in notFoundLights) emptyMessage += $"  • {light.name}\n";
            EditorUtility.DisplayDialog("剔除灯光结果", emptyMessage, "确定");
            return;
        }

        // ● 立即刷新缓冲区，使剔除的灯光在场景中停止生效
        if (removedPointLights.Count > 0) _manager.UpdateLightsBuffer();
        if (removedSpotLights.Count > 0) _manager.UpdateSpotLightsBuffer();

        // ● 标记管理器与场景为已修改
        MarkManagerModified();
        Repaint();

        string message = "";
        if (removedPointLights.Count > 0) message += $"成功从管理器剔除 {removedPointLights.Count} 个点光源。\n";
        if (removedSpotLights.Count > 0) message += $"成功从管理器剔除 {removedSpotLights.Count} 个聚光灯。\n";

        if (notFoundLights.Count > 0)
        {
            message += $"\n以下 {notFoundLights.Count} 个灯光不在管理器中：\n";
            foreach (var light in notFoundLights) message += $"  • {light.name}\n";
        }

        EditorUtility.DisplayDialog("剔除灯光结果", message, "确定");
        Debug.Log(message);
    }

    /// 选择管理器中收集的所有材质球
    /// 这个方法会从ComputeBufferLightManager的targetMaterials列表中获取所有材质，
    /// 并在Project窗口中选择这些材质球
    private void SelectAllMaterialsInManager()
    {
        if (!_computeBufferFileExists)
        {
            Debug.LogWarning("ComputeBuffer.cs文件不存在，无法选择材质");
            EditorUtility.DisplayDialog("错误", "ComputeBuffer.cs文件不存在，无法选择材质。", "确定");
            return;
        }

        if (!_manager || _manager.Equals(null))
        {
            Debug.LogWarning("无法选择材质：未找到ComputeBufferLightManager实例");
            EditorUtility.DisplayDialog("错误", "未找到ComputeBufferLightManager实例，请先创建或查找管理器。", "确定");
            return;
        }

        try
        {
            // 获取管理器中的材质列表
            var materials = _manager.targetMaterials;
            
            if (materials == null || materials.Count == 0)
            {
                Debug.LogWarning("管理器中的材质列表为空，无法选择材质");
                EditorUtility.DisplayDialog("提示", "管理器中的材质列表为空，请先点击【收集材质】按钮来填充列表。", "确定");
                return;
            }

            // 清理null材质引用
            var validMaterials = materials.Where(material => material != null).ToList();
            
            if (validMaterials.Count == 0)
            {
                Debug.LogWarning("管理器中的材质列表全部为空，无法选择材质");
                EditorUtility.DisplayDialog("提示", "管理器中的材质列表全部为空，请先点击【收集材质】按钮来填充列表。", "确定");
                return;
            }

            // 选择所有材质球
            Selection.objects = validMaterials.ToArray();
            
            // 聚焦到Project窗口
            EditorUtility.FocusProjectWindow();
            
            // 如果只有一个材质，聚焦到该材质
            if (validMaterials.Count == 1)
            {
                EditorGUIUtility.PingObject(validMaterials[0]);
            }
            
            Debug.Log($"已选择 {validMaterials.Count} 个材质球");
            
            // 显示成功消息
            // EditorUtility.DisplayDialog("选择完成", 
            //     $"已成功选择 {validMaterials.Count} 个材质球。\n\n" +
            //     "材质球已在Project窗口中被选中，可以对其进行批量操作。", 
            //     "确定");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"选择材质时出错: {e.Message}");
            Debug.LogException(e);
            EditorUtility.DisplayDialog("错误", $"选择材质时出错: {e.Message}", "确定");
        }
    }
    
    /// 创建纯色纹理
    /// <param name="width">纹理宽度</param>
    /// <param name="height">纹理高度</param>
    /// <param name="color">纹理颜色</param>
    /// <returns>创建的纹理</returns>
    private Texture2D CreateColorTexture(int width, int height, Color color)
    {
        Texture2D texture = new Texture2D(width, height);
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }
        texture.SetPixels(pixels);
        texture.Apply();
        // 正确设置HideFlags.HideAndDontSave，避免Unity编辑器试图持久化临时纹理
        // 这解决了断言失败：'!(o->TestHideFlag(Object::kDontSaveInEditor) && (options & kAllowDontSaveObjectsToBePersistent) == 0)'
        texture.hideFlags = HideFlags.HideAndDontSave;
        return texture;
    }
}
#endif
