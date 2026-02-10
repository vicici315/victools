using UnityEngine;
using UnityEditor;

namespace VicTools
{
    public class VicToolsHelpWindow : EditorWindow
    {
        private Vector2 scrollPosition;
        private EditorStyle style;
        private GUIStyle s_CustomHelpBox;
        [MenuItem("Tools/VicTools(YD)/关于VicTools(YD)", false, 9999)]
        public static void ShowWindow()
        {
            VicToolsHelpWindow window = GetWindow<VicToolsHelpWindow>("About");
            window.minSize = new Vector2(760, 500);
            window.maxSize = new Vector2(760, 2000);
            window.Show();
        }
        
        void OnGUI()
        {
            if (style == null)
            {
                style = EditorStyle.Get;
            }

            s_CustomHelpBox = new GUIStyle(EditorStyles.helpBox)
            {
                // 设置背景色（关键步骤）
                normal = new GUIStyleState()
                {
                    background = MakeTex(2, 2, new Color(0.1f, 0.12f, 0.12f, 0.53f)) // 半透明蓝色
                },
                // 可选：调整内边距、字体等
                padding = new RectOffset(10, 10, 6, 6), //内部偏倚
                margin = new RectOffset(10, 9, 5, 5) //外部偏移（左，右，上，下）
            };
            // 标题
            EditorGUILayout.Space(10);
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
            titleStyle.fontSize = 20;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            EditorGUILayout.LabelField("🔧 关于 VicTools(YD)", titleStyle, GUILayout.Height(34));
            EditorGUILayout.Space(10);
            
            EditorGUILayout.BeginVertical(s_CustomHelpBox);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, false, false);
            
            // 主要功能部分
            DrawSection("主要功能", new string[] {
                "• 场景工具 - 快速选择和操作场景中的对象",
                "• 材质查找 - 高速查找和管理材质",
                "• 资源工具 - 项目资源文件管理批量配置；安全的批量重命名（将保留资源引用）"
            });
            
            // 使用提示部分
            DrawSection("使用提示", new string[] {
                "• 点击上方按钮切换不同工具",
                "• 使用Ctrl+点击可添加选择",
                "• 拖拽对象到资源箱区域可快速添加各种对象到资源箱中，便于选择和快速赋予材质等操作",
                "• 主窗口右上角第一个按钮可以设置工具标签的位置，根据自己的使用习惯自定义",
                "（详细操作说明请查看帮助文档）"
            });
            
            // 版本信息
            DrawSection("版本信息", new string[] {
                "• v2.1 【场景工具2.7-资源箱】优化全局存档改为本地Library\\VicTools，修改自定义存档路径为Editor\\VicTools\\ResourceBox。",
                "• v2.0 改版Package管理及更新。",
                "• v1.4.8 【材质查找1.3】优化UI界面。",
                "• v1.4.7 【场景工具2.6】添加（校正(PBR_Mobile)烘焙高光方向）按钮。",
                "• v1.4.0 【场景工具2.5】优化资源箱列表在场景对象需要刷新时保留对象名显示；【性能分析1.4】资源利用率检查 (测试)。",
                "• v1.3.9 【场景工具2.4】添加层级操作按钮。",
                "• v1.3.8 【资源工具1.3】优化批量重命名资源对象时的安全性；【材质查找1.2】添加（查找所有Shader）按钮。",
                "• v1.3.6 添加全局光照对象检查，添加信息显示选项优化性能分析界面。",
                "• v1.3.5 重启引擎保留窗口停靠，材质查找列表添加赋予按钮；添加独立窗口；优化设置贴图参数。",
                "• v1.3.3 添加窗口停靠设置，优化其它工具。",
                "• v1.3.2 【场景工具2.1】修复资源箱Bug，添加选中对象标记；其它优化。",
                ""
            });
            
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(style.area);
            // 链接部分
            DrawLinksSection();
            EditorGUILayout.EndVertical();
            // 版本信息
            GUIStyle versionStyle = new GUIStyle(EditorStyles.label);
            versionStyle.alignment = TextAnchor.MiddleCenter;
            versionStyle.fontSize = 12;
            EditorGUILayout.LabelField($"版本：{VicToolsConfig.Ver}  |  开发者：Vic (YD)", versionStyle);
            EditorGUILayout.Space(10);

        }
        // 工具函数：创建单色纹理
        private static Texture2D MakeTex(int width, int height, Color col)
        {
            var pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;

            var result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
        private void DrawSection(string title, string[] items)
        {
            EditorGUILayout.Space(15);
            GUIStyle newStyle = new GUIStyle(style.normalfont);
            newStyle.fontSize = 20;
            EditorGUILayout.LabelField(title, newStyle);

            EditorGUILayout.Space(8);
            GUIStyle textStyle = new GUIStyle(style.normalfont_Hui_Wrap);
            textStyle.fontSize = 18;
            foreach (string item in items)
            {
                EditorGUILayout.LabelField(item, textStyle);
            }
        }
        
        private void DrawLinksSection()
        {
            
            // EditorGUILayout.Space(30);
            GUIStyle linkStyle = new GUIStyle(style.normalfont);
            linkStyle.fontSize = 20;
            EditorGUILayout.LabelField("相关链接", linkStyle);
            
            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal(style.area);
            // 飞书文档链接
            if (DrawLinkButton("📑 帮助文档", "https://nyq1lw99l7.feishu.cn/wiki/GVDYwV0TFiEPl2kTJzWcwcI6n6d?from=from_copylink"))
            {
                Application.OpenURL("https://nyq1lw99l7.feishu.cn/wiki/GVDYwV0TFiEPl2kTJzWcwcI6n6d?from=from_copylink");
            }
            
            EditorGUILayout.Space(20);
            
            // 问题反馈链接
            if (DrawLinkButton("💡 问题反馈及需求建议", "https://nyq1lw99l7.feishu.cn/wiki/NtNEwDxpiiBQijksYJMcqixNnqg?from=from_copylink"))
            {
                Application.OpenURL("https://nyq1lw99l7.feishu.cn/wiki/NtNEwDxpiiBQijksYJMcqixNnqg?from=from_copylink");
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            // 使用教程链接
            // if (DrawLinkButton("🎬 视频教程", "https://www.youtube.com/your-channel"))
            // {
            //     Application.OpenURL("https://www.youtube.com/your-channel");
            // }
        }
        
        private bool DrawLinkButton(string label, string url)
        {
            GUIStyle linkStyle = new GUIStyle(style.link);
            linkStyle.padding = new RectOffset(0, 0, 0, 0);
            
            GUIContent content = new GUIContent(label);
            
            // 计算文本大小
            Vector2 textSize = linkStyle.CalcSize(content);
            
            // 创建一个水平布局，让链接文本左对齐
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.Space(20); // 左侧弹性空间
            
            // 绘制链接文本
            Rect labelRect = GUILayoutUtility.GetRect(content, linkStyle, GUILayout.Width(textSize.x), GUILayout.Height(textSize.y));
            
            // 绘制文本
            GUI.Label(labelRect, content, linkStyle);
            
            // 绘制下划线
            Rect underlineRect = new Rect(labelRect.x, labelRect.y + labelRect.height - 2, textSize.x, 1);
            EditorGUI.DrawRect(underlineRect, new Color(0.1f, 0.3f, 0.8f, 0.8f));
            
            // 添加鼠标悬停效果
            EditorGUIUtility.AddCursorRect(labelRect, MouseCursor.Link);
            
            // 检测点击
            bool clicked = false;
            if (Event.current.type == EventType.MouseDown && labelRect.Contains(Event.current.mousePosition))
            {
                clicked = true;
                Event.current.Use();
            }
            
            GUILayout.FlexibleSpace(); // 右侧弹性空间
            EditorGUILayout.EndHorizontal();
            
            return clicked;
        }
    }
}
