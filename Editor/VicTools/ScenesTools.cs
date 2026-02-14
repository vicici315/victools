using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
// using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VicTools
{
    /// 资源箱文件数据序列化类
    [Serializable]
    public class ResourceBoxFileData
    {
        public string creationTime;
        public string LastModifiedTime { get; set; }
        public List<ResourceBoxFileItem> items = new();
        
        public ResourceBoxFileData()
        {
            creationTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastModifiedTime = creationTime;
        }
    }
    
    /// 路径工具类，用于处理正确的Library目录路径
    public static class PathHelper
    {
        /// 获取项目根目录的Library目录路径
        /// <example>
        /// 示例：如果Application.dataPath为 ".../yd_gqsj/Assets"
        /// 则返回 ".../yd_gqsj/Library"
        /// </example>
        public static string GetLibraryPath()
        {
            // Application.dataPath 返回 Assets 目录的路径
            // 通过返回上一级目录再进入Library目录
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Library"));
        }
        
        /// 获取VicTools在Library目录中的完整路径
        /// <example>
        /// 示例：如果GetLibraryPath()返回 ".../yd_gqsj/Library"
        /// 则返回 ".../yd_gqsj/Library/VicTools"
        /// </example>
        public static string GetVicToolsLibraryPath()
        {
            return System.IO.Path.Combine(GetLibraryPath(), "VicTools");
        }
        
        /// 获取全局资源箱文件的完整路径
        /// <example>
        /// 示例：如果GetVicToolsLibraryPath()返回 ".../yd_gqsj/Library/VicTools"
        /// 则返回 ".../yd_gqsj/Library/VicTools/GlobalResourceBox.json"
        /// </example>
        public static string GetGlobalResourceBoxPath()
        {
            return System.IO.Path.Combine(GetVicToolsLibraryPath(), "GlobalResourceBox.json");
        }
        
        /// 获取项目Assets/Editor目录的完整路径
        /// <example>
        /// 示例：如果Application.dataPath为 ".../yd_gqsj/Assets"
        /// 则返回 ".../yd_gqsj/Assets/Editor"
        /// </example>
        public static string GetAssetsEditorPath()
        {
            return System.IO.Path.Combine(Application.dataPath, "Editor/VicTools/ResourceBox");
        }
    }
    
/// 资源箱文件项目序列化类
[Serializable]
public class ResourceBoxFileItem
{
    public string guid;
    public string Type { get; set; }
    public string name;
    public bool isAsset;
    public string displayName; // 添加displayName字段，用于保存对象的显示名称
}


    /// 场景工具子窗口
    public class ScenesTools : SubWindow
    {
        private readonly List<Object> _resourceBox = new List<Object>(); // 资源箱列表
        private readonly Dictionary<Object, string> _resourceBoxDisplayNames = new Dictionary<Object, string>(); // 对象到displayName的映射
        private readonly Dictionary<int, string> _nullObjectDisplayNames = new Dictionary<int, string>(); // 用于存储null对象的displayName，使用索引作为键
        private readonly Dictionary<int, int> _resourceBoxInstanceIDs = new Dictionary<int, int>(); // 存储资源箱索引到对象InstanceID的映射
        private readonly Dictionary<int, string> _instanceIDToDisplayName = new Dictionary<int, string>(); // 存储InstanceID到displayName的映射
        private Vector2 _resourceBoxScrollPosition; // 资源箱滚动位置
        private string _searchText = ""; // 搜索文本
        private Material _selectedMaterial; // 用于存储用户手动选择的材质
        private Texture2D _lightDirIcon; // lightDir.png图标
        private bool _selPrefab = false;
        private bool _selMesh = false;
        private bool _selLODGroup = false;
        private bool _selMissMat = false;
        private bool _selMissScript = false;
        // 二级选项
        private bool _selAct = false;
        private bool _selMeshObj = true;
        private bool _selParticleObj = false;
        private bool _selParent = false;
        // 资源箱文件管理相关变量
        private string _resourceBoxFileName = ""; // 当前资源箱文件名
        private int _selectedFileIndex; // 选中的文件索引
        private readonly List<string> _availableFiles = new List<string>(); // 可用的资源箱文件列表
        // 注意：ResourceBoxDirectory和GlobalResourceBoxPath现在通过PathHelper类获取

        // 搜索历史记录管理器
        private readonly SearchHistoryManager _searchHistoryManager;
        
        // 选中反馈相关变量
        private readonly HashSet<Object> _selectedObjectsInResourceBox = new();

        public ScenesTools(string name, EditorWindow parent) : base("[场景工具 v2.12]", parent)
        {
            // 初始化搜索历史记录管理器
            _searchHistoryManager = new SearchHistoryManager("VicTools_ScenesTools");
        }

        // ReSharper disable Unity.PerformanceAnalysis
        public override void OnEnable()
        {
            base.OnEnable();
            // 注册游戏运行状态变化事件
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            // 注册选择变化事件
            Selection.selectionChanged += OnSelectionChanged;

            // 加载上次搜索文本
            _searchText = _searchHistoryManager.LoadLastSearchText();
            _resourceBoxFileName = EditorPrefs.GetString("ProjectTools_RBFileName", "");
            _selectedFileIndex = EditorPrefs.GetInt("ProjectTools_RBnewSelectedFileIndex", 0);
            // 加载五个挑选选项的存档设置
            _selPrefab = EditorPrefs.GetBool("ScenesTools_selPrefab", false);
            _selMesh = EditorPrefs.GetBool("ScenesTools_selMesh", false);
            _selLODGroup = EditorPrefs.GetBool("ScenesTools_selLODGroup", false);
            _selMissMat = EditorPrefs.GetBool("ScenesTools_selMissMat", false);
            _selMissScript = EditorPrefs.GetBool("ScenesTools_selMissScript", false);
            // 加载二级选项的存档设置
            _selAct = EditorPrefs.GetBool("ScenesTools_selAct", false);
            _selMeshObj = EditorPrefs.GetBool("ScenesTools_selMeshObj", true);
            _selParticleObj = EditorPrefs.GetBool("ScenesTools_selParticleObj", false);
            _selParent = EditorPrefs.GetBool("ScenesTools_selParent", false);
            // 加载保存的资源箱数据
            LoadResourceBox();
            
            // 刷新可用文件列表
            RefreshAvailableFiles();
            
            // 自动刷新资源箱，尝试重新加载null对象
            RecheckNullObjectsInResourceBox();
            
            // 初始化选中对象状态
            UpdateSelectedObjectsInResourceBox();
            // 加载lightDir图标 - 使用Unity包路径（兼容开发环境和打包发布）
            // 方法1：直接使用包路径（推荐，因为package.json中的name是固定的）
            string lightDirIcon = "Packages/com.youdoo.victools/Editor/VicTools/lightDir.png";
            
            // 方法2：备用方案，使用PackageInfo获取包路径（需要Unity 2019.3+）
            // #if UNITY_2019_3_OR_NEWER
            // var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.youdoo.victools");
            // if (packageInfo != null)
            // {
            //     lightDirIcon = System.IO.Path.Combine(packageInfo.resolvedPath, "Editor/VicTools/lightDir.png");
            //     // 注意：resolvedPath是绝对路径，需要转换为相对路径或使用其他方式加载
            // }
            // #endif
            
            // 加载lightDir图标
            _lightDirIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(lightDirIcon);
            
            // 如果加载失败，尝试使用相对路径（针对某些特殊情况）
            if (_lightDirIcon == null)
            {
                Debug.LogWarning($"无法加载lightDir图标，路径: {lightDirIcon}");
                // 可以尝试其他备用路径或使用默认图标
            }
        }

        public override void OnDisable()
        {
            base.OnDisable();
            // 取消注册游戏运行状态变化事件
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            // 取消注册选择变化事件
            Selection.selectionChanged -= OnSelectionChanged;

            // 保存当前搜索文本
            _searchHistoryManager.SaveLastSearchText(_searchText);
            EditorPrefs.SetString("ProjectTools_RBFileName", _resourceBoxFileName);
            EditorPrefs.SetInt("ProjectTools_RBnewSelectedFileIndex", _selectedFileIndex);
            // 保存五个挑选选项的存档设置
            EditorPrefs.SetBool("ScenesTools_selPrefab", _selPrefab);
            EditorPrefs.SetBool("ScenesTools_selMesh", _selMesh);
            EditorPrefs.SetBool("ScenesTools_selLODGroup", _selLODGroup);
            EditorPrefs.SetBool("ScenesTools_selMissMat", _selMissMat);
            EditorPrefs.SetBool("ScenesTools_selMissScript", _selMissScript);
            // 保存二级选项的存档设置
            EditorPrefs.SetBool("ScenesTools_selAct", _selAct);
            EditorPrefs.SetBool("ScenesTools_selMeshObj", _selMeshObj);
            EditorPrefs.SetBool("ScenesTools_selParticleObj", _selParticleObj);
            EditorPrefs.SetBool("ScenesTools_selParent", _selParent);
            // 保存资源箱数据（自动保存，不显示提示框）
            // SaveResourceBox();
        }


        /// 当游戏运行状态发生变化时调用
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    // 游戏开始运行前保存资源箱数据（自动保存，不显示提示框）
                    SaveResourceBox(false);
                    Debug.Log("游戏开始运行前保存资源箱数据");
                    break;
                    
                case PlayModeStateChange.EnteredEditMode:
                    // 游戏停止后重新加载资源箱数据
                    LoadResourceBox();
                    Debug.Log("游戏停止后重新加载资源箱数据");
                    break;
                    
                case PlayModeStateChange.ExitingPlayMode:
                    // 游戏停止前保存资源箱数据（自动保存，不显示提示框）
                    SaveResourceBox(false);
                    Debug.Log("游戏停止前保存资源箱数据");
                    break;
                    
                case PlayModeStateChange.EnteredPlayMode:
                    // 游戏开始运行后重新加载资源箱数据
                    LoadResourceBox();
                    Debug.Log("游戏开始运行后重新加载资源箱数据");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }

        /// 当选择发生变化时调用
        private void OnSelectionChanged()
        {
            UpdateSelectedObjectsInResourceBox();
        }

        /// 更新资源箱中选中对象的状态
        private void UpdateSelectedObjectsInResourceBox()
        {
            _selectedObjectsInResourceBox.Clear();
            
            // 获取当前选中的所有对象
            var selectedObjects = Selection.objects;
            if (selectedObjects == null || selectedObjects.Length == 0)
                return;

            // 检查每个选中的对象是否在资源箱中
            foreach (var selectedObj in selectedObjects)
            {
                if (selectedObj != null && _resourceBox.Contains(selectedObj))
                {
                    _selectedObjectsInResourceBox.Add(selectedObj);
                }
            }

            // 强制重绘窗口以更新显示
            if (Parent)
                Parent.Repaint();
        }
        // ReSharper disable Unity.PerformanceAnalysis
        public override void OnGUI()
        {
            var style = EditorStyle.Get;

            // 处理回车键事件 - 必须在GUI元素创建之前处理
            _searchHistoryManager.HandleEnterKeyEvent(_searchText, "ScenesTools_SearchTextField", (searchText) =>
            {
                // 回车键按下时的回调函数
                Debug.Log($"回车执行搜索: {searchText}");
                // 这里可以添加实际的搜索逻辑
            });
            // [1]VerticalLayoutStart();
            EditorGUILayout.BeginVertical(style.area);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("★ 速选工具", style.subheading, GUILayout.Width(110));
            GUI.contentColor = Color.cyan;
            
            // 获取选中对象数量 - 支持独立窗口和主窗口
            var currentSelectedCount = 0;
            
            // 检查是否是独立窗口
            WinScenesTools standaloneWindow = Parent as WinScenesTools;
            if (standaloneWindow != null)
            {
                // 从独立窗口获取选中数量
                currentSelectedCount = standaloneWindow.StandaloneSelectedCount;
            }
            else
            {
                // 从主窗口获取全局选中数量
                VicToolsWindow mainWindow = Parent as VicToolsWindow;
                if (mainWindow != null)
                {
                    currentSelectedCount = mainWindow.globalSelectedObjectsCount;
                }
            }
            
            EditorGUILayout.LabelField($"   选中：( {currentSelectedCount} )", style.subheading);
            GUI.contentColor = Color.white;
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            // 搜索功能区域
            EditorGUILayout.BeginVertical(style.subheading);
            EditorGUILayout.Space();

            // 搜索文本输入框 - 改进版本：回车键保存历史记录
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(false));
            // GUIStyle searchTextFieldStyle = new GUIStyle(style.normalfont);
            // searchTextFieldStyle.fixedHeight = 20;
            GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button("选择相似名称对象", style.normalButton, GUILayout.Width(140)))
            {
                SelectSimilarObjects();
            }
            if (GUILayout.Button("选择名称：", style.normalButton, GUILayout.Width(80)))
            {
                SelectObjectsByName(_searchText);
            }
            GUI.backgroundColor = Color.white;
            _searchText = CreateTextFieldWithStyle("", _searchText,
            _ =>
            {
                // 文本变化时不立即保存到历史记录，等待回车或搜索按钮
                // 这里只更新文本，不保存历史
            }, 1, 0, TextAnchor.MiddleLeft, style.normalfont, null, "ScenesTools_SearchTextField");

            // 搜索按钮
            // GUI.backgroundColor = Color.green;
            // if (GUILayout.Button("搜索", GUILayout.Width(60)))
            // {
            //     if (!string.IsNullOrEmpty(searchText))
            //     {
            //         searchHistoryManager.AddToSearchHistory(searchText);
            //         // 这里可以添加实际的搜索逻辑
            //         Debug.Log($"执行搜索: {searchText}");
            //     }
            // }
            // GUI.backgroundColor = Color.white;


            // 使用搜索历史管理器绘制历史记录选择器
            _searchHistoryManager.DrawSearchHistorySelector(ref _searchText, SelectObjectsByName);
            EditorGUILayout.EndHorizontal();


            // 清除历史记录按钮 - 右对齐
            // searchHistoryManager.DrawClearHistoryButton("删除历史", style.normalfont_Hui, true);

            // 资源箱功能
            DrawResourceBoxSection();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndVertical();
            // [1]VerticalLayoutEnd();


            // 显示选择的对象数量
            // if (selectedObjectsCount > 0)
            // {
            //     EditorGUILayout.LabelField($"选中对象数量: 【 {selectedObjectsCount} 】", EditorStyles.miniLabel);
            // }
        }

        /// 选择场景中名称包含指定文本的所有对象
        private void SelectObjectsByName(string searchText)
        {
            if (string.IsNullOrEmpty(searchText))
            {
                EditorUtility.DisplayDialog("提示", "请输入要搜索的名称", "确定");
                return;
            }

            // 在场景中查找所有名称包含搜索文本的对象
            var matchingObjects = FindObjectsByName(searchText);

            if (matchingObjects.Count == 0)
            {
                // EditorUtility.DisplayDialog("提示", $"未找到名称包含 \"{searchText}\" 的对象", "确定");
                return;
            }

            // 选择这些对象
            Selection.objects = matchingObjects.ToArray();
            
            // 更新选中数量 - 支持独立窗口和主窗口
            var standaloneWindow = Parent as WinScenesTools;
            if (standaloneWindow)
            {
                // 更新独立窗口的选中数量
                standaloneWindow.StandaloneSelectedCount = matchingObjects.Count;
                standaloneWindow.Repaint();
            }
            else
            {
                // 更新主窗口的全局选中数量
                var mainWindow = Parent as VicToolsWindow;
                if (mainWindow)
                {
                    mainWindow.globalSelectedObjectsCount = matchingObjects.Count;
                    mainWindow.Repaint();
                }
            }

            // 添加到搜索历史
            _searchHistoryManager.AddToSearchHistory(searchText);

            // Debug.Log($"已选择 {matchingObjects.Count} 个名称包含 \"{searchText}\" 的对象");
        }

        /// 选择场景中与当前选中对象名称相似的所有物体
        private void SelectSimilarObjects()
        {
            // 获取当前选中的对象
            var selectedObjects = Selection.gameObjects;

            if (selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先选择一个对象", "确定");
                return;
            }

            // 以选择的第一个对象名作为基础
            var selectedObject = selectedObjects[0];
            var selectedName = selectedObject.name;

            // 提取名称模式（去掉数字后缀等）
            var namePattern = ExtractNamePattern(selectedName);

            if (string.IsNullOrEmpty(namePattern))
            {
                EditorUtility.DisplayDialog("提示", "无法识别名称模式", "确定");
                return;
            }

            // 在场景中查找所有相似名称的对象
            var similarObjects = FindSimilarObjects(namePattern);

            if (similarObjects.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "未找到名称相似的对象", "确定");
                return;
            }

            // 选择这些对象
            Selection.objects = similarObjects.ToArray();
            
            // 更新选中数量 - 支持独立窗口和主窗口
            var standaloneWindow = Parent as WinScenesTools;
            if (standaloneWindow)
            {
                // 更新独立窗口的选中数量
                standaloneWindow.StandaloneSelectedCount = similarObjects.Count;
                standaloneWindow.Repaint();
            }
            else
            {
                // 更新主窗口的全局选中数量
                var mainWindow = Parent as VicToolsWindow;
                if (!mainWindow) return;
                mainWindow.globalSelectedObjectsCount = similarObjects.Count;
                mainWindow.Repaint();
            }
            // EditorUtility.DisplayDialog("完成", $"已选择 {similarObjects.Count} 个相似对象", "确定");
        }

        /// 从对象名称中提取模式（去掉数字后缀等）
        private static string ExtractNamePattern(string objectName)
        {
            // 常见的命名模式匹配
            // 1. 带数字后缀：Object_01, Object_02 等
            // 2. 带括号数字：Object (1), Object (2) 等
            // 3. 纯数字后缀：Object1, Object2 等

            // 移除常见的数字后缀模式
            var pattern = objectName;

            // 匹配下划线加数字：_01, _02, _1, _2 等
            pattern = Regex.Replace(pattern, @"_\d+$", "");

            // 匹配括号数字：(1), (2), (01), (02) 等
            pattern = Regex.Replace(pattern, @"\s*\(\d+\)$", "");

            // 匹配纯数字后缀：1, 2, 01, 02 等
            pattern = Regex.Replace(pattern, @"\d+$", "");

            // 如果处理后名称太短，返回原始名称
            if (pattern.Length < 2)
            {
                return objectName;
            }

            return pattern.Trim();
        }

        /// 在场景中查找所有名称包含指定文本的对象
        private static List<GameObject> FindObjectsByName(string searchText)
        {
            // 获取场景中的所有游戏对象（使用新的API，不排序以获得更好的性能）
            var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);

            return allObjects.Where(obj => obj.name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        /// 在场景中查找所有名称相似的对象
        private static List<GameObject> FindSimilarObjects(string namePattern)
        {
            // 获取场景中的所有游戏对象（使用新的API，不排序以获得更好的性能）
            var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);

            return allObjects.Where(obj => obj.name.StartsWith(namePattern)).ToList();
        }

        // ReSharper disable Unity.PerformanceAnalysis
        /// 绘制资源箱界面
        private void DrawResourceBoxSection()
        {
            var style = EditorStyle.Get;

            // 添加对象到资源箱按钮
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            GUI.backgroundColor = Color.cyan;
            _selectedMaterial = EditorGUILayout.ObjectField("", _selectedMaterial, typeof(Material), false, GUILayout.MinWidth(115)) as Material;

            // 放入按钮 - 将selectedMaterial材质放入资源箱
            GUI.backgroundColor = Color.magenta;
            if (_selectedMaterial != null)
            {
                var putButtonContent = new GUIContent("↓", "将左边获取的材质放入资源箱");
                if (GUILayout.Button(putButtonContent, style.normalButton, GUILayout.Width(25)))
                {
                    PutSelectedMaterialToResourceBox();
                }
            }
            var putButton2Content = new GUIContent("●↓", "将选中对象的材质放入资源箱");
            if (GUILayout.Button(putButton2Content, style.normalButton, GUILayout.Width(40)))
            {
                OnlySelectedMaterialToResourceBox();
            }
            GUI.backgroundColor = Color.cyan;
            
            var selectButtonContent = new GUIContent("选择相同材质对象", "选择使用选择对象相同材质的所有对象");
            if (GUILayout.Button(selectButtonContent, style.normalButton, GUILayout.Width(130)))
            {
                // 优先使用selectedMaterial，如果为空则使用选中对象的材质
                // if (selectedMaterial != null)
                // {
                //     SelectObjectsUsingMaterial(selectedMaterial);
                // }
                // else
                // {
                    // 选择场景中与选择物体相同材质的物体
                    SelectObjectsUsingSelectedObjectMaterial();
                // }
            }

            EditorGUILayout.EndHorizontal();

            // 拖拽区域
            EditorGUILayout.BeginVertical(style.subheading);

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            // 创建拖拽区域
            var dropArea = GUILayoutUtility.GetRect(210, 50, GUILayout.ExpandWidth(true));
            // ★创建居中文本的GUIStyle
            var centeredHelpBox = new GUIStyle(EditorStyles.helpBox);
            centeredHelpBox.alignment = TextAnchor.MiddleCenter;
            centeredHelpBox.fontSize = 12;
            // 绘制带边框的拖拽区域，使用居中文本样式
            GUI.Box(dropArea, "拖拽区域：从 Project 或场景 Hierarchy 中拖入资源箱 ↓", centeredHelpBox);

            // 处理拖拽事件
            var evt = Event.current;
            switch (evt.type)
            {
                case EventType.DragUpdated:
                    if (dropArea.Contains(evt.mousePosition))
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        evt.Use(); // 消耗事件，防止进一步处理
                    }
                    break;
                case EventType.DragPerform:
                    if (dropArea.Contains(evt.mousePosition))
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        DragAndDrop.AcceptDrag();
                        HandleDragAndDropObjects(DragAndDrop.objectReferences);
                        evt.Use(); // 消耗事件，防止进一步处理
                    }
                    break;
                case EventType.MouseDown:
                case EventType.MouseUp:
                case EventType.MouseMove:
                case EventType.MouseDrag:
                case EventType.KeyDown:
                case EventType.KeyUp:
                case EventType.ScrollWheel:
                case EventType.Repaint:
                case EventType.Layout:
                case EventType.DragExited:
                case EventType.Ignore:
                case EventType.Used:
                case EventType.ValidateCommand:
                case EventType.ExecuteCommand:
                case EventType.ContextClick:
                case EventType.MouseEnterWindow:
                case EventType.MouseLeaveWindow:
                case EventType.TouchDown:
                case EventType.TouchUp:
                case EventType.TouchMove:
                case EventType.TouchEnter:
                case EventType.TouchLeave:
                case EventType.TouchStationary:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            GUI.backgroundColor = Color.magenta;
            var cusButtonStyle = new GUIStyle(style.normalButton)
            {
                fixedHeight = 46
            };
            if (GUILayout.Button("放入资源箱 ↓", cusButtonStyle, GUILayout.Width(100)))
            {
                AddSelectedToResourceBox();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"资源箱: ( {_resourceBox.Count} )", style.subheading, GUILayout.Height(24));
            GUILayout.FlexibleSpace();
            // 清除资源箱按钮（带确认）
            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("清空资源箱 X"))
            {
                ClearResourceBoxWithConfirmation();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();


            EditorGUILayout.EndVertical();

            // 资源箱文件管理区域
            EditorGUILayout.BeginVertical(style.subheading);
            // EditorGUILayout.LabelField("资源箱存档管理", style.subheading2);
            
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button(new GUIContent("保存", "保存当前资源箱，资源名留空时使用当前场景名"), GUILayout.Width(40)))
            {
                SaveResourceBoxToFile();
            }
            // 文件名输入和操作按钮
            // EditorGUILayout.LabelField("资源名:", GUILayout.Width(42));
            _resourceBoxFileName = EditorGUILayout.TextField(_resourceBoxFileName);
            
            // 刷新文件列表按钮
            GUI.backgroundColor = Color.yellow;
            if (GUILayout.Button("刷新→", GUILayout.Width(50)))
            {
                RefreshAvailableFiles();
                LoadResourceBox();
                // 重新检查null对象，尝试在当前场景中重新加载它们
                RecheckNullObjectsInResourceBox();
            }
            // GUI.backgroundColor = Color.white;
            // 文件选择下拉列表
            // EditorGUILayout.LabelField("选择资源:", GUILayout.Width(55));
            int newSelectedFileIndex = EditorGUILayout.Popup(_selectedFileIndex, _availableFiles.ToArray());
            if (newSelectedFileIndex != _selectedFileIndex)
            {
                _selectedFileIndex = newSelectedFileIndex;
                if (_selectedFileIndex >= 0 && _selectedFileIndex < _availableFiles.Count)
                {
                    string selectedFileName = _availableFiles[_selectedFileIndex];
                    if (selectedFileName != "新建资源箱")
                    {
                        _resourceBoxFileName = selectedFileName;
                        // LoadResourceBoxFromFile();
                    }
                }
            }
            // 保存和加载按钮
            GUI.backgroundColor = Color.magenta;
            if (GUILayout.Button("载入↓", GUILayout.Width(50)))
            {
                LoadResourceBoxFromFile();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();


            EditorGUILayout.EndVertical();

            // 显示资源箱内容
            if (_resourceBox.Count > 0)
            {
                // 在绘制之前，检测并保存刚变为null的对象的displayName
                for (int i = 0; i < _resourceBox.Count; i++)
                {
                    Object obj = _resourceBox[i];
                    bool isObjectNull = !obj;
                    
                    if (!isObjectNull)
                    {
                        // 对象有效，保存或更新它的InstanceID和displayName映射
                        int instanceID = obj.GetInstanceID();
                        _resourceBoxInstanceIDs[i] = instanceID;
                        
                        // 计算并保存displayName
                        var parentName = GetTopLevelParentName(obj);
                        var displayName = !string.IsNullOrEmpty(parentName) 
                            ? $"<{obj.GetType().Name}> [{parentName}]← {obj.name}" 
                            : $"<{obj.GetType().Name}> {obj.name}";
                        _instanceIDToDisplayName[instanceID] = displayName;
                    }
                    else if (!_nullObjectDisplayNames.ContainsKey(i))
                    {
                        // 对象为null，但_nullObjectDisplayNames中还没有保存它的displayName
                        // 尝试通过InstanceID找到它之前的displayName
                        if (_resourceBoxInstanceIDs.TryGetValue(i, out int instanceID))
                        {
                            if (_instanceIDToDisplayName.TryGetValue(instanceID, out string savedDisplayName))
                            {
                                _nullObjectDisplayNames[i] = savedDisplayName;
                            }
                            else
                            {
                                // 如果找不到，使用默认名称
                                _nullObjectDisplayNames[i] = "<未知类型> 未知对象";
                            }
                        }
                        else
                        {
                            // 如果没有保存InstanceID，尝试从保存的数据中加载
                            var jsonData = EditorPrefs.GetString("VicTools_ScenesTools_ResourceBox", "");
                            if (!string.IsNullOrEmpty(jsonData))
                            {
                                try
                                {
                                    var resourceBoxData = JsonUtility.FromJson<ResourceBoxData>(jsonData);
                                    if (resourceBoxData != null && resourceBoxData.items != null && i < resourceBoxData.items.Count)
                                    {
                                        var item = resourceBoxData.items[i];
                                        if (!string.IsNullOrEmpty(item.displayName))
                                        {
                                            _nullObjectDisplayNames[i] = item.displayName;
                                        }
                                        else if (!string.IsNullOrEmpty(item.name))
                                        {
                                            _nullObjectDisplayNames[i] = $"<未知类型> {item.name}";
                                        }
                                        else
                                        {
                                            _nullObjectDisplayNames[i] = "<未知类型> 未知对象";
                                        }
                                    }
                                    else
                                    {
                                        _nullObjectDisplayNames[i] = "<未知类型> 未知对象";
                                    }
                                }
                                catch
                                {
                                    _nullObjectDisplayNames[i] = "<未知类型> 未知对象";
                                }
                            }
                            else
                            {
                                _nullObjectDisplayNames[i] = "<未知类型> 未知对象";
                            }
                        }
                        
                        // 立即保存资源箱数据，确保null对象的displayName被持久化
                        SaveResourceBox(false);
                    }
                }
                
                // 添加使用提示
                EditorGUILayout.LabelField("提示：按住 Ctrl 键点击\"选择\"按钮可添加选择，按住 Shift 键选择整个层级", style.normalfont_Hui_Cen);
                // 滚动视图显示资源箱内容 - 自适应高度，使用剩余空间
                _resourceBoxScrollPosition = EditorGUILayout.BeginScrollView(_resourceBoxScrollPosition, GUILayout.ExpandHeight(true));

                EditorGUILayout.BeginVertical(EditorStyles.textArea, GUILayout.ExpandWidth(true));

                for (int i = 0; i < _resourceBox.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();

                    // 显示对象名称和类型 - 靠右对齐
                    Object obj = _resourceBox[i];
                    
                    // 检查对象是否在选中列表中
                    bool isSelected = _selectedObjectsInResourceBox.Contains(obj);
                    
                    string displayName;
                    bool isObjectNull = !obj;
                    
                    if (isObjectNull)
                    {
                        // 如果对象不存在，从null对象字典中获取保存的displayName
                        if (_nullObjectDisplayNames.TryGetValue(i, out string savedDisplayName))
                        {
                            displayName = savedDisplayName;
                        }
                        else
                        {
                            // 这种情况理论上不应该发生，因为我们在绘制前已经处理了
                            displayName = "<未知类型> 未知对象";
                        }
                    }
                    else
                    {
                        var parentName = GetTopLevelParentName(obj);
                        displayName =
                            // 有父对象：显示 [父对象名] 对象名 (类型)
                            !string.IsNullOrEmpty(parentName) ? $"<{obj.GetType().Name}> [{parentName}]← {obj.name}" :
                            // 没有父对象：显示 对象名 (类型)
                            $"<{obj.GetType().Name}> {obj.name}";
                    }

                    // 使用自定义GUIStyle实现真正的右对齐，并设置最小宽度
                    var rightAlignedLabel2 = new GUIStyle(EditorStyles.label);
                    rightAlignedLabel2.alignment = TextAnchor.MiddleRight;
                    rightAlignedLabel2.clipping = TextClipping.Clip;
                    
                    // 如果对象被选中，改变文本颜色为黄色高亮
                    if (isSelected)
                    {
                        rightAlignedLabel2.normal.textColor = Color.yellow;
                        rightAlignedLabel2.hover.textColor = Color.cyan;
                    }
                    // 如果对象不存在，设置为灰色
                    else if (isObjectNull)
                    {
                        rightAlignedLabel2.normal.textColor = Color.gray;
                        rightAlignedLabel2.hover.textColor = Color.gray;
                    }
                    
                    // 计算可用宽度（总宽度减去按钮宽度），并设置最小宽度限制
                    const float buttonAreaWidth2 = 120; // 按钮区域固定宽度
                    const float minLabelWidth2 = 100; // 标签最小宽度
                    var availableWidth2 = Mathf.Max(EditorGUIUtility.currentViewWidth - buttonAreaWidth2, minLabelWidth2);
                    
                    // 使用GUILayout.Width限制标签最大宽度，确保按钮区域始终可见
                    var labelRect2 = GUILayoutUtility.GetRect(
                        new GUIContent(displayName), 
                        rightAlignedLabel2, 
                        GUILayout.ExpandWidth(true),
                        GUILayout.MinWidth(minLabelWidth2),
                        GUILayout.MaxWidth(availableWidth2)
                    );
                    
                    // 绘制右对齐的标签，使用文本裁剪
                    GUI.Label(labelRect2, displayName, rightAlignedLabel2);

                    // 如果是Material类型，添加赋予按钮
                    if (!isObjectNull && obj is Material material)
                    {
                        GUI.backgroundColor = Color.green;
                        if (GUILayout.Button("←", GUILayout.Width(20), GUILayout.ExpandWidth(false)))
                        {
                            SceneTools.AssignMaterialToSelectedModels(material);
                        }
                        GUI.backgroundColor = Color.white;
                    } else {
                        // if (!isObjectNull && obj is not SceneAsset)
                        GUILayout.Space(20);
                    }

                    // 选择按钮 - 支持Ctrl键和Shift键添加选择
                    GUI.backgroundColor = Color.cyan;
                    
                    // 在创建按钮之前检查对象是否有效且没有DontSaveInEditor标志
                    var canCreateButton = obj && !obj.Equals(null);
                    if (canCreateButton)
                    {
                        // 检查对象是否具有kDontSaveInEditor标志，如果有则清除它
                        if (((int)obj.hideFlags & (int)HideFlags.DontSaveInEditor) != 0)
                        {
                            obj.hideFlags &= ~HideFlags.DontSaveInEditor;
                            Debug.Log($"已清除对象 {obj.name} 的DontSaveInEditor标志");
                        }
                    }
                    
                    // 额外的安全检查：确保对象在GUI操作前是有效的
                    if (canCreateButton && obj != null)
                    {
                        try
                        {
                            // 这里应该包含选择按钮的创建逻辑
                            // 如果是SceneAsset类型，添加打开按钮
                            if (obj is SceneAsset)
                            {
                                GUI.backgroundColor = Color.black;
                                if (GUILayout.Button("□", GUILayout.Width(20), GUILayout.ExpandWidth(false)))
                                {
                                    // 打开场景
                                    string scenePath = AssetDatabase.GetAssetPath(obj);
                                    if (!string.IsNullOrEmpty(scenePath))
                                    {
                                        // 检查当前场景是否有未保存的更改
                                        if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                                        {
                                            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
                                        }
                                    }
                                }
                                GUI.backgroundColor = Color.cyan;
                            }
                            if (GUILayout.Button("选择", GUILayout.Width(40), GUILayout.ExpandWidth(false)))
                            {
                                // 再次验证对象有效性
                                if (obj != null && !obj.Equals(null))
                                {
                                    // 检查是否按住Ctrl键或Shift键
                                    Event currentEvent = Event.current;
                                    bool isCtrlPressed = currentEvent.control || currentEvent.command; // command键用于Mac
                                    bool isShiftPressed = currentEvent.shift;
                                    
                                    if (isCtrlPressed)
                                    {
                                        // Ctrl+点击：添加到当前选择
                                        List<Object> currentSelection = new List<Object>(Selection.objects);
                                        if (!currentSelection.Contains(obj))
                                        {
                                            currentSelection.Add(obj);
                                            Selection.objects = currentSelection.ToArray();
                                        }
                                    }
                                    else if (isShiftPressed)
                                    {
                                        Selection.activeObject = null;
                                        Selection.activeObject = obj;
                                        SceneTools.SelectAllHierarchy();
                                    }
                                    else
                                    {
                                        // 普通点击：替换当前选择
                                        Selection.activeObject = null;
                                        Selection.activeObject = obj;
                                    }

                                    EditorGUIUtility.PingObject(obj);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"创建选择按钮时出错: {e.Message}");
                        }
                    }
                    else
                    {
                        // 如果对象无效，显示禁用的按钮
                        GUI.enabled = false;
                        GUILayout.Button("选择", GUILayout.Width(40), GUILayout.ExpandWidth(false));
                        GUI.enabled = true;
                    }

                    // 移除按钮 - 设置为红色
                    GUI.backgroundColor = Color.red;
                    if (GUILayout.Button("X", GUILayout.Width(20), GUILayout.ExpandWidth(false)))
                    {
                        // 删除对象前，先清理相关的映射
                        if (_resourceBoxInstanceIDs.TryGetValue(i, out int instanceID))
                        {
                            _instanceIDToDisplayName.Remove(instanceID);
                        }
                        
                        _resourceBox.RemoveAt(i);
                        
                        // 重新构建null对象显示名称字典和InstanceID映射，更新所有受影响的索引
                        var tempNullDisplayNames = new Dictionary<int, string>();
                        var tempInstanceIDs = new Dictionary<int, int>();
                        
                        foreach (var kvp in _nullObjectDisplayNames)
                        {
                            if (kvp.Key < i)
                            {
                                // 删除位置之前的索引保持不变
                                tempNullDisplayNames[kvp.Key] = kvp.Value;
                            }
                            else if (kvp.Key > i)
                            {
                                // 删除位置之后的索引需要减1
                                tempNullDisplayNames[kvp.Key - 1] = kvp.Value;
                            }
                            // kvp.Key == i 的项被删除，不添加到新字典中
                        }
                        
                        foreach (var kvp in _resourceBoxInstanceIDs)
                        {
                            if (kvp.Key < i)
                            {
                                tempInstanceIDs[kvp.Key] = kvp.Value;
                            }
                            else if (kvp.Key > i)
                            {
                                tempInstanceIDs[kvp.Key - 1] = kvp.Value;
                            }
                        }
                        
                        _nullObjectDisplayNames.Clear();
                        _resourceBoxInstanceIDs.Clear();
                        
                        foreach (var kvp in tempNullDisplayNames)
                        {
                            _nullObjectDisplayNames[kvp.Key] = kvp.Value;
                        }
                        
                        foreach (var kvp in tempInstanceIDs)
                        {
                            _resourceBoxInstanceIDs[kvp.Key] = kvp.Value;
                        }
                        
                        i--; // 调整索引
                        // 立即保存资源箱数据
                        SaveResourceBox(false);
                    }
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.LabelField($"{i + 1}", GUILayout.Width(25), GUILayout.ExpandWidth(false));

                    EditorGUILayout.EndHorizontal();
                }

                // 结束资源箱内容列表
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndScrollView();
            }
            else
            {
                EditorGUILayout.Space(30);
                EditorGUILayout.LabelField("> 资源箱为空 <", style.normalfont_Hui_Cen);
            }
            
            // 添加层级设置按钮 - 紧靠在资源箱列表的底部
            // 参数设置
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            // 一级选项
            _selPrefab = base.CreateToggleWithStyle(new GUIContent("Prefab", "挑选 预设 对象"), _selPrefab, null, null, null, null, 50, 20);
            _selMesh = base.CreateToggleWithStyle(new GUIContent("Mesh", "挑选 非预设 对象"), _selMesh, null, null, null, null, 40, 20);
            _selLODGroup = base.CreateToggleWithStyle(new GUIContent("LOD", "挑选带 LODGroup 对象"), _selLODGroup, null, null, null, null, 30, 20);
            _selMissMat = base.CreateToggleWithStyle(new GUIContent("Miss Mat", "挑选 丢失材质球 的模型对象"), _selMissMat, null, null, null, null, 60, 20);
            _selMissScript = base.CreateToggleWithStyle(new GUIContent("Miss Scirpt", "挑选 丢失脚本 的对象"), _selMissScript, null, null, null, null, 75, 20);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            // 二级选项
            _selAct = base.CreateToggleWithStyle(new GUIContent("Active", "勾选时只挑选 激活 的对象，取消时只挑选 未激活 的对象"), _selAct, null, null, null, null, 50, 20);
            
            // _selMeshObj和_selParticleObj的自动勾选逻辑
            _selMeshObj = base.CreateToggleWithStyle(new GUIContent("isMesh", "挑选 模型 对象"), _selMeshObj, null, null, null, null, 50, 20);
            _selParticleObj = base.CreateToggleWithStyle(new GUIContent("Particle", "挑选 粒子 对象"), _selParticleObj, null, null, null, null, 55, 20);
            _selParent = base.CreateToggleWithStyle(new GUIContent("Parent", "挑选 父对象"), _selParent, null, null, null, null, 50, 20);
            if (GUILayout.Button("Off", GUILayout.Width(40), GUILayout.ExpandWidth(false)))
            {
                _selPrefab = false;
                _selMesh = false;
                _selLODGroup = false;
                _selMissMat = false;
                _selMissScript = false;
            }
            // 处理自动勾选逻辑
            /*
            if (newSelMeshObj != _selMeshObj || newSelParticleObj != _selParticleObj)
            {
                // 如果取消勾选一项且另一项未勾选，则自动勾选另一项
                if (!newSelMeshObj && !newSelParticleObj)
                {
                    // 如果两个都取消了，根据哪个被取消来决定勾选哪个
                    if (!newSelMeshObj && _selMeshObj) // 刚刚取消了isMesh
                    {
                        newSelParticleObj = true;
                    }
                    else if (!newSelParticleObj && _selParticleObj) // 刚刚取消了Particle
                    {
                        newSelMeshObj = true;
                    }
                }
                
                _selMeshObj = newSelMeshObj;
                _selParticleObj = newSelParticleObj;
            }
            */
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            // 添加lightDir图标按钮 - 在"设置层级"按钮左边
            if (_lightDirIcon != null)
            {
                GUI.backgroundColor = new Color(0.66f, 0.66f, 0.66f); // 浅黄色背景
                if (GUILayout.Button(new GUIContent(_lightDirIcon, "校正(PBR_Mobile)烘焙高光方向"), GUILayout.Height(35), GUILayout.Width(38)))
                {
                    SceneTools.ApplyLightDirectionToMaterials();
                }
                GUI.backgroundColor = new Color(0.4f, 0.5f, 0.7f);
            }
            if (GUILayout.Button(new GUIContent("设置层级", "设置所有选择对象放入最后选择的对象中"), GUILayout.Height(30)))
            {
                SceneTools.SetSelectedObjectsAsChildren();
            }
            if (GUILayout.Button(new GUIContent("跳出层级", "将选中对象放置到最外层级（Prefab需要Unpack）"), GUILayout.Height(30)))
            {
                SceneTools.RemoveSelectedObjectsFromParent();
            }
            GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button(new GUIContent("选择层级", "选择当前对象所在层级的所有对象"), GUILayout.Height(30)))
            {
                SceneTools.SelectAllHierarchy();
            }
            GUI.backgroundColor = new Color(0.6f, 0.8f, 0.6f);
            if (GUILayout.Button(new GUIContent("挑选", "根据选项选择场景中的物体"), GUILayout.Height(30)))
            {
                // 根据选项选择场景中的物体
                SceneTools.SelectObjectsByType(_selMesh, _selPrefab, _selLODGroup, _selMissMat, _selMissScript, _selAct, _selMeshObj, _selParticleObj, _selParent);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        /// 处理拖拽的对象
        private void HandleDragAndDropObjects(Object[] draggedObjects)
        {
            if (draggedObjects == null || draggedObjects.Length == 0)
                return;

            var addedCount = 0;
            foreach (var obj in draggedObjects)
            {
                if (!obj || _resourceBox.Contains(obj)) continue;
                // 检查对象是否具有kDontSaveInEditor标志，如果有则清除它
                if (((int)obj.hideFlags & (int)HideFlags.DontSaveInEditor) != 0)
                {
                    obj.hideFlags &= ~HideFlags.DontSaveInEditor;
                    Debug.Log($"已清除对象 {obj.name} 的DontSaveInEditor标志");
                }
                    
                _resourceBox.Add(obj);
                
                // 计算并保存displayName到字典
                var parentName = GetTopLevelParentName(obj);
                var displayName = !string.IsNullOrEmpty(parentName) 
                    ? $"<{obj.GetType().Name}> [{parentName}]← {obj.name}" 
                    : $"<{obj.GetType().Name}> {obj.name}";
                _resourceBoxDisplayNames[obj] = displayName;
                
                addedCount++;
            }

            if (addedCount <= 0) return;
            // 强制重绘窗口
            if (Parent)
                Parent.Repaint();

            // 立即保存资源箱数据（自动保存，不显示提示框）
            SaveResourceBox(false);
                
            // Debug.Log($"通过拖拽添加了 {addedCount} 个对象到资源箱");
        }

        /// 添加选中的对象到资源箱
        private void AddSelectedToResourceBox()
        {
            var selectedObjects = Selection.objects;

            if (selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先选择要添加到资源箱的对象", "确定");
                return;
            }

            var addedCount = 0;
            foreach (var obj in selectedObjects)
            {
                if (!obj || _resourceBox.Contains(obj)) continue;
                // 检查对象是否具有kDontSaveInEditor标志，如果有则清除它
                if (((int)obj.hideFlags & (int)HideFlags.DontSaveInEditor) != 0)
                {
                    obj.hideFlags &= ~HideFlags.DontSaveInEditor;
                    Debug.Log($"已清除对象 {obj.name} 的DontSaveInEditor标志");
                }
                    
                _resourceBox.Add(obj);
                
                // 计算并保存displayName到字典
                var parentName = GetTopLevelParentName(obj);
                var displayName = !string.IsNullOrEmpty(parentName) 
                    ? $"<{obj.GetType().Name}> [{parentName}]← {obj.name}" 
                    : $"<{obj.GetType().Name}> {obj.name}";
                _resourceBoxDisplayNames[obj] = displayName;
                
                addedCount++;
            }

            if (addedCount <= 0) return;
            // 强制重绘窗口
            if (Parent)
                Parent.Repaint();
                
            // 立即保存资源箱数据（自动保存，不显示提示框）
            SaveResourceBox(false);
        }

        /// 带确认的清除资源箱
        private void ClearResourceBoxWithConfirmation()
        {
            if (_resourceBox.Count == 0)
            {
                Debug.Log("资源箱已经是空的");
                return;
            }

            var confirm = EditorUtility.DisplayDialog(
                "确认清除",
                $"确定要清除资源箱中的所有 {_resourceBox.Count} 个对象吗？",
                "确定清除",
                "取消"
            );

            if (!confirm) return;
            _resourceBox.Clear();
            _nullObjectDisplayNames.Clear(); // 清空null对象显示名称字典
            _resourceBoxInstanceIDs.Clear(); // 清空InstanceID映射
            _instanceIDToDisplayName.Clear(); // 清空InstanceID到displayName的映射
            // 清除保存的数据
            EditorPrefs.DeleteKey("VicTools_ScenesTools_ResourceBox");
            // 强制重绘窗口
            if (Parent)
                Parent.Repaint();
                
            Debug.Log("资源箱已清空，保存的数据已清除");
        }

        /// 获取对象的最高级父对象名
        private static string GetTopLevelParentName(Object obj)
        {
            if (obj is not GameObject gameObject) return string.Empty;
            var root = gameObject.transform.root;
            // 如果根对象不是自己，说明有父对象
            return root != gameObject.transform ? root.name : string.Empty;
        }

        /// 自动获取当前场景中选择的物体的材质，并选择所有使用相同材质的物体
        private void SelectObjectsUsingSelectedObjectMaterial()
        {
            // 检查场景中是否选择了物体
            if (Selection.activeGameObject == null)
            {
                Debug.LogWarning("无法选择物体：场景中没有选择任何物体");
                EditorUtility.DisplayDialog("选择错误", "请先在场景中选择一个物体，然后点击此按钮。", "确定");
                return;
            }

            // 获取选择物体的Renderer组件
            var selectedRenderer = Selection.activeGameObject.GetComponent<Renderer>();
            if (!selectedRenderer)
            {
                Debug.LogWarning($"无法获取材质：选择的物体 '{Selection.activeGameObject.name}' 没有Renderer组件");
                EditorUtility.DisplayDialog("选择错误", $"选择的物体 '{Selection.activeGameObject.name}' 没有Renderer组件，无法获取材质。", "确定");
                return;
            }

            // 获取选择物体的材质
            var selectedMaterials = selectedRenderer.sharedMaterials;
            if (selectedMaterials == null || selectedMaterials.Length == 0)
            {
                Debug.LogWarning($"无法获取材质：选择的物体 '{Selection.activeGameObject.name}' 没有材质");
                EditorUtility.DisplayDialog("选择错误", $"选择的物体 '{Selection.activeGameObject.name}' 没有材质。", "确定");
                return;
            }

            // 使用第一个材质作为目标材质
            var targetMaterial = selectedMaterials[0];
            if (!targetMaterial)
            {
                Debug.LogWarning($"无法获取材质：选择的物体 '{Selection.activeGameObject.name}' 的材质为空");
                EditorUtility.DisplayDialog("选择错误", $"选择的物体 '{Selection.activeGameObject.name}' 的材质为空。", "确定");
                return;
            }

            // 调用现有的材质选择方法
            SelectObjectsUsingMaterial(targetMaterial);
            _selectedMaterial = targetMaterial;
            // if (extractMaterial)
            // {
            //     selectedMaterial = targetMaterial; // 更新选中的材质
            // }
        }
        /// 查找并选择场景中使用指定材质的所有模型
        /// <param name="targetMaterial">要查找的材质</param>
        private void SelectObjectsUsingMaterial(Material targetMaterial)
        {
            // 在方法开头声明变量，避免作用域冲突
            var standaloneWindow = Parent as WinScenesTools;
            
            if (!targetMaterial)
            {
                Debug.LogWarning("无法选择模型：材质为空");
                // 更新选中数量为0 - 支持独立窗口和主窗口
                if (standaloneWindow)
                {
                    // 更新独立窗口的选中数量
                    standaloneWindow.StandaloneSelectedCount = 0;
                    standaloneWindow.Repaint();
                }
                else
                {
                    // 更新主窗口的全局选中数量
                    var mainWindow = Parent as VicToolsWindow;
                    if (!mainWindow) return;
                    mainWindow.globalSelectedObjectsCount = 0;
                    mainWindow.Repaint();
                }
                return;
            }
            // 获取场景中的所有根对象
            var allGameObjects = SceneManager.GetActiveScene().GetRootGameObjects();
            var objectsUsingMaterial = new List<GameObject>();
            // 遍历所有对象查找使用指定材质的Renderer
            foreach (var rootObject in allGameObjects)
            {
                var renderers = rootObject.GetComponentsInChildren<Renderer>(true);

                foreach (var renderer in renderers)
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material != targetMaterial) continue;
                        objectsUsingMaterial.Add(renderer.gameObject);
                        break; // 找到匹配后跳出内层循环
                    }
                }
            }
            
            // 更新选中数量 - 支持独立窗口和主窗口
            if (standaloneWindow)
            {
                // 更新独立窗口的选中数量
                standaloneWindow.StandaloneSelectedCount = objectsUsingMaterial.Count;
                standaloneWindow.Repaint();
            }
            else
            {
                // 更新主窗口的全局选中数量
                VicToolsWindow mainWindow = Parent as VicToolsWindow;
                if (mainWindow)
                {
                    mainWindow.globalSelectedObjectsCount = objectsUsingMaterial.Count;
                    mainWindow.Repaint();
                }
            }

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

        /// 将selectedMaterial材质放入资源箱
        private void OnlySelectedMaterialToResourceBox()
        {
            Material materialToAdd = null;
            // 检查场景中是否选择了物体
            if (Selection.activeGameObject)
            {
                // 获取选择物体的Renderer组件
                var selectedRenderer = Selection.activeGameObject.GetComponent<Renderer>();
                if (selectedRenderer)
                {
                    // 获取选择物体的材质
                    Material[] selectedMaterials = selectedRenderer.sharedMaterials;
                    if (selectedMaterials != null && selectedMaterials.Length > 0 && selectedMaterials[0] != null)
                    {
                        // 使用第一个材质作为要添加的材质
                        materialToAdd = selectedMaterials[0];
                        Debug.Log($"将场景中选中第一个对象材质 '{materialToAdd.name}' 放入资源箱");
                    }
                }
            }

            // 如果仍然没有材质，显示警告
            if (!materialToAdd)
            {
                Debug.LogWarning("无法放入材质：selectedMaterial为空且选中对象没有材质");
                EditorUtility.DisplayDialog("放入失败", "请先在材质选择框中选择一个材质，或者选择一个有材质的对象", "确定");
                return;
            }

            // 检查材质是否已经在资源箱中
            if (_resourceBox.Contains(materialToAdd))
            {
                // 如果材质已经在资源箱中，选中该材质对象
                Selection.activeObject = materialToAdd;
                EditorGUIUtility.PingObject(materialToAdd);
                Debug.Log($"材质 '{materialToAdd.name}' 已经在资源箱中，已选中该材质");
                return;
            }

            // 检查对象是否具有kDontSaveInEditor标志，如果有则清除它
            if (((int)materialToAdd.hideFlags & (int)HideFlags.DontSaveInEditor) != 0)
            {
                materialToAdd.hideFlags &= ~HideFlags.DontSaveInEditor;
                Debug.Log($"已清除材质 {materialToAdd.name} 的DontSaveInEditor标志");
            }

            // 将材质添加到资源箱
            _resourceBox.Add(materialToAdd);
            
            // 计算并保存displayName到字典
            var parentName = GetTopLevelParentName(materialToAdd);
            var displayName = !string.IsNullOrEmpty(parentName) 
                ? $"<{materialToAdd.GetType().Name}> [{parentName}]← {materialToAdd.name}" 
                : $"<{materialToAdd.GetType().Name}> {materialToAdd.name}";
            _resourceBoxDisplayNames[materialToAdd] = displayName;
            
            // 强制重绘窗口
            if (Parent)
                Parent.Repaint();

            // 立即保存资源箱数据（自动保存，不显示提示框）
            SaveResourceBox(false);

            Debug.Log($"已将材质 '{materialToAdd.name}' 放入资源箱");
        }
        
        /// 将selectedMaterial材质放入资源箱
        private void PutSelectedMaterialToResourceBox()
        {
            Material materialToAdd;

            // 逻辑：当selectedMaterial不为空时，将selectedMaterial中的材质放入资源箱
            // 当selectedMaterial为空时，将场景中选中第一个对象材质放入资源箱
            if (_selectedMaterial)
            {
                materialToAdd = _selectedMaterial;
                Debug.Log($"将selectedMaterial中的材质 '{materialToAdd.name}' 放入资源箱");
            }else
            {
                Debug.LogWarning("无法放入材质：selectedMaterial为空");
                return;
            }

            // 检查材质是否已经在资源箱中
            if (_resourceBox.Contains(materialToAdd))
            {
                // Debug.LogWarning($"材质 '{materialToAdd.name}' 已经在资源箱中");
                EditorUtility.DisplayDialog("重复材质", $"材质 '{materialToAdd.name}' 已经在资源箱中", "确定");
                return;
            }

            // 检查对象是否具有kDontSaveInEditor标志，如果有则清除它
            if (((int)materialToAdd.hideFlags & (int)HideFlags.DontSaveInEditor) != 0)
            {
                materialToAdd.hideFlags &= ~HideFlags.DontSaveInEditor;
                Debug.Log($"已清除材质 {materialToAdd.name} 的DontSaveInEditor标志");
            }

            // 将材质添加到资源箱
            _resourceBox.Add(materialToAdd);
            
            // 计算并保存displayName到字典
            var parentName = GetTopLevelParentName(materialToAdd);
            var displayName = !string.IsNullOrEmpty(parentName) 
                ? $"<{materialToAdd.GetType().Name}> [{parentName}]← {materialToAdd.name}" 
                : $"<{materialToAdd.GetType().Name}> {materialToAdd.name}";
            _resourceBoxDisplayNames[materialToAdd] = displayName;
            
            // 强制重绘窗口
            if (Parent)
                Parent.Repaint();

            // 立即保存资源箱数据
            SaveResourceBox(false);

            Debug.Log($"已将材质 '{materialToAdd.name}' 放入资源箱");
        }

        

        /// 保存资源箱数据到全局文件（优化版本，解决重名问题）
        /// <param name="showConfirmation">是否显示确认提示框</param>
        private void SaveResourceBox(bool showConfirmation = true)
        {
            // 如果需要显示确认提示框，则显示
            if (showConfirmation)
            {
                var confirm = EditorUtility.DisplayDialog(
                    "确认保存",
                    $"确定要保存资源箱中的 {_resourceBox.Count} 个对象吗？",
                    "确定保存",
                    "取消"
                );

                if (!confirm)
                {
                    Debug.Log("用户取消了保存操作");
                    return;
                }
            }

            // 创建资源箱数据列表，保存所有对象（包括null对象）
            var resourceBoxData = new List<ResourceBoxItem>();
            
            if (_resourceBox != null)
            {
                for (int i = 0; i < _resourceBox.Count; i++)
                {
                    var obj = _resourceBox[i];
                    
                    if (obj)
                    {
                        // 检查对象是否具有kDontSaveInEditor标志，如果有则跳过保存
                        if (((int)obj.hideFlags & (int)HideFlags.DontSaveInEditor) != 0)
                        {
                            Debug.LogWarning($"对象 {obj.name} 具有DontSaveInEditor标志，跳过保存");
                            continue;
                        }
                            
                        // 获取对象的全局唯一标识符
                        string guid;
                        var assetPath = AssetDatabase.GetAssetPath(obj);
                            
                        if (!string.IsNullOrEmpty(assetPath))
                        {
                            // 如果是项目资源，使用GUID（持久化）
                            guid = AssetDatabase.AssetPathToGUID(assetPath);
                        }
                        else
                        {
                            // 如果是场景对象，使用优化的唯一标识符
                            if (obj is GameObject gameObject)
                            {
                                // 使用新的唯一标识符系统
                                guid = GetGameObjectUniqueIdentifier(gameObject);
                            }
                            else
                            {
                                // 其他场景对象使用实例ID（临时）
                                guid = $"INSTANCE:{obj.GetInstanceID()}";
                            }
                        }

                        // 计算displayName（与DrawResourceBoxSection中相同的逻辑）
                        string displayName;
                        var parentName = GetTopLevelParentName(obj);
                        displayName = !string.IsNullOrEmpty(parentName) 
                            ? $"<{obj.GetType().Name}> [{parentName}]← {obj.name}" 
                            : $"<{obj.GetType().Name}> {obj.name}";

                        resourceBoxData.Add(new ResourceBoxItem
                        {
                            guid = guid,
                            type = obj.GetType().FullName,
                            name = obj.name,
                            isAsset = !string.IsNullOrEmpty(assetPath),
                            displayName = displayName  // 保存displayName
                        });
                    }
                    else
                    {
                        // 处理null对象
                        // 尝试从null对象字典中获取displayName
                        string displayName;
                        if (_nullObjectDisplayNames.TryGetValue(i, out string savedDisplayName))
                        {
                            displayName = savedDisplayName;
                        }
                        else
                        {
                            // 如果没有保存的displayName，使用默认名称
                            displayName = $"<未知类型> 未知对象";
                        }
                        
                        // 对于null对象，使用特殊的标识符
                        resourceBoxData.Add(new ResourceBoxItem
                        {
                            guid = "NULL:OBJECT",  // 特殊的标识符表示null对象
                            type = "System.Null",
                            name = "NullObject",
                            isAsset = false,
                            displayName = displayName  // 保存displayName
                        });
                    }
                }
            }

            // 序列化数据
            var jsonData = JsonUtility.ToJson(new ResourceBoxData { 
                items = resourceBoxData
            });
            
            // 保存到全局文件
            SaveResourceBoxToGlobalFile(jsonData);
            // Debug.Log($"资源箱数据已保存到全局文件，包含 {resourceBoxData.Count} 个对象（包括 {resourceBoxData.Count(item => item.guid == "NULL:OBJECT")} 个null对象）");
        }
        
        /// <summary>
        /// 保存资源箱数据到全局文件
        /// </summary>
        /// <param name="jsonData">JSON格式的资源箱数据</param>
        private void SaveResourceBoxToGlobalFile(string jsonData)
        {
            try
            {
                // 使用PathHelper获取正确的全局文件路径
                var globalResourceBoxPath = PathHelper.GetGlobalResourceBoxPath();
                
                // 确保目录存在
                var directoryPath = System.IO.Path.GetDirectoryName(globalResourceBoxPath);
                if (!string.IsNullOrEmpty(directoryPath) && !System.IO.Directory.Exists(directoryPath))
                {
                    System.IO.Directory.CreateDirectory(directoryPath);
                }
                
                // 保存到文件
                System.IO.File.WriteAllText(globalResourceBoxPath, jsonData);
                Debug.Log($"资源箱数据已保存到全局文件: {globalResourceBoxPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"保存资源箱数据到全局文件时出错: {e.Message}");
                // 如果保存到文件失败，回退到EditorPrefs
                EditorPrefs.SetString("VicTools_ScenesTools_ResourceBox", jsonData);
                Debug.LogWarning($"已回退到EditorPrefs保存资源箱数据");
            }
        }

        /// 从全局文件加载资源箱数据（优化版本，解决重名问题）
        private void LoadResourceBox()
        {
            string jsonData = "";
            
            // 使用PathHelper获取正确的全局文件路径
            var globalResourceBoxPath = PathHelper.GetGlobalResourceBoxPath();
            
            // 首先尝试从全局文件加载
            if (System.IO.File.Exists(globalResourceBoxPath))
            {
                try
                {
                    jsonData = System.IO.File.ReadAllText(globalResourceBoxPath);
                    Debug.Log($"从全局文件加载资源箱数据: {globalResourceBoxPath}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"读取全局文件时出错: {e.Message}");
                    // 如果读取文件失败，回退到EditorPrefs
                    jsonData = EditorPrefs.GetString("VicTools_ScenesTools_ResourceBox", "");
                    Debug.LogWarning($"已回退到EditorPrefs加载资源箱数据");
                }
            }
            else
            {
                // 如果全局文件不存在，尝试从EditorPrefs加载（向后兼容）
                jsonData = EditorPrefs.GetString("VicTools_ScenesTools_ResourceBox", "");
                Debug.Log($"全局文件不存在，从EditorPrefs加载资源箱数据");
            }
            
            if (string.IsNullOrEmpty(jsonData))
            {
                // 没有保存的数据
                return;
            }

            try
            {
                var resourceBoxData = JsonUtility.FromJson<ResourceBoxData>(jsonData);
                
                if (resourceBoxData == null)
                {
                    return;
                }

                // 加载资源箱对象
                _resourceBox.Clear();
                _nullObjectDisplayNames.Clear(); // 清空null对象显示名称字典
                _resourceBoxInstanceIDs.Clear(); // 清空InstanceID映射
                _instanceIDToDisplayName.Clear(); // 清空InstanceID到displayName的映射
                var hasSceneObjectChanges = false; // 标记是否有场景对象变化
                var hasDataInconsistency = false; // 标记是否有数据不一致

                if (resourceBoxData.items != null)
                {
                    foreach (var item in resourceBoxData.items)
                    {
                        Object obj = null;
                        var debugInfo = ""; // 添加debugInfo变量定义
                        
                        if (item.isAsset)
                        {
                            // 加载项目资源
                            var assetPath = AssetDatabase.GUIDToAssetPath(item.guid);
                            if (!string.IsNullOrEmpty(assetPath))
                            {
                                obj = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                                debugInfo = $"项目资源: {item.name} (GUID: {item.guid})";
                                
                                // 检查项目资源是否仍然存在且类型匹配
                                if (obj && obj.GetType().FullName != item.type)
                                {
                                    Debug.LogWarning($"项目资源类型不匹配: 期望={item.type}, 实际={obj.GetType().FullName}");
                                    hasDataInconsistency = true;
                                }
                            }
                            else
                            {
                                debugInfo = $"项目资源未找到: {item.name} (GUID: {item.guid})";
                                hasDataInconsistency = true;
                            }
                        }
                        else
                        {
                            // 处理场景对象 - 使用新的标识符系统
                            if (item.guid.StartsWith("ROOT:") || item.guid.StartsWith("SCENE:"))
                            {
                                // 使用新的FindGameObjectByIdentifier方法查找对象
                                obj = FindGameObjectByIdentifier(item.guid);
                                debugInfo = $"场景对象: {item.name} (标识符: {item.guid})";
                                
                                // 检查是否需要更新标识符
                                if (obj)
                                {
                                    var currentIdentifier = GetGameObjectUniqueIdentifier(obj as GameObject);
                                    if (currentIdentifier != item.guid)
                                    {
                                        // 标识符发生变化，标记需要更新
                                        hasSceneObjectChanges = true;
                                        debugInfo += $" → 标识符已更新: {currentIdentifier}";
                                    }
                                    // else
                                    // {
                                    //     Debug.Log($"场景对象标识符匹配: {currentIdentifier}");
                                    // }
                                    
                                    // 检查场景对象类型是否匹配
                                    if (obj.GetType().FullName != item.type)
                                    {
                                        Debug.LogWarning($"场景对象类型不匹配: 期望={item.type}, 实际={obj.GetType().FullName}");
                                        hasDataInconsistency = true;
                                    }
                                }
                                    
                                if (obj == null)
                                {
                                    // 如果通过标识符找不到，尝试通过实例ID查找
                                    var parts = item.guid.Split(':');
                                    if (parts.Length >= 4 && int.TryParse(parts[3], out int instanceID))
                                    {
                                        // 使用InstanceIDToObject查找对象
                                        var instanceObj = EditorUtility.InstanceIDToObject(instanceID);
                                        if (instanceObj && instanceObj is GameObject gameObject)
                                        {
                                            // 验证场景匹配
                                            var currentScene = SceneManager.GetActiveScene();
                                            if (gameObject.scene == currentScene)
                                            {
                                                obj = gameObject;
                                                debugInfo += $" → 通过实例ID找到: {obj.name} (InstanceID: {instanceID})";
                                                // 使用实例ID找到说明标识符系统有问题
                                            }
                                            else
                                            {
                                                debugInfo += $" → 实例ID对象场景不匹配: {instanceID}";
                                            }

                                            hasDataInconsistency = true; // 使用实例ID找到说明标识符系统有问题
                                        }
                                        else
                                        {
                                            // 如果InstanceIDToObject失败，尝试在场景中查找具有相同实例ID的对象
                                            var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                                            foreach (var sceneObj in allObjects)
                                            {
                                                if (sceneObj.GetInstanceID() == instanceID)
                                                {
                                                    // 验证场景匹配
                                                    var currentScene = SceneManager.GetActiveScene();
                                                    if (sceneObj.scene == currentScene)
                                                    {
                                                        obj = sceneObj;
                                                        debugInfo += $" → 通过场景遍历找到: {obj.name} (InstanceID: {instanceID})";
                                                        hasDataInconsistency = true; // 使用实例ID找到说明标识符系统有问题
                                                        break;
                                                    }
                                                }
                                            }
                                            if (obj == null)
                                            {
                                                debugInfo += $" → 实例ID查找失败: {instanceID}";
                                                hasDataInconsistency = true;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        hasDataInconsistency = true;
                                    }
                                }
                            }
                            else if (item.guid.StartsWith("INSTANCE:"))
                            {
                                // 实例ID对象：尝试通过实例ID查找
                                var instancePart = item.guid.Substring("INSTANCE:".Length);
                                if (int.TryParse(instancePart, out int instanceID))
                                {
                                    var instanceObj = EditorUtility.InstanceIDToObject(instanceID);
                                    if (instanceObj && instanceObj is GameObject gameObject)
                                    {
                                        // 验证场景匹配
                                        var currentScene = SceneManager.GetActiveScene();
                                        if (gameObject.scene == currentScene)
                                        {
                                            obj = gameObject;
                                            debugInfo = $"实例对象: {item.name} (InstanceID: {instanceID})";
                                            // 使用实例ID说明需要更新为新的标识符系统
                                        }
                                        else
                                        {
                                            debugInfo = $"实例ID对象场景不匹配: {item.name} (InstanceID: {instanceID})";
                                        }
                                        // 使用实例ID说明需要更新为新的标识符系统
                                    }
                                    else
                                    {
                                        debugInfo = $"实例ID查找失败: {item.name} (InstanceID: {instanceID})";
                                    }

                                    hasDataInconsistency = true; // 使用实例ID说明需要更新为新的标识符系统
                                }
                                else
                                {
                                    debugInfo = $"实例ID格式错误: {item.name} (标识符: {item.guid})";
                                    hasDataInconsistency = true;
                                }
                            }
                            else if (item.guid == "NULL:OBJECT")
                            {
                                // 处理null对象
                                obj = null;
                                debugInfo = $"null对象: {item.displayName}";
                            }
                            else
                            {
                                // 向后兼容：处理旧的SCENE:格式标识符
                                if (item.guid.StartsWith("SCENE:"))
                                {
                                    var parts = item.guid.Split(':');
                                    if (parts.Length >= 3)
                                    {
                                        var fullPath = parts[2];
                                        
                                        // 尝试使用旧的方法查找
                                        obj = FindGameObjectByPath(fullPath);
                                        debugInfo = $"旧格式场景对象: {item.name} (路径: {fullPath})";
                                        hasDataInconsistency = true; // 旧格式需要更新
                                    }
                                }
                                else
                                {
                                    debugInfo = $"未知标识符格式: {item.name} (标识符: {item.guid})";
                                    hasDataInconsistency = true;
                                }
                            }
                        }

                        // 即使对象为null，也添加到资源箱，以便显示对象名称
                        if (obj)
                        {
                            // 检查对象是否具有kDontSaveInEditor标志，如果有则清除它
                            if (((int)obj.hideFlags & (int)HideFlags.DontSaveInEditor) != 0)
                            {
                                obj.hideFlags &= ~HideFlags.DontSaveInEditor;
                                debugInfo += " → 已清除DontSaveInEditor标志";
                            }
                            
                            // 额外的安全检查：确保对象在添加到资源箱前是有效的
                            if (obj && !obj.Equals(null))
                            {
                                _resourceBox.Add(obj);
                                
                                // 保存displayName到字典（如果ResourceBoxItem中有displayName字段）
                                if (!string.IsNullOrEmpty(item.displayName))
                                {
                                    _resourceBoxDisplayNames[obj] = item.displayName;
                                }
                                // Debug.Log($"✓ 成功加载对象: {debugInfo}");
                            }
                            else
                            {
                                Debug.LogWarning($"✗ 对象无效，跳过添加: {debugInfo}");
                                hasDataInconsistency = true;
                            }
                        }
                        else
                        {
                            // 对象为null，但仍然添加到资源箱以便显示
                            _resourceBox.Add(null);
                            hasDataInconsistency = true;
                            
                            // 保存displayName到null对象字典，使用当前索引作为键
                            if (!string.IsNullOrEmpty(item.displayName))
                            {
                                _nullObjectDisplayNames[_resourceBox.Count - 1] = item.displayName;
                            }
                            else
                            {
                                // 如果没有displayName，使用默认名称
                                _nullObjectDisplayNames[_resourceBox.Count - 1] = $"<未知类型> {item.name}";
                            }
                            
                            // Debug.LogWarning($"✗ 无法加载对象，但保留显示名称: {debugInfo}");
                        }
                    }
                }

                // Debug.Log($"资源箱数据已加载，成功恢复 {loadedCount} 个对象");
                
                // 检查是否有场景对象变化或数据不一致，如果有则自动保存更新数据
                if (hasSceneObjectChanges || hasDataInconsistency)
                {
                    Debug.Log($"检测到数据差异，自动更新资源箱数据... (场景对象变化: {hasSceneObjectChanges}, 数据不一致: {hasDataInconsistency})");
                    // 自动保存更新后的数据
                    SaveResourceBox(false);
                    Debug.Log("资源箱数据已自动更新");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"加载资源箱数据时出错: {e.Message}");
                // 在出错时清空资源箱，避免使用损坏的数据
                _resourceBox.Clear();
                _nullObjectDisplayNames.Clear(); // 清空null对象显示名称字典
                _resourceBoxInstanceIDs.Clear(); // 清空InstanceID映射
                _instanceIDToDisplayName.Clear(); // 清空InstanceID到displayName的映射
            }
        }

        /// <summary>
        /// 从displayName中提取原始对象名称
        /// </summary>
        private string ExtractOriginalNameFromDisplayName(string displayName)
        {
            // displayName格式示例: 
            // 1. "<GameObject> [Parent]← ObjectName" - 提取"ObjectName"
            // 2. "<GameObject> ObjectName" - 提取"ObjectName"
            // 3. "<GameObject> Object Name With Spaces" - 提取"Object Name With Spaces"
            
            // 查找"← "分隔符（注意包含空格）
            int arrowIndex = displayName.LastIndexOf("← ");
            if (arrowIndex != -1)
            {
                // 提取箭头后面的部分
                return displayName.Substring(arrowIndex + 2).Trim();
            }
            
            // 如果没有箭头，查找第一个"> "后的内容
            int typeEndIndex = displayName.IndexOf("> ");
            if (typeEndIndex != -1)
            {
                // 提取类型后面的部分
                string afterType = displayName.Substring(typeEndIndex + 2).Trim();
                
                // 检查是否有方括号父对象标记
                int bracketStart = afterType.IndexOf('[');
                if (bracketStart != -1)
                {
                    // 有父对象标记，查找"]← "分隔符
                    int bracketArrowIndex = afterType.LastIndexOf("]← ");
                    if (bracketArrowIndex != -1)
                    {
                        return afterType.Substring(bracketArrowIndex + 3).Trim();
                    }
                }
                else
                {
                    // 没有父对象标记，直接返回类型后面的所有内容
                    return afterType;
                }
            }
            
            // 如果以上方法都失败，尝试查找最后一个空格（备用方案）
            int spaceIndex = displayName.LastIndexOf(' ');
            if (spaceIndex != -1 && spaceIndex > 0)
            {
                // 确保不是提取类型名（如"GameObject"）
                string beforeSpace = displayName.Substring(0, spaceIndex);
                if (!beforeSpace.EndsWith(">") && !beforeSpace.EndsWith("]"))
                {
                    return displayName.Substring(spaceIndex + 1).Trim();
                }
            }
            
            return displayName;
        }

        /// 获取GameObject的唯一标识符（解决重名问题）
        private static string GetGameObjectUniqueIdentifier(GameObject gameObject)
        {
            if (!gameObject) return "";
            
            var fullPath = GetGameObjectFullPath(gameObject);
            var scenePath = gameObject.scene.path;
            
            // 如果对象是根对象且没有父级
            if (!gameObject.transform.parent)
            {
                // 如果scenePath为空或空字符串（未保存的场景），使用场景名称和实例ID作为备用标识符
                if (!string.IsNullOrEmpty(scenePath))
                    return $"ROOT:{scenePath}:{gameObject.name}:{gameObject.GetInstanceID()}";
                var sceneName = gameObject.scene.name;
                if (string.IsNullOrEmpty(sceneName))
                {
                    sceneName = "UnsavedScene";
                }
                return $"ROOT:{sceneName}:{gameObject.name}:{gameObject.GetInstanceID()}";
                // 对于已保存的场景中的根对象，也添加实例ID确保唯一性
            }
            
            // 对于有层级路径的对象，如果scenePath为空或空字符串，也添加实例ID确保唯一性
            if (string.IsNullOrEmpty(scenePath))
            {
                string sceneName = gameObject.scene.name;
                if (string.IsNullOrEmpty(sceneName))
                {
                    sceneName = "UnsavedScene";
                }
                return $"SCENE:{sceneName}:{fullPath}:{gameObject.GetInstanceID()}";
            }
            
            // 对于已保存的场景中的有层级路径对象，也添加实例ID确保唯一性
            return $"SCENE:{scenePath}:{fullPath}:{gameObject.GetInstanceID()}";
        }

        /// <summary>
        /// 通过唯一标识符查找场景中的GameObject（优化版本，解决重名问题）
        /// </summary>
        private GameObject FindGameObjectByIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;
            
            // 解析标识符
            var parts = identifier.Split(':');
            if (parts.Length < 3) return null;
            
            var identifierType = parts[0];
            var scenePath = parts[1];
            var objectPath = parts[2];
            
            // 处理包含实例ID的标识符（新格式）
            var hasInstanceID = parts.Length >= 4;
            var instanceID = hasInstanceID && int.TryParse(parts[3], out int id) ? id : -1;
            
            GameObject foundObject = null;
            var currentScene = SceneManager.GetActiveScene();
            
            // 首先尝试精确匹配（包括场景匹配）
            if (identifierType == "ROOT")
            {
                // 查找根对象
                var rootObjects = currentScene.GetRootGameObjects();
                foreach (var rootObject in rootObjects)
                {
                    if (rootObject.name == objectPath)
                    {
                        // 如果有实例ID，验证是否匹配
                        if (hasInstanceID && instanceID != -1)
                        {
                            if (rootObject.GetInstanceID() == instanceID)
                            {
                                foundObject = rootObject;
                                break;
                            }
                        }
                        else
                        {
                            foundObject = rootObject;
                            break;
                        }
                    }
                }
            }
            else if (identifierType == "SCENE")
            {
                // 查找有层级路径的对象
                var rootObjects = currentScene.GetRootGameObjects();
                foreach (var rootObject in rootObjects)
                {
                    Transform foundTransform = rootObject.transform.Find(objectPath);
                    if (foundTransform != null)
                    {
                        // 如果有实例ID，验证是否匹配
                        if (hasInstanceID && instanceID != -1)
                        {
                            if (foundTransform.gameObject.GetInstanceID() == instanceID)
                            {
                                foundObject = foundTransform.gameObject;
                                break;
                            }
                        }
                        else
                        {
                            foundObject = foundTransform.gameObject;
                            break;
                        }
                    }
                }
                
                // 如果直接路径查找失败，尝试通过完整路径匹配
                if (foundObject == null)
                {
                    var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                    foreach (var obj in allObjects)
                    {
                        string currentPath = GetGameObjectFullPath(obj);
                        if (currentPath == objectPath)
                        {
                            // 如果有实例ID，验证是否匹配
                            if (hasInstanceID && instanceID != -1)
                            {
                                if (obj.GetInstanceID() == instanceID)
                                {
                                    foundObject = obj;
                                    break;
                                }
                            }
                            else
                            {
                                foundObject = obj;
                                break;
                            }
                        }
                    }
                }
            }
            
            // 如果精确匹配失败，尝试通过实例ID查找（作为备用方案）
            if (foundObject == null && hasInstanceID && instanceID != -1)
            {
                Object obj = EditorUtility.InstanceIDToObject(instanceID);
                if (obj is GameObject gameObject && gameObject != null)
                {
                    // 不再验证场景匹配，允许跨场景恢复
                    Debug.Log($"通过实例ID成功找到对象: {gameObject.name} (InstanceID: {instanceID})");
                    foundObject = gameObject;
                }
                else
                {
                    // 如果InstanceIDToObject失败，尝试在场景中查找具有相同实例ID的对象
                    var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                    foreach (var sceneObj in allObjects)
                    {
                        if (sceneObj.GetInstanceID() == instanceID)
                        {
                            // 不再验证场景匹配，允许跨场景恢复
                            Debug.Log($"通过场景遍历找到对象: {sceneObj.name} (InstanceID: {instanceID})");
                            foundObject = sceneObj;
                            break;
                        }
                    }
                }
            }
            
            // 如果仍然找不到，尝试基于名称和路径的模糊匹配（最终备用方案）
            if (foundObject == null)
            {
                foundObject = FindGameObjectByFuzzyMatching(objectPath);
                if (foundObject != null)
                {
                    Debug.Log($"通过模糊匹配找到对象: {foundObject.name} (路径: {objectPath})");
                }
            }
            
            // 如果找到了对象，但场景不匹配，记录警告但不阻止恢复
            // if (foundObject != null)
            // {
            //     var currentScenePath = currentScene.path;
            //     if (!string.IsNullOrEmpty(scenePath) && !string.IsNullOrEmpty(currentScenePath) && scenePath != currentScenePath)
            //     {
            //         Debug.LogWarning($"场景不匹配但成功恢复对象：保存时的场景 '{scenePath}'，当前场景 '{currentScenePath}'，对象 '{foundObject.name}'");
            //     }
            // }
            
            return foundObject;
        }

        /// <summary>
        /// 通过完整路径查找场景中的GameObject
        /// </summary>
        private GameObject FindGameObjectByPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;
            
            var currentScene = SceneManager.GetActiveScene();
            var rootObjects = currentScene.GetRootGameObjects();

            return (from rootObject in rootObjects select rootObject.transform.Find(fullPath) into foundTransform where foundTransform select foundTransform.gameObject).FirstOrDefault();
        }

        /// <summary>
        /// 通过模糊匹配查找场景中的GameObject（最终备用方案）
        /// </summary>
        private static GameObject FindGameObjectByFuzzyMatching(string objectPath)
        {
            var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            
            // 尝试基于名称的模糊匹配
            var pathParts = objectPath.Split('/');
            var targetName = pathParts.Length > 0 ? pathParts[pathParts.Length - 1] : objectPath;

            return (from obj in allObjects where obj.name == targetName let currentPath = GetGameObjectFullPath(obj) where currentPath.EndsWith(objectPath) || objectPath.EndsWith(currentPath) select obj).FirstOrDefault();
        }
        

        /// <summary>
        /// 资源箱数据序列化类
        /// </summary>
        [Serializable]
        private class ResourceBoxData
        {
            public List<ResourceBoxItem> items;
        }

        /// <summary>
        /// 资源箱项目序列化类
        /// </summary>
        [Serializable]
        private class ResourceBoxItem
        {
            public string guid;
            public string type;
            public string name;
            public bool isAsset;
            public string displayName; // 添加displayName字段，用于保存对象的显示名称
        }

        #region 资源箱文件管理方法

        /// <summary>
        /// 刷新可用资源箱文件列表
        /// </summary>
        private void RefreshAvailableFiles()
        {
            _availableFiles.Clear();
            
            // 使用PathHelper获取正确的资源箱目录路径
            var fullDirectoryPath = PathHelper.GetAssetsEditorPath();
            if (!System.IO.Directory.Exists(fullDirectoryPath))
            {
                System.IO.Directory.CreateDirectory(fullDirectoryPath);
                return;
            }

            // 获取所有JSON文件
            var jsonFiles = System.IO.Directory.GetFiles(fullDirectoryPath, "*.json");
            foreach (var filePath in jsonFiles)
            {
                var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                _availableFiles.Add(fileName);
            }

            // 如果没有文件，添加默认选项
            if (_availableFiles.Count == 0)
            {
                _availableFiles.Add("新建资源箱");
            }
        }

        /// <summary>
        /// 保存资源箱数据到文件（优化版本，解决重名问题）
        /// </summary>
        /// <param name="showConfirmation">是否显示确认提示框</param>
        private void SaveResourceBoxToFile(bool showConfirmation = true)
        {
            // 如果资源箱文件名为空，自动获取当前场景名作为资源箱名
            if (string.IsNullOrEmpty(_resourceBoxFileName))
            {
                var currentSceneName = SceneManager.GetActiveScene().name;
                if (string.IsNullOrEmpty(currentSceneName))
                {
                    currentSceneName = "UntitledScene";
                }
                _resourceBoxFileName = currentSceneName;
            }

            // 如果需要显示确认提示框，则显示
            if (showConfirmation)
            {
                var confirm = EditorUtility.DisplayDialog(
                    "确认保存",
                    $"确定要将资源箱中的 {_resourceBox.Count} 个对象保存到\n文件 <{_resourceBoxFileName}> 吗？",
                    "确定保存",
                    "取消"
                );

                if (!confirm)
                {
                    Debug.Log("用户取消了保存操作");
                    return;
                }
            }

            // 创建资源箱文件数据
            var fileData = new ResourceBoxFileData
            {
                LastModifiedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            // 转换资源箱对象为序列化数据
            foreach (var obj in _resourceBox.Where(obj => obj))
            {
                // 检查对象是否具有kDontSaveInEditor标志，如果有则跳过保存
                if (((int)obj.hideFlags & (int)HideFlags.DontSaveInEditor) != 0)
                {
                    Debug.LogWarning($"对象 {obj.name} 具有DontSaveInEditor标志，跳过保存到文件");
                    continue;
                }
                    
                string guid;
                var assetPath = AssetDatabase.GetAssetPath(obj);
                    
                if (!string.IsNullOrEmpty(assetPath))
                {
                    // 项目资源使用GUID
                    guid = AssetDatabase.AssetPathToGUID(assetPath);
                }
                else
                {
                    // 场景对象使用优化的唯一标识符
                    if (obj is GameObject gameObject)
                    {
                        // 使用新的唯一标识符系统
                        guid = GetGameObjectUniqueIdentifier(gameObject);
                    }
                    else
                    {
                        guid = $"INSTANCE:{obj.GetInstanceID()}";
                    }
                }

                // 计算displayName（与DrawResourceBoxSection中相同的逻辑）
                string displayName;
                var parentName = GetTopLevelParentName(obj);
                displayName = !string.IsNullOrEmpty(parentName) 
                    ? $"<{obj.GetType().Name}> [{parentName}]← {obj.name}" 
                    : $"<{obj.GetType().Name}> {obj.name}";

                fileData.items.Add(new ResourceBoxFileItem
                {
                    guid = guid,
                    Type = obj.GetType().FullName,
                    name = obj.name,
                    isAsset = !string.IsNullOrEmpty(assetPath),
                    displayName = displayName  // 保存displayName
                });
            }

            // 序列化并保存到文件
            var jsonData = JsonUtility.ToJson(fileData, true);
            // 使用PathHelper获取正确的资源箱目录路径
            var resourceBoxDirectoryPath = PathHelper.GetAssetsEditorPath();
            var filePath = System.IO.Path.Combine(resourceBoxDirectoryPath, _resourceBoxFileName + ".json");
            
            try
            {
                System.IO.File.WriteAllText(filePath, jsonData);
                AssetDatabase.Refresh();
                RefreshAvailableFiles();
                
                // 更新选中文件索引
                _selectedFileIndex = _availableFiles.IndexOf(_resourceBoxFileName);
                if (_selectedFileIndex == -1) _selectedFileIndex = 0;
                
                Debug.Log($"资源箱数据已保存到文件: {_resourceBoxFileName}.json");
                // EditorUtility.DisplayDialog("保存成功", $"资源箱数据已保存到文件: {resourceBoxFileName}.json", "确定");
            }
            catch (Exception e)
            {
                Debug.LogError($"保存资源箱文件时出错: {e.Message}");
                EditorUtility.DisplayDialog("保存失败", $"保存文件时出错: {e.Message}", "确定");
            }
        }

        /// <summary>
        /// 从文件加载资源箱数据
        /// </summary>
        private void LoadResourceBoxFromFile()
        {
            if (_availableFiles.Count == 0 || _selectedFileIndex < 0 || _selectedFileIndex >= _availableFiles.Count)
            {
                EditorUtility.DisplayDialog("错误", "没有可用的资源箱文件", "确定");
                return;
            }

            string fileName = _availableFiles[_selectedFileIndex];
            if (fileName == "新建资源箱")
            {
                EditorUtility.DisplayDialog("提示", "请先保存一个新的资源箱文件", "确定");
                return;
            }

            // 如果资源箱中有内容，提示用户是否替换
            if (_resourceBox.Count > 0)
            {
                var confirm = EditorUtility.DisplayDialog(
                    "确认替换",
                    "资源箱中已有内容，确定要替换为文件中的内容吗？",
                    "确定替换",
                    "取消"
                );

                if (!confirm)
                {
                    Debug.Log("用户取消了替换操作");
                    return;
                }
            }

            // 使用PathHelper获取正确的资源箱目录路径
            var resourceBoxDirectoryPath = PathHelper.GetAssetsEditorPath();
            var filePath = System.IO.Path.Combine(resourceBoxDirectoryPath, fileName + ".json");
            
            if (!System.IO.File.Exists(filePath))
            {
                EditorUtility.DisplayDialog("错误", $"文件不存在: {fileName}.json", "确定");
                return;
            }

            try
            {
                var jsonData = System.IO.File.ReadAllText(filePath);
                var fileData = JsonUtility.FromJson<ResourceBoxFileData>(jsonData);
                
                if (fileData == null)
                {
                    EditorUtility.DisplayDialog("错误", "文件格式不正确", "确定");
                    return;
                }

                // 清空当前资源箱
                _resourceBox.Clear();
                _nullObjectDisplayNames.Clear(); // 清空null对象显示名称字典
                _resourceBoxInstanceIDs.Clear(); // 清空InstanceID映射
                _instanceIDToDisplayName.Clear(); // 清空InstanceID到displayName的映射
                var loadedCount = 0;
                var failedCount = 0;
                var hasSceneObjectChanges = false; // 标记是否有场景对象变化

                Debug.Log($"开始从文件加载资源箱数据: {fileName}.json，包含 {fileData.items.Count} 个项目");

                // 加载资源箱对象
                for (var i = 0; i < fileData.items.Count; i++)
                {
                    var item = fileData.items[i];
                    Object obj = null;
                    var debugInfo = "";
                    
                    if (item.isAsset)
                    {
                        // 加载项目资源
                        var assetPath = AssetDatabase.GUIDToAssetPath(item.guid);
                        if (!string.IsNullOrEmpty(assetPath))
                        {
                            obj = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                            debugInfo = $"项目资源: {item.name} (GUID: {item.guid})";
                        }
                        else
                        {
                            debugInfo = $"项目资源未找到: {item.name} (GUID: {item.guid})";
                        }
                    }
                    else
                    {
                        // 处理场景对象 - 使用新的标识符系统
                        if (item.guid.StartsWith("ROOT:") || item.guid.StartsWith("SCENE:"))
                        {
                            // 使用新的FindGameObjectByIdentifier方法查找对象
                            obj = FindGameObjectByIdentifier(item.guid);
                            debugInfo = $"场景对象: {item.name} (标识符: {item.guid})";
                            
                            // 检查是否需要更新标识符
                            if (obj)
                            {
                                var currentIdentifier = GetGameObjectUniqueIdentifier(obj as GameObject);
                                if (currentIdentifier != item.guid)
                                {
                                    // 标识符发生变化，标记需要更新
                                    hasSceneObjectChanges = true;
                                    debugInfo += $" → 标识符已更新: {currentIdentifier}";
                                }
                            }
                            
                            if (!obj)
                            {
                                // 如果通过标识符找不到，尝试通过实例ID查找
                                var parts = item.guid.Split(':');
                                if (parts.Length >= 4 && int.TryParse(parts[3], out int instanceID))
                                {
                                    var instanceObj = EditorUtility.InstanceIDToObject(instanceID);
                                    if (instanceObj && instanceObj is GameObject gameObject)
                                    {
                                        // 验证场景匹配
                                        var currentScene = SceneManager.GetActiveScene();
                                        if (gameObject.scene == currentScene)
                                        {
                                            obj = gameObject;
                                            debugInfo += $" → 通过实例ID找到: {obj.name} (InstanceID: {instanceID})";
                                        }
                                        else
                                        {
                                            debugInfo += $" → 实例ID对象场景不匹配: {instanceID}";
                                        }
                                    }
                                    else
                                    {
                                        // 如果InstanceIDToObject失败，尝试在场景中查找具有相同实例ID的对象
                                        var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                                        foreach (var sceneObj in allObjects)
                                        {
                                            if (sceneObj.GetInstanceID() != instanceID) continue;
                                            // 验证场景匹配
                                            var currentScene = SceneManager.GetActiveScene();
                                            if (sceneObj.scene != currentScene) continue;
                                            obj = sceneObj;
                                            debugInfo += $" → 通过场景遍历找到: {obj.name} (InstanceID: {instanceID})";
                                            break;
                                        }
                                        if (!obj)
                                        {
                                            debugInfo += $" → 实例ID查找失败: {instanceID}";
                                        }
                                    }
                                }
                            }
                        }
                        else if (item.guid.StartsWith("INSTANCE:"))
                        {
                            // 实例ID对象：尝试通过实例ID查找
                            var instancePart = item.guid.Substring("INSTANCE:".Length);
                            if (int.TryParse(instancePart, out int instanceID))
                            {
                                var instanceObj = EditorUtility.InstanceIDToObject(instanceID);
                                if (instanceObj && instanceObj is GameObject gameObject)
                                {
                                    // 验证场景匹配
                                    var currentScene = SceneManager.GetActiveScene();
                                    if (gameObject.scene == currentScene)
                                    {
                                        obj = gameObject;
                                        debugInfo = $"实例对象: {item.name} (InstanceID: {instanceID})";
                                    }
                                    else
                                    {
                                        debugInfo = $"实例ID对象场景不匹配: {item.name} (InstanceID: {instanceID})";
                                    }
                                }
                                else
                                {
                                    debugInfo = $"实例ID查找失败: {item.name} (InstanceID: {instanceID})";
                                }
                            }
                            else
                            {
                                debugInfo = $"实例ID格式错误: {item.name} (标识符: {item.guid})";
                            }
                        }
                        else
                        {
                            // 向后兼容：处理旧的SCENE:格式标识符
                            if (item.guid.StartsWith("SCENE:"))
                            {
                                var parts = item.guid.Split(':');
                                if (parts.Length >= 3)
                                {
                                    var fullPath = parts[2];
                                    
                                    // 尝试使用旧的方法查找
                                    obj = FindGameObjectByPath(fullPath);
                                    debugInfo = $"旧格式场景对象: {item.name} (路径: {fullPath})";
                                }
                            }
                            else
                            {
                                debugInfo = $"未知标识符格式: {item.name} (标识符: {item.guid})";
                            }
                        }
                    }

                    // 即使对象为null，也添加到资源箱，以便显示对象名称
                    if (obj)
                    {
                        // 检查对象是否具有kDontSaveInEditor标志，如果有则清除它
                        if (((int)obj.hideFlags & (int)HideFlags.DontSaveInEditor) != 0)
                        {
                            obj.hideFlags &= ~HideFlags.DontSaveInEditor;
                        }
                        
                        _resourceBox.Add(obj);
                        loadedCount++;
                        
                        // 保存displayName到字典（如果ResourceBoxFileItem中有displayName字段）
                        if (!string.IsNullOrEmpty(item.displayName))
                        {
                            _resourceBoxDisplayNames[obj] = item.displayName;
                        }
                    }
                    else
                    {
                        // 对象为null，但仍然添加到资源箱以便显示
                        _resourceBox.Add(null);
                        failedCount++;
                        
                        // 保存displayName到null对象字典，使用当前索引作为键
                        if (!string.IsNullOrEmpty(item.displayName))
                        {
                            _nullObjectDisplayNames[_resourceBox.Count - 1] = item.displayName;
                        }
                        else
                        {
                            // 如果没有displayName，使用默认名称
                            _nullObjectDisplayNames[_resourceBox.Count - 1] = $"<未知类型> {item.name}";
                        }
                        
                        // Debug.LogWarning($"[{i+1}/{fileData.items.Count}] ✗ 无法加载对象，但保留显示名称: {debugInfo}");
                    }
                }

                _resourceBoxFileName = fileName;
                
                Debug.Log($"从文件加载资源箱数据完成: {fileName}.json (成功: {loadedCount}, 失败: {failedCount}, 总计: {fileData.items.Count})");
                
                // 检查是否有场景对象变化，如果有则自动保存更新数据
                if (hasSceneObjectChanges)
                {
                    Debug.Log("检测到场景对象标识符变化，自动更新资源箱数据...");
                    // 同时更新EditorPrefs和文件数据，确保数据一致性
                    // SaveResourceBox(false);
                    SaveResourceBoxToFile(false);
                    Debug.Log("资源箱数据已自动更新（EditorPrefs和文件）");
                }
                SaveResourceBox(false);
                // 强制重绘窗口
                if (Parent)
                    Parent.Repaint();
            }
            catch (Exception e)
            {
                Debug.LogError($"加载资源箱文件时出错: {e.Message}");
                EditorUtility.DisplayDialog("加载失败", $"加载文件时出错: {e.Message}", "确定");
            }
        }

        #endregion

        

        /// 更新选中数量显示
        private void UpdateSelectedCountDisplay()
        {
            // 支持独立窗口和主窗口
            var standaloneWindow = Parent as WinScenesTools;
            if (standaloneWindow != null)
            {
                // 更新独立窗口的选中数量
                standaloneWindow.StandaloneSelectedCount = Selection.gameObjects.Length;
                standaloneWindow.Repaint();
            }
            else
            {
                // 更新主窗口的全局选中数量
                var mainWindow = Parent as VicToolsWindow;
                if (mainWindow != null)
                {
                    mainWindow.globalSelectedObjectsCount = Selection.gameObjects.Length;
                    mainWindow.Repaint();
                }
            }
        }

        /// <summary>
        /// 重新检查资源箱中的null对象，尝试在当前场景中重新加载它们
        /// </summary>
        private void RecheckNullObjectsInResourceBox()
        {
            bool hasChanges = false;
            
            for (int i = 0; i < _resourceBox.Count; i++)
            {
                var obj = _resourceBox[i];
                
                // 如果对象为null，尝试重新加载
                if (!obj)
                {
                    // 尝试从null对象字典中获取保存的displayName
                    if (_nullObjectDisplayNames.TryGetValue(i, out string savedDisplayName))
                    {
                        // 从displayName中提取原始对象名称
                        string originalName = ExtractOriginalNameFromDisplayName(savedDisplayName);
                        
                        // 尝试在当前场景中查找同名对象
                        var foundObject = FindObjectByNameInCurrentScene(originalName);
                        
                        if (foundObject)
                        {
                            // 找到对象，替换资源箱中的null
                            _resourceBox[i] = foundObject;
                            
                            // 将displayName移动到_resourceBoxDisplayNames字典
                            _resourceBoxDisplayNames[foundObject] = savedDisplayName;
                            
                            // 从null对象字典中移除
                            _nullObjectDisplayNames.Remove(i);
                            
                            hasChanges = true;
                            Debug.Log($"重新找到对象: {savedDisplayName}");
                        }
                    }
                }
            }
            
            if (hasChanges)
            {
                // 保存更新后的资源箱数据
                SaveResourceBox(false);
                Debug.Log("已更新资源箱中的null对象");
            }
        }

        /// <summary>
        /// 在当前场景中按名称查找对象
        /// </summary>
        private GameObject FindObjectByNameInCurrentScene(string objectName)
        {
            var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in allObjects)
            {
                if (obj.name == objectName)
                {
                    return obj;
                }
            }
            return null;
        }

        /// <summary>
        /// 获取GameObject的完整路径（从根对象到当前对象的路径）
        /// </summary>
        private static string GetGameObjectFullPath(GameObject gameObject)
        {
            if (!gameObject) return "";
            
            var path = gameObject.name;
            var current = gameObject.transform.parent;
            
            while (current)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            
            return path;
        }
        
    }
}
