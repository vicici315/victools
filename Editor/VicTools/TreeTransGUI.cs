// TreeTransGUI 2.0  参数区改为按章节分组绘制（参考 Glass_carWindowGUI 风格）
//                              - 每个章节用 GUILayout.VerticalScope(EditorStyles.helpBox) 包裹成浅色矩形
//                              - 章节标题："{N} {中文} ({English})" + HeaderStyle.{Base|Lighting|...|Render} 配色
//                              - toggle 章节（Wind）显式监听 + keyword 同步（_UseWind <-> _WIND）
//                              - 渲染设置章节末尾追加 RenderQueueField / EnableInstancingField / DoubleSidedGIField
// TreeTransGUI 1.1  在顶部 4 按钮之后追加材质参数区（基类 PropertiesDefaultGUI）
// TreeTransGUI 1.0  初版：CustomEditor 脚本
//                              - 仅暴露 4 个功能按钮：存档 / 读档 / 预设 / 重置
//                              - 参数绘制：刻意保留为空，统一交给 Unity 默认 Inspector
//                                （按住 Alt 键点击材质可看到默认面板；本 GUI 不遮挡自定义操作）
//                              - 存档 / 读档 / 预设 / 重置 实现完全对齐 TransCutoutGUI，
//                                仅分类目录与 shader 名称不同：
//                                  存档 / 读档 路径：Library/VicTools/Tree/<shaderName>/<preset>.json
//                                  预设       路径：Packages/com.youdoo.victools/Runtime/Shaders/<shaderName>/<preset>.json
//                              - 重置逻辑：Default.json 存在 → 直接应用；不存在 → 用 Shader 默认值自动生成后应用
//                              - shader 名处理："Custom/Tree_Trans" → "Custom_Tree_Trans"
//                              - 同步 _UseWind (toggle) ↔ _WIND (keyword)，兜底 Unity Inspector 编辑器之外的修改路径

using UnityEngine;
using UnityEditor;
using VicTools;

public class TreeTransGUI : ShaderGUI
{
    private MaterialEditor m_MaterialEditor;
    private MaterialProperty[] m_Properties;

    // 存档 / 读档 根目录（Library/VicTools/ 下分组）
    private const string SaveFolderBase   = "Library/VicTools/Tree";
    // 预设根目录（包内只读）
    private const string PresetFolderBase = "Packages/com.youdoo.victools/Runtime/Shaders";

    // ── 缓存材质属性（按 Tree_Trans.shader Properties 块顺序） ──
    // [Header(Base)]
    private MaterialProperty baseColor;
    private MaterialProperty baseMap;
    private MaterialProperty cutoff;
    // [Header(Lighting)]
    private MaterialProperty halfLambert;
    // [Header(Virtual Shadow)]
    private MaterialProperty shadowColor;
    private MaterialProperty shadowStrength;
    private MaterialProperty shadowSoftness;
    private MaterialProperty virtualShadowBias;
    private MaterialProperty shadowBrightness;
    private MaterialProperty shadowNormalMap;
    private MaterialProperty shadowNormalScale;
    // [Header(Ramp Gradient)]
    private MaterialProperty rampMap;
    private MaterialProperty rampStrength;
    private MaterialProperty rampRow;
    private MaterialProperty rampOffset;
    private MaterialProperty rampHeight;
    // [Header(Wind)]
    private MaterialProperty useWind;
    private MaterialProperty windSpeed;
    private MaterialProperty windStrength;
    private MaterialProperty windDirection;
    private MaterialProperty windRadius;
    private MaterialProperty windNoiseTex;
    private MaterialProperty windNoiseScale;
    private MaterialProperty windNoiseStrength;
    // [Header(Options)]
    private MaterialProperty cullMode;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        m_MaterialEditor = materialEditor;
        m_Properties = properties;

        FindProperties();

        // 顶部 4 按钮
        DrawGlobalSettings();

        EditorGUILayout.Space(4);

        // 6 个章节（按 shader Properties [Header(...)] 顺序）
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawBaseSection();
        }
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawLightingSection();
        }
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawVirtualShadowSection();
        }
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawRampGradientSection();
        }
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawWindSection();
        }
        DrawRenderSettingsSection();
    }

    private void FindProperties()
    {
        // [Header(Base)]
        baseColor        = FindProperty("_BaseColor",        m_Properties);
        baseMap          = FindProperty("_BaseMap",          m_Properties);
        cutoff           = FindProperty("_Cutoff",           m_Properties);
        // [Header(Lighting)]
        halfLambert      = FindProperty("_HalfLambert",      m_Properties);
        // [Header(Virtual Shadow)]
        shadowColor      = FindProperty("_ShadowColor",      m_Properties);
        shadowStrength   = FindProperty("_ShadowStrength",   m_Properties);
        shadowSoftness   = FindProperty("_ShadowSoftness",   m_Properties);
        virtualShadowBias= FindProperty("_VirtualShadowBias",m_Properties);
        shadowBrightness = FindProperty("_ShadowBrightness", m_Properties);
        shadowNormalMap  = FindProperty("_ShadowNormalMap",  m_Properties, false); // [Normal] 标注，仍按 TexEnv 处理
        shadowNormalScale= FindProperty("_ShadowNormalScale",m_Properties);
        // [Header(Ramp Gradient)]
        rampMap          = FindProperty("_RampMap",          m_Properties);
        rampStrength     = FindProperty("_RampStrength",     m_Properties);
        rampRow          = FindProperty("_RampRow",          m_Properties);
        rampOffset       = FindProperty("_RampOffset",       m_Properties);
        rampHeight       = FindProperty("_RampHeight",       m_Properties);
        // [Header(Wind)]
        useWind          = FindProperty("_UseWind",          m_Properties);
        windSpeed        = FindProperty("_WindSpeed",        m_Properties);
        windStrength     = FindProperty("_WindStrength",     m_Properties);
        windDirection    = FindProperty("_WindDirection",    m_Properties);
        windRadius       = FindProperty("_WindRadius",       m_Properties);
        windNoiseTex     = FindProperty("_WindNoiseTex",     m_Properties);
        windNoiseScale   = FindProperty("_WindNoiseScale",   m_Properties);
        windNoiseStrength= FindProperty("_WindNoiseStrength",m_Properties);
        // [Header(Options)]
        cullMode         = FindProperty("_Cull",             m_Properties);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 全局工具栏：存档 / 读档 / 重置 / 预设
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void DrawGlobalSettings()
    {
        EditorGUILayout.BeginHorizontal();

        GUILayout.Label(HeaderStyle.Rich("全局设置", HeaderStyle.HeaderTitle), EditorStyle.Get.BoldLabelRichStyle);

        GUI.backgroundColor = new Color(0.3f, 0.8f, 1.0f);
        if (GUILayout.Button(new GUIContent("存档", "保存当前材质参数到本地 JSON 存档"), GUILayout.Width(50)))
        {
            EditorApplication.delayCall += SaveMaterialParameters;
        }

        GUI.backgroundColor = new Color(0.5f, 1.0f, 0.5f);
        if (GUILayout.Button(new GUIContent("读档 ▾", "从本地存档中选择并应用"), GUILayout.Width(55)))
        {
            ShowLoadDropdown();
        }

        GUI.backgroundColor = new Color(1.0f, 0.8f, 0.3f);
        if (GUILayout.Button(new GUIContent("重置参数", "使用 Default 存档重置参数（不动纹理）"), GUILayout.Width(60)))
        {
            EditorApplication.delayCall += ResetMaterialParameters;
        }

        GUI.backgroundColor = new Color(0.9f, 0.7f, 1.0f);
        if (GUILayout.Button(new GUIContent("预设 ▾", "从包内预设中选择并应用"), GUILayout.Width(55)))
        {
            ShowPresetDropdown();
        }

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 章节 1：基础属性 (Base)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void DrawBaseSection()
    {
        GUILayout.Label(HeaderStyle.Rich("1 \u258c Base 属性 (Base Properties)", HeaderStyle.Base), EditorStyle.Get.BoldLabelRichStyle);
        if (baseMap != null)
        {
            m_MaterialEditor.TexturePropertySingleLine(new GUIContent("主纹理 (RGB=A)"), baseMap);
        }
        if (baseColor != null) m_MaterialEditor.ColorProperty(baseColor, "基础颜色 (Base Color)");
        if (cutoff != null)    m_MaterialEditor.RangeProperty(cutoff, "透明阈值 (Alpha Cutoff)");
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 章节 2：光照参数 (Lighting)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void DrawLightingSection()
    {
        GUILayout.Label(HeaderStyle.Rich("2 \u258c 光照参数 (Lighting)", HeaderStyle.Lighting), EditorStyle.Get.BoldLabelRichStyle);
        if (halfLambert != null)
            m_MaterialEditor.RangeProperty(halfLambert, "Half Lambert (半 Lambert)");
        EditorGUILayout.HelpBox(
            "Half Lambert 公式：NdotL * (1 - k) + k\n" +
            "k = _HalfLambert，0 = Lambert，1 = 均匀（无光照）。",
            MessageType.Info);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 章节 3：虚拟阴影 (Virtual Shadow)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void DrawVirtualShadowSection()
    {
        GUILayout.Label(HeaderStyle.Rich("3 \u258c 虚拟阴影 (Virtual Shadow)", HeaderStyle.Lighting), EditorStyle.Get.BoldLabelRichStyle);
        if (shadowColor != null)      m_MaterialEditor.ColorProperty(shadowColor, "阴影颜色 (Shadow Color)");
        if (shadowStrength != null)   m_MaterialEditor.RangeProperty(shadowStrength, "阴影强度 (Strength)");
        if (shadowSoftness != null)   m_MaterialEditor.RangeProperty(shadowSoftness, "阴影软度 (Softness)");
        if (virtualShadowBias != null)m_MaterialEditor.RangeProperty(virtualShadowBias, "阴影偏移 (Bias)");
        if (shadowBrightness != null) m_MaterialEditor.RangeProperty(shadowBrightness, "阴影亮度 (Brightness)");

        EditorGUILayout.Space(4);
        if (shadowNormalMap != null)
        {
            m_MaterialEditor.TexturePropertySingleLine(new GUIContent("阴影法线贴图"), shadowNormalMap);
        }
        if (shadowNormalScale != null)
            m_MaterialEditor.RangeProperty(shadowNormalScale, "阴影法线强度 (Normal Scale)");

        EditorGUILayout.HelpBox(
            "虚拟阴影算法：NdotL + Bias -> smoothstep -> ShadowColor / White 双层 lerp。\n" +
            "配合 ShadowNormalMap 可让叶子 / 树皮等细节方向产生正确的明暗。",
            MessageType.Info);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 章节 4：高度渐变 (Ramp Gradient)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void DrawRampGradientSection()
    {
        GUILayout.Label(HeaderStyle.Rich("4 \u258c 高度渐变 (Ramp Gradient)", HeaderStyle.Fresnel), EditorStyle.Get.BoldLabelRichStyle);
        if (rampMap != null)
            m_MaterialEditor.TexturePropertySingleLine(new GUIContent("渐变贴图 (X=高度,Y=预设行)"), rampMap);
        if (rampStrength != null) m_MaterialEditor.RangeProperty(rampStrength, "渐变强度 (Strength)");
        if (rampRow != null)      m_MaterialEditor.RangeProperty(rampRow, "渐变行选择 (Row)");
        if (rampOffset != null)   m_MaterialEditor.RangeProperty(rampOffset, "渐变底部偏移 (Offset)");
        if (rampHeight != null)   m_MaterialEditor.RangeProperty(rampHeight, "渐变高度范围 (Height)");
        EditorGUILayout.HelpBox(
            "Ramp 公式：saturate((posOS.y - _RampOffset) / _RampHeight)\n" +
            "RampStrength = 0 时不应用渐变；RampRow 选择图集行；_RampMap [NoScaleOffset] 不接受 tiling/offset。",
            MessageType.Info);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 章节 5：风力 (Wind) — 含 _UseWind toggle 与 _WIND keyword 同步
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void DrawWindSection()
    {
        if (useWind == null) return;

        // 显式监听 toggle：强制同步 keyword（防止 [Toggle(_WIND)] 自动处理在某些 Editor 场景下失效）
        EditorGUI.BeginChangeCheck();
        HeaderStyle.ShaderProperty(m_MaterialEditor, useWind, "5 \u258c 风力 (Wind)", HeaderStyle.Interaction);
        if (EditorGUI.EndChangeCheck())
        {
            foreach (var obj in m_MaterialEditor.targets)
            {
                Material mat = obj as Material;
                if (mat == null) continue;
                if (useWind.floatValue > 0.5f)
                    mat.EnableKeyword("_WIND");
                else
                    mat.DisableKeyword("_WIND");
            }
        }

        if (useWind.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "风力算法：径向权重 \u00d7 (主摆动 + 谐波摆动 + 噪声纹理抖动)\n" +
                "全部沿 _WindDirection 方向施加位移。_WindNoiseTex 可空，为空时只有正弦摆动。",
                MessageType.Info);

            if (windSpeed != null)         m_MaterialEditor.RangeProperty(windSpeed, "风速 (Speed)");
            if (windStrength != null)      m_MaterialEditor.RangeProperty(windStrength, "风力强度 (Strength)");
            if (windDirection != null)     m_MaterialEditor.VectorProperty(windDirection, "风向 (XZ Plane, W=相位)");
            if (windRadius != null)        m_MaterialEditor.RangeProperty(windRadius, "风力半径 (Radius)");
            if (windNoiseTex != null)
                m_MaterialEditor.TexturePropertySingleLine(new GUIContent("噪声贴图 (R,可选)"), windNoiseTex);
            if (windNoiseScale != null)    m_MaterialEditor.RangeProperty(windNoiseScale, "噪声缩放 (Scale)");
            if (windNoiseStrength != null) m_MaterialEditor.RangeProperty(windNoiseStrength, "噪声强度 (Strength)");

            EditorGUI.indentLevel--;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 章节 6：渲染设置 (Render Settings)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void DrawRenderSettingsSection()
    {
        GUILayout.Label(HeaderStyle.Rich("6 \u258c 渲染设置 (Render Settings)", HeaderStyle.Render), EditorStyle.Get.BoldLabelRichStyle);
        if (cullMode != null) m_MaterialEditor.ShaderProperty(cullMode, "剔除模式 (Cull Mode)");
        m_MaterialEditor.RenderQueueField();
        m_MaterialEditor.EnableInstancingField();
        m_MaterialEditor.DoubleSidedGIField();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 路径工具
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    /// shader.name 例如 "Custom/Tree_Trans" → 文件夹安全名 "Custom_Tree_Trans"
    private static string GetShaderFolderName(Material material)
    {
        return (material != null && material.shader != null)
            ? material.shader.name.Replace("/", "_")
            : null;
    }

    /// 取得指定 preset 的完整存档路径（保证父目录存在）
    private string GetPresetPath(string presetName)
    {
        string folderPath = GetSaveFolder();
        if (string.IsNullOrEmpty(folderPath)) return null;
        return folderPath + "/" + presetName + ".json";
    }

    /// 取得当前 shader 的存档目录（保证存在），用于"存档 / 读档"
    private string GetSaveFolder()
    {
        Material material = m_MaterialEditor.target as Material;
        string shaderName = GetShaderFolderName(material);
        if (string.IsNullOrEmpty(shaderName)) return null;

        string folderPath = SaveFolderBase + "/" + shaderName;
        if (!System.IO.Directory.Exists(folderPath))
            System.IO.Directory.CreateDirectory(folderPath);
        return folderPath;
    }

    /// 取得当前 shader 的预设目录（在包内），用于"预设"
    private string GetPresetFolder()
    {
        Material material = m_MaterialEditor.target as Material;
        string shaderName = GetShaderFolderName(material);
        if (string.IsNullOrEmpty(shaderName)) return null;

        string folderPath = PresetFolderBase + "/" + shaderName;
        if (!System.IO.Directory.Exists(folderPath))
            System.IO.Directory.CreateDirectory(folderPath);
        return folderPath;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 存档
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void SaveMaterialParameters()
    {
        string defaultFolder = GetSaveFolder();
        if (string.IsNullOrEmpty(defaultFolder)) return;

        string presetPath = EditorUtility.SaveFilePanel(
            "保存 Tree_Trans 材质参数存档",
            defaultFolder,
            "Tree_TransPreset",
            "json"
        );

        if (string.IsNullOrEmpty(presetPath)) return;

        string fileName = System.IO.Path.GetFileNameWithoutExtension(presetPath);
        SaveMaterialParametersToFile(fileName);
    }

    private void SaveMaterialParametersToFile(string presetName)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        // 构建 MaterialProperty 类型对照表（用于修正 ShaderUtil 对 Vector/TexEnv 的不准确判断）
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

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        bool first = true;

        for (int i = 0; i < propertyCount; i++)
        {
            string propertyName = ShaderUtil.GetPropertyName(shader, i);
            ShaderUtil.ShaderPropertyType propertyType = ShaderUtil.GetPropertyType(shader, i);

            if (!material.HasProperty(propertyName)) continue;

            // 修正 Vector：ShaderUtil 把 _WindDirection (Vector) 视作 Vector
            MaterialProperty.PropType mpType;
            if (propTypeMap.TryGetValue(propertyName, out mpType)
                && mpType == MaterialProperty.PropType.Vector)
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
                        texPath = AssetDatabase.GetAssetPath(tex);

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

        string path = GetPresetPath(presetName);
        if (string.IsNullOrEmpty(path)) return;
        System.IO.File.WriteAllText(path, sb.ToString());

        Debug.Log($"Tree_Trans 材质参数已保存到: {path}（含纹理引用）");
    }

    private static string EscapeJsonString(string str)
    {
        if (string.IsNullOrEmpty(str)) return "";
        return str.Replace("\\", "/");
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 读档 / 预设下拉（共用下拉组件）
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void ShowLoadDropdown()    => ShowFolderDropdown(GetSaveFolder(),    "（无存档）");
    private void ShowPresetDropdown()  => ShowFolderDropdown(GetPresetFolder(),  "（无预设）");

    private void ShowFolderDropdown(string folderPath, string emptyLabel)
    {
        if (string.IsNullOrEmpty(folderPath)) return;

        string[] files = System.IO.Directory.GetFiles(folderPath, "*.json");
        GenericMenu menu = new GenericMenu();

        if (files.Length == 0)
        {
            menu.AddDisabledItem(new GUIContent(emptyLabel));
        }
        else
        {
            foreach (string file in files)
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(file);
                string filePath = file;
                menu.AddItem(new GUIContent(fileName), false, () =>
                {
                    EditorApplication.delayCall += () => LoadPresetFile(filePath);
                });
            }
        }

        menu.ShowAsContext();
    }

    private void LoadPresetFile(string filePath)
    {
        if (!System.IO.File.Exists(filePath)) return;
        LoadMaterialParametersFromFile(filePath, isReset: false);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 通用读档实现（也用于"重置"路径，isReset 决定是否替换纹理）
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void LoadMaterialParametersFromFile(string filePath, bool isReset)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        if (!System.IO.File.Exists(filePath))
        {
            Debug.LogWarning($"存档文件不存在: {filePath}");
            return;
        }

        string json = System.IO.File.ReadAllText(filePath);

        Undo.RecordObject(material, isReset ? "Reset Tree_Trans Material Parameters" : "Load Tree_Trans Material Parameters");

        var propTypeMap = new System.Collections.Generic.Dictionary<string, MaterialProperty.PropType>();
        if (m_Properties != null)
        {
            foreach (var mp in m_Properties)
            {
                if (mp != null)
                    propTypeMap[mp.name] = mp.type;
            }
        }

        var lines = json.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        var missingTextures = new System.Collections.Generic.List<string>();

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

            // 纹理项：{ "path": ..., "tiling": ..., "offset": ... }
            if (valueStr.StartsWith("{"))
            {
                string texJson = valueStr;
                while (!texJson.Contains("}") && lineIdx + 1 < lines.Length)
                {
                    lineIdx++;
                    texJson += lines[lineIdx];
                }

                // 重置路径不替换纹理；读档路径按存档 path 加载
                if (!isReset)
                {
                    string texPath = ExtractJsonStringValue(texJson, "path");
                    if (!string.IsNullOrEmpty(texPath))
                    {
                        Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                        if (tex != null)
                            material.SetTexture(propertyName, tex);
                        else
                            missingTextures.Add($"  {propertyName}: {texPath}");
                    }
                    else
                    {
                        // 路径为空：清除该纹理（让默认值生效）
                        material.SetTexture(propertyName, null);
                    }
                }

                // tiling/offset 总是按存档同步
                float[] tilingValues = ExtractJsonFloatArray(texJson, "tiling");
                if (tilingValues != null && tilingValues.Length == 2)
                    material.SetTextureScale(propertyName, new Vector2(tilingValues[0], tilingValues[1]));

                float[] offsetValues = ExtractJsonFloatArray(texJson, "offset");
                if (offsetValues != null && offsetValues.Length == 2)
                    material.SetTextureOffset(propertyName, new Vector2(offsetValues[0], offsetValues[1]));

                continue;
            }

            // [r, g, b, a] 项：Vector 或 Color
            if (valueStr.StartsWith("["))
            {
                valueStr = valueStr.Trim('[', ']');
                string[] parts = valueStr.Split(',');
                if (parts.Length == 4)
                {
                    float[] values = new float[4];
                    for (int i = 0; i < 4; i++)
                        float.TryParse(parts[i].Trim(), out values[i]);

                    MaterialProperty.PropType loadPropType;
                    bool isVector = propTypeMap.TryGetValue(propertyName, out loadPropType)
                                 && loadPropType == MaterialProperty.PropType.Vector;

                    if (isVector)
                        material.SetVector(propertyName, new Vector4(values[0], values[1], values[2], values[3]));
                    else
                    {
                        try { material.SetColor(propertyName, new Color(values[0], values[1], values[2], values[3])); }
                        catch { material.SetVector(propertyName, new Vector4(values[0], values[1], values[2], values[3])); }
                    }
                }
            }
            // 标量项
            else
            {
                if (float.TryParse(valueStr, out float floatValue))
                    material.SetFloat(propertyName, floatValue);
            }
        }

        if (missingTextures.Count > 0)
        {
            string msg = "以下纹理资源不存在，已跳过：\n\n" + string.Join("\n", missingTextures);
            EditorUtility.DisplayDialog("纹理缺失警告", msg, "确定");
            Debug.LogWarning("[TreeTransGUI] 读档时部分纹理资源不存在:\n" + string.Join("\n", missingTextures));
        }

        // 同步 _UseWind (toggle) ↔ _WIND (keyword)。
        // Unity Inspector 对 [Toggle(_WIND)] 属性会自动同步；
        // 这里主动做一次是为了兜底"读档绕过 Inspector" / "脚本直接赋值"的边界情况。
        SyncUseWindKeyword(material);

        EditorUtility.SetDirty(material);
        if (m_MaterialEditor != null) m_MaterialEditor.Repaint();
        SceneView.RepaintAll();

        string texInfo = (!isReset && json.Contains("\"path\":")) ? "（含纹理）" : "（仅参数）";
        Debug.Log($"Tree_Trans 材质参数已加载{texInfo}: {filePath}");
    }

    /// _UseWind → _WIND 关键字同步（shader_feature_local _WIND 的 toggle 配对）
    private static void SyncUseWindKeyword(Material material)
    {
        if (material == null || !material.HasProperty("_UseWind")) return;
        bool enabled = material.GetFloat("_UseWind") > 0.5f;
        if (enabled) material.EnableKeyword("_WIND");
        else        material.DisableKeyword("_WIND");
    }

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
            float.TryParse(parts[i].Trim(), out result[i]);
        return result;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 重置
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void ResetMaterialParameters()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        string defaultPresetPath = GetPresetPath("Default");
        if (string.IsNullOrEmpty(defaultPresetPath)) return;

        if (System.IO.File.Exists(defaultPresetPath))
        {
            if (EditorUtility.DisplayDialog("重置参数",
                "将使用 Default 存档重置参数。\n\n注意：纹理不会被替换。",
                "确定", "取消"))
            {
                LoadMaterialParametersFromFile(defaultPresetPath, isReset: true);
            }
        }
        else
        {
            if (EditorUtility.DisplayDialog("创建 Default 存档",
                "Default 存档不存在，将使用 Shader 默认值创建 Default 存档后再重置。",
                "确定", "取消"))
            {
                CreateDefaultPresetFromShaderDefaults(defaultPresetPath, material);
                LoadMaterialParametersFromFile(defaultPresetPath, isReset: true);
            }
        }
    }

    /// 用 Shader 默认值（即新建临时 Material）创建 Default.json 存档
    private void CreateDefaultPresetFromShaderDefaults(string path, Material material)
    {
        Material tempMaterial = new Material(material.shader);
        Shader shader = material.shader;
        int propertyCount = ShaderUtil.GetPropertyCount(shader);

        var sb = new System.Text.StringBuilder();
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

        System.IO.File.WriteAllText(path, sb.ToString());

        Object.DestroyImmediate(tempMaterial);

        Debug.Log($"Tree_Trans 材质 Default 存档已创建: {path}");
    }
}
