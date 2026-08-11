using System.Collections.Generic;
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
            DrawSection("包含工具模块", new string[] {
                "• 场景工具 - 快速选择和操作场景中的对象",
                "• 材质查找 - 高速查找和管理材质",
                "• 资源工具 - 项目资源文件管理批量配置；安全的批量重命名（将保留资源引用）",
                "• 性能分析 - 对场景进行资源暂用评估，显示内存资源等基础信息，可以对场景内容分类统计快速选择"
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
            DrawVersionSection(BuildVersionHistory());

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

        /// 版本信息条目：版本号 + 该版本的所有变更
        private class VersionEntry
        {
            public string Version;     // 如 "v2.10.4"
            public List<string> Changes; // 该版本下的所有变更行
        }

        /// 构建版本信息数据：按从新到旧排列，每个版本是一组变更
        private List<VersionEntry> BuildVersionHistory()
        {
            var history = new List<VersionEntry>();

            // 工具方法：添加一个版本段（按列书写可读性更好）
            void Add(string version, params string[] changes)
            {
                history.Add(new VersionEntry
                {
                    Version = version,
                    Changes = new List<string>(changes)
                });
            }

            Add("v2.11.1",
                "【Menu】菜单添加镜像模型功能，对选中对象进行快速安全镜像处理，避免出现法线贴图反转问题。",
                "PBR_Mobile 7.2 主材质添加[查找贴图]按钮，一键自动赋予PBR配套贴图，无需一张张贴图配置。"
            );

            Add("v2.11.0",
                "材质GUI添加材质参数着色样式，根据参数类型进行分类着色，让材质参数更直观。",
                "添加新工具【无限循环滚动v2.2】，可以快速对美术资源进行无限循环游走滚动，可以用于道路背景墙等无限移动模拟。",
                "添加新材质【Texture-UI 1.0】 基于 Custom/Texture 改编的UI专用版本。",
                "材质读档添加“排除主纹理”选项按钮，读取材质参数时可以选则不读取主要贴图包含PBR和自发光配套贴图。"
            );

            Add("v2.10.6",
                "【新材质】TransCutout 2.4 全透明 stencil mask 材质，集成 DitherTemporalAA 函数（颗粒状渐变，模拟 UE 同名函数效果），支持粒子系统 ColorOverLifetime 驱动透明阈值。",
                "Glass_carWindow 1.4 添加顶点风动位移，可用于模拟树叶飘动、生物游动效果。",
                "[Material:URP]菜单完善各材质对应图标。",
                "FPS 5.4 移除 TMP 支持，统一使用 UnityEngine.UI.Text。"
            );

            Add("v2.10.5",
                "LatticeModifierEditor v3.32 PlayMode 性能优化：进入运行时（Application.isPlaying）后，OnSceneGUI 跳过所有点/线着色计算（背面判断、深度排序、点拖拽、框选等），改为统一浅灰色立方体外壳，大幅降低 Play Mode 下的 Editor 性能开销。",
                "PBR_Mobile7.1 软阴影重构：等边三角形120°采样(中心+3点,减少到4次采样,固定权重2:1:1:1÷8)；顶点阴影/像素阴影互斥重构(_USEVERSHADOW激活时跳过shadow map采样)；修正权重归一化；添加ShadowMap边界检测(sc.z≤0排除范围外错误阴影)",
                "场景工具 v2.30 【选择名称:】按钮支持选中未激活对象。",
                "ShadowReceiver 12.0 接收阴影材质（等边三角形 120° 软阴影 + 单次 shadow map 比较，无 PCF 开销）"
            );

            Add("v2.10.4",
                "添加新工具菜单：创建 FPS帧率显示（可以自动创建帧率显示脚本相关对象）。",
                "LatticeModifier v3.26 重置晶格体位置：新增 initLatticePos / initLatticeRot / initLatticeScale 序列化字段，InitializeLattice 时保存初始 Transform，ResetToInitialTransform() 可复位到初始化时的位姿。",
                "LatticeModifierEditor v3.31 晶格线宽（屏幕像素）。Handles.DrawLine 是固定 1px，改用 DrawAAPolyLine 可控。",
                "针对DepthPrimingMode（深度预填充模式）Forced模式，优化性能与适配。\n  →  FurShell_Mobile_Single_Combined 毛发材质 Queue 改为 Transparent 队列。\n  →  Grass 2.4 DepthOnly pass 改为纯 vertex（不跑 Hull/Domain/Geometry），用草根 mesh 顶点位置写深度，解决 URP DepthPrimingMode=Forced 下 GS 双面薄片 Z 精度不一致导致的黑色面重叠问题。\n  →  OutlineZOffset1.4 添加 DepthOnly pass 用于兼容 URP DepthPrimingMode=Forced，避免描边材质不可见。",
                "PBR_Mobile 6.5 性能优化：MRA 贴图条件采样（仅 _USEMSAMAP 开启时采样），新增 _DisableBakedSpecular / _DisableIndirectSpecular 开关按需裁剪烘焙高光与间接高光计算，GUI 同步暴露选项。",
                "PBR_Mobile_Trans 6.5 _ZWrite 改用 shader_feature_local 关键字化（_ZWWRITE），仅在使用时编译对应变体。",
                "场景工具 v2.29 资源箱添加【场景】按钮，一键将当前打开的所有场景（多场景支持）放入资源箱。"
                );

            Add("v2.10.3",
                "场景工具 v2.28 资源箱场景对象存储机制增强：每个场景对象持久化记录 scenePath / sceneName / sceneGuid。",
                "LatticeModifier v3.23 性能 + 内存根治（针对 26 Renderer 共享 LatticeModifier 场景，端到端治理玩家端内存增长）。",
                "“Esc” 快捷键在取消晶格点选择的基础上，支持晶格对象与模型对象快速切换。",
                "LatticeModifier v3.24 内部点压缩（surfaceOnly 模式）。",
                "LatticeModifierEditor v3.26 解决多 Inspector 窗口下「扩展选择」「取消选择」按钮锁定 / 失效。\n  →  LatticeModifierEditor v3.27 Esc 定位优化：FindLatticesByName 增加渲染器目标验证，排除同名但无关联的晶格，解决同名模型选中错误晶格体的问题。");

            Add("v2.10.2",
                "SpotLightVolume v6.1 - 射线遮挡支持角色碰撞：新增 occlusionDetectTriggers 选项，可检测 Trigger 类型碰撞体。\n  →  SpotLightVolume v6.2 - 蒙版投影：新增 maskTexture 蒙版纹理模拟窗格光柱投影，沿光轴等比投射到锥体横截面，支持 enableMask 开关和 maskIntensity 强度控制。",
                "SpotLightVolumeCore v5.2 解决聚光灯体积光模拟边缘在深度雾中出现硬边问题。",
                "LatticeModifier v3.10 修复「单个静态对象打包后不可见 / 材质变灰」的真正根因——运行时 Mesh 可读性，取消目标模式（SingleRenderer / MultiRenderer），单 / 多对象统一处理 + 修复污染。\n  →  LatticeModifier v3.11 修复「目标对象带缩放时晶格变形被放大（叠加）」。\n  →  LatticeModifier v3.12：缓存蒙皮数据（仅当确为带蒙皮目标时有意义；非蒙皮 Mesh 这些为空数组）。\n  →  LatticeModifier v3.13：取消蒙皮双缓冲 + 每帧重新赋值 sharedMesh。\n  →  LatticeModifier v3.14：变形性能优化，修复运行时帧率骤降（修正缓存网格命名避免每帧重建、消除 Mathf.Pow、权重缓存、变更检测改用相对矩阵刚性同移零开销）。");

            Add("v2.10.1",
                "主菜单使用 AdvancedDropdown 菜单系统支持图标，添加相关菜单图标。",
                "LatticeModifierEditor 3.2 Inspector 暴露边缘羽化参数（feather），实时调整晶格边界变形衰减；添加晶格轴心设置；Esc 取消选择晶格点。",
                "LatticeModifier 3.3 修复轴心旋转后变形方向错位：统一使用当前晶格变换计算参数坐标，轴心操作同步更新内部数据；修复 Undo 支持（记录子 CP Transform）；羽化基于当前晶格包围盒从中心向边缘衰减。",
                "场景工具 v2.27 修复【挑选-MissMat】逻辑，排除粒子 Trail Material；添加重置场景对象位移旋转缩放变换工具按钮。");

            Add("v2.10.0",
                "LatticeModifier 3.0 重构：引入 DeformTarget 封装单 Renderer 变形管线，消除 Single / Multi 大量重复逻辑，添加【链接选中对象到晶格】功能按钮。",
                "LatticeModifierEditor 3.1 新增多目标模式【修复丢失绑定】按钮，自动检测并重新链接列表中未绑定到晶格的 Renderer。",
                "SmoothMeshNormal_1.6 优化【选择描边对象】按钮：选中 _OL 对象后立即刷新列表，保留所有操作按钮控件，固定平滑 mesh 后缀名。",
                "场景工具 v2.26 添加【MissMesh 选项】挑选，新增【丢失 mesh 自动找回按钮】，优化 FindMissMeshs：原有精确匹配失败后，新增基于对象名称的模糊相似匹配（拆词加权 + 阈值过滤），提升丢失 Mesh 找回成功率，OL 对象优先查找 _SmoothNormal 平滑处理过的 mesh。");

            Add("v2.9.8",
                "TextureGaussianBlur_HLSL v1.0 — 高斯模糊材质 Shader。",
                "Tools 菜单添加：探照灯体积雾（SpotLightVolume），SpotLightVolume v1.0 - 优化轻量探照灯体积雾效果。",
                "RotationController v2.0 - 新增 PingPong 摆动模式、AnimationCurve 缓动曲线、RotationMode 枚举，重构代码结构。");

            Add("v2.9.7",
                "Custom_Snow 1.1 优化雪地闪烁表现，陡峭斜面剔除：法线 Y 分量越小说明越陡峭，剔除闪光避免拉线。\n  →  Custom_Snow 1.2 使用 _SparkleTex G 通道噪波增强镜头转动时随机闪烁效果，添加【蒙版纹理密度】值，完善 GUI 参数说明。",
                "整合雪地交互方案，实现脚本自动化创建操作，自动挂载交互所需脚本。",
                "场景工具 v2.22 修复资源箱对象异常丢失，添加 ResourceBoxBuildPreprocessor — 打包前确保资源箱数据已保存。\n  →  场景工具 v2.23 防止打包后产生灰色重复对象，又不会在刷新时误删已有的正常记录。",
                "FurShell 1.7 修复“使用圆锥风力”选项开关控制无效问题。",
                "WindConeController 2.0 优化控制参数，修复 targetFurRenderer 相关参数控制实时生效。",
                "Grass 2.0 添加草地交互系统，添加草地控制器脚本。");

            Add("v2.9.6",
                "[Material] 菜单添加 +Ctrl 键时更换材质。",
                "OutlineZOffset 1.3 优化轮廓描边算法。",
                "Custom_Hair 2.1 添加【各向异性高光】选项控制是否计算高光。",
                "SmoothMeshNormal_1.4 添加<创建描边模型>按钮，自动克隆 _OL 描边对象并生成平滑网格，添加描边材质预置接口。\n  →  SmoothMeshNormal_1.5 添加<统一法线>按钮，将选中对象的 Mesh 法线重新计算为统一平滑法线。",
                "【场景工具 v2.20】添加晶格对象快速选择按钮。",
                "【场景工具 v2.21】修复 Gug：FindGameObjectByIdentifier 在查找场景对象时，如果保存的 InstanceID 与当前对象的 InstanceID 不匹配（domain reload、编辑器重启后 InstanceID 会变化）。",
                "LatticeModifier 2.11 修复 Undo 操作可能导致 Renderer 上的 Mesh 引用被恢复为 originalMesh 或 null。",
                "LatticeModifierEditor 2.9 优化晶格体背面控制点压暗显示（支持透视 / 正交）；优化控制点显示顺序。",
                "Glass_MobileNew v2.8 果冻效果实现，顶点变形支持 UV 采样模式（蒙皮模型稳定不跳动）；优化阴影与控制基础颜色关系。",
                "Grass 1.7 添加草体透贴纹理参数，UV 使用两段草体高度平展，适用于 1~2 段草体，添加剔除方式选项。",
                "材质 GUI 脚本优化 [读档 ▾] 为菜单模式，所有材质 GUI 添加 [预设 ▾] 菜单列；统一所有 Shader 的主材质关键字名称，在读取纹理时保留现有主贴图。",
                "Glass_MobileNew v2.9 修复 UV 采样模式接缝破面和蒙皮抖动：改为纯法线膨胀（顶点色 R × 强度），不采样贴图，彻底消除接缝差异。");

            Add("v2.9.5",
                "[Material] 菜单添加卡通材质 CustomToon。",
                "CustomToon 1.4（2D 卡通材质）添加【明暗对比】参数【自身阴影】选项；优化自身阴影过暗受环境光影响。\n  →  CustomHair 1.0（头发材质）双层各向异性高光。",
                "LatticeModifier 2.10 添加<扩展选择>按钮，可以扩展选择表面晶格控制点，优化控制点选择逻辑；添加<创建快照>功能按钮。",
                "【场景工具 v2.19】修复丢失对象删除出现的 Bug。");

            Add("v2.9.4",
                "【场景工具 v2.18】资源箱添加<按类型排序>按钮，对现有资源按类型排列，新增对象会自动按类型排列。",
                "新增 [Material] 菜单，快速创建 Custom Shader 材质球。",
                "[Tools] 菜单添加（晶格控制器、混合变形控制、主材质自发光闪烁、旋转动画控制）组件工具快速创建。",
                "Texture 1.3 不再依赖 DepthOnlyPass.hlsl，改为自己实现的轻量 pass，CBUFFER 用 _MainTex_ST 保持一致。",
                "LatticeModifierEditor 2.4 3D 视图选中同步：注册 Selection.selectionChanged，选中 CP 节点时遍历控制点找到对应索引，写入 selectedPoints 并触发 SceneView.RepaintAll()，Scene 视图里对应控制点会高亮显示。");

            Add("v2.9.3",
                "Grass 1.0 添加自定义草地生成材质。",
                "LatticeModifier 1.1 移动晶格或模型时，处于晶格范围内的顶点实时变形，离开后恢复原形。\n  →  LatticeModifier 1.2 支持子物体控制点（CP_x_y_z），可被 Animation / Timeline K 帧驱动变形。\n  →  LatticeModifier 1.3 选中晶格点时同步选中 Hierarchy 中对应 CP 节点。\n  →  LatticeModifier 1.4 静态 SceneView 回调，选中 CP 后晶格线框持续绘制；修复打包后动画不生效。\n  →  LatticeModifier 2.0 支持单个模型或整个预设 / 带蒙皮角色，新增多目标模式自动收集所有子 Renderer。\n  →  LatticeModifier 2.1 添加删除晶格功能（还原 Mesh 并删除晶格物体），添加目标时自动识别带骨骼角色父级。\n  →  LatticeModifier 2.2 支持不可读 Mesh（通过 Instantiate / BakeMesh 自动获取可读副本），修复只收集部分 Renderer 的问题。\n  →  LatticeModifier 2.3 SkinnedMeshRenderer 双缓冲 Mesh 交替赋值，保留骨骼动画；重新初始化可保留晶格编辑恢复控制。\n  →  LatticeModifier 2.4 修复运行时晶格变形失效：OnEnable 自动重建变形 Mesh 管线，保留控制点，动画与晶格叠加生效。\n  →  LatticeModifier 2.5 新增手动指定 Renderer 列表（manualRenderers），支持多选对象创建晶格，严格按列表变形不展开子级。\n  →  LatticeModifier 2.6 重新初始化保留控制点不再重置；运行 / 停止游戏自动重建 Mesh 管线；脏标记 + 顶点缓存优化编辑器性能。\n  →  LatticeModifier 2.7 安全 Mesh 销毁机制：只销毁 _LatticeDeform 变形副本，防止共享 Mesh 资源被误删导致模型消失。\n  →  LatticeModifier 2.8 重写烘焙晶格变形功能，解决 mesh 丢失 bug。",
                "LatticeModifierEditor 2.3 支持缩放旋转等工具手柄操作控制点；优化缩放旋转工具操作。",
                "ShadowReceiver 1.0 新增接收阴影材质，支持环境光。");

            Add("v2.9.2",
                "主窗口 Tools 菜单添加[创建晶格控制器]。",
                "LatticeModifier 1.0 FFD 晶格变形场，晶格挂在独立空物体上，支持控制点 Timeline 动画。");

            Add("v2.9.1",
                "PBR_Mobile 6.4 添加 Meta Pass 支持烘焙器正确读取材质 albedo 和 emission；修正 GI 合成公式分离间接漫反射与间接高光（与 URP Lit 能量分配一致）。",
                "优化所有自定义 Shader UI 布局，归类更清晰。",
                "PBR_Lighting.shadergraph 材质修复主贴图 Tilling Offset 参数的共用问题。",
                "BlendShapeAnimator v1.7 - 添加 SkinnedMeshRenderer 包围盒扩展，防止轴心偏移导致视锥剔除（修复轴心偏移问题）。");

            Add("v2.9.0",
                "OutlineOffset_URP v1.1 添加轮廓外描边材质。",
                "SmoothMeshNormal v1.2 轮廓描边辅助工具用于圆滑 mesh 法线，添加<覆盖>选项，支持生成平滑网格直接覆盖原有 Mesh。\n  →  SmoothMeshNormal v1.3 添加<选择父对象>按钮。",
                "FurShell v1.6 添加法线纹理控制毛发簇效果。",
                "Glass_MobileNew v2.2 添加法线控制顶点位移、uv 游走实现水柱流动效果。\n  →  Glass_MobileNew v2.3 添加顶点颜色 R 通道作为顶点位移蒙版，约束水流起始位置的偏移。\n  →  Glass_MobileNew v2.4 修复法线控制顶点位移跳动问题。",
                "PBR_Mobile v6.3 添加“禁用主光颜色”选项，取消勾选时使用默认白色。",
                "PBR_Mobile_Trans.shader 继承 PBR_Mobile 6.3 添加“禁用主光颜色”选项。",
                "BlendShapeAnimator v1.3（BlendShaper 混合变形顶点定位脚本）支持 SkinnedMeshRenderer 和普通 MeshFilter 模型的顶点追踪。",
                "CustomParticle.shader 1.0 水质感粒子材质，粒子系统控制透明。1.1 添加反射、法线、Fresnel。");

            Add("v2.8.2",
                "【资源工具 v1.6】设置贴图尺寸时检查 Override For Android 选项并关闭。",
                "PBR_Mobile 6.2 支持烘焙模式 Shadowmask 模式，修复该模式时使用顶点阴影时报错。",
                "Tree_Trans 1.0 植被透明材质，虚拟光照，ShadowMap 只带投影。",
                "FPS 5.0 优化算法：指数移动平均（EMA）对 1 / unscaledDeltaTime 做平滑。");

            Add("v2.8.1",
                "【场景工具 2.17】添加【↓】快速统一赋予最后选中对象的材质按钮；添加模型一键落地按钮，以碰撞体落地操作（Ctrl+点击：以模型底部落地）。",
                "【材质查找 1.4】支持按住 Ctrl 键加选查找到的模型与材质球。",
                "PBR_Mobile v6.1 添加变色通道控制，MRA 贴图的 a 通道作为基础颜色蒙版；优化自定义聚光灯提高刷新率保证视觉效果的流畅性。",
                "Glass_carWindow 两个玻璃材质 GUI 共用匹配。");

            Add("v2.8.0",
                "【性能分析 v1.8】对象统计添加错误选项用于列出相关潜在错误对象，扫描缺失脚本材质等对象。",
                "【资源工具 v1.5】修改设置尺寸判断为大于等于设定值。",
                "【场景工具 v2.16】添加【选择材质】按钮，用于选择场景中选中对象的材质球。",
                "FurShell v1.4 毛发材质添加 GUI 控制，修复风场脚本圆锥体角度压扁 Bug。",
                "PBR_Mobile v6.0 完善所有效果，继承原始表现效果；添加[统一阴影]按钮，用于统一设置“自身阴影衰减”值，使场景中阴影保持一致的明暗度（包括 PBR_Mobile_Trans）。");

            Add("v2.7.10",
                "PBR_Mobile v5.9 优化高光算法，高光随模型边缘形状挤压还原真实高光效果；增加金属反射对比及对反射的颜色控制。",
                "Glass_carWindow 添加 Ramp 渐变贴图，可用于模拟肥皂泡效果。",
                "添加毛发材质 FurShell_Mobile_SingleC，支持团结引擎版本。");

            Add("v2.7.9",
                "PBR_Mobile v5.8 优化高光基础能量：提高非金属材质的基础高光强度。",
                "添加 EmissionFlicker v1.0 PBR_Mobile 自发光闪烁脚本。",
                "添加\"Custom\\Texture\"天空盒 shader。");

            Add("v2.7.8",
                "【性能分析 v1.7】对象统计模块添加静态对象统计快速选择。",
                "PBR_Mobile v5.8 优化高光亮度，移除 specularColor 削减；烘焙高光受实时阴影影响；添加【存档】【读档】【重置参数】按钮还原所有参数默认值，重置参数按钮默认读取 Default 存档（如果覆盖该存档则重置参数会读取 Default 文件中的设置）。",
                "工具主窗口左上角添加【Menu】辅助功能菜单，包含（校正(PBR_Mobile)烘焙高光方向、校正 PBR_Mobile 5.8 高光数值）。");

            Add("v2.7.7",
                "【场景工具 v2.15】添加一键切换 lighting 材质可接收实时灯光（用于场景烘焙打灯时查看实时灯光效果）快速切换功能；支持 PBR_Mobile_Trans 材质烘焙高光校正。",
                "PBR_Mobile v5.6 修复反射被烘焙光照覆盖问题。",
                "PBR_Mobile v5.7 烘焙投影支持，使用 Unity 标准的 Subtractive 模式方法。");

            Add("v2.7.6",
                "【性能分析 1.6】优化未使用资源扫描和删除逻辑，修复 Prefab 嵌套依赖检测问题。",
                "增强 Prefab 依赖关系检测，通过 GUID 引用识别嵌套 Prefab。",
                "将依赖关系检查提前到扫描阶段，大幅提升删除操作速度。",
                "单个资源删除和批量删除都会检查依赖关系，防止误删。",
                "列表中被引用的资源显示[被引用]标记，删除按钮置灰。",
                "激活资源利用检查时自动关闭其他模块，腾出显示空间。",
                "优化未使用资源列表布局，支持自动扩展并保持最小可见高度。",
                "PBR_Mobile 5.3 优化自身阴影平滑度，减少阶梯状硬边；自身阴影强度大于 0.9 不进行自身阴影计算。\n  →  PBR_Mobile 5.5 完善自身阴影与半兰伯特阴影。");

            Add("v2.7.5",
                "PBR_Mobile 5.2 优化材质 UI 操作界面，隐藏未激活的参数缩减界面。",
                "【性能分析 v1.5】优化未使用资源扫码准确度，精确查找 BuildSetting 中添加场景的资源使用，添加（扫描所有场景）选项。",
                "【场景工具 v2.13】优化挑选二级选项逻辑，更准确的挑选操作。",
                "【Compute Buffer Tool v3.4】管理器添加 SpotTexture 批量设置所有 PBR_Mobile 材质参数。");

            Add("v2.7.4",
                "【场景工具 v2.12】优化资源箱丢失对象保留正确名称，启动工具自动刷新。",
                "【ComputeBufferTool 3.3】添加（剔除材质↑）按钮，可以剔除模型或 Project 中的材质球，强化（添加材质↓）按钮也可添加场景对象材质。");

            Add("v2.7.3",
                "【场景工具 v2.11】完善[挑选]按钮功能优化判断逻辑。",
                "【ComputeBufferTool 3.2】优化用户界面，添加（添加材质↓）按钮用于向管理器添加 Project 中选择的材质球。");

            Add("v2.7.2",
                "优化挑选选项逻辑；添加（Off）按钮用于关闭所有一级选项。");

            Add("v2.7.1",
                "【场景工具 v2.9】添加[挑选]按钮，可以根据选项快速挑选相应对象，添加二级选择选项。",
                "【ComputeBufferTool 3.1】优化材质列表，添加（选择材质）按钮用于选择收集的材质球。");

            Add("v2.6.1",
                "【资源工具 v1.4】添加模型批量检查 GenerateLightmapUVs。",
                "【场景工具 v2.8】添加[Mesh]按钮，根据场景非预设模型快速选择。",
                "PBR_Mobile 5.1 支持虚拟聚光灯，聚光纹理彩色光环。");

            Add("v2.1",
                "【场景工具 2.7-资源箱】优化全局存档改为本地 Library\\VicTools，修改自定义存档路径为 Editor\\VicTools\\ResourceBox。");

            Add("v2.0",
                "改版 Package 管理及更新。");

            Add("v1.4.8",
                "【材质查找 v1.3】优化 UI 界面。");

            Add("v1.4.7",
                "【场景工具 v2.6】添加（校正(PBR_Mobile)烘焙高光方向）按钮。");

            Add("v1.4.0",
                "【场景工具 v2.5】优化资源箱列表在场景对象需要刷新时保留对象名显示；【性能分析 1.4】资源利用率检查（测试）。");

            Add("v1.3.9",
                "【场景工具 v2.4】添加层级操作按钮。");

            Add("v1.3.8",
                "【资源工具 v1.3】优化批量重命名资源对象时的安全性；【材质查找 1.2】添加（查找所有 Shader）按钮。");

            Add("v1.3.6",
                "添加全局光照对象检查，添加信息显示选项优化性能分析界面。");

            Add("v1.3.5",
                "重启引擎保留窗口停靠，材质查找列表添加赋予按钮；添加独立窗口；优化设置贴图参数。");

            Add("v1.3.3",
                "添加窗口停靠设置，优化其它工具。");

            Add("v1.3.2",
                "【场景工具 v2.1】修复资源箱 Bug，添加选中对象标记；其它优化。");

            return history;
        }

        /// 绘制"版本信息"区块：版本标题突出 + 变更条目整齐缩进 + 视觉分组
        private void DrawVersionSection(List<VersionEntry> history)
        {
            EditorGUILayout.Space(15);
            // 标题
            GUIStyle titleStyle = new GUIStyle(style.normalfont);
            titleStyle.fontSize = 20;
            EditorGUILayout.LabelField("版本信息", titleStyle);
            EditorGUILayout.Space(8);

            // 版本标题样式：粗体 + 强调色 + 稍大字号
            GUIStyle versionTitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.35f, 1.0f, 0.65f) }, // 颜色强调
                margin = new RectOffset(0, 4, 2, 0)
            };

            // 变更条目样式：灰色 + 缩进 + 自动换行 + 适当行距
            GUIStyle changeStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 13,
                wordWrap = true,
                normal = { textColor = new Color(0.78f, 0.78f, 0.78f) },
                margin = new RectOffset(20, 0, 1, 1) // 左侧缩进 20 像素
            };

            // 分隔线样式
            GUIStyle separatorStyle = new GUIStyle();
            separatorStyle.fixedHeight = 1;
            separatorStyle.normal.background = MakeTex(1, 1, new Color(0.3f, 0.3f, 0.3f, 0.4f));
            separatorStyle.margin = new RectOffset(0, 0, 6, 6);

            for (int i = 0; i < history.Count; i++)
            {
                var entry = history[i];

                // 版本标题行：■ vX.Y.Z
                EditorGUILayout.BeginHorizontal();
                // 左侧色块
                var colorRect = GUILayoutUtility.GetRect(22, 6, GUILayout.Width(20));
                EditorGUI.DrawRect(colorRect, new Color(0.35f, 1.0f, 0.65f, 0.9f));
                EditorGUILayout.LabelField($" {entry.Version}", versionTitleStyle);
                EditorGUILayout.EndHorizontal();

                // 该版本下的所有变更
                foreach (var change in entry.Changes)
                {
                    if (string.IsNullOrEmpty(change)) continue;
                    // 项目符号统一为「·」，全角空格缩进与正文对齐
                    EditorGUILayout.LabelField($"  ●  {change}", changeStyle);
                }

                // 版本之间加分隔线（最后一条不加）
                if (i < history.Count - 1)
                {
                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField("", separatorStyle);
                }
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
