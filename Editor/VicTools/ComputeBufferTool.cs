// Compute Buffer Tool v3.0 支持最高2盏SpotLight
// Compute Buffer Tool v2.0.3 编辑器模式实时更新优化 - 增强Compute Buffer系统与编辑器集成，支持非运行模式下点光效果预览

// Compute Buffer Tool v2.0
// 场景中使用PBR_Mobile材质的材质列表与管理器材质列表分开

// Compute Buffer Tool v1.1
// 无需管理器可使用PBR_Mobile材质收集
// 主要功能：
// 1. Compute Buffer系统管理 - 管理GPU端的点光源数据缓冲区
// 2. 材质管理 - 自动查找和管理使用PBR_Mobile着色器的材质
// 3. 点光源收集 - 自动收集场景中的点光源并转换为Compute Buffer格式
// 4. 材质选择工具 - 根据材质快速选择场景中使用该材质的模型
// 5. 缓冲区清理 - 完全重置Compute Buffer系统，释放GPU资源
// 6. 实时参数更新 - 动态更新材质参数和光源数据
// 
// 使用场景：
// - 动态点光源管理
// - 材质批量操作
// - 场景对象快速选择

using UnityEditor;
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
    [MenuItem("Tools/VicTools(YD)/Compute Buffer Tool", false, 2000)]
    public static void ShowWindow()
    {
        // 设置窗口宽度和高度
        var window = EditorWindow.GetWindow<ComputeBufferTool>("Compute Buffer Tool v3.0");
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
        // 检查ComputeBuffer.cs文件是否存在 - 使用绝对路径
        var computeBufferPath = Path.Combine(Application.dataPath, "GameMain/Scripts/Shader/ComputeBuffer.cs");
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

        // 工具启动时执行指定函数
        OnToolStartup();
    }

    private void OnGUI()
    {
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

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
                                _manager.UpdateAllMaterials();
                            }
                        }

                        if (GUILayout.Button("重置到默认值"))
                        {
                            if (_manager && !_manager.Equals(null))
                            {
                                _manager.ResetMaterialToDefaults();
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
                        if (GUILayout.Button("获取管理器材质列表", GUILayout.Height(30)))
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
                        // 添加查找PBR_Mobile材质的按钮
                        if (GUILayout.Button("●收集材质到管理器", GUILayout.Height(30)))
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
                                    _manager.EditorFindPBRMobileMaterials();
                                    RefreshTargetMaterials();
                                }
                                // else
                                // {
                                //     EditorUtility.DisplayDialog("错误", "无法创建或找到ComputeBufferLightManager实例，无法查找PBR_Mobile材质。", "确定");
                                // }
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"查找PBR_Mobile材质时出错: {e.Message}");
                                Debug.LogException(e);
                                EditorUtility.DisplayDialog("错误", $"查找PBR_Mobile材质时出错: {e.Message}", "确定");
                            }
                        }

                        if (GUILayout.Button("●收集点光源到管理器", GUILayout.Height(30)))
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
                                    _manager.CollectScenePointLights();
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
                        
                        if (GUILayout.Button("●收集聚光灯到管理器", GUILayout.Height(30)))
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
                                    _manager.CollectSceneSpotLights();
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


                        EditorGUILayout.LabelField("管理器材质列表", EditorStyles.miniBoldLabel != null ? EditorStyles.miniBoldLabel : new GUIStyle());
                        // [SEARCH: 材质列表显示] - 材质列表显示和选择区域
                        // 显示材质列表并添加选择按钮，选择场景中使用了该材质的模型
                        if (_targetMaterials != null && _targetMaterials.Count > 0)
                        {
                            EditorGUILayout.HelpBox($"管理器有 {_targetMaterials.Count} 个 PBR_Mobile 材质在列表中。选择一个材质来查找使用它的模型：", MessageType.Info);

                            for (var i = 0; i < _targetMaterials.Count; i++)
                            {
                                var material = _targetMaterials[i];
                                if (!material) continue;
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.ObjectField($"管理器材质 {i}", material, typeof(Material), false);
                                GUI.backgroundColor = Color.cyan;
                                if (GUILayout.Button("选择模型", GUILayout.Width(80)))
                                {
                                    SelectObjectsUsingMaterial(material);
                                }
                                GUI.backgroundColor = Color.white;
                                EditorGUILayout.EndHorizontal();
                            }
                        }
                        else
                        {
                            EditorGUILayout.HelpBox("材质列表为空，请先点击【查找PBR_Mobile材质】按钮来填充列表。", MessageType.Warning);
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


        EditorGUILayout.Space();
        EditorGUILayout.LabelField("编辑器工具", EditorStyles.boldLabel != null ? EditorStyles.boldLabel : new GUIStyle());
        if (GUILayout.Button("查找场景中 PBR_Mobile 材质", GUILayout.Height(22)))
        {
            try
            {
                ToolFindPbrMobileMaterials();

            }
            catch (System.Exception e)
            {
                Debug.LogError($"查找PBR_Mobile材质时出错: {e.Message}");
                Debug.LogException(e);
                EditorUtility.DisplayDialog("错误", $"查找PBR_Mobile材质时出错: {e.Message}", "确定");
            }
        }
        if (_toolTargetMaterials != null && _toolTargetMaterials.Count > 0)
        {
            EditorGUILayout.HelpBox($"当前场景有 {_toolTargetMaterials.Count} 个材质在列表中，选择一个材质来查找使用它的模型：", MessageType.Info);

            for (var i = 0; i < _toolTargetMaterials.Count; i++)
            {
                var material = _toolTargetMaterials[i];
                if (!material) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField($"场景材质 {i + 1}", material, typeof(Material), false);
                GUI.backgroundColor = Color.cyan;
                if (GUILayout.Button("选择模型", GUILayout.Width(80)))
                {
                    SelectObjectsUsingMaterial(material);
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
            }
        }
        else
        {
            EditorGUILayout.HelpBox("材质列表为空，请先点击【查找PBR_Mobile材质】按钮来填充列表。", MessageType.Warning);
        }

        // [SEARCH: 自定义材质选择工具] - 自定义材质选择工具区域
        // 自定义材质选择功能 - 始终显示，不依赖于管理器状态
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("自定义材质选择工具", EditorStyles.boldLabel != null ? EditorStyles.boldLabel : new GUIStyle());

        EditorGUILayout.BeginHorizontal();
        // 获取材质选项参数 - 默认不勾选
        // 是否提取选中对象材质选项
        bool previousExtractMaterial = _extractMaterial;
        _extractMaterial = EditorGUILayout.Toggle("获取选中对象材质", _extractMaterial);
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

        EditorGUILayout.EndScrollView();
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
            var targetObject = GameObject.Find(_manager.name);
            if (targetObject)
            {
                DestroyImmediate(targetObject);
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

            // 添加ComputeBufferLightManager组件
            _manager = managerObject.AddComponent<ComputeBufferLightManager>();

            // 选择并聚焦到新创建的对象
            Selection.activeObject = managerObject;
            EditorGUIUtility.PingObject(managerObject);

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
                if (!material || material.shader.name != "Custom/PBR_Mobile") continue;
                // ● 同时添加到targetMaterials列表，方便在Inspector中查看
                if (!_toolTargetMaterials.Contains(material))
                {
                    _toolTargetMaterials.Add(material);
                }
            }
        }
        Debug.Log($"找到 {_toolTargetMaterials.Count} 个使用PBR_Mobile Shader的材质");
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
                // 使用DestroyImmediate在编辑器模式下立即销毁对象
                DestroyImmediate(managerObject);
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

        // 更新选择的对象数量
        _selectedObjectsCount = objectsUsingMaterial.Count;

        if (objectsUsingMaterial.Count > 0)
        {
            // 选择所有使用该材质的对象
            Selection.objects = objectsUsingMaterial.ToArray();
            Debug.Log($"已选择 {objectsUsingMaterial.Count} 个使用材质 '{targetMaterial.name}' 的模型");

            // 如果只有一个对象，聚焦到该对象
            if (objectsUsingMaterial.Count == 1)
            {
                EditorGUIUtility.PingObject(objectsUsingMaterial[0]);
            }
        }
        else
        {
            Debug.LogWarning($"场景中没有找到使用材质 '{targetMaterial.name}' 的模型");
        }
    }

    
    /// 工具启动时执行的函数。重写此方法以添加自定义启动逻辑。
    protected virtual void OnToolStartup()
    {
        // 默认实现为空，子类可以重写此方法以在工具启动时执行自定义逻辑
        ToolFindPbrMobileMaterials();
    }
}
