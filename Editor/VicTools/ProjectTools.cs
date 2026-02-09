using UnityEngine;
using UnityEditor;
using System.Collections.Generic; // 用于List、HashSet等集合类型
using System.Linq; // 用于LINQ操作，如Count()等
using UnityEngine.SceneManagement; // 用于场景管理，如Scene类、场景操作等
using UnityEditor.SceneManagement;
using System.Xml;
// using System.Diagnostics; // 用于编辑器场景管理，如EditorSceneManager、OpenSceneMode等

namespace VicTools
{
    
    /// 重命名预览项
    [System.Serializable]
    public class RenamePreviewItem
    {
        public string originalName;
        public string newName;
        public string assetPath;
        public bool willConflict;
        
        public RenamePreviewItem(string originalName, string newName, string assetPath, bool willConflict = false)
        {
            this.originalName = originalName;
            this.newName = newName;
            this.assetPath = assetPath;
            this.willConflict = willConflict;
        }
    }

    /// 贴图检查结果项
    [System.Serializable]
    public class TextureCheckResult
    {
        public string texturePath;
        public string textureName;
        public bool isCorrect;
        public List<string> errorDetails;
        public Texture2D textureObject;
        
        public TextureCheckResult(string texturePath, bool isCorrect, List<string> errorDetails)
        {
            this.texturePath = texturePath;
            this.textureName = System.IO.Path.GetFileName(texturePath);
            this.isCorrect = isCorrect;
            this.errorDetails = errorDetails ?? new List<string>();
            this.textureObject = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        }
    }

    
    /// 项目资源工具子窗口
    public class ProjectTools : SubWindow
    {
        private int _maxSizeValue = 512;
        private int _filterValue = 512; // 过滤值，只有当贴图当前MaxSize大于此值时才会被设置
        private bool _includeSubfolders;
        private bool _workPath;
        private bool _editSrgb;
        private bool _editMipMap;
        private bool _editAlphaIsTrans;
        private bool _useTextureSizeFilter;
        private bool _useParameters;
        private TextureImporterAlphaSource _editAlphaSource = TextureImporterAlphaSource.None;
        private string _filePath = "Assets";
        private string _subffix = "";
        private int _suffixIndex; // 保存后缀选择器的索引状态
        private int _alphaSourceIndex = 0; // 保存AlphaSource选择器的索引状态，默认None
        private readonly List<string> _processedTextures = new List<string>();
        // private Vector2 scrollPosition;
        private readonly SearchHistoryManager _searchHistoryManager;
        
        // 批量重命名相关变量
        private string _renamePrefix = "";
        private string _renameSuffix = "";
        private int _startNumber = 1;
        private int _numberDigits = 2;
        private bool _showRenamePreview;
        private readonly List<RenamePreviewItem> _renamePreviewItems = new();
        // private Vector2 renamePreviewScrollPosition;
        // private int snappedMaxSizeValue = 1024; // 吸附后的MaxSize值
        // private int snappedFilterValue = 512;   // 吸附后的过滤值

        // 检查参数结果相关变量
        private bool _showCheckResult;
        private bool _showTextureEditResult;
        private readonly List<TextureCheckResult> _checkResults = new();
        private Vector2 _checkResultScrollPosition;
        
        // 已处理贴图列表滚动位置
        private Vector2 _processedTexturesScrollPosition;

        public ProjectTools(string name, EditorWindow parent) : base("[资源工具 v1.3]", parent)
        {
            // 初始化路径历史管理器，使用唯一的键名避免与场景工具冲突
            _searchHistoryManager = new SearchHistoryManager("ProjectTools_PathHistory", 10);
        }

        public override void OnEnable()
        {
            // 加载上次搜索文本
            _filePath = _searchHistoryManager.LoadLastSearchText();
            // 加载上次使用的后缀
            _subffix = EditorPrefs.GetString("ProjectTools_LastSuffix", "");
            _renamePrefix = EditorPrefs.GetString("ProjectTools_LastRenamePrefix", "");
            _renameSuffix = EditorPrefs.GetString("ProjectTools_LastRenameSuffix", "");
            _startNumber = EditorPrefs.GetInt("ProjectTools_LastStartNumber", 1);
            _numberDigits = EditorPrefs.GetInt("ProjectTools_LastNumberDigits", 2);
            _maxSizeValue = EditorPrefs.GetInt("ProjectTools_maxSizeValue", 512);
            _filterValue = EditorPrefs.GetInt("ProjectTools_filterValue", 512);
            _editSrgb = EditorPrefs.GetBool("ProjectTools_edit_sRGB", false);
            _editMipMap = EditorPrefs.GetBool("ProjectTools_edit_mipMap", false);
            _editAlphaIsTrans = EditorPrefs.GetBool("ProjectTools_edit_alphaIsTrans", false);
            _editAlphaSource = (TextureImporterAlphaSource)EditorPrefs.GetInt("ProjectTools_edit_alphaSource", (int)TextureImporterAlphaSource.None);
            _alphaSourceIndex = EditorPrefs.GetInt("ProjectTools_alphaSourceIndex", 0); // 默认None
            _useTextureSizeFilter = EditorPrefs.GetBool("ProjectTools_useTextureSizeFilter", false);
            _suffixIndex = EditorPrefs.GetInt("ProjectTools_suffixIndex", 0);
            _useParameters = EditorPrefs.GetBool("ProjectTools_useParameters", false);
        }
        public override void OnDisable()
        {
            // 保存搜索文本
            _searchHistoryManager.SaveLastSearchText(_filePath);
            // 保存当前使用的后缀
            EditorPrefs.SetString("ProjectTools_LastSuffix", _subffix);
            EditorPrefs.SetString("ProjectTools_LastRenamePrefix", _renamePrefix);
            EditorPrefs.SetString("ProjectTools_LastRenameSuffix", _renameSuffix);
            EditorPrefs.SetInt("ProjectTools_LastStartNumber", _startNumber);
            EditorPrefs.SetInt("ProjectTools_LastNumberDigits", _numberDigits);
            EditorPrefs.SetInt("ProjectTools_maxSizeValue", _maxSizeValue);
            EditorPrefs.SetInt("ProjectTools_filterValue", _filterValue);
            EditorPrefs.SetBool("ProjectTools_edit_sRGB", _editSrgb);
            EditorPrefs.SetBool("ProjectTools_edit_mipMap", _editMipMap);
            EditorPrefs.SetBool("ProjectTools_edit_alphaIsTrans", _editAlphaIsTrans);
            EditorPrefs.SetInt("ProjectTools_edit_alphaSource", (int)_editAlphaSource);
            EditorPrefs.SetInt("ProjectTools_alphaSourceIndex", _alphaSourceIndex);
            EditorPrefs.SetBool("ProjectTools_useTextureSizeFilter", _useTextureSizeFilter);
            EditorPrefs.SetInt("ProjectTools_suffixIndex", _suffixIndex);
            EditorPrefs.SetBool("ProjectTools_useParameters", _useParameters);
        }

        
        // ReSharper disable Unity.PerformanceAnalysis
        /// 将值吸附到最近的2的次方值
        
        /// <param name="value">输入值</param>
        /// <returns>吸附后的2的次方值</returns>
        // private int SnapToPowerOfTwo(int value)
        // {
        //     // 常见的2的次方值序列
        //     int[] powerOfTwoValues = { 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8196 };
            
        //     // 如果值已经是2的次方值，直接返回
        //     if (System.Array.IndexOf(powerOfTwoValues, value) >= 0)
        //     {
        //         return value;
        //     }
            
        //     // 找到最接近的2的次方值
        //     int closestValue = powerOfTwoValues[0];
        //     int minDifference = Mathf.Abs(value - closestValue);
            
        //     for (int i = 1; i < powerOfTwoValues.Length; i++)
        //     {
        //         int difference = Mathf.Abs(value - powerOfTwoValues[i]);
        //         if (difference < minDifference)
        //         {
        //             minDifference = difference;
        //             closestValue = powerOfTwoValues[i];
        //         }
        //     }
            
        //     return closestValue;
        // }

        public override void OnGUI()
        {
            var style = EditorStyle.Get;
            // 处理回车键事件 - 必须在GUI元素创建之前处理
            _searchHistoryManager.HandleEnterKeyEvent(_filePath, "ProjectTools_pathField", (filePath) =>
            {
                // 回车键按下时的回调函数
                Debug.Log($"回车执行搜索: {filePath}");
                // 这里可以添加实际的搜索逻辑
            }, true);

            EditorGUILayout.BeginVertical(style.area);

            // 批量设置贴图MaxSize
            DrawTextureMaxSizeSection();
            EditorGUILayout.Space();
            // 创建更醒目的黄色分隔线
            GUIStyle separatorStyle = new GUIStyle();
            separatorStyle.normal.background = CreateColorTexture(1, 1, new Color(0.7f, 0.5f, 0.0f)); // 黄色
            separatorStyle.normal.background.hideFlags = HideFlags.HideAndDontSave;
            GUILayout.Box("", separatorStyle, GUILayout.Height(2), GUILayout.ExpandWidth(true));
            // 批量重命名
            DrawBatchRenameSection();
            
            EditorGUILayout.EndVertical();
        }

        private void DrawTextureMaxSizeSection()
        {
            var style = EditorStyle.Get;
            
            
            EditorGUILayout.LabelField("★ 批量设置贴图", style.subheading);
            
            // 参数设置 - 带2的次方值吸附功能
            // EditorGUILayout.BeginVertical(style.area);
            EditorGUILayout.Space();
            // 目标MaxSize值 - 使用紧凑布局让控件向左靠拢
            EditorGUILayout.BeginHorizontal();
            // 使用贴图尺寸过滤
            _useTextureSizeFilter = EditorGUILayout.Toggle(_useTextureSizeFilter, GUILayout.Width(20));
            
            // 根据useTextureSizeFilter状态决定是否锁定尺寸选项控件
            var wasEnabled = GUI.enabled;
            if (!_useTextureSizeFilter)
            {
                GUI.enabled = false; // 禁用控件
                _useParameters = true;
            }
            
            // 贴图尺寸选择器 - 使用基类的专用方法
            // ("参数名字文本", 当前值, onSizeChanged事件, 标签宽度, 选择器宽度, 样式, 标签样式, 尺寸选项数组)
            _filterValue = base.CreateEnumPopupSizeSelector("设置尺寸大于:", _filterValue, null, 92, 100, null, style.normalfont, null);
            _maxSizeValue = base.CreateEnumPopupSizeSelector(" 改为:", _maxSizeValue, null, 41, 100, null, style.normalfont, null);
            
            GUILayout.FlexibleSpace(); // 添加弹性空间，让控件向左靠拢
            // 恢复GUI状态
            GUI.enabled = wasEnabled;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            // GUI.backgroundColor = Color.red;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            
            _useParameters = EditorGUILayout.Toggle(_useParameters, GUILayout.Width(16));
            var wasEnabled2 = GUI.enabled;
            if (!_useParameters)
            {
                _useTextureSizeFilter = true;
                GUI.enabled = false; // 禁用控件
            }
            EditorGUILayout.LabelField("参数:", style.normalfont, GUILayout.Width(35));
            // EditorGUILayout.Space();
            _editSrgb = CreateToggleWithStyle("sRGB", _editSrgb, null, null, null, null, 42, 20);
            _editAlphaIsTrans = CreateToggleWithStyle("AlphaIsTransparency", _editAlphaIsTrans, null, null, null, null, 140, 20);
            _editMipMap = CreateToggleWithStyle("GenerateMipmap", _editMipMap, null, null, null, null, 120, 20);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            // AlphaSource 参数选项 - 在 GenerateMipmap 下面添加
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(" ", style.normalfont, GUILayout.Width(55));
            _alphaSourceIndex = (int)_editAlphaSource;
            _alphaSourceIndex = base.CreateStringOptionsSelector("AlphaSource", _alphaSourceIndex, 
                (newIndex) => {
                    _editAlphaSource = (TextureImporterAlphaSource)newIndex;
                    _alphaSourceIndex = newIndex; // 同步更新字段变量
                }, 90, 140, null, style.normalfont, new string[] { "None", "Input Texture Alpha", "From Gray Scale" });
            _editAlphaSource = (TextureImporterAlphaSource)_alphaSourceIndex;
            // GUILayout.FlexibleSpace();
            // 恢复GUI状态
            GUI.enabled = wasEnabled2;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(3);
            EditorGUILayout.EndVertical();

            // EditorGUILayout.Space(3);

            // EditorGUILayout.LabelField("> 数值会按箭头指向的吸附2次方值设置 <", style.normalfont_Hui_Cen);

            // EditorGUILayout.EndVertical();

            // 路径输入区域 - 带历史记录功能
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            
            // 路径标签
            // EditorGUILayout.LabelField("路径:", style.normalfont, GUILayout.Width(35));

            //提取路径按钮 - 带Tooltip提示
            if (GUILayout.Button(new GUIContent("路径:", "获取Project窗口中选中对象的目录路径"), GUILayout.Width(45), GUILayout.Height(20)))
            {
                GetSelectedObjectPath();
            }
            // 路径文本框
            _filePath = base.CreateTextFieldWithStyle("路径：", _filePath,
            (newText) =>
            {
                // 文本变化时不立即保存到历史记录，等待回车或搜索按钮
                // 这里只更新文本，不保存历史
            }, 1, 0, TextAnchor.MiddleLeft, null, null, "ProjectTools_pathField");

            
            // 使用搜索历史管理器绘制历史记录选择器
            _searchHistoryManager.DrawSearchHistorySelector(ref _filePath, (selectedHistory) => {

                Debug.Log($"选择了历史记录: {selectedHistory}");

            });

            EditorGUILayout.EndHorizontal();
            

            // 使用紧凑布局让两个Toggle控件向左靠拢且没有间隔
            EditorGUILayout.BeginHorizontal();
            
            // 处理路径中的贴图资源 - 使用固定宽度且不扩展
            _workPath = base.CreateToggleWithStyle("处理路径", _workPath, null, null, null, null, 60, 20);

            // 使用带样式的Toggle控件 - 使用固定宽度且不扩展
            _includeSubfolders = base.CreateToggleWithStyle("包含子文件夹", _includeSubfolders, null, null, null, null, 90, 20);

            if (GUILayout.Button("跳转路径", GUILayout.Width(60)))
            {
                JumpToPath();
            }

            // 后缀选择器 - 使用基类的字符串选项选择器
            _suffixIndex = base.CreateStringOptionsSelector("  后缀：", _suffixIndex, 
                (newIndex) =>
                {
                    // 索引变化时的回调函数
                    var suffixOptions = new string[] { "", "_D", "_N", "_MRA", "_E" };
                    _renameSuffix = _subffix = suffixOptions[newIndex];
                    switch (newIndex)
                    {
                        case 1:
                            _editSrgb = true;
                            _editAlphaIsTrans = false;
                            _editAlphaSource = TextureImporterAlphaSource.None;
                            break;
                        case 2:
                        case 3:
                            _editSrgb = false;
                            _editAlphaIsTrans = false;
                            _editAlphaSource = TextureImporterAlphaSource.None;
                            break;
                        case 4:
                            _editSrgb = true;
                            _editAlphaIsTrans = false;
                            _editAlphaSource = TextureImporterAlphaSource.None;
                            break;
                    }

                    // Debug.Log($"后缀选择变化: {subffix}");
                }, 45, 60, null, style.normalfont, new string[] { "无后缀", "_D", "_N", "_MRA", "_E" }, "后缀过滤只在处理路径时有效！");
            _subffix = base.CreateTextFieldWithStyle("", _subffix, null, 1, 50, TextAnchor.MiddleLeft, null, null, "ProjectTools_subffixSel");
            
            // 确保subffix变量与当前选择同步
            // string[] suffixOptions = new string[] { "", "_N", "_D", "_S", "_M", "_E", "_R" };
            // subffix = suffixOptions[suffixIndex];
            
            
            // 添加弹性空间将控件推到左侧
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            
            // 操作按钮
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("批量设置贴图参数", style.normalButton))
            {
                SetSelectedTexturesParameter();
            }
            // 检查参数按钮
            GUI.backgroundColor = Color.yellow;
            if (GUILayout.Button("检查贴图参数", style.normalButton))
            {
                CheckSuffixParameters();
            }
            // GUI.backgroundColor = Color.red;
            // if (GUILayout.Button("※清除处理记录", style.normalButton_R, GUILayout.Width(120)))
            // {
            //     ClearProcessedTextures();
            // }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            
            // 显示检查结果
            if (_showCheckResult && _checkResults.Count > 0)
            {
                EditorGUILayout.LabelField($"贴图参数检查结果 ({_checkResults.Count} 个):", style.subheading2);
                
                // 统计正确和错误的贴图数量
                int correctCount = _checkResults.Count(r => r.isCorrect);
                int incorrectCount = _checkResults.Count(r => !r.isCorrect);
                GUI.contentColor = Color.green;
                EditorGUILayout.LabelField($"✓ 参数正确: {correctCount} 个", style.normalfont);
                EditorGUILayout.BeginHorizontal();
                GUI.contentColor = Color.red;
                EditorGUILayout.LabelField($"✗ 参数错误: {incorrectCount} 个", style.normalfont);
                GUI.contentColor = Color.white;
                // 添加关闭检查结果的按钮
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("关闭检查结果", GUILayout.Width(100)))
                {
                    _showCheckResult = false;
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
                // EditorGUILayout.Space();
                
                // 显示检查结果列表 - 使用滚动视图
                GUI.backgroundColor = Color.gray;
                // checkResultScrollPosition = EditorGUILayout.BeginScrollView(checkResultScrollPosition, GUILayout.Height(Mathf.Min(checkResults.Count * 40, 300))); // 限制最大高度为300，最小高度根据项目数量计算
                _checkResultScrollPosition = EditorGUILayout.BeginScrollView(_checkResultScrollPosition, GUILayout.ExpandHeight(true)); // 扩展高度
                EditorGUILayout.BeginVertical(EditorStyles.textArea);
                
                foreach (var result in _checkResults)
                {
                    EditorGUILayout.BeginHorizontal();    // 错误详情
                    
                    // 显示贴图名称和状态
                    if (result.isCorrect)
                    {
                        GUI.color = Color.green;
                        EditorGUILayout.LabelField("✓", GUILayout.Width(20));
                        GUI.color = Color.white;
                        EditorGUILayout.LabelField(result.textureName, style.normalfont, GUILayout.ExpandWidth(true));
                    }
                    else
                    {
                        GUI.color = Color.red;
                        EditorGUILayout.LabelField("✗", GUILayout.Width(20));
                        GUI.color = Color.white;
                        EditorGUILayout.LabelField(result.textureName, style.normalfont, GUILayout.ExpandWidth(true));
                    }
                    // 选择按钮
                    GUI.backgroundColor = Color.cyan;
                    if (GUILayout.Button("选择", GUILayout.Width(50)))
                    {   
                        // 检查是否按住Ctrl键
                        Event currentEvent = Event.current;
                        bool isCtrlPressed = currentEvent.control || currentEvent.command; // command键用于Mac

                        if (isCtrlPressed)
                        {
                            // Ctrl+点击：添加到当前选择
                            if (result.textureObject != null)
                            {
                                // 检查是否有 DontSaveInEditor 标志，记录警告但不跳过
                                bool hasDontSaveFlag = (result.textureObject.hideFlags & HideFlags.DontSaveInEditor) != 0;
                                if (hasDontSaveFlag)
                                {
                                    Debug.LogWarning($"贴图 '{result.textureObject.name}': 带有 DontSaveInEditor 标志，尝试选择但可能有限制");
                                }
                                
                                List<Object> currentSelection = new List<Object>(Selection.objects);
                                if (!currentSelection.Contains(result.textureObject))
                                {
                                    currentSelection.Add(result.textureObject);
                                    Selection.objects = currentSelection.ToArray();
                                }
                            }
                        }
                        else
                        {
                            // 选择贴图
                            if (result.textureObject != null)
                            {
                                // 检查是否有 DontSaveInEditor 标志，记录警告但不跳过
                                bool hasDontSaveFlag = (result.textureObject.hideFlags & HideFlags.DontSaveInEditor) != 0;
                                if (hasDontSaveFlag)
                                {
                                    Debug.LogWarning($"贴图 '{result.textureObject.name}': 带有 DontSaveInEditor 标志，尝试选择但可能有限制");
                                }
                            }
                            Selection.activeObject = null; // 先取消所有选择
                            Selection.activeObject = result.textureObject;
                        }
                    }
                    GUI.backgroundColor = Color.white;
                    
                    EditorGUILayout.EndHorizontal();    // 错误详情
                    
                    // 显示错误详情（如果有）
                    if (!result.isCorrect && result.errorDetails.Count > 0)
                    {
                        EditorGUILayout.BeginHorizontal();
                        // GUILayout.Space(25); // 缩进
                        EditorGUILayout.BeginVertical();
                        foreach (string error in result.errorDetails)
                        {
                            EditorGUILayout.LabelField($"  {error}", style.normalfont_Hui);
                        }
                        EditorGUILayout.EndVertical();
                        EditorGUILayout.EndHorizontal();
                    }
                    
                    // 添加分隔线
                    GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
                }
                
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndScrollView();
                
                // EditorGUILayout.Space();
            }
            
            // 显示处理结果
            if (_showTextureEditResult && _processedTextures.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"已处理 {_processedTextures.Count} 个贴图:", style.subheading2);
                // 添加关闭检查结果的按钮
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("关闭处理结果", GUILayout.Width(100)))
                {
                    _showTextureEditResult = false;
                    _processedTextures.Clear();
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
                
                // 使用滚动视图显示已处理贴图列表
                GUI.backgroundColor = Color.gray;
                _processedTexturesScrollPosition = EditorGUILayout.BeginScrollView(_processedTexturesScrollPosition, GUILayout.ExpandHeight(true));
                EditorGUILayout.BeginVertical(EditorStyles.textArea);
                GUI.backgroundColor = Color.cyan;
                // 使用自定义GUIStyle实现真正的右对齐，并设置最小宽度
                GUIStyle rightAlignedLabel = new GUIStyle(EditorStyles.label);
                rightAlignedLabel.alignment = TextAnchor.MiddleRight;
                rightAlignedLabel.clipping = TextClipping.Clip;
                foreach (var texturePath in _processedTextures)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(texturePath, rightAlignedLabel, GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("选择", GUILayout.Width(50)))
                    {   
                        // 检查是否按住Ctrl键
                        Event currentEvent = Event.current;
                        bool isCtrlPressed = currentEvent.control || currentEvent.command; // command键用于Mac

                        if (isCtrlPressed)
                        {
                            // Ctrl+点击：添加到当前选择
                            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                            if (texture != null)
                            {
                                // 检查是否有 DontSaveInEditor 标志，记录警告但不跳过
                                bool hasDontSaveFlag = (texture.hideFlags & HideFlags.DontSaveInEditor) != 0;
                                if (hasDontSaveFlag)
                                {
                                    Debug.LogWarning($"贴图 '{texture.name}': 带有 DontSaveInEditor 标志，尝试选择但可能有限制");
                                }
                                
                                List<Object> currentSelection = new List<Object>(Selection.objects);
                                if (!currentSelection.Contains(texture))
                                {
                                    currentSelection.Add(texture);
                                    Selection.objects = currentSelection.ToArray();
                                }
                            }
                        }
                        else
                        {
                            //选择贴图
                            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                            if (texture != null)
                            {
                                // 检查是否有 DontSaveInEditor 标志，记录警告但不跳过
                                bool hasDontSaveFlag = (texture.hideFlags & HideFlags.DontSaveInEditor) != 0;
                                if (hasDontSaveFlag)
                                {
                                    Debug.LogWarning($"贴图 '{texture.name}': 带有 DontSaveInEditor 标志，尝试选择但可能有限制");
                                }
                            }
                            Selection.activeObject = null; // 先取消所有选择
                            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndScrollView();
            }
        }

        private void SetSelectedTexturesParameter()
        {
            // 不再清除处理记录，而是添加新处理的贴图
            var textureCount = 0;
            var successCount = 0;
            var skippedCount = 0;
            var twomessage = true;
            // 用于收集控制台反馈信息
            var consoleMessages = new List<string>();

            // 检查模式：路径模式或选中模式
            if (_workPath && !string.IsNullOrEmpty(_filePath))
            {
                var godo = EditorUtility.DisplayDialog("提示", $"确定批量设置路径\n{_filePath}\n中的贴图文件？", "确定", "取消");
                if (!godo)
                {
                    return;
                }
                
                // 路径模式：根据后缀自动设置相应的参数，与"检查贴图参数"逻辑一致
                if (!string.IsNullOrEmpty(_subffix))
                {
                    // 根据后缀自动设置参数
                    switch (_subffix)
                    {
                        case "_D": // 漫反射贴图
                            _editSrgb = true;
                            _editAlphaIsTrans = false;
                            // _editMipMap = true;
                            _editAlphaSource = TextureImporterAlphaSource.None;
                            break;
                        case "_N": // 法线贴图
                        case "_MRA": // 金属度、粗糙度、环境光遮蔽贴图
                            _editSrgb = false;
                            _editAlphaIsTrans = false;
                            // _editMipMap = true;
                            _editAlphaSource = TextureImporterAlphaSource.None;
                            break;
                        case "_E": // 自发光贴图
                            _editSrgb = true;
                            _editAlphaIsTrans = false;
                            // _editMipMap = true;
                            _editAlphaSource = TextureImporterAlphaSource.None;
                            break;
                    }
                    Debug.Log($"路径模式下根据后缀 {_subffix} 自动设置参数: sRGB={_editSrgb}, AlphaIsTransparency={_editAlphaIsTrans}, GenerateMipmap={_editMipMap}, AlphaSource={_editAlphaSource}");
                }
                
                // 路径模式：不需要选中对象，处理路径中的贴图（后缀为"_N"的贴图）
                ProcessPathTextures(_filePath, ref textureCount, ref successCount, ref skippedCount);
            }
            else
            {
                // 选中模式：需要检查是否有选中对象
                var selectedObjects = Selection.objects;
                if (selectedObjects == null || selectedObjects.Length == 0)
                {
                    EditorUtility.DisplayDialog("提示", "请先在Project窗口中选择贴图文件", "确定");
                    return;
                }
                EditorGUILayout.BeginVertical(EditorStyles.textArea);
                // 处理选中的对象
                foreach (var obj in selectedObjects)
                {
                    // 检查是否有 DontSaveInEditor 标志，记录警告但不跳过
                    bool hasDontSaveFlag = obj != null && (obj.hideFlags & HideFlags.DontSaveInEditor) != 0;
                    if (hasDontSaveFlag)
                    {
                        Debug.LogWarning($"对象 '{obj?.name}': 带有 DontSaveInEditor 标志，尝试处理但可能有限制");
                    }
                    
                    if (obj is Texture2D)
                    {
                        textureCount++;
                        var assetPath = AssetDatabase.GetAssetPath(obj);

                        // 特殊处理：当后缀为"_N"时，检查并设置TextureType为NormalMap
                        var textureTypeSet = false;
                        var fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                        if (fileName.EndsWith("_N"))
                        {
                            textureTypeSet = SetTextureTypeToNormalMap(assetPath);
                            if (textureTypeSet)
                            {
                                consoleMessages.Add($"✓ {System.IO.Path.GetFileName(assetPath)}: TextureType已设置为NormalMap");
                            }
                        }

                        var result = SetTextureMaxSize(assetPath, _maxSizeValue);
                        if (result)
                        {
                            successCount++;

                            // 记录TextureType设置情况到统计中
                            Debug.Log(textureTypeSet
                                ? $"成功设置贴图: {assetPath} (已设置TextureType为NormalMap)"
                                : $"成功设置贴图: {assetPath}");
                            // 检查是否已存在，避免重复添加
                            if (!_processedTextures.Contains(assetPath))
                            {
                                _processedTextures.Add(assetPath);
                            }
                        }
                        else
                        {
                            skippedCount++;
                            consoleMessages.Add($"✗ {System.IO.Path.GetFileName(assetPath)}: 跳过设置");
                        }
                    }
                    else if (obj.GetType() == typeof(DefaultAsset)) // 文件夹
                    {
                        EditorUtility.DisplayDialog("提示", "请选择贴图文件！", "确定");
                        twomessage = false;
                        // string folderPath = AssetDatabase.GetAssetPath(obj);
                        // ProcessFolderTextures(folderPath, ref textureCount, ref successCount, ref skippedCount);
                    }
                }
                EditorGUILayout.EndVertical();
            }

            // 刷新数据库
            AssetDatabase.Refresh();
            
            // 如果当前显示检查结果，更新检查结果列表中的错误信息
            if (_showCheckResult && _checkResults.Count > 0)
            {
                UpdateCheckResultsAfterBatchSet();
            }
            
            // 显示结果
            if (textureCount > 0)
            {
                var modeInfo = _workPath && !string.IsNullOrEmpty(_filePath) ? "路径模式" : "选中模式";
                var sRGBStatus = _editSrgb ? "启用" : "禁用";
                var mipMapStatus = _editMipMap ? "启用" : "禁用";
                var alphaIsTransStatus = _editAlphaIsTrans ? "启用" : "禁用";
                var alphaSourceStatus = _editAlphaSource.ToString();
                
                // 根据useTextureSizeFilter状态决定是否显示MaxSize和过滤值信息
                var parameterInfo = _useTextureSizeFilter ? $"实际使用的参数:\nMaxSize: {_maxSizeValue}\n过滤值: {_filterValue}\nsRGB: {sRGBStatus}\nGenerateMipmap: {mipMapStatus}\nAlphaIsTransparency: {alphaIsTransStatus}\nAlphaSource: {alphaSourceStatus}" : $"实际使用的参数:\nsRGB: {sRGBStatus}\nGenerateMipmap: {mipMapStatus}\nAlphaIsTransparency: {alphaIsTransStatus}\nAlphaSource: {alphaSourceStatus}";

                // 构建完整的显示信息，包含控制台反馈
                var resultMessage = $"处理完成! ({modeInfo})\n找到贴图: {textureCount} 个\n成功设置: {successCount} 个\n跳过设置: {skippedCount} 个\n\n{parameterInfo}";
                
                // 如果有控制台反馈信息，添加到结果中
                if (consoleMessages.Count > 0)
                {
                    resultMessage += $"\n\n详细反馈:\n{string.Join("\n", consoleMessages)}";
                }
                _showTextureEditResult = true;
                EditorUtility.DisplayDialog("完成", resultMessage, "确定");
            }
            else
            {
                if (twomessage)
                {
                    EditorUtility.DisplayDialog("提示", "未找到贴图文件！", "确定");
                }
            }
        }

        // 设置贴图MaxSize 实现函数
        private bool SetTextureMaxSize(string assetPath, int maxSize)
        {
            try
            {
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    // 获取当前MaxSize
                    var currentMaxSize = importer.maxTextureSize;

                    switch (_useTextureSizeFilter)
                    {
                        // 检查过滤条件：当启用尺寸过滤且当前MaxSize小于等于过滤值时，完全跳过设置
                        case true when currentMaxSize <= _filterValue:
                            Debug.Log($"跳过贴图 {assetPath} - 当前MaxSize ({currentMaxSize}) 等于或小于过滤值 ({_filterValue})");
                            return false; // 完全跳过设置
                        // 当未启用尺寸过滤时，跳过MaxSize设置，但仍然设置其他参数
                        case false:
                            Debug.Log($"跳过贴图 {assetPath} 的MaxSize设置 - 尺寸过滤未启用，但会设置其他参数");
                            // 继续执行其他参数的设置
                            break;
                        default:
                            // 设置新的MaxSize
                            importer.maxTextureSize = maxSize;
                            break;
                    }

                    // 保存原始设置以便撤销
                    _ = importer.GetPlatformTextureSettings("Android").overridden;
                    var originalAlphaSource = importer.alphaSource;
                    var originalSrgb = importer.sRGBTexture;
                    var originalMipMap = importer.mipmapEnabled;
                    var originalAlphaIsTrans = importer.alphaIsTransparency;
                    if (_useParameters)
                    {

                        // 设置AlphaSource参数
                        if (_editAlphaSource != originalAlphaSource)
                        {
                            importer.alphaSource = _editAlphaSource;
                        }

                        // 设置sRGB参数
                        if (_editSrgb != originalSrgb)
                        {
                            importer.sRGBTexture = _editSrgb;
                        }

                        // 设置GenerateMipmap参数
                        if (_editMipMap != originalMipMap)
                        {
                            importer.mipmapEnabled = _editMipMap;
                        }

                        // 设置AlphaIsTransparency参数
                        if (_editAlphaIsTrans != originalAlphaIsTrans)
                        {
                            importer.alphaIsTransparency = _editAlphaIsTrans;
                        }

                    }
                    // 关闭Override For Android设置
                    TextureImporterPlatformSettings androidSettings = importer.GetPlatformTextureSettings("Android");
                    androidSettings.overridden = false;
                    importer.SetPlatformTextureSettings(androidSettings);

                    // 应用更改
                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();

                    // 根据是否设置了MaxSize来记录不同的日志
                    if (_useTextureSizeFilter)
                    {
                        if (_useParameters){
                            Debug.Log($"已设置贴图 {assetPath} 的MaxSize为: {maxSize} (原值: {currentMaxSize}), " +
                                 $"AlphaSource为: {_editAlphaSource} (原值: {originalAlphaSource}), " +
                                 $"sRGB为: {_editSrgb} (原值: {originalSrgb}), " +
                                 $"GenerateMipmap为: {_editMipMap} (原值: {originalMipMap}), " +
                                 $"AlphaIsTransparency为: {_editAlphaIsTrans} (原值: {originalAlphaIsTrans}), " +
                                 "并关闭了Override For Android");
                        }else{
                            Debug.Log($"已设置贴图 {assetPath} 的MaxSize为: {maxSize} (原值: {currentMaxSize}), " +
                                 "并关闭了Override For Android");
                        }
                    }else{
                        Debug.Log($"已设置贴图 {assetPath} 的参数: " +
                                 $"AlphaSource为: {_editAlphaSource} (原值: {originalAlphaSource}), " +
                                 $"sRGB为: {_editSrgb} (原值: {originalSrgb}), " +
                                 $"GenerateMipmap为: {_editMipMap} (原值: {originalMipMap}), " +
                                 $"AlphaIsTransparency为: {_editAlphaIsTrans} (原值: {originalAlphaIsTrans}), " +
                                 "并关闭了Override For Android");
                    }
                    return true;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"设置贴图 {assetPath} 的MaxSize失败: {e.Message}");
            }

            return false;
        }

        
        /// 清除处理记录 - 带确认提示
        // private void ClearProcessedTextures()
        // {
        //     if (processedTextures.Count == 0)
        //     {
        //         // EditorUtility.DisplayDialog("提示", "当前没有处理记录需要清除", "确定");
        //         return;
        //     }

        //     bool confirmClear = EditorUtility.DisplayDialog(
        //         "确认清除处理记录",
        //         $"确定要清除所有处理记录吗？\n\n这将删除 {processedTextures.Count} 个贴图的处理记录。",
        //         "确定清除",
        //         "取消"
        //     );

        //     if (confirmClear)
        //     {
        //         processedTextures.Clear();
        //         Debug.Log($"已清除 {processedTextures.Count} 个贴图的处理记录");
        //     }
        // }

        
        /// 处理路径中后缀为"_N"的贴图
        private void ProcessPathTextures(string path, ref int textureCount, ref int successCount, ref int skippedCount)
        {
            // 使用HashSet来跟踪已处理的贴图路径，避免重复计数
            var processedPaths = new HashSet<string>();
            ProcessPathTexturesRecursive(path, ref textureCount, ref successCount, ref skippedCount, processedPaths);
        }

        
        /// 递归处理路径中指定后缀的贴图（内部方法）
        private void ProcessPathTexturesRecursive(string path, ref int textureCount, ref int successCount, ref int skippedCount, HashSet<string> processedPaths)
        {
            // 获取路径中的所有贴图文件
            var texturePaths = AssetDatabase.FindAssets("t:Texture2D", new[] { path });
            EditorGUILayout.BeginVertical(EditorStyles.textArea);
            foreach (var guid in texturePaths)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                
                // 检查贴图是否在当前路径下（不包含子文件夹）
                if (!_includeSubfolders)
                {
                    // 获取贴图所在的目录
                    var textureDirectory = System.IO.Path.GetDirectoryName(assetPath);
                    // 如果贴图目录不等于当前路径，说明在子文件夹中，跳过
                    if (textureDirectory != path)
                    {
                        continue; // 跳过子文件夹中的贴图
                    }
                }
                
                // 检查贴图文件名是否包含指定后缀
                var fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                if (fileName.EndsWith(_subffix))
                {
                    // 检查是否已经处理过这个贴图（避免重复计数）
                    if (processedPaths.Contains(assetPath))
                    {
                        // Debug.Log($"跳过已处理的贴图: {assetPath}");
                        continue;
                    }
                    
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    if (!texture) continue;
                    // Debug.Log($"找到符合条件的贴图: {assetPath}");
                        
                    // 特殊处理：当后缀为"_N"时，检查并设置TextureType为NormalMap
                    if (_subffix == "_N")
                    {
                        SetTextureTypeToNormalMap(assetPath);
                    }
                        
                    var result = SetTextureMaxSize(assetPath, _maxSizeValue);
                    textureCount++;
                    if (result)
                    {
                        successCount++;
                        processedPaths.Add(assetPath); // 标记为已处理
                            
                        // 记录TextureType设置情况到统计中
                        
                        // 检查是否已存在，避免重复添加
                        if (!_processedTextures.Contains(assetPath))
                        {
                            _processedTextures.Add(assetPath);
                        }
                    }
                    else
                    {
                        // 当SetTextureMaxSize返回false时，表示贴图被完全跳过
                        skippedCount++;
                        processedPaths.Add(assetPath); // 标记为已处理
                        // Debug.Log($"跳过贴图: {assetPath}");
                    }
                }
                else
                {
                    // 不符合指定后缀条件的贴图不计入总数
                    Debug.Log($"跳过不符合条件的贴图: {assetPath} (文件名: {fileName})");
                }
            }
            EditorGUILayout.EndVertical();
            // 只有当包含子文件夹选项勾选时，才递归处理子文件夹
            if (!_includeSubfolders) return;
            var subFolders = AssetDatabase.GetSubFolders(path);
            foreach (var subFolder in subFolders)
            {
                ProcessPathTexturesRecursive(subFolder, ref textureCount, ref successCount, ref skippedCount, processedPaths);
            }
        }

        
        /// 获取Project窗口中选中的第一个对象的路径
        private void GetSelectedObjectPath()
        {
            // 获取Project窗口中选中的所有对象
            var selectedObjects = Selection.objects;
            
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "请在Project窗口中选择至少一个对象", "确定");
                return;
            }

            // 获取第一个选中的对象
            var firstSelectedObject = selectedObjects[0];
            
            if (firstSelectedObject == null)
            {
                EditorUtility.DisplayDialog("错误", "选中的对象为空", "确定");
                return;
            }

            // 检查是否有 DontSaveInEditor 标志，记录警告但不跳过
            bool hasDontSaveFlag = (firstSelectedObject.hideFlags & HideFlags.DontSaveInEditor) != 0;
            if (hasDontSaveFlag)
            {
                Debug.LogWarning($"对象 '{firstSelectedObject.name}': 带有 DontSaveInEditor 标志，尝试获取路径但可能有限制");
            }

            // 获取对象的完整路径
            var assetPath = AssetDatabase.GetAssetPath(firstSelectedObject);
            
            if (string.IsNullOrEmpty(assetPath))
            {
                EditorUtility.DisplayDialog("错误", "无法获取选中对象的路径", "确定");
                return;
            }

            // 提取目录路径（不包含对象名）
            var directoryPath = System.IO.Path.GetDirectoryName(assetPath);
            
            if (string.IsNullOrEmpty(directoryPath))
            {
                EditorUtility.DisplayDialog("错误", "无法提取目录路径", "确定");
                return;
            }

            // 将目录路径设置到文本框中
            _filePath = directoryPath;
            
            // 显示成功消息
            // EditorUtility.DisplayDialog("成功", $"已获取选中对象所在目录:\n{directoryPath}", "确定");
            
            // 强制重绘窗口以更新文本框显示
            Parent?.Repaint();
        }

        
        /// 跳转到指定路径目录 - 使用正确的Unity API
        private void JumpToPath()
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                EditorUtility.DisplayDialog("提示", "请输入要跳转的路径", "确定");
                return;
            }

            // 检查路径是否存在
            if (!AssetDatabase.IsValidFolder(_filePath) && !System.IO.File.Exists(_filePath))
            {
                EditorUtility.DisplayDialog("错误", $"路径不存在: {_filePath}", "确定");
                return;
            }

            // 确定要选择的文件夹路径
            var folderPath = _filePath;
            
            // 如果路径是文件，则获取其所在目录
            if (System.IO.File.Exists(_filePath))
            {
                folderPath = System.IO.Path.GetDirectoryName(_filePath);
            }

            // 加载文件夹对象
            var folderObject = AssetDatabase.LoadAssetAtPath<Object>(folderPath);
            
            if (!folderObject)
            {
                EditorUtility.DisplayDialog("错误", $"无法加载路径对象: {folderPath}", "确定");
                return;
            }

            try
            {
                // 第一步：选中对象并聚焦Project窗口
                Selection.activeObject = null;
                Selection.activeObject = folderObject;
                EditorUtility.FocusProjectWindow();

                // 第二步：使用PingObject定位到目标目录
                // 这个方法会展开所有父目录并高亮显示目标目录
                EditorGUIUtility.PingObject(folderObject);

                Debug.Log($"已跳转到路径: {folderPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"跳转路径失败: {e.Message}");
                EditorUtility.DisplayDialog("错误", $"跳转路径失败: {e.Message}", "确定");
            }
        }

        
        // ReSharper disable Unity.PerformanceAnalysis
        // ReSharper disable Unity.PerformanceAnalysis
        // ReSharper disable Unity.PerformanceAnalysis
        /// 绘制批量重命名界面
        private void DrawBatchRenameSection()
        {
            var style = EditorStyle.Get;
            
            // 在批量重命名上面添加间隔
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("★ 批量重命名(安全)", style.subheading);
            
            EditorGUILayout.BeginVertical(style.area);
            
            // 重命名参数设置
            EditorGUILayout.BeginHorizontal();
            
            // 使用带工具提示的标签和文本框
            EditorGUILayout.LabelField(new GUIContent("命名:", "命名留空时使用原始名称"), style.normalfont, GUILayout.Width(40));
            // 修复文本框无法输入的问题 - 使用基类的文本字段方法
            _renamePrefix = base.CreateTextFieldWithStyle("", _renamePrefix, 
            (newText) => {
                _renamePrefix = newText; // 立即更新变量值
                GenerateRenamePreview();
            }, 1, 0, TextAnchor.MiddleLeft, null, null, null);
            
            _renameSuffix = base.CreateTextFieldWithStyle("后缀:", _renameSuffix, 
            (newText) => {
                _renameSuffix = newText; // 立即更新变量值
                GenerateRenamePreview();
            }, 40, 0, TextAnchor.MiddleLeft, style.normalfont, null, null);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            
            // 起始编号输入框 - 使用更简单的布局
            EditorGUILayout.LabelField("起始:", style.normalfont, GUILayout.Width(35));
            // GUI.SetNextControlName("ProjectTools_startNumber");
            // startNumber = EditorGUILayout.IntField(startNumber, GUILayout.Width(80));
            _startNumber = base.CreateIntSliderWithEvent("", _startNumber, 0, 100, 
                (newValue) => {
                    _startNumber = newValue; // 立即更新变量值
                    GenerateRenamePreview();
                }, 90, 400, 1);

            EditorGUILayout.Space();
            // 编号位数输入框 - 使用更简单的布局
            EditorGUILayout.LabelField(new GUIContent("位数:", "编号位数为 0 时不使用编号"), style.normalfont, GUILayout.Width(35));
            // GUI.SetNextControlName("ProjectTools_numberDigits");
            // numberDigits = EditorGUILayout.IntField(numberDigits, GUILayout.Width(80));
            _numberDigits = base.CreateIntSliderWithEvent("", _numberDigits, 0, 10, 
                (newValue) => {
                    _numberDigits = newValue; // 立即更新变量值
                    GenerateRenamePreview();
                }, 90, 400, 1);
            
            EditorGUILayout.Space();
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space();
            
            // 操作按钮
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("(1) 预览重命名", style.normalButton))
            {
                GenerateRenamePreview();
            }
            GUI.backgroundColor = Color.blue;
            if (GUILayout.Button("(2) 执行重命名", style.normalButton))
            {
                ExecuteBatchRename();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
            
            // 显示重命名预览
            if (!_showRenamePreview || _renamePreviewItems.Count <= 0) return;
            // 统计不同类型对象的数量
            var assetCount = _renamePreviewItems.Count(item => item.assetPath != "SceneObject");
            var sceneObjectCount = _renamePreviewItems.Count(item => item.assetPath == "SceneObject");
            var conflictCount = _renamePreviewItems.Count(item => item.willConflict);
                
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"重命名预览 ({_renamePreviewItems.Count} 个对象):", style.subheading2,
                GUILayout.MinWidth(160),
                GUILayout.ExpandWidth(true));

            switch (assetCount)
            {
                // 显示统计信息
                case > 0 when sceneObjectCount > 0:
                    EditorGUILayout.LabelField($"[资源: {assetCount} | 场景: {sceneObjectCount}]", style.normalfont,
                        GUILayout.MinWidth(90),
                        GUILayout.ExpandWidth(true));
                    break;
                case > 0:
                    EditorGUILayout.LabelField($"[资源对象: {assetCount}]", style.normalfont,
                        GUILayout.MinWidth(90),
                        GUILayout.ExpandWidth(true));
                    break;
                default:
                {
                    if (sceneObjectCount > 0)
                    {
                        EditorGUILayout.LabelField($"[场景物件: {sceneObjectCount}]", style.normalfont,
                            GUILayout.MinWidth(90),
                            GUILayout.ExpandWidth(true));
                    }

                    break;
                }
            }
                
            // 如果有冲突，显示警告
            if (conflictCount > 0)
            {
                GUI.color = Color.red;
                EditorGUILayout.LabelField($"[冲突: {conflictCount}]", style.normalfont,
                    GUILayout.MinWidth(70),
                    GUILayout.ExpandWidth(true));
                GUI.color = Color.white;
            }
                
            EditorGUILayout.EndHorizontal();
                
            EditorGUILayout.BeginVertical(EditorStyles.textArea);
                
            // 创建右对齐的GUIStyle
            // GUIStyle rightAlignedStyle = new GUIStyle(style.normalfont);
            // rightAlignedStyle.alignment = TextAnchor.MiddleRight;
            // rightAlignedStyle.clipping = TextClipping.Clip;
            // 在 foreach 循环之前创建一次（避免每帧重复 new）
            var rightAlignedStyle = new GUIStyle(style.normalfont)
            {
                alignment = TextAnchor.MiddleRight,
                wordWrap = false, // 关键：禁止换行
                clipping = TextClipping.Clip // 超出部分裁剪
            };
            foreach (var item in _renamePreviewItems)
            {
                EditorGUILayout.BeginHorizontal();

                // 1. 类型标签 - 固定宽度 + 颜色区分
                var typeLabel = item.assetPath == "SceneObject" ? "[场景]" : "[资源]";
                var originalColor = GUI.color;
                GUI.color = item.assetPath == "SceneObject" ? Color.cyan : Color.yellow;
                EditorGUILayout.LabelField(typeLabel, style.normalfont, GUILayout.Width(40));
                GUI.color = originalColor;

                // 2. 原始名称 - 弹性宽度，右对齐，可压缩但不低于最小可读宽度
                EditorGUILayout.LabelField(
                    item.originalName,
                    rightAlignedStyle,
                    GUILayout.MinWidth(60),
                    GUILayout.ExpandWidth(true)
                );

                // 3. 箭头 - 固定宽度
                EditorGUILayout.LabelField("→", style.normalfont, GUILayout.Width(20));

                // 4. 新名称 - 弹性宽度，右对齐，冲突时标红
                originalColor = GUI.color;
                if (item.willConflict)
                {
                    GUI.color = Color.red;
                }
                EditorGUILayout.LabelField(
                    item.newName,
                    rightAlignedStyle,
                    GUILayout.MinWidth(60),
                    GUILayout.ExpandWidth(true)
                );
                GUI.color = originalColor;

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        
        /// 生成重命名预览
        private void GenerateRenamePreview()
        {            
            _renamePreviewItems.Clear();
            _showRenamePreview = true;
            
            // 获取选中的对象
            Object[] selectedObjects = Selection.objects;
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "请选择要重命名的对象（Assets或场景物件）", "确定");
                _showRenamePreview = false;
                return;
            }
            
            // 调试日志：显示选中的对象信息
            Debug.Log($"GenerateRenamePreview: 选中了 {selectedObjects.Length} 个对象");
            
            // 检查是否有重复的新名称
            HashSet<string> newNames = new HashSet<string>();
            
            for (int i = 0; i < selectedObjects.Length; i++)
            {
                Object obj = selectedObjects[i];
                
                if (obj == null)
                {
                    Debug.LogWarning($"对象 {i}: 为空，跳过");
                    continue;
                }
                
                // 检查是否有 DontSaveInEditor 标志，记录警告但不跳过
                bool hasDontSaveFlag = (obj.hideFlags & HideFlags.DontSaveInEditor) != 0;
                if (hasDontSaveFlag)
                {
                    Debug.LogWarning($"对象 {i} '{obj.name}': 带有 DontSaveInEditor 标志，尝试处理但可能有限制");
                }
                
                // Debug.Log($"对象 {i}: {obj.name} (类型: {obj.GetType()}, 是否为GameObject: {obj is GameObject}, DontSaveInEditor: {hasDontSaveFlag})");
                
                // 如果是GameObject，显示更多详细信息
                if (obj is GameObject)
                {
                    GameObject gameObject = obj as GameObject;
                    Debug.Log($"  GameObject详细信息: 场景有效={gameObject.scene.IsValid()}, 场景名称={gameObject.scene.name}, 预制体实例={PrefabUtility.IsPartOfPrefabInstance(gameObject)}");
                }
                
                // 检测对象类型：Assets对象还是场景物件
                bool isAsset = IsAssetObject(obj);
                string originalName = "";
                string assetPath = "";
                
                // 调试日志：显示对象类型检测结果
                // Debug.Log($"对象 {i} '{obj.name}': IsAssetObject = {isAsset}");
                
                if (isAsset)
                {
                    // Assets对象
                    assetPath = AssetDatabase.GetAssetPath(obj);
                    if (string.IsNullOrEmpty(assetPath))
                    {
                        Debug.LogWarning($"对象 {i} '{obj.name}': 无法获取Assets路径，跳过");
                        continue;
                    }
                    
                    originalName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                    // Debug.Log($"Assets对象: {originalName} (路径: {assetPath})");
                }
                else
                {
                    // 场景物件
                    GameObject gameObject = obj as GameObject;
                    if (gameObject == null)
                    {
                        Debug.LogWarning($"对象 {i} '{obj.name}': 不是GameObject，跳过");
                        continue;
                    }
                    
                    originalName = gameObject.name;
                    assetPath = "SceneObject"; // 标记为场景物件
                    // Debug.Log($"场景物件: {originalName} (场景: {gameObject.scene.name})");
                }
                
                // 生成新名称
                string newName = GenerateNewName(originalName, i);
                
                // 检查是否有冲突（仅对Assets对象检查文件冲突）
                bool willConflict = false;
                if (isAsset)
                {
                    string extension = System.IO.Path.GetExtension(assetPath);
                    willConflict = newNames.Contains(newName + extension);
                    newNames.Add(newName + extension);
                }
                else
                {
                    // 场景物件检查同层级下的名称冲突
                    willConflict = CheckSceneObjectNameConflict(obj as GameObject, newName);
                }
                
                _renamePreviewItems.Add(new RenamePreviewItem(originalName, newName, assetPath, willConflict));
            }
            
            // 调试日志：显示最终预览结果
            Debug.Log($"GenerateRenamePreview: 生成了 {_renamePreviewItems.Count} 个预览项");
            
            // 如果有冲突，显示警告
            if (_renamePreviewItems.Exists(item => item.willConflict))
            {
                Debug.LogWarning("检测到重命名冲突！请检查预览中的红色项目!");
            }
            
            // 强制重绘窗口以确保预览立即显示
            Parent?.Repaint();
        }
        /// 检查对象是否为Assets对象
        private bool IsAssetObject(Object obj)
        {
            if (obj == null)
                return false;
                
            // 场景中的GameObject不是Assets对象
            if (obj is GameObject)
            {
                GameObject gameObject = obj as GameObject;
                // 如果GameObject在场景中（无论是否为预制体实例），都是场景物件
                if (gameObject.scene.IsValid())
                    return false;
            }
            
            // 其他情况认为是Assets对象
            return true;
        }
        
        /// 检查场景物件名称冲突
        private bool CheckSceneObjectNameConflict(GameObject gameObject, string newName)
        {
            if (gameObject == null)
                return false;
                
            // 如果有父级，检查同层级下的名称冲突
            if (gameObject.transform.parent)
            {
                var parent = gameObject.transform.parent;
                for (var i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    if (child != gameObject.transform && child.name == newName)
                    {
                        return true;
                    }
                }
            }
            else
            {
                // 如果是根级对象，检查场景中所有根级对象的名称冲突
                var scene = gameObject.scene;
                if (!scene.IsValid()) return false;
                var rootObjects = scene.GetRootGameObjects();
                return rootObjects.Any(rootObject => rootObject != gameObject && rootObject.name == newName);
            }
            return false;
        }

        
        /// 生成新名称
        private string GenerateNewName(string originalName, int index)
        {
            // 如果编号位数为0，不使用编号
            if (_numberDigits == 0)
            {
                // 如果前缀为空，使用原始名称加后缀
                if (string.IsNullOrEmpty(_renamePrefix))
                {
                    return $"{originalName}{_renameSuffix}";
                }
                // 否则使用前缀加后缀
                return $"{_renamePrefix}{_renameSuffix}";
            }
            
            // 生成数字部分
            string numberPart = (_startNumber + index).ToString($"D{_numberDigits}");
            
            // 如果前缀为空，使用原始名称
            if (string.IsNullOrEmpty(_renamePrefix))
            {
                return $"{originalName}_{numberPart}{_renameSuffix}";
            }
            
            // 组合新名称
            return $"{_renamePrefix}_{numberPart}{_renameSuffix}";
        }

        
        /// 执行批量重命名 - 支持Assets对象和场景物件
        private void ExecuteBatchRename()
        {
            if (_renamePreviewItems.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先执行第(1)步：生成重命名预览", "确定");
                return;
            }
            
            // 统计不同类型的对象数量
            int assetCount = _renamePreviewItems.Count(item => item.assetPath != "SceneObject");
            int sceneObjectCount = _renamePreviewItems.Count(item => item.assetPath == "SceneObject");
            
            // 添加重命名执行确认
            string confirmMessage = $"确定要执行批量重命名吗？\n\n";
            if (assetCount > 0 && sceneObjectCount > 0)
            {
                confirmMessage += $"这将重命名 {assetCount} 个Assets对象和 {sceneObjectCount} 个场景物件。";
            }
            else if (assetCount > 0)
            {
                confirmMessage += $"这将重命名 {assetCount} 个Assets对象。";
            }
            else if (sceneObjectCount > 0)
            {
                confirmMessage += $"这将重命名 {sceneObjectCount} 个场景物件。";
            }
            
            bool confirmRename = EditorUtility.DisplayDialog(
                "确认执行重命名",
                confirmMessage,
                "确定执行",
                "取消"
            );
            
            if (!confirmRename)
            {
                return;
            }
            
            // 检查是否有冲突
            if (_renamePreviewItems.Exists(item => item.willConflict))
            {
                var continueAnyway = EditorUtility.DisplayDialog("警告", 
                    "检测到重命名冲突！继续执行可能会导致文件覆盖或场景物件名称重复。是否继续？", 
                    "继续执行", "取消");
                
                if (!continueAnyway)
                    return;
            }
            
            var successCount = 0;
            var errorCount = 0;
            var sceneObjectSuccessCount = 0;
            var sceneObjectErrorCount = 0;
            
            // 开始资产操作，支持撤销（仅对Assets对象）
            if (assetCount > 0)
            {
                AssetDatabase.StartAssetEditing();
            }
            
            try
            {
                // 获取当前选中的对象
                var selectedObjects = Selection.objects;
                if (selectedObjects == null || selectedObjects.Length == 0)
                {
                    EditorUtility.DisplayDialog("错误", "没有选中的对象", "确定");
                    return;
                }
                
                // 执行重命名
                for (var i = 0; i < _renamePreviewItems.Count; i++)
                {
                    var item = _renamePreviewItems[i];
                    var newName = GenerateNewName(item.originalName, i);
                    
                    try
                    {
                        // 检查对象是否有 DontSaveInEditor 标志，记录警告但不跳过
                        if (i < selectedObjects.Length)
                        {
                            var obj = selectedObjects[i];
                            if (obj != null)
                            {
                                bool hasDontSaveFlag = (obj.hideFlags & HideFlags.DontSaveInEditor) != 0;
                                if (hasDontSaveFlag)
                                {
                                    Debug.LogWarning($"对象 {i} '{obj.name}': 带有 DontSaveInEditor 标志，尝试重命名但可能有限制");
                                }
                            }
                        }
                        
                        if (item.assetPath != "SceneObject")
                        {
                            // Assets对象重命名
                            var error = AssetDatabase.RenameAsset(item.assetPath, newName);
                            
                            if (string.IsNullOrEmpty(error))
                            {
                                successCount++;
                                Debug.Log($"成功重命名Assets对象: {item.originalName} → {newName}");
                                
                                // 对于材质等可能被引用的资源，额外确保引用更新
                                var asset = AssetDatabase.LoadAssetAtPath<Object>(item.assetPath);
                                if (asset is Material or Texture2D)
                                {
                                    UpdateAssetReferences(item.assetPath, newName);
                                }
                            }
                            else
                            {
                                errorCount++;
                                Debug.LogError($"重命名Assets对象失败: {item.originalName} → {error}");
                            }
                        }
                        else
                        {
                            // 场景物件重命名
                            if (i < selectedObjects.Length)
                            {
                                var obj = selectedObjects[i];
                                if (obj is GameObject gameObject)
                                {
                                    var originalName = gameObject.name;
                                    
                                    // 使用Undo记录操作，支持撤销
                                    Undo.RecordObject(gameObject, "Rename Scene Object");
                                    gameObject.name = newName;
                                    
                                    // 标记场景为已修改，需要保存
                                    if (gameObject.scene.IsValid())
                                    {
                                        EditorSceneManager.MarkSceneDirty(gameObject.scene);
                                    }
                                    
                                    sceneObjectSuccessCount++;
                                    Debug.Log($"成功重命名场景物件: {originalName} → {newName}");
                                }
                                else
                                {
                                    sceneObjectErrorCount++;
                                    Debug.LogError($"重命名场景物件失败: 选中的对象不是GameObject");
                                }
                            }
                            else
                            {
                                sceneObjectErrorCount++;
                                Debug.LogError($"重命名场景物件失败: 索引超出范围 (i={i}, selectedObjects.Length={selectedObjects.Length})");
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                        if (item.assetPath != "SceneObject")
                        {
                            errorCount++;
                            Debug.LogError($"重命名Assets对象异常: {item.originalName} → {e.Message}");
                        }
                        else
                        {
                            sceneObjectErrorCount++;
                            Debug.LogError($"重命名场景物件异常: {item.originalName} → {e.Message}");
                        }
                    }
                }
            }
            finally
            {
                if (assetCount > 0)
                {
                    AssetDatabase.StopAssetEditing();
                    AssetDatabase.Refresh();
                }
            }
            
            // 显示结果
            string resultMessage = "重命名完成!\n";
            
            if (assetCount > 0)
            {
                resultMessage += $"Assets对象: 成功 {successCount} 个, 失败 {errorCount} 个\n";
            }
            
            if (sceneObjectCount > 0)
            {
                resultMessage += $"场景物件: 成功 {sceneObjectSuccessCount} 个, 失败 {sceneObjectErrorCount} 个";
            }
            
            if (errorCount > 0 || sceneObjectErrorCount > 0)
            {
                resultMessage += "\n\n请查看控制台了解详细错误信息。";
            }
            
            EditorUtility.DisplayDialog("完成", resultMessage, "确定");
            
            // 清空预览
            _renamePreviewItems.Clear();
            _showRenamePreview = false;
        }
        
        /// 重命名操作信息
        private struct RenameOperationInfo
        {
            public string originalName;
            public string newName;
            public string assetPath;
            public bool isAsset;
            public int index;
        }
        
        
        /// 资产重命名信息
        private struct AssetRenameInfo
        {
            public string originalPath;
            public string newName;
            public Object asset;
        }
        
        
        /// 更新资产引用，确保场景中的引用不会丢失
        private void UpdateAssetReferences(string originalPath, string newName)
        {
            try
            {
                // 获取原始资产和新资产的GUID
                string originalGuid = AssetDatabase.AssetPathToGUID(originalPath);
                string directory = System.IO.Path.GetDirectoryName(originalPath);
                string extension = System.IO.Path.GetExtension(originalPath);
                string newPath = System.IO.Path.Combine(directory, newName + extension);
                string newGuid = AssetDatabase.AssetPathToGUID(newPath);
                
                if (string.IsNullOrEmpty(newGuid))
                {
                    Debug.LogWarning($"无法获取新资产的GUID: {newPath}");
                    return;
                }
                
                // 查找所有场景文件并更新引用
                string[] scenePaths = AssetDatabase.FindAssets("t:Scene");
                foreach (string sceneGuid in scenePaths)
                {
                    string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuid);
                    UpdateSceneReferences(scenePath, originalGuid, newGuid);
                }
                
                // 查找所有预制体文件并更新引用
                string[] prefabPaths = AssetDatabase.FindAssets("t:Prefab");
                foreach (string prefabGuid in prefabPaths)
                {
                    string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
                    UpdatePrefabReferences(prefabPath, originalGuid, newGuid);
                }
                
                // 查找所有材质文件并更新引用
                string[] materialPaths = AssetDatabase.FindAssets("t:Material");
                foreach (string materialGuid in materialPaths)
                {
                    string materialPath = AssetDatabase.GUIDToAssetPath(materialGuid);
                    UpdateMaterialReferences(materialPath, originalGuid, newGuid);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"更新资产引用失败: {e.Message}");
            }
        }
        
        
        /// 更新场景中的引用
        private static void UpdateSceneReferences(string scenePath, string originalGuid, string newGuid)
        {
            try
            {
                // 加载场景
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                var sceneModified = false;
                
                // 获取场景中的所有游戏对象
                var rootObjects = scene.GetRootGameObjects();
                
                foreach (var rootObject in rootObjects)
                {
                    // 递归遍历场景中的所有组件
                    var components = rootObject.GetComponentsInChildren<Component>(true);
                    foreach (var component in components)
                    {
                        if (component == null) continue;
                        
                        var serializedComponent = new SerializedObject(component);
                        var iterator = serializedComponent.GetIterator();
                        
                        while (iterator.NextVisible(true))
                        {
                            // 检查属性是否为对象引用类型
                            if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                            var referencedObject = iterator.objectReferenceValue;
                            if (!referencedObject) continue;
                            var referencedGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(referencedObject));
                            if (referencedGuid != originalGuid) continue;
                            // 找到匹配的引用，更新为新的资产
                            var newAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                                AssetDatabase.GUIDToAssetPath(newGuid));
                            if (!newAsset) continue;
                            iterator.objectReferenceValue = newAsset;
                            serializedComponent.ApplyModifiedProperties();
                            sceneModified = true;
                            Debug.Log($"更新场景 {scenePath} 中组件 {component.GetType().Name} 的引用: {originalGuid} → {newGuid}");
                        }
                    }
                }
                // 如果场景被修改，保存场景
                if (sceneModified)
                {
                    EditorSceneManager.SaveScene(scene);
                    Debug.Log($"已保存场景 {scenePath} 的引用更新");
                }
                // 关闭场景
                EditorSceneManager.CloseScene(scene, true);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"更新场景引用失败 {scenePath}: {e.Message}");
            }
        }
        
        
        /// 更新预制体中的引用
        private static void UpdatePrefabReferences(string prefabPath, string originalGuid, string newGuid)
        {
            try
            {
                // 加载预制体
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (!prefab) return;
                
                var prefabModified = false;
                
                // 获取预制体中的所有组件
                var components = prefab.GetComponentsInChildren<Component>(true);
                foreach (var component in components)
                {
                    if (!component) continue;
                    
                    var serializedComponent = new SerializedObject(component);
                    var iterator = serializedComponent.GetIterator();
                    
                    while (iterator.NextVisible(true))
                    {
                        // 检查属性是否为对象引用类型
                        if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var referencedObject = iterator.objectReferenceValue;
                        if (!referencedObject) continue;
                        var referencedGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(referencedObject));
                        if (referencedGuid != originalGuid) continue;
                        // 找到匹配的引用，更新为新的资产
                        var newAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                            AssetDatabase.GUIDToAssetPath(newGuid));
                        if (!newAsset) continue;
                        iterator.objectReferenceValue = newAsset;
                        serializedComponent.ApplyModifiedProperties();
                        prefabModified = true;
                        Debug.Log($"更新预制体 {prefabPath} 中组件 {component.GetType().Name} 的引用: {originalGuid} → {newGuid}");
                    }
                }
                
                // 如果预制体被修改，保存预制体
                if (!prefabModified) return;
                EditorUtility.SetDirty(prefab);
                AssetDatabase.SaveAssets();
                Debug.Log($"已保存预制体 {prefabPath} 的引用更新");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"更新预制体引用失败 {prefabPath}: {e.Message}");
            }
        }
        
        
        /// 更新材质中的引用（主要用于纹理引用）
        private static void UpdateMaterialReferences(string materialPath, string originalGuid, string newGuid)
        {
            try
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (!material) return;
                var serializedMaterial = new SerializedObject(material);
                var textureProperties = serializedMaterial.FindProperty("m_SavedProperties.m_TexEnvs");

                if (textureProperties is not { isArray: true }) return;
                for (var i = 0; i < textureProperties.arraySize; i++)
                {
                    var textureProperty = textureProperties.GetArrayElementAtIndex(i);
                    var textureObjectProperty = textureProperty.FindPropertyRelative("second.m_Texture");

                    if (textureObjectProperty == null) continue;
                    var textureGuid = textureObjectProperty.stringValue;
                    if (textureGuid != originalGuid) continue;
                    textureObjectProperty.stringValue = newGuid;
                    serializedMaterial.ApplyModifiedProperties();
                    EditorUtility.SetDirty(material);
                    Debug.Log($"更新材质 {materialPath} 中的纹理引用: {originalGuid} → {newGuid}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"更新材质引用失败 {materialPath}: {e.Message}");
            }
        }

        /// 检查后缀参数 - 根据当前后缀过滤并检查相应贴图的参数是否正确
        private void CheckSuffixParameters()
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                EditorUtility.DisplayDialog("提示", "请先设置要检查的路径", "确定");
                return;
            }

            if (string.IsNullOrEmpty(_subffix))
            {
                EditorUtility.DisplayDialog("提示", "请先选择要检查的后缀", "确定");
                return;
            }

            // 获取预期的参数设置
            bool expectedSrgb;

            // 根据后缀设置预期的参数
            switch (_subffix)
            {
                case "_D": // 漫反射贴图
                    expectedSrgb = true;
                    break;
                case "_N": // 法线贴图
                case "_MRA": // 金属度、粗糙度、环境光遮蔽贴图
                    expectedSrgb = false;
                    break;
                case "_E": // 自发光贴图
                    expectedSrgb = true;
                    break;
                default:
                    EditorUtility.DisplayDialog("提示", $"未知的后缀类型: {_subffix}", "确定");
                    return;
            }

            const bool expectedAlphaIsTrans = false;
            const bool expectedMipMap = true;
            const TextureImporterAlphaSource expectedAlphaSource = TextureImporterAlphaSource.None;

            // 清空之前的检查结果
            _checkResults.Clear();

            // 查找路径中符合后缀条件的贴图
            var texturePaths = AssetDatabase.FindAssets("t:Texture2D", new[] { _filePath });
            var matchingTexturesCount = 0;
            var correctTexturesCount = 0;
            var incorrectTexturesCount = 0;

            foreach (var guid in texturePaths)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                
                // 检查贴图是否在当前路径下（不包含子文件夹）
                if (!_includeSubfolders)
                {
                    var textureDirectory = System.IO.Path.GetDirectoryName(assetPath);
                    if (textureDirectory != _filePath)
                    {
                        continue; // 跳过子文件夹中的贴图
                    }
                }
                
                // 检查贴图文件名是否包含指定后缀
                var fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                if (!fileName.EndsWith(_subffix)) continue;
                matchingTexturesCount++;
                    
                // 检查贴图参数是否正确，并收集错误信息
                var textureErrors = new List<string>();
                var isCorrect = CheckTextureParametersWithDetails(assetPath, expectedSrgb, expectedAlphaIsTrans, expectedMipMap, expectedAlphaSource, ref textureErrors);
                    
                if (isCorrect)
                {
                    correctTexturesCount++;
                }
                else
                {
                    incorrectTexturesCount++;
                }
                    
                // 将检查结果添加到列表中
                _checkResults.Add(new TextureCheckResult(assetPath, isCorrect, textureErrors));
            }

            // 设置显示检查结果
            _showCheckResult = true;

            // 在控制台输出详细结果
            Debug.Log($"=== 参数检查结果 ===");
            Debug.Log($"后缀: {_subffix}, 路径: {_filePath}");
            Debug.Log($"找到匹配贴图: {matchingTexturesCount} 个");
            Debug.Log($"参数正确: {correctTexturesCount} 个");
            Debug.Log($"参数错误: {incorrectTexturesCount} 个");
            
            foreach (var result in _checkResults)
            {
                if (!result.isCorrect)
                {
                    Debug.LogWarning($"参数错误: {result.texturePath}");
                    foreach (string error in result.errorDetails)
                    {
                        Debug.Log($"  {error}");
                    }
                }
            }
        }

        /// 设置贴图TextureType为NormalMap（仅当后缀为"_N"时调用）
        private bool SetTextureTypeToNormalMap(string assetPath)
        {
            try
            {
                TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    // 检查当前TextureType
                    TextureImporterType currentTextureType = importer.textureType;
                    
                    // 如果当前不是NormalMap类型，则设置为NormalMap
                    if (currentTextureType != TextureImporterType.NormalMap)
                    {
                        // 保存原始设置以便记录
                        TextureImporterType originalTextureType = currentTextureType;
                        
                        // 设置TextureType为NormalMap
                        importer.textureType = TextureImporterType.NormalMap;
                        
                        // 应用更改
                        EditorUtility.SetDirty(importer);
                        importer.SaveAndReimport();
                        
                        Debug.Log($"已设置贴图 {assetPath} 的TextureType为NormalMap (原值: {originalTextureType})");
                        return true;
                    }
                    else
                    {
                        Debug.Log($"贴图 {assetPath} 的TextureType已经是NormalMap，无需修改");
                        return false;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"设置贴图 {assetPath} 的TextureType失败: {e.Message}");
            }

            return false;
        }

        /// 检查单个贴图的参数是否正确，并收集详细的错误信息
        private bool CheckTextureParametersWithDetails(string assetPath, bool expected_sRGB, bool expected_alphaIsTrans, bool expected_mipMap, TextureImporterAlphaSource expected_alphaSource, ref List<string> errorDetails)
        {
            try
            {
                TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    bool sRGB_correct = importer.sRGBTexture == expected_sRGB;
                    bool alphaIsTrans_correct = importer.alphaIsTransparency == expected_alphaIsTrans;
                    bool mipMap_correct = importer.mipmapEnabled == expected_mipMap;
                    bool alphaSource_correct = importer.alphaSource == expected_alphaSource;
                    
                    // 特殊检查：当后缀为"_N"时，检查TextureType是否为NormalMap
                    bool textureType_correct = true;
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                    if (fileName.EndsWith("_N"))
                    {
                        textureType_correct = importer.textureType == TextureImporterType.NormalMap;
                        if (!textureType_correct)
                        {
                            errorDetails.Add($"TextureType: {importer.textureType} (应为: NormalMap)");
                        }
                    }

                    // 收集错误的参数信息
                    if (!sRGB_correct)
                        errorDetails.Add($"sRGB: {importer.sRGBTexture} (应为: {expected_sRGB})");
                    
                    if (!alphaIsTrans_correct)
                        errorDetails.Add($"AlphaIsTransparency: {importer.alphaIsTransparency} (应为: {expected_alphaIsTrans})");
                    
                    if (!mipMap_correct)
                        errorDetails.Add($"GenerateMipmap: {importer.mipmapEnabled} (应为: {expected_mipMap})");
                    
                    if (!alphaSource_correct)
                        errorDetails.Add($"AlphaSource: {importer.alphaSource} (应为: {expected_alphaSource})");

                    return sRGB_correct && alphaIsTrans_correct && mipMap_correct && alphaSource_correct && textureType_correct;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"检查贴图参数失败 {assetPath}: {e.Message}");
                errorDetails.Add($"检查失败: {e.Message}");
            }

            return false;
        }

        /// 检查单个贴图的参数是否正确
        private bool CheckTextureParameters(string assetPath, bool expected_sRGB, bool expected_alphaIsTrans, bool expected_mipMap, TextureImporterAlphaSource expected_alphaSource)
        {
            try
            {
                TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    bool sRGB_correct = importer.sRGBTexture == expected_sRGB;
                    bool alphaIsTrans_correct = importer.alphaIsTransparency == expected_alphaIsTrans;
                    bool mipMap_correct = importer.mipmapEnabled == expected_mipMap;
                    bool alphaSource_correct = importer.alphaSource == expected_alphaSource;

                    return sRGB_correct && alphaIsTrans_correct && mipMap_correct && alphaSource_correct;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"检查贴图参数失败 {assetPath}: {e.Message}");
            }

            return false;
        }

        /// 批量设置贴图参数后更新检查结果
        private void UpdateCheckResultsAfterBatchSet()
        {
            if (_checkResults.Count == 0)
                return;

            Debug.Log("开始更新批量设置后的检查结果...");

            // 获取预期的参数设置
            bool expected_sRGB = false;
            bool expected_alphaIsTrans = false;
            bool expected_mipMap = false;
            TextureImporterAlphaSource expected_alphaSource = TextureImporterAlphaSource.None;

            // 根据后缀设置预期的参数
            switch (_subffix)
            {
                case "_D": // 漫反射贴图
                    expected_sRGB = true;
                    expected_alphaIsTrans = false;
                    expected_mipMap = true;
                    expected_alphaSource = TextureImporterAlphaSource.None;
                    break;
                case "_N": // 法线贴图
                    expected_sRGB = false;
                    expected_alphaIsTrans = false;
                    expected_mipMap = true;
                    expected_alphaSource = TextureImporterAlphaSource.None;
                    break;
                case "_MRA": // 金属度、粗糙度、环境光遮蔽贴图
                    expected_sRGB = false;
                    expected_alphaIsTrans = false;
                    expected_mipMap = true;
                    expected_alphaSource = TextureImporterAlphaSource.None;
                    break;
                case "_E": // 自发光贴图
                    expected_sRGB = true;
                    expected_alphaIsTrans = false;
                    expected_mipMap = true;
                    expected_alphaSource = TextureImporterAlphaSource.None;
                    break;
                default:
                    Debug.LogWarning($"未知的后缀类型: {_subffix}，无法更新检查结果");
                    return;
            }

            int updatedCount = 0;
            int correctedCount = 0;

            // 遍历所有检查结果并更新状态
            foreach (var result in _checkResults)
            {
                // 重新检查贴图参数
                List<string> newErrorDetails = new List<string>();
                bool newIsCorrect = CheckTextureParametersWithDetails(result.texturePath, expected_sRGB, expected_alphaIsTrans, expected_mipMap, expected_alphaSource, ref newErrorDetails);
                
                // 更新结果状态
                bool statusChanged = (result.isCorrect != newIsCorrect);
                result.isCorrect = newIsCorrect;
                result.errorDetails = newErrorDetails;
                
                if (statusChanged)
                {
                    updatedCount++;
                    if (newIsCorrect)
                    {
                        correctedCount++;
                        Debug.Log($"✓ 贴图参数已修正: {result.textureName}");
                    }
                    else
                    {
                        Debug.Log($"✗ 贴图参数仍有错误: {result.textureName}");
                    }
                }
            }

            Debug.Log($"检查结果更新完成: 更新了 {updatedCount} 个贴图状态，其中 {correctedCount} 个已修正");
            
            // 强制重绘窗口以显示更新后的结果
            Parent?.Repaint();
        }

        /// 记录贴图参数到控制台 - 只输出错误的参数
        private void LogTextureParameters(string assetPath)
        {
            try
            {
                TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    // 获取预期的参数设置
                    bool expected_sRGB = false;
                    bool expected_alphaIsTrans = false;
                    bool expected_mipMap = false;
                    TextureImporterAlphaSource expected_alphaSource = TextureImporterAlphaSource.None;

                    // 根据后缀设置预期的参数
                    switch (_subffix)
                    {
                        case "_D": // 漫反射贴图
                            expected_sRGB = true;
                            expected_alphaIsTrans = false;
                            expected_mipMap = true;
                            expected_alphaSource = TextureImporterAlphaSource.None;
                            break;
                        case "_N": // 法线贴图
                            expected_sRGB = false;
                            expected_alphaIsTrans = false;
                            expected_mipMap = true;
                            expected_alphaSource = TextureImporterAlphaSource.None;
                            break;
                        case "_MRA": // 金属度、粗糙度、环境光遮蔽贴图
                            expected_sRGB = false;
                            expected_alphaIsTrans = false;
                            expected_mipMap = true;
                            expected_alphaSource = TextureImporterAlphaSource.None;
                            break;
                        case "_E": // 自发光贴图
                            expected_sRGB = true;
                            expected_alphaIsTrans = false;
                            expected_mipMap = true;
                            expected_alphaSource = TextureImporterAlphaSource.None;
                            break;
                    }

                    // 检查哪些参数错误
                    List<string> incorrectParams = new List<string>();
                    
                    if (importer.sRGBTexture != expected_sRGB)
                        incorrectParams.Add($"sRGB: {importer.sRGBTexture} (应为: {expected_sRGB})");
                    
                    if (importer.alphaIsTransparency != expected_alphaIsTrans)
                        incorrectParams.Add($"AlphaIsTransparency: {importer.alphaIsTransparency} (应为: {expected_alphaIsTrans})");
                    
                    if (importer.mipmapEnabled != expected_mipMap)
                        incorrectParams.Add($"GenerateMipmap: {importer.mipmapEnabled} (应为: {expected_mipMap})");
                    
                    if (importer.alphaSource != expected_alphaSource)
                        incorrectParams.Add($"AlphaSource: {importer.alphaSource} (应为: {expected_alphaSource})");

                    // 只输出错误的参数
                    if (incorrectParams.Count > 0)
                    {
                        Debug.Log($"参数错误 - {System.IO.Path.GetFileName(assetPath)}:");
                        foreach (string param in incorrectParams)
                        {
                            Debug.Log($"  {param}");
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"记录贴图参数失败 {assetPath}: {e.Message}");
            }
        }

    }
}
