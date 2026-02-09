using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
// using System.Diagnostics;    // 用于调试(会导致Debug错误)

namespace VicTools
{
    /// <summary>
    /// 搜索历史记录管理器 - 可复用的工具类
    /// 
    /// 设计理念：
    /// - 每个实例管理特定类型的历史记录（如场景搜索、路径搜索等）
    /// - 使用不同的存储键来避免EditorPrefs冲突
    /// - 提供完整的GUI组件和持久化功能
    /// 
    /// 使用示例：
    /// - ScenesTools：管理场景搜索文本历史
    /// - ProjectTools：管理文件路径历史
    /// 
    /// 核心功能：
    /// 1. 历史记录的添加、保存、加载
    /// 2. 历史记录的去重和数量限制
    /// 3. 下拉选择器UI组件
    /// 4. 清除历史记录功能
    /// 5. 回车键事件处理
    /// </summary>
    public class SearchHistoryManager
    {
        // 内部存储的搜索历史记录列表
        private List<string> searchHistory = new List<string>();
        
        // EditorPrefs存储键，用于持久化历史记录
        private string searchHistoryKey;
        
        // EditorPrefs存储键，用于持久化上次搜索文本
        private string lastSearchTextKey;
        
        // 最大历史记录数量限制
        private int maxHistoryCount;
        
        /// <summary>
        /// 搜索历史记录
        /// </summary>
        public List<string> SearchHistory => searchHistory;
        
        /// <summary>
        /// 获取搜索历史记录列表
        /// </summary>
        /// <returns>搜索历史记录列表</returns>
        public List<string> GetSearchHistory()
        {
            return new List<string>(searchHistory);
        }
        
        /// <summary>
        /// 构造函数
        /// 
        /// 重要：每个工具应该使用不同的uniqueKeyPrefix来避免历史记录冲突
        /// 例如：
        /// - ScenesTools: "VicTools_ScenesTools"
        /// - ProjectTools: "ProjectTools_PathHistory"
        /// </summary>
        /// <param name="uniqueKeyPrefix">唯一键前缀，用于区分不同的搜索历史记录</param>
        /// <param name="maxHistory">最大历史记录数量，默认10</param>
        public SearchHistoryManager(string uniqueKeyPrefix, int maxHistory = 10)
        {
            // 生成存储键：前缀 + 特定后缀，确保唯一性
            searchHistoryKey = $"{uniqueKeyPrefix}_SearchHistory";
            lastSearchTextKey = $"{uniqueKeyPrefix}_LastSearchText";
            maxHistoryCount = maxHistory;
            
            // 自动加载历史记录 - 构造函数中自动初始化
            LoadSearchHistory();
        }
        
        /// <summary>
        /// 添加搜索文本到历史记录
        /// 
        /// 功能特点：
        /// 1. 自动去重：如果文本已存在，先移除再重新添加
        /// 2. 最新优先：新添加的文本放在列表开头
        /// 3. 数量限制：自动限制历史记录数量
        /// 4. 自动保存：添加后自动持久化到EditorPrefs
        /// </summary>
        /// <param name="text">搜索文本</param>
        public void AddToSearchHistory(string text)
        {
            // 验证输入：空文本或纯空白文本不添加到历史记录
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
                return;

            // 移除重复项 - 确保历史记录中每个文本只出现一次
            searchHistory.RemoveAll(item => item == text);

            // 添加到列表开头 - 最新使用的记录显示在最前面
            searchHistory.Insert(0, text);

            // 限制历史记录数量 - 防止列表无限增长
            if (searchHistory.Count > maxHistoryCount)
            {
                // 移除超出限制的旧记录（从列表末尾开始移除）
                searchHistory.RemoveRange(maxHistoryCount, searchHistory.Count - maxHistoryCount);
            }
            
            // 自动保存到EditorPrefs - 确保数据持久化
            SaveSearchHistory();
        }
        
        /// <summary>
        /// 保存搜索历史记录到EditorPrefs
        /// 
        /// 持久化机制：
        /// 1. 使用JSON序列化将List<string>转换为字符串
        /// 2. 存储在EditorPrefs中，确保Unity编辑器重启后数据不丢失
        /// 3. 当历史记录为空时自动清理存储键，避免存储冗余数据
        /// 
        /// 存储格式：
        /// - 使用Unity内置的JsonUtility进行序列化
        /// - 通过StringListWrapper包装器类处理List<string>的序列化
        /// </summary>
        public void SaveSearchHistory()
        {
            if (searchHistory.Count == 0)
            {
                // 历史记录为空时删除存储键，避免存储冗余数据
                EditorPrefs.DeleteKey(searchHistoryKey);
                return;
            }

            // 将历史记录列表转换为JSON字符串
            string historyJson = JsonUtility.ToJson(new StringListWrapper { items = searchHistory });
            EditorPrefs.SetString(searchHistoryKey, historyJson);
        }
        
        /// <summary>
        /// 从EditorPrefs加载搜索历史记录
        /// 
        /// 加载流程：
        /// 1. 检查EditorPrefs中是否存在对应的存储键
        /// 2. 如果存在，读取JSON字符串并反序列化为List<string>
        /// 3. 处理可能的反序列化异常，确保系统稳定性
        /// 4. 如果加载失败，初始化空的历史记录列表
        /// 
        /// 异常处理：
        /// - 捕获JSON反序列化异常
        /// - 记录警告日志但不中断程序执行
        /// - 提供默认的空列表作为回退方案
        /// </summary>
        public void LoadSearchHistory()
        {
            if (EditorPrefs.HasKey(searchHistoryKey))
            {
                string historyJson = EditorPrefs.GetString(searchHistoryKey);
                if (!string.IsNullOrEmpty(historyJson))
                {
                    try
                    {
                        // 使用JsonUtility反序列化JSON字符串
                        StringListWrapper wrapper = JsonUtility.FromJson<StringListWrapper>(historyJson);
                        searchHistory = wrapper?.items ?? new List<string>();
                    }
                    catch (System.Exception e)
                    {
                        // 反序列化失败时记录警告并初始化空列表
                        Debug.LogWarning($"加载搜索历史记录失败: {e.Message}");
                        searchHistory = new List<string>();
                    }
                }
            }
        }
        
        /// <summary>
        /// 保存上次搜索文本
        /// 
        /// 用途：
        /// - 保存用户最后一次使用的搜索文本
        /// - 在编辑器重启后恢复上次的搜索状态
        /// - 提供更好的用户体验，减少重复输入
        /// 
        /// 存储策略：
        /// - 非空文本：保存到EditorPrefs
        /// - 空文本：删除存储键，避免存储无效数据
        /// </summary>
        /// <param name="searchText">要保存的搜索文本</param>
        public void SaveLastSearchText(string searchText)
        {
            if (!string.IsNullOrEmpty(searchText))
            {
                EditorPrefs.SetString(lastSearchTextKey, searchText);
            }
            else
            {
                EditorPrefs.DeleteKey(lastSearchTextKey);
            }
        }
        
        /// <summary>
        /// 加载上次搜索文本
        /// 
        /// 恢复机制：
        /// - 检查EditorPrefs中是否存在上次搜索文本的存储键
        /// - 如果存在，返回存储的文本
        /// - 如果不存在，返回空字符串
        /// 
        /// 使用场景：
        /// - 编辑器窗口初始化时恢复上次搜索状态
        /// - 提供连续的工作体验
        /// </summary>
        /// <returns>上次保存的搜索文本，如果不存在则返回空字符串</returns>
        public string LoadLastSearchText()
        {
            if (EditorPrefs.HasKey(lastSearchTextKey))
            {
                return EditorPrefs.GetString(lastSearchTextKey, "");
            }
            return "";
        }
        
        /// <summary>
        /// 清除搜索历史记录
        /// 
        /// 清理操作：
        /// 1. 清空内部历史记录列表
        /// 2. 同步清理EditorPrefs中的持久化数据
        /// 3. 确保UI组件立即反映清理状态
        /// 
        /// 使用场景：
        /// - 用户手动清理历史记录
        /// - 系统重置操作
        /// - 隐私保护需求
        /// </summary>
        public void ClearSearchHistory()
        {
            searchHistory.Clear();
            SaveSearchHistory();
        }
        
        /// <summary>
        /// 将指定的历史记录项移到最前面
        /// 
        /// 使用场景：
        /// - 用户从下拉列表选择历史记录时
        /// - 需要将常用记录提升到前面
        /// - 实现"最近使用优先"的排序策略
        /// 
        /// 操作流程：
        /// 1. 从当前位置移除指定项（如果存在）
        /// 2. 将该项插入到列表开头
        /// 3. 应用数量限制规则
        /// 4. 自动持久化更改
        /// </summary>
        /// <param name="historyItem">要移到最前面的历史记录项</param>
        public void MoveHistoryToFront(string historyItem)
        {
            if (string.IsNullOrEmpty(historyItem))
                return;

            // 如果历史记录项存在，先移除它（去重）
            searchHistory.RemoveAll(item => item == historyItem);
            
            // 然后添加到列表开头（最新使用优先）
            searchHistory.Insert(0, historyItem);
            
            // 限制历史记录数量，保持列表整洁
            if (searchHistory.Count > maxHistoryCount)
            {
                searchHistory.RemoveRange(maxHistoryCount, searchHistory.Count - maxHistoryCount);
            }
            
            // 自动保存到持久化存储
            SaveSearchHistory();
        }

        /// <summary>
        /// 绘制搜索历史选择器（下拉选择器）
        /// 
        /// UI组件功能：
        /// 1. 显示历史记录下拉列表，用户可以选择之前的搜索记录
        /// 2. 包含"删除历史"选项，方便用户清理历史记录
        /// 3. 选择历史记录后自动更新当前搜索文本
        /// 4. 支持回调函数，便于外部处理选择事件
        /// 
        /// 交互逻辑：
        /// - 选择历史记录：更新搜索文本并移动记录到最前面
        /// - 选择删除历史：清除所有历史记录
        /// - 支持回调：允许外部脚本响应选择事件
        /// 
        /// 布局特点：
        /// - 固定宽度99像素，保持UI一致性
        /// - 自动处理空历史记录情况
        /// - 修复了Unity编辑器断言失败错误
        /// </summary>
        /// <param name="currentSearchText">当前搜索文本引用（ref参数，直接修改调用方的变量）</param>
        /// <param name="onHistorySelected">历史记录被选择时的回调函数，可选参数</param>
        public void DrawSearchHistorySelector(ref string currentSearchText, System.Action<string> onHistorySelected = null)
        {
            // EditorGUILayout.Space(5);
            
            if (searchHistory.Count > 0)
            {
                // 创建下拉选择器选项数组：历史记录 + 删除历史选项
                string[] historyOptions = new string[searchHistory.Count + 2];
                historyOptions[0] = "-- 选择历史 --"; // 注释掉的标题选项
                for (int i = 0; i < searchHistory.Count; i++)
                {
                    historyOptions[i + 1] = searchHistory[i];
                }
                // 添加删除历史选项作为最后一项
                historyOptions[searchHistory.Count + 1] = "※ 删除历史 ※";
                
                // 修复Unity编辑器断言失败错误：使用-1作为默认选择而不是0
                // 这样可以避免Unity尝试序列化某些对象状态
                int selectedIndex = EditorGUILayout.Popup(-1, historyOptions, GUILayout.Width(25));
                // Debug.Log($"测试打印：{historyOptions[selectedIndex]}>{selectedIndex}");
                if (selectedIndex > 0)
                {
                    
                    // 检查是否选择了删除历史选项（最后一项）
                    if (selectedIndex == searchHistory.Count + 1)
                    {
                        // 删除历史记录
                        ClearSearchHistory();
                        Debug.Log("已清除搜史记录");
                    }
                    else
                    {
                        // 选择普通历史记录
                        string selectedHistory = searchHistory[selectedIndex - 1];  // 原来有第一项标题需要减一[selectedIndex - 1]
                        currentSearchText = selectedHistory;
                        
                        // 将选择的历史记录移到最前面（最新使用优先）
                        MoveHistoryToFront(selectedHistory);
                        
                        // 调用回调函数，允许外部脚本处理选择事件
                        onHistorySelected?.Invoke(selectedHistory);
                    }
                }
            }
            // else
            // {
            //     // 当没有搜索历史记录时，显示提示信息（注释掉的备用UI）
            //     EditorGUILayout.LabelField("搜索历史：暂无记录", EditorStyles.miniLabel);
            // }
        }

        /// <summary>
        /// 绘制清除历史记录按钮
        /// 
        /// 布局选项：
        /// 1. 右对齐模式：使用FlexibleSpace将按钮推到右侧，适合工具栏布局
        /// 2. 左对齐模式：默认布局，适合普通表单布局
        /// 
        /// 自定义选项：
        /// - 可自定义按钮文本
        /// - 可传入自定义GUIStyle
        /// - 固定宽度110像素，保持UI一致性
        /// 
        /// 使用场景：
        /// - 工具栏：通常使用右对齐模式
        /// - 表单：通常使用左对齐模式
        /// </summary>
        /// <param name="buttonText">按钮显示文本，默认为"<清除历史>"</param>
        /// <param name="style">按钮样式，可选参数，默认使用GUI.skin.button</param>
        /// <param name="alignRight">是否右对齐布局，默认为false</param>
        public void DrawClearHistoryButton(string buttonText = "<清除历史>", GUIStyle style = null, bool alignRight = false)
        {
            if (alignRight)
            {
                // 右对齐布局：使用水平布局和弹性空间
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace(); // 添加弹性空间将按钮推到右边
                if (GUILayout.Button(buttonText, style ?? GUI.skin.button, GUILayout.Width(110)))
                {
                    ClearSearchHistory();
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                // 默认左对齐布局
                if (GUILayout.Button(buttonText, style ?? GUI.skin.button, GUILayout.Width(110)))
                {
                    ClearSearchHistory();
                }
            }
        }

        /// <summary>
        /// 处理回车键事件
        /// 
        /// 功能说明：
        /// 1. 检测回车键按下事件
        /// 2. 检查焦点是否在指定的搜索文本框上
        /// 3. 当条件满足时，将当前搜索文本添加到历史记录
        /// 4. 支持回调函数，便于外部处理搜索逻辑
        /// 
        /// 条件检查：
        /// - 事件类型：KeyDown
        /// - 按键：KeyCode.Return（回车键）
        /// - 焦点控制：检查焦点是否在指定的搜索文本框上
        /// - 文本验证：搜索文本不能为空
        /// 
        /// 注意：当前实现中部分逻辑被注释，可根据需要启用
        /// </summary>
        /// <param name="searchText">当前搜索文本</param>
        /// <param name="controlName">搜索文本框控件名称，必须提供唯一的控件名称</param>
        /// <param name="onEnterPressed">回车键按下时的回调函数，可选参数</param>
        /// <param name="validateText"></param>
        /// <returns>是否处理了回车键事件</returns>
        public bool HandleEnterKeyEvent(string searchText, string controlName, System.Action<string> onEnterPressed = null, bool validateText = false)
        {
            // 检查回车键事件
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
            {
                // 检查焦点是否在搜索文本框上
                string focusedControl = GUI.GetNameOfFocusedControl();
                bool isSearchFieldFocused = focusedControl == controlName;
                if (validateText)
                {
                    if (ValidateFilePath(searchText))
                    {
                        AddToSearchHistory(searchText);
                    }
                } else {
                    // Debug.Log($"检测到回车键事件 - 焦点在: {focusedControl}, 搜索文本: {searchText}");
                    if (!string.IsNullOrEmpty(searchText))
                    {
                        AddToSearchHistory(searchText);
                        // 这里可以添加实际的搜索逻辑
                        // Debug.Log($"执行搜索: {searchText}");
                    }
                }
                
                // 注释掉的严格条件检查逻辑：
                // 只有当焦点在搜索文本框上并且搜索文本不为空时才处理回车事件
                // if (isSearchFieldFocused && !string.IsNullOrEmpty(searchText))
                // {
                //     // 确保保存到历史记录
                //     AddToSearchHistory(searchText);
                //     Debug.Log($"回车执行搜索并保存历史: {searchText} (焦点在: {focusedControl})");
                    
                //     // 调用回调函数
                //     onEnterPressed?.Invoke(searchText);
                    
                //     Event.current.Use(); // 消耗事件，防止进一步处理
                //     return true;
                // }
                // else if (isSearchFieldFocused && string.IsNullOrEmpty(searchText))
                // {
                //     Debug.LogWarning("回车键未被处理：搜索文本为空");
                // }
                // else
                // {
                //     Debug.LogWarning($"回车键未被处理：焦点不在搜索文本框上 (当前焦点: {focusedControl})");
                // }
            }
            
            return false;
        }

        /// <summary>
        /// 用于JSON序列化的字符串列表包装器
        /// 
        /// 设计目的：
        /// - Unity的JsonUtility无法直接序列化List<string>
        /// - 通过包装器类实现List<string>的序列化
        /// - 提供与EditorPrefs兼容的数据格式
        /// 
        /// 序列化格式：
        /// {
        ///   "items": ["记录1", "记录2", "记录3"]
        /// }
        /// </summary>
        [System.Serializable]
        private class StringListWrapper
        {
            public List<string> items = new List<string>();
        }
        
        
        /// <summary>
        /// 验证文件路径是否存在 - 路径合法检测功能
        /// </summary>
        /// <param name="path">要验证的路径</param>
        /// <returns>如果路径存在返回true，否则返回false并显示错误提示</returns>
        private bool ValidateFilePath(string path)
        {
            path = path.Trim(); // 去除路径前后的空格
            path = path.Replace("\\", "/"); // 替换路径中的反斜杠为正斜杠 (Unity项目路径要求)
            path = path.Replace("//", "/"); // 替换连续的斜杠为单个斜杠
            path = path.TrimEnd('/');   // 去除路径末尾的斜杠
            
            // 检查路径是否为空或空白
            if (string.IsNullOrWhiteSpace(path))
            {
                EditorUtility.DisplayDialog("路径错误", "路径不能为空", "确定");
                return false;
            }

            // 检查路径是否以"Assets"开头（Unity项目路径要求）
            if (!path.StartsWith("Assets"))
            {
                EditorUtility.DisplayDialog("路径错误", 
                    $"路径必须以'Assets'开头\n当前路径: {path}", "确定");
                return false;
            }

            // 检查路径是否存在（文件夹或文件）
            bool pathExists = false;
            
            // 检查是否为文件夹
            if (System.IO.Directory.Exists(path))
            {
                pathExists = true;
            }
            // 检查是否为文件
            else if (System.IO.File.Exists(path))
            {
                pathExists = true;
            }
            // 检查是否为Unity资源路径
            else
            {
                // 尝试通过AssetDatabase检查路径是否存在
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset != null)
                {
                    pathExists = true;
                }
            }

            if (!pathExists)
            {
                EditorUtility.DisplayDialog("路径不存在", 
                    $"指定的路径不存在:\n{path}\n\n请检查路径是否正确", "确定");
                return false;
            }

            // 路径验证通过
            Debug.Log($"路径验证通过: {path}");
            return true;
        }
    }
}
