using UnityEngine;
using UnityEditor;

namespace VicTools
{
    /// 提供一个独立的编辑器窗口来分析和监控场景的性能指标
    public class TestScenePerformanceAnalyzer : EditorWindow
    {
        private ScenePerformanceAnalyzer _analyzer;
        
        // 独立窗口的选中对象数量 - 用于实时更新
        public int StandaloneSelectedCount { get; private set; }
        
		//[MenuItem("Tools/VicTools(YD)/[ ScenesTools ]", false, 2003)]
        /// 显示性能分析器窗口的菜单项
        [MenuItem("Tools/VicTools(YD)/[ ScenesTools ] 独立窗口 >/[性能分析]", false, 3001)]
        public static void ShowWindow()
        {
            var window = GetWindow<TestScenePerformanceAnalyzer>(VicToolsConfig.PerformanceAnalyzerWindowName);
            window.minSize = new Vector2(420, 962);
            window.maxSize = new Vector2(1680, 1832);
        }

        /// 窗口启用时调用，初始化性能分析器
        private void OnEnable()
        {
            try
            {
                // 注册选择变化事件以实现实时更新
                Selection.selectionChanged += OnSelectionChanged;
                
                _analyzer = new ScenePerformanceAnalyzer("分析器", this);
                _analyzer.OnEnable();
                
                Debug.Log("性能分析独立窗口已初始化");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"初始化性能分析独立窗口失败: {ex.Message}");
            }
        }

        /// 窗口禁用时调用，清理资源
        private void OnDisable()
        {
            try
            {
                // 取消注册选择变化事件
                Selection.selectionChanged -= OnSelectionChanged;

                if (_analyzer == null) return;
                _analyzer.OnDisable();
                Debug.Log("性能分析独立窗口已禁用");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"禁用性能分析独立窗口时出错: {ex.Message}");
            }
        }

        /// 窗口销毁时调用，清理资源
        private void OnDestroy()
        {
            try
            {
                // 确保取消注册选择变化事件
                Selection.selectionChanged -= OnSelectionChanged;

                if (_analyzer == null) return;
                _analyzer.OnDestroy();
                _analyzer = null;
                Debug.Log("性能分析独立窗口已销毁");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"销毁性能分析独立窗口时出错: {ex.Message}");
            }
        }

        /// 窗口获得焦点时调用
        private void OnFocus()
        {
            _analyzer?.OnFocus();
        }

        /// 窗口失去焦点时调用
        private void OnLostFocus()
        {
            _analyzer?.OnLostFocus();
        }

        /// 层级结构变化时调用
        private void OnHierarchyChange()
        {
            _analyzer?.OnHierarchyChange();
        }

        /// 处理选择变化事件 - 更新独立窗口的选中对象数量
        private void OnSelectionChanged()
        {
            try
            {
                // 更新独立窗口的选中对象数量
                StandaloneSelectedCount = Selection.gameObjects.Length;
                
                // 强制重绘当前窗口以更新显示
                Repaint();
                
                // Debug.Log($"性能分析独立窗口更新选中数量: {standaloneSelectedCount}");
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
                    Debug.LogError($"绘制性能分析界面失败: {ex.Message}");
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
                EditorGUILayout.LabelField("分析器未初始化");
                
                if (GUILayout.Button("初始化分析器"))
                {
                    OnEnable();
                }
            }
        }
    }
}
