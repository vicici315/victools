using UnityEngine;
using UnityEditor;

namespace VicTools
{
    /// Shader 混淆工具独立窗口
    public class WinShaderObfuscator : EditorWindow
    {
        private ShaderObfuscator _obfuscator;

        [MenuItem("Tools/VicTools(YD)/[ ScenesTools ] 独立窗口 >/[Shader 混淆]", false, 1804)]
        public static void ShowWindow()
        {
            var window = GetWindow<WinShaderObfuscator>("Shader 混淆");
            window.minSize = new Vector2(480, 420);
            window.maxSize = new Vector2(1200, 1600);
        }

        private void OnEnable()
        {
            _obfuscator = new ShaderObfuscator(this);
        }

        private void OnGUI()
        {
            if (_obfuscator != null)
            {
                _obfuscator.OnGUI();
            }
            else
            {
                EditorGUILayout.LabelField("Shader 混淆器未初始化");
                if (GUILayout.Button("重新初始化"))
                    OnEnable();
            }
        }
    }
}
