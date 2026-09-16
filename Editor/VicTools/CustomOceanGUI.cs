// Custom_Ocean.shader GUI控制脚本
// 参考 Glass_carWindowGUI.cs 的控制逻辑与样式实现
// CustomOceanGUIv1.0 存档/读档/预设/重置 + 深度渐变、风格化波浪、渲染设置分区绘制
// CustomOceanGUIv1.1 对照 Custom_Ocean.shader 现有参数，移除已删除的边缘浪花（_EdgeFoamWidth/_EdgeFoamStrength）分区
// CustomOceanGUIv1.2 新增海面泡沫（Surface Foam）分区，绘制 _FoamMap 及泡沫参数
// CustomOceanGUIv1.3 泡沫分区新增细节纹理 _FoamDetailMap，Offset 作为游走参数（Tiling/Offset 可编辑）
// CustomOceanGUIv1.4 深度分区新增水色斑驳参数；泡沫细节参数改名为「泡沫网强度/泡沫网裁切」
// CustomOceanGUIv1.5 移除无实际效果的 _FoamMap 分区（含泡沫强度/裁切/流速），泡沫统一由 _FoamDetailMap 驱动
// CustomOceanGUIv1.6 水色斑驳新增「斑驳扰动速度」，说明文案改为域扭曲双层噪声算法
// CustomOceanGUIv1.7 海面泡沫新增「波浪扭曲泡沫」（波浪贴图作为泡沫扭曲源）
// CustomOceanGUIv1.8 渲染设置补充 URP 雾效说明（雾由 Renderer Data 控制）
// CustomOceanGUIv1.9 波浪/泡沫扭曲说明更新：条纹位置用平滑线性水深，扭曲改为双向梯度
// CustomOceanGUIv1.10 泡沫扭曲源由波浪贴图改为水色斑驳输出
// CustomOceanGUIv1.11 浪花颜色标注 A = 不透明度
// 注意：Custom_Ocean 无 shader_feature toggle，读档后无需同步 keyword

using UnityEngine;
using UnityEditor;
using VicTools;

public class CustomOceanGUI : ShaderGUI
{
    private MaterialEditor m_MaterialEditor;
    private MaterialProperty[] m_Properties;

    // 缓存属性：深度渐变
    private MaterialProperty depthShallowColor;
    private MaterialProperty depthDeepColor;
    private MaterialProperty depthMaxDistance;
    private MaterialProperty waterColorVariation;
    private MaterialProperty waterVariationScale;
    private MaterialProperty waterVariationSpeed;

    // 缓存属性：风格化波浪
    private MaterialProperty baseMap;       // 波浪贴图（与其他 Shader 共用键名 _BaseMap）
    private MaterialProperty waveColor;
    private MaterialProperty waveMaxDistance;
    private MaterialProperty waveFalloff;
    private MaterialProperty waveSpeed;
    private MaterialProperty waveCount;
    private MaterialProperty waveStrength;
    private MaterialProperty waveCutOff;

    // 缓存属性：海面泡沫
    private MaterialProperty foamColor;
    private MaterialProperty foamDetailMap;
    private MaterialProperty foamDetailStrength;
    private MaterialProperty foamDetailCutOff;
    private MaterialProperty foamDistortStrength;

    // 缓存属性：渲染设置
    private MaterialProperty cullMode;
    private MaterialProperty srcBlend;
    private MaterialProperty dstBlend;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        m_MaterialEditor = materialEditor;
        m_Properties = properties;

        // 查找所有属性
        FindProperties();

        // 绘制 GUI
        DrawGlobalSettings();
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawDepthGradient();
        }
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawStylizedWave();
        }
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawSurfaceFoam();
        }
        DrawRenderSettings();
    }

    private void FindProperties()
    {
        depthShallowColor = FindProperty("_DepthShallowColor", m_Properties);
        depthDeepColor    = FindProperty("_DepthDeepColor", m_Properties);
        depthMaxDistance  = FindProperty("_DepthMaxDistance", m_Properties);
        waterColorVariation  = FindProperty("_WaterColorVariation", m_Properties);
        waterVariationScale  = FindProperty("_WaterVariationScale", m_Properties);
        waterVariationSpeed  = FindProperty("_WaterVariationSpeed", m_Properties);

        baseMap          = FindProperty("_BaseMap", m_Properties);
        waveColor        = FindProperty("_WaveColor", m_Properties);
        waveMaxDistance  = FindProperty("_WaveMaxDistance", m_Properties);
        waveFalloff      = FindProperty("_WaveFalloff", m_Properties);
        waveSpeed        = FindProperty("_WaveSpeed", m_Properties);
        waveCount        = FindProperty("_WaveCount", m_Properties);
        waveStrength     = FindProperty("_WaveStrength", m_Properties);
        waveCutOff       = FindProperty("_WaveCutOff", m_Properties);

        foamColor           = FindProperty("_FoamColor", m_Properties);
        foamDetailMap       = FindProperty("_FoamDetailMap", m_Properties);
        foamDetailStrength  = FindProperty("_FoamDetailStrength", m_Properties);
        foamDetailCutOff    = FindProperty("_FoamDetailCutOff", m_Properties);
        foamDistortStrength = FindProperty("_FoamDistortStrength", m_Properties);

        cullMode = FindProperty("_Cull", m_Properties);
        srcBlend = FindProperty("_SrcBlend", m_Properties);
        dstBlend = FindProperty("_DstBlend", m_Properties);
    }

    private void DrawGlobalSettings()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(HeaderStyle.Rich("全局设置", HeaderStyle.HeaderTitle), EditorStyle.Get.BoldLabelRichStyle);

        // 添加存档按钮
        GUI.backgroundColor = new Color(0.3f, 0.8f, 1.0f); // 蓝色背景
        if (GUILayout.Button("存档", GUILayout.Width(50)))
        {
            EditorApplication.delayCall += SaveMaterialParameters;
        }

        // 添加读档按钮
        GUI.backgroundColor = new Color(0.5f, 1.0f, 0.5f); // 绿色背景
        if (GUILayout.Button("读档 ▾", GUILayout.Width(55)))
        {
            ShowLoadDropdown();
        }

        // 添加重置按钮
        GUI.backgroundColor = new Color(1.0f, 0.8f, 0.3f); // 黄色背景
        if (GUILayout.Button("重置参数", GUILayout.Width(60)))
        {
            EditorApplication.delayCall += ResetMaterialParameters;
        }

        // 预设下拉菜单
        GUI.backgroundColor = new Color(0.9f, 0.7f, 1.0f); // 紫色背景
        if (GUILayout.Button("预设 ▾", GUILayout.Width(55)))
        {
            ShowPresetDropdown();
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    /// 显示读档下拉菜单（用户存档，Library 目录）
    private void ShowLoadDropdown()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        string shaderName = material.shader.name.Replace("/", "_");
        string folderPath = "Library/VicTools/Ocean/" + shaderName;

        if (!System.IO.Directory.Exists(folderPath))
        {
            System.IO.Directory.CreateDirectory(folderPath);
        }

        string[] files = System.IO.Directory.GetFiles(folderPath, "*.json");

        GenericMenu menu = new GenericMenu();

        if (files.Length == 0)
        {
            menu.AddDisabledItem(new GUIContent("（无预设存档）"));
        }
        else
        {
            foreach (string file in files)
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(file);
                string filePath = file; // 捕获到局部变量供lambda使用
                menu.AddItem(new GUIContent(fileName), false, () =>
                {
                    EditorApplication.delayCall += () => LoadPresetFile(filePath);
                });
            }
        }

        menu.ShowAsContext();
    }

    /// 显示预设下拉菜单（随包分发的预设，Packages 目录）
    private void ShowPresetDropdown()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        string shaderName = material.shader.name.Replace("/", "_");
        string folderPath = "Packages/com.youdoo.victools/Runtime/Shaders/" + shaderName;

        if (!System.IO.Directory.Exists(folderPath))
        {
            System.IO.Directory.CreateDirectory(folderPath);
        }

        string[] files = System.IO.Directory.GetFiles(folderPath, "*.json");

        GenericMenu menu = new GenericMenu();

        if (files.Length == 0)
        {
            menu.AddDisabledItem(new GUIContent("（无预设存档）"));
        }
        else
        {
            foreach (string file in files)
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(file);
                string filePath = file; // 捕获到局部变量供lambda使用
                menu.AddItem(new GUIContent(fileName), false, () =>
                {
                    EditorApplication.delayCall += () => LoadPresetFile(filePath);
                });
            }
        }

        menu.ShowAsContext();
    }

    /// 从预设文件加载
    private void LoadPresetFile(string filePath)
    {
        if (!System.IO.File.Exists(filePath)) return;
        LoadMaterialParametersFromFile(filePath);
    }

    private void DrawDepthGradient()
    {
        GUILayout.Label(HeaderStyle.Rich("1 ▌深度渐变 (Depth Gradient)", HeaderStyle.Base), EditorStyle.Get.BoldLabelRichStyle);
        EditorGUILayout.HelpBox(
            "基于场景深度：越浅越接近浅水色，越深越接近深水色。\n" +
            "需要 URP 开启 Depth Texture（深度纹理）才能正确采样。",
            MessageType.Info);
        m_MaterialEditor.ColorProperty(depthShallowColor, "浅水颜色");
        m_MaterialEditor.ColorProperty(depthDeepColor, "深水颜色");
        m_MaterialEditor.FloatProperty(depthMaxDistance, "深度最大距离");
        m_MaterialEditor.RangeProperty(waterColorVariation, "水色斑驳强度");
        m_MaterialEditor.FloatProperty(waterVariationScale, "斑驳密度 (相对泡沫 Tiling)");
        m_MaterialEditor.FloatProperty(waterVariationSpeed, "斑驳扰动速度");

        // EditorGUILayout.HelpBox(
        //     "水色斑驳：用「海面泡沫」分区的泡沫纹理做低频采样，让浅/深水色交织出青色亮斑与深蓝暗块（参考实拍海面）。\n" +
        //     "采样频率 = 泡沫纹理 Tiling × 斑驳密度，取 0.3~0.6 得到大块斑驳；斑驳强度 0 时关闭该效果。\n" +
        //     "算法：一层噪声做域扭曲打散平铺感，另两层一大一小尺度缓慢反向漂移混合成大小不一的斑块；\n" +
        //     "斑驳值直接偏移深度渐变因子（亮斑趋浅水色、暗斑趋深水色），再叠加同色相轻微明暗起伏。",
        //     MessageType.Info);
    }

    private void DrawStylizedWave()
    {
        GUILayout.Label(HeaderStyle.Rich("2 ▌岸边波浪 (Shore Wave)", HeaderStyle.Sparkle), EditorStyle.Get.BoldLabelRichStyle);

        m_MaterialEditor.TexturePropertySingleLine(new GUIContent("波浪贴图 (R 通道)"), baseMap);
        if (baseMap.textureValue != null)
        {
            EditorGUI.indentLevel++;

            // 显示波浪贴图的 Tiling 和 Offset 参数
            Material material = m_MaterialEditor.target as Material;
            if (material != null)
            {
                Vector2 tiling = material.GetTextureScale("_BaseMap");
                Vector2 offset = material.GetTextureOffset("_BaseMap");

                EditorGUI.BeginChangeCheck();
                tiling = EditorGUILayout.Vector2Field("Tiling", tiling);
                offset = EditorGUILayout.Vector2Field("Offset", offset);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(material, "Change Wave Texture Tiling/Offset");
                    material.SetTextureScale("_BaseMap", tiling);
                    material.SetTextureOffset("_BaseMap", offset);
                    EditorUtility.SetDirty(material);
                }
            }

            EditorGUI.indentLevel--;
        }

        m_MaterialEditor.ColorProperty(waveColor, "岸边浪花颜色 (A = 不透明度)");
        m_MaterialEditor.FloatProperty(waveMaxDistance, "浪花最大距离");
        m_MaterialEditor.RangeProperty(waveFalloff, "岸边波浪外扩 (越小越宽)");
        m_MaterialEditor.FloatProperty(waveSpeed, "岸边波浪速度");
        m_MaterialEditor.FloatProperty(waveCount, "岸边波浪密度");
        m_MaterialEditor.FloatProperty(waveStrength, "岸边波浪强度");
        m_MaterialEditor.RangeProperty(waveCutOff, "岸边波浪裁切");

        // EditorGUILayout.HelpBox(
        //     "算法：贴图 R 通道 × 浪花衰减 × 强度，超过裁切值时二值化为条纹。\n" +
        //     "外扩 < 1 时衰减曲线被压平，浪花带向远海延伸更宽（只作用于强度，不改变条纹间距）；\n" +
        //     "条纹位置用的是「屏幕空间十字 5 点平均 + 线性」的水深，沿纵深方向间距均匀、\n" +
        //     "不会被海底起伏按各方向拉扯得疏密不匀；\n" +
        //     "波浪密度与贴图 Tiling Y 共同控制条纹数量；\n" +
        //     "浪花颜色的 A 通道 = 浪花不透明度：A 越小浪花越淡、越透出水色（A = 1 为完全覆盖），\n" +
        //     "浪花处的水面 alpha 取「水面与浪花 A 取大」，只增不减，不会把水面挖透明。",
        //     MessageType.Info);
    }

    private void DrawSurfaceFoam()
    {
        GUILayout.Label(HeaderStyle.Rich("3 ▌海面泡沫 (Surface Foam)", HeaderStyle.Sparkle), EditorStyle.Get.BoldLabelRichStyle);

        m_MaterialEditor.TexturePropertySingleLine(new GUIContent("泡沫纹理 (R 通道 / Offset = 游走)"), foamDetailMap);
        if (foamDetailMap.textureValue != null)
        {
            DrawTextureTilingOffset("_FoamDetailMap", "Change Foam Texture Tiling/Offset");
        }

        m_MaterialEditor.ColorProperty(foamColor, "泡沫颜色");
        m_MaterialEditor.RangeProperty(foamDetailStrength, "泡沫网强度");
        m_MaterialEditor.RangeProperty(foamDetailCutOff, "泡沫网裁切 (越高网线越细)");
        m_MaterialEditor.RangeProperty(foamDistortStrength, "波浪扭曲泡沫 (用波浪贴图扭动泡沫)");

        // EditorGUILayout.HelpBox(
        //     "海面泡沫：单张纹理双层错位采样，覆盖全海面（不依赖岸边深度）。\n" +
        //     "1）泡沫网：两层取 1-|a-b| 得到交织成网的等值轮廓，裁切越高网线越细 —— 对应参考图的全海面泡沫网；\n" +
        //     "2）片状白沫：取纹理高亮区域，裁切成块白沫。\n" +
        //     "Offset 即游走参数（X/Y 为漂移方向与速度，受时间驱动），Tiling 控制泡沫疏密；\n" +
        //     "该纹理同时用于「深度渐变」分区的水色斑驳低频采样。\n" +
        //     "波浪扭曲：扭曲源是「水色斑驳」的大尺度层输出，取其在 U/V 两个方向的前向差分构成二维扭曲向量\n" +
        //     "（两轴等权、均值为 0），偏移两层泡沫采样坐标，使泡沫网随水团被撕扯扭动；\n" +
        //     "斑驳是低频大块噪声，扭曲柔和、方向随水团流动，泡沫网不会被条纹切成一条条；\n" +
        //     "扭曲随「斑驳速度」漂移，强度 0 时关闭。",
        //     MessageType.Info);
    }

    /// 绘制纹理的 Tiling / Offset 编辑（写回材质，支持 Undo）
    private void DrawTextureTilingOffset(string propertyName, string undoLabel)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null) return;

        EditorGUI.indentLevel++;

        Vector2 tiling = material.GetTextureScale(propertyName);
        Vector2 offset = material.GetTextureOffset(propertyName);

        EditorGUI.BeginChangeCheck();
        tiling = EditorGUILayout.Vector2Field("Tiling", tiling);
        offset = EditorGUILayout.Vector2Field("Offset", offset);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(material, undoLabel);
            material.SetTextureScale(propertyName, tiling);
            material.SetTextureOffset(propertyName, offset);
            EditorUtility.SetDirty(material);
        }

        EditorGUI.indentLevel--;
    }

    private void DrawRenderSettings()
    {
        GUILayout.Label(HeaderStyle.Rich("4 ▌渲染设置 (Render Settings)", HeaderStyle.Render), EditorStyle.Get.BoldLabelRichStyle);

        m_MaterialEditor.ShaderProperty(cullMode, "剔除模式");
        m_MaterialEditor.ShaderProperty(srcBlend, "源混合 (Src Blend)");
        m_MaterialEditor.ShaderProperty(dstBlend, "目标混合 (Dst Blend)");

        m_MaterialEditor.RenderQueueField();
        m_MaterialEditor.EnableInstancingField();
        m_MaterialEditor.DoubleSidedGIField();

        // EditorGUILayout.HelpBox(
        //     "雾：本 Shader 的主 Pass 已接入 URP 雾（multi_compile_fog + MixFog），\n" +
        //     "雾的颜色/模式/起止距离在「Universal Renderer Data → Lighting → Fog」中设置，此面板无独立开关。\n" +
        //     "关闭 Fog 时自动使用无雾变体，混合时只影响 RGB，alpha 与泡沫透明度不受雾影响。",
        //     MessageType.Info);
    }

    /// 获取材质参数存档路径（Library中，用户存读档）
    private string GetPresetPath(string presetName)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return null;

        string shaderName = material.shader.name.Replace("/", "_");
        string folderPath = "Library/VicTools/Ocean/" + shaderName;

        if (!System.IO.Directory.Exists(folderPath))
        {
            System.IO.Directory.CreateDirectory(folderPath);
        }

        return folderPath + "/" + presetName + ".json";
    }

    /// 存档材质参数（含纹理引用）
    private void SaveMaterialParameters()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        string shaderName = material.shader.name.Replace("/", "_");
        string defaultPath = "Library/VicTools/Ocean/" + shaderName;

        // 确保目录存在
        if (!System.IO.Directory.Exists(defaultPath))
        {
            System.IO.Directory.CreateDirectory(defaultPath);
        }

        // 弹出输入框让用户输入存档名称
        string presetName = EditorUtility.SaveFilePanel(
            "保存海洋材质参数存档",
            defaultPath,
            "OceanPreset",
            "json"
        );

        if (string.IsNullOrEmpty(presetName)) return;

        // 提取文件名（不含扩展名）
        string fileName = System.IO.Path.GetFileNameWithoutExtension(presetName);

        SaveMaterialParametersToFile(fileName);
    }

    /// 保存材质参数到指定文件
    private void SaveMaterialParametersToFile(string presetName)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        // 从 MaterialProperty 列表构建类型对照表，修正 ShaderUtil 对 Vector 类型的误判
        var propTypeMap = new System.Collections.Generic.Dictionary<string, MaterialProperty.PropType>();
        if (m_Properties != null)
        {
            foreach (var mp in m_Properties)
            {
                if (mp != null)
                    propTypeMap[mp.name] = mp.type;
            }
        }

        Shader shader = material.shader;
        int propertyCount = ShaderUtil.GetPropertyCount(shader);

        // 手动构建JSON
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        bool first = true;

        for (int i = 0; i < propertyCount; i++)
        {
            string propertyName = ShaderUtil.GetPropertyName(shader, i);
            ShaderUtil.ShaderPropertyType propertyType = ShaderUtil.GetPropertyType(shader, i);

            if (!material.HasProperty(propertyName)) continue;

            // 优先用 MaterialProperty.type 修正类型（某些 Unity 版本 Vector → Float 误判）
            MaterialProperty.PropType mpropType;
            if (propTypeMap.TryGetValue(propertyName, out mpropType)
                && mpropType == MaterialProperty.PropType.Vector)
            {
                propertyType = ShaderUtil.ShaderPropertyType.Vector;
            }

            if (!first) sb.AppendLine(",");
            first = false;

            sb.Append("  \"" + propertyName + "\": ");

            switch (propertyType)
            {
                case ShaderUtil.ShaderPropertyType.Color:
                    Color color = material.GetColor(propertyName);
                    sb.Append($"[{color.r}, {color.g}, {color.b}, {color.a}]");
                    break;

                case ShaderUtil.ShaderPropertyType.Vector:
                    Vector4 vector = material.GetVector(propertyName);
                    sb.Append($"[{vector.x}, {vector.y}, {vector.z}, {vector.w}]");
                    break;

                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:
                    sb.Append(material.GetFloat(propertyName).ToString());
                    break;

                case ShaderUtil.ShaderPropertyType.TexEnv:
                    Texture tex = material.GetTexture(propertyName);
                    string texPath = "";
                    if (tex != null)
                    {
                        texPath = AssetDatabase.GetAssetPath(tex);
                    }
                    Vector2 tiling = material.GetTextureScale(propertyName);
                    Vector2 offset = material.GetTextureOffset(propertyName);
                    sb.Append("{");
                    sb.Append($"\"path\": \"{EscapeJsonString(texPath)}\", ");
                    sb.Append($"\"tiling\": [{tiling.x}, {tiling.y}], ");
                    sb.Append($"\"offset\": [{offset.x}, {offset.y}]");
                    sb.Append("}");
                    break;
            }
        }

        sb.AppendLine();
        sb.AppendLine("}");

        // 保存到文件
        string path = GetPresetPath(presetName);
        System.IO.File.WriteAllText(path, sb.ToString());

        Debug.Log($"海洋材质参数已保存到: {path}（含纹理引用）");
    }

    /// JSON字符串转义（处理路径中的反斜杠）
    private static string EscapeJsonString(string str)
    {
        if (string.IsNullOrEmpty(str)) return "";
        return str.Replace("\\", "/");
    }

    /// 从文件加载材质参数
    private void LoadMaterialParametersFromFile(string filePath, bool isReset = false)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        if (!System.IO.File.Exists(filePath))
        {
            Debug.LogWarning($"存档文件不存在: {filePath}");
            return;
        }

        // 读取JSON
        string json = System.IO.File.ReadAllText(filePath);

        // 检测存档中是否包含纹理数据
        bool hasTextureData = json.Contains("\"path\":");
        bool loadTextures = false;

        if (hasTextureData && !isReset)
        {
            loadTextures = EditorUtility.DisplayDialog("读取纹理",
                "存档中包含纹理贴图引用，是否同时读取纹理？\n\n选择「是」将还原纹理贴图及Tiling/Offset\n选择「否」仅读取数值参数",
                "是，读取纹理", "否，仅参数");
        }

        // 记录撤销操作
        Undo.RecordObject(material, "Load Ocean Material Parameters");

        // 构建 MaterialProperty 类型对照表，用于区分配 Color/Vector
        var propTypeMap = new System.Collections.Generic.Dictionary<string, MaterialProperty.PropType>();
        if (m_Properties != null)
        {
            foreach (var mp in m_Properties)
            {
                if (mp != null)
                    propTypeMap[mp.name] = mp.type;
            }
        }

        // 手动解析JSON
        var lines = json.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);

        // 收集缺失纹理的警告信息
        System.Collections.Generic.List<string> missingTextures = new System.Collections.Generic.List<string>();

        for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            string line = lines[lineIdx];
            if (!line.Contains(":")) continue;

            string trimmed = line.Trim().TrimEnd(',');
            int colonIndex = trimmed.IndexOf(':');
            if (colonIndex < 0) continue;

            string propertyName = trimmed.Substring(0, colonIndex).Trim().Trim('"');
            string valueStr = trimmed.Substring(colonIndex + 1).Trim();

            if (!material.HasProperty(propertyName)) continue;

            // 纹理类型：值以 { 开头
            if (valueStr.StartsWith("{"))
            {
                // 拼接多行直到找到 }
                string texJson = valueStr;
                while (!texJson.Contains("}") && lineIdx + 1 < lines.Length)
                {
                    lineIdx++;
                    texJson += lines[lineIdx];
                }

                // 纹理贴图本身：仅在用户选择加载纹理时还原
                if (loadTextures)
                {
                    // 主贴图已有时不替换
                    bool isMainTex = (propertyName == "_BaseMap" || propertyName == "_MainTex");
                    if (isMainTex && material.GetTexture(propertyName) != null)
                    {
                        // 跳过主贴图替换，但仍读取 Tiling/Offset
                    }
                    else
                    {
                        // 解析纹理路径
                        string texPath = ExtractJsonStringValue(texJson, "path");

                        if (!string.IsNullOrEmpty(texPath))
                        {
                            Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                            if (tex != null)
                            {
                                material.SetTexture(propertyName, tex);
                            }
                            else
                            {
                                missingTextures.Add($"  {propertyName}: {texPath}");
                            }
                        }
                        else
                        {
                            // 路径为空，清除纹理
                            material.SetTexture(propertyName, null);
                        }
                    }
                }

                // Tiling/Offset 属于数值参数，无论是否加载纹理都应还原
                float[] tilingValues = ExtractJsonFloatArray(texJson, "tiling");
                if (tilingValues != null && tilingValues.Length == 2)
                {
                    material.SetTextureScale(propertyName, new Vector2(tilingValues[0], tilingValues[1]));
                }

                float[] offsetValues = ExtractJsonFloatArray(texJson, "offset");
                if (offsetValues != null && offsetValues.Length == 2)
                {
                    material.SetTextureOffset(propertyName, new Vector2(offsetValues[0], offsetValues[1]));
                }

                continue;
            }

            // 数组类型（Color或Vector）
            if (valueStr.StartsWith("["))
            {
                valueStr = valueStr.Trim('[', ']');
                string[] parts = valueStr.Split(',');
                if (parts.Length == 4)
                {
                    float[] values = new float[4];
                    for (int i = 0; i < 4; i++)
                    {
                        float.TryParse(parts[i].Trim(), out values[i]);
                    }

                    // 根据 MaterialProperty.type 区分 Vector 和 Color，避免误用 SetColor
                    MaterialProperty.PropType loadPropType;
                    bool isVector = propTypeMap.TryGetValue(propertyName, out loadPropType)
                                 && loadPropType == MaterialProperty.PropType.Vector;

                    if (isVector)
                    {
                        material.SetVector(propertyName, new Vector4(values[0], values[1], values[2], values[3]));
                    }
                    else
                    {
                        try
                        {
                            material.SetColor(propertyName, new Color(values[0], values[1], values[2], values[3]));
                        }
                        catch
                        {
                            material.SetVector(propertyName, new Vector4(values[0], values[1], values[2], values[3]));
                        }
                    }
                }
            }
            else
            {
                // Float类型
                if (float.TryParse(valueStr, out float floatValue))
                {
                    material.SetFloat(propertyName, floatValue);
                }
            }
        }

        // 显示缺失纹理警告
        if (missingTextures.Count > 0)
        {
            string msg = "以下纹理资源不存在，已跳过：\n\n" + string.Join("\n", missingTextures);
            EditorUtility.DisplayDialog("纹理缺失警告", msg, "确定");
            Debug.LogWarning("[CustomOceanGUI] 读档时部分纹理资源不存在:\n" + string.Join("\n", missingTextures));
        }

        // 同步 shader_feature toggle 对应的 keyword
        // 注意：Custom_Ocean 当前无 toggle keyword，此方法保留供后续扩展
        SyncShaderKeywords(material);

        EditorUtility.SetDirty(material);
        if (m_MaterialEditor != null) m_MaterialEditor.Repaint();
        SceneView.RepaintAll();

        string texInfo = loadTextures ? "（含纹理）" : "（仅参数）";
        Debug.Log($"海洋材质参数已从存档加载{texInfo}: {filePath}");
    }

    /// 从简易JSON中提取字符串值
    private static string ExtractJsonStringValue(string json, string key)
    {
        string search = "\"" + key + "\":";
        int idx = json.IndexOf(search);
        if (idx < 0) return null;

        int start = json.IndexOf('"', idx + search.Length);
        if (start < 0) return null;
        int end = json.IndexOf('"', start + 1);
        if (end < 0) return null;

        return json.Substring(start + 1, end - start - 1);
    }

    /// 从简易JSON中提取浮点数组
    private static float[] ExtractJsonFloatArray(string json, string key)
    {
        string search = "\"" + key + "\":";
        int idx = json.IndexOf(search);
        if (idx < 0) return null;

        int start = json.IndexOf('[', idx);
        if (start < 0) return null;
        int end = json.IndexOf(']', start);
        if (end < 0) return null;

        string arrayStr = json.Substring(start + 1, end - start - 1);
        string[] parts = arrayStr.Split(',');
        float[] result = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            float.TryParse(parts[i].Trim(), out result[i]);
        }
        return result;
    }

    /// 同步所有 shader_feature toggle 对应的 keyword
    /// Custom_Ocean 当前无 toggle keyword；读档后如新增 toggle 在此登记
    private void SyncShaderKeywords(Material material)
    {
        var toggleKeywords = new System.Collections.Generic.Dictionary<string, string>
        {
            // 示例：{ "_UseXXX", "_USEXXX" },
        };

        foreach (var pair in toggleKeywords)
        {
            if (!material.HasProperty(pair.Key)) continue;

            bool enabled = material.GetFloat(pair.Key) > 0.5f;
            if (enabled)
                material.EnableKeyword(pair.Value);
            else
                material.DisableKeyword(pair.Value);
        }
    }

    /// 重置材质参数为默认值（使用Default存档或shader默认值）
    private void ResetMaterialParameters()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        string defaultPresetPath = GetPresetPath("Default");

        // 检查Default存档是否存在
        if (System.IO.File.Exists(defaultPresetPath))
        {
            // 使用Default存档
            if (EditorUtility.DisplayDialog("重置参数",
                "将使用Default存档重置参数。\n\n注意：纹理不会被重置。",
                "确定", "取消"))
            {
                LoadMaterialParametersFromFile(defaultPresetPath, true);
            }
        }
        else
        {
            // Default存档不存在，创建它
            if (EditorUtility.DisplayDialog("创建Default存档",
                "Default存档不存在，将使用Shader默认值创建Default存档。",
                "确定", "取消"))
            {
                // 创建临时材质以获取shader默认值
                Material tempMaterial = new Material(material.shader);

                Shader shader = material.shader;
                int propertyCount = ShaderUtil.GetPropertyCount(shader);

                // 手动构建JSON（与SaveMaterialParametersToFile一致的格式）
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("{");
                bool first = true;

                for (int i = 0; i < propertyCount; i++)
                {
                    string propertyName = ShaderUtil.GetPropertyName(shader, i);
                    ShaderUtil.ShaderPropertyType propertyType = ShaderUtil.GetPropertyType(shader, i);

                    if (!tempMaterial.HasProperty(propertyName)) continue;

                    if (!first) sb.AppendLine(",");
                    first = false;

                    sb.Append("  \"" + propertyName + "\": ");

                    switch (propertyType)
                    {
                        case ShaderUtil.ShaderPropertyType.Color:
                            Color c = tempMaterial.GetColor(propertyName);
                            sb.Append($"[{c.r}, {c.g}, {c.b}, {c.a}]");
                            break;
                        case ShaderUtil.ShaderPropertyType.Vector:
                            Vector4 v = tempMaterial.GetVector(propertyName);
                            sb.Append($"[{v.x}, {v.y}, {v.z}, {v.w}]");
                            break;
                        case ShaderUtil.ShaderPropertyType.Float:
                        case ShaderUtil.ShaderPropertyType.Range:
                            sb.Append(tempMaterial.GetFloat(propertyName).ToString());
                            break;
                        case ShaderUtil.ShaderPropertyType.TexEnv:
                            sb.Append("{\"path\": \"\", \"tiling\": [1, 1], \"offset\": [0, 0]}");
                            break;
                    }
                }

                sb.AppendLine();
                sb.AppendLine("}");

                System.IO.File.WriteAllText(defaultPresetPath, sb.ToString());

                Object.DestroyImmediate(tempMaterial);

                Debug.Log($"海洋材质Default存档已创建: {defaultPresetPath}");

                // 加载Default存档（重置模式，不弹纹理提示）
                LoadMaterialParametersFromFile(defaultPresetPath, true);
            }
        }
    }
}
