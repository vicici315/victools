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
            window.minSize = new Vector2(1110, 500);
            window.maxSize = new Vector2(1110, 2000);
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
                "■ v2.8.2",
"【资源工具 v1.6】 设置贴图尺寸时检查Override For Android选项并关闭。",
"PBR_Mobile6.2 支持烘焙模式Shadowmask模式，修复该模式时使用顶点阴影时报错。",
"Tree_Trans 1.0 植被透明材质，虚拟光照，ShadowMap  只带投影。",
                "■ v2.8.1",
"【场景工具2.17】添加【↓】快速统一赋予最后选中对象的材质按钮；添加模型一键落地按钮，以碰撞体落地操作（Ctrl+点击：以模型底部落地）。",
"【材质查找1.4】支持按住Ctrl键加选查找到的模型与材质球。",
"PBR_Mobile6..1 添加变色通道控制，MRA贴图的a通道作为基础颜色蒙版；优化自定义聚光灯提高刷新率保证视觉效果的流畅性。",
"Glass_carWindow 两个玻璃材质GUI共用匹配。",
                "■ v2.8.0",
"【性能分析1.8】对象统计添加错误选项用于列出相关潜在错误对象，扫描缺失脚本材质等对象。",
"【资源工具1.5】修改设置尺寸判断为大于等于设定值。",
"【场景工具2.16】添加【选择材质】按钮，用于选择场景中选中对象的材质球。",
"FurShell 1.4毛发材质添加GUI控制，修复风场脚本圆锥体角度压扁Bug。",
"PBR_Mobile6.0 完善所有效果，继承原始表现效果；添加[统一阴影]按钮，用于统一设置“自身阴影衰减”值，使场景中阴影保持一致的明暗度（包括PBR_Mobile_Trans）。",
                "■ v2.7.10",
"PBR_Mobile5.9 优化高光算法，高光随模型边缘形状挤压还原真实高光效果；增加金属反射对比及对反射的颜色控制。",
"Glass_carWindow添加Ramp渐变贴图，可用于模拟肥皂泡效果。",
"添加毛发材质FurShell_Mobile_SingleC，支持团结引擎版本。",
                "■ v2.7.9",
"PBR_Mobile5.8 优化高光基础能量：提高非金属材质的基础高光强度。",
"添加EmissionFlicker1.0 PBR_Mobile自发光闪烁脚本。",
"添加\"Custom\\Texture\"天空盒shader。",
                "■ v2.7.8",
"【性能分析1.7】 对象统计模块添加静态对象统计快速选择。",
"PBR_Mobile5.8 优化高光亮度，移除specularColor削减；烘焙高光受实时阴影影响；添加【存档】【读档】【重置参数】按钮还原所有参数默认值，重置参数按钮默认读取Default存档（如果覆盖该存档则重置参数会读取Default文件中的设置）。",
"工具主窗口左上角添加【Menu】辅助功能菜单，包含（校正(PBR_Mobile)烘焙高光方向、校正PBR_Mobile5.8高光数值）。",
                "■ v2.7.7",
"【场景工具2.15】 添加一键切换lighting材质可接收实时灯光(用于场景烘焙打灯时查看实时灯光效果)快速切换功能；支持PBR_Mobile_Trans材质烘焙高光校正。",
"PBR_Mobile5.6  修复反射被烘焙光照覆盖问题，",
"PBR_Mobile5.7 烘焙投影支持，使用Unity标准的Subtractive模式方法。",
                "■ v2.7.6",
                "• 【性能分析1.6】优化未使用资源扫描和删除逻辑，修复Prefab嵌套依赖检测问题",
                "   增强Prefab依赖关系检测，通过GUID引用识别嵌套Prefab",
                "   将依赖关系检查提前到扫描阶段，大幅提升删除操作速度",
                "   单个资源删除和批量删除都会检查依赖关系，防止误删",
                "   列表中被引用的资源显示[被引用]标记，删除按钮置灰",
                "   激活资源利用检查时自动关闭其他模块，腾出显示空间",
                "   优化未使用资源列表布局，支持自动扩展并保持最小可见高度。",
                "• PBR_Mobile5.3 优化自身阴影平滑度，减少阶梯状硬边；自身阴影强度 大于0.9不进行自身阴影计算",
                "• PBR_Mobile5.5 完善自身阴影与半兰伯特阴影。",
                "■ v2.7.5",
                "PBR_Mobile5.2 优化材质UI操作界面，隐藏未激活的参数缩减界面",
"【性能分析1.5】 优化未使用资源扫码准确度，精确查找BuildSetting中添加场景的资源使用，添加（扫描所有场景）选项",
"【场景工具2.13】 优化挑选二级选项逻辑，更准确的挑选操作",
"【Compute Buffer Tool v3.4】 管理器添加SpotTexture批量设置所有PBR_Mobile材质参数",
                "■ v2.7.4",
                "【场景工具2.12】优化资源箱丢失对象保留正确名称，启动工具自动刷新",
                "【ComputeBufferTool3.3】添加（剔除材质↑）按钮，可以剔除模型或Project中的材质球，强化（添加材质↓）按钮也可添加场景对象材质",
                "■ v2.7.3 【场景工具2.11】完善[挑选]按钮功能优化判断逻辑",
                "【ComputeBufferTool3.2】优化用户界面，添加（添加材质↓）按钮用于向管理器添加Project中选择的材质球",
                "■ v2.7.2 优化挑选选项逻辑；添加（Off）按钮用于关闭所有一级选项",
                "■ v2.7.1 【场景工具2.9】添加[挑选]按钮，可以根据选项快速挑选相应对象，添加二级选择选项",
                "【ComputeBufferTool3.1】优化材质列表，添加（选择材质）按钮用于选择收集的材质球",
                "■ v2.6.1 【资源工具1.4】添加模型批量检查GenerateLightmapUVs",
                "【场景工具2.8】添加[Mesh]按钮，根据场景非预设模型快速选择",
                "PBR_Mobile5.1 支持虚拟聚光灯，聚光纹理彩色光环",
                "■ v2.1 【场景工具2.7-资源箱】优化全局存档改为本地Library\\VicTools，修改自定义存档路径为Editor\\VicTools\\ResourceBox",
                "■ v2.0 改版Package管理及更新",
                "■ v1.4.8 【材质查找1.3】优化UI界面",
                "■ v1.4.7 【场景工具2.6】添加（校正(PBR_Mobile)烘焙高光方向）按钮",
                "■ v1.4.0 【场景工具2.5】优化资源箱列表在场景对象需要刷新时保留对象名显示；【性能分析1.4】资源利用率检查 (测试)",
                "■ v1.3.9 【场景工具2.4】添加层级操作按钮",
                "■ v1.3.8 【资源工具1.3】优化批量重命名资源对象时的安全性；【材质查找1.2】添加（查找所有Shader）按钮",
                "■ v1.3.6 添加全局光照对象检查，添加信息显示选项优化性能分析界面",
                "■ v1.3.5 重启引擎保留窗口停靠，材质查找列表添加赋予按钮；添加独立窗口；优化设置贴图参数",
                "■ v1.3.3 添加窗口停靠设置，优化其它工具",
                "■ v1.3.2 【场景工具2.1】修复资源箱Bug，添加选中对象标记；其它优化",
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
