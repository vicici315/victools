using UnityEngine;
using UnityEditor;

namespace VicTools
{
    /// 提供一个独立的编辑器窗口来分析和监控场景的性能指标
    public class WinShaderMaterialFinder : EditorWindow
    {
        private ShaderMaterialFinder _analyzer;
        /// 显示性能分析器窗口的菜单项
        [MenuItem("Tools/VicTools(YD)/[ ScenesTools ] 独立窗口 >/[材质查找器]", false, 3003)]
        public static void ShowWindow()
        {
            var window = GetWindow<WinShaderMaterialFinder>("材质查找");
            window.minSize = new Vector2(470, 562);
            window.maxSize = new Vector2(1680, 1832);
        }

        /// 窗口启用时调用，初始化性能分析器
        private void OnEnable()
        {
            _analyzer = new ShaderMaterialFinder("材质查找器", this);
            _analyzer.OnEnable();
        }

        /// 绘制窗口GUI界面
        private void OnGUI()
        {
            if (_analyzer != null)
            {
                _analyzer.OnGUI();
            }
            else
            {
                EditorGUILayout.LabelField("材质查找器未初始化");
            }
        }
    }
}
