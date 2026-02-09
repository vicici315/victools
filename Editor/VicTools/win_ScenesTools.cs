using UnityEngine;
using UnityEditor;

namespace VicTools
{
    /// 提供一个独立的编辑器窗口来使用场景工具
    public class WinScenesTools : EditorWindow
    {
        private ScenesTools _analyzer;
        
        // 独立窗口的选中对象数量 - 不依赖主窗口
        public int StandaloneSelectedCount { get; set; }
        
        /// 显示场景工具独立窗口的菜单项
        [MenuItem("Tools/VicTools(YD)/[ ScenesTools ] 独立窗口 >/[场景工具]", false, 3002)]
        public static void ShowWindow()
        {
            var window = GetWindow<WinScenesTools>("场景工具");
            window.minSize = new Vector2(490, 562);
            window.maxSize = new Vector2(1680, 1832);
        }

        /// 窗口启用时调用，初始化场景工具
        private void OnEnable()
        {
            try
            {
                // 注册选择变化事件
                Selection.selectionChanged += OnSelectionChanged;
                
                // 创建场景工具实例
                _analyzer = new ScenesTools("场景工具", this);
                _analyzer.OnEnable();
                
                Debug.Log("场景工具独立窗口已初始化");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"初始化场景工具独立窗口失败: {ex.Message}");
            }
        }

        /// 窗口禁用时调用，清理资源
        private void OnDisable()
        {
            try
            {
                // 取消注册选择变化事件
                Selection.selectionChanged -= OnSelectionChanged;
                
                if (_analyzer != null)
                {
                    _analyzer.OnDisable();
                    Debug.Log("场景工具独立窗口已禁用");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"禁用场景工具独立窗口时出错: {ex.Message}");
            }
        }

        /// 窗口销毁时调用，清理资源
        private void OnDestroy()
        {
            try
            {
                // 确保取消注册选择变化事件
                Selection.selectionChanged -= OnSelectionChanged;
                
                if (_analyzer != null)
                {
                    _analyzer.OnDestroy();
                    _analyzer = null;
                    Debug.Log("场景工具独立窗口已销毁");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"销毁场景工具独立窗口时出错: {ex.Message}");
            }
        }

        /// 窗口获得焦点时调用
        private void OnFocus()
        {
            if (_analyzer != null)
            {
                _analyzer.OnFocus();
            }
        }

        /// 窗口失去焦点时调用
        private void OnLostFocus()
        {
            if (_analyzer != null)
            {
                _analyzer.OnLostFocus();
            }
        }

        /// 层级结构变化时调用
        private void OnHierarchyChange()
        {
            if (_analyzer != null)
            {
                _analyzer.OnHierarchyChange();
            }
        }

        /// 处理选择变化事件 - 只更新独立窗口的选中对象数量
        private void OnSelectionChanged()
        {
            try
            {
                // 更新独立窗口的选中对象数量
                StandaloneSelectedCount = Selection.gameObjects.Length;
                
                // 强制重绘当前窗口以更新显示
                Repaint();
                
                // Debug.Log($"场景工具独立窗口更新选中数量: {standaloneSelectedCount}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"处理选择变化事件时出错: {ex.Message}");
            }
        }

        /// 绘制窗口GUI界面
        private void OnGUI()
        {
            if (_analyzer != null)
            {
                try
                {
                    _analyzer.OnGUI();
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"绘制场景工具界面失败: {ex.Message}");
                    EditorGUILayout.HelpBox($"界面绘制出错：{ex.Message}\n请查看控制台获取详细信息", MessageType.Error);
                    
                    // 提供重新初始化的按钮
                    if (GUILayout.Button("重新初始化"))
                    {
                        OnEnable();
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("场景工具未初始化");
                
                if (GUILayout.Button("初始化场景工具"))
                {
                    OnEnable();
                }
            }
        }
    }
}
