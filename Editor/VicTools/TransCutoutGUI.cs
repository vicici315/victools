// TransCutoutGUI 1.1  DitherSize 自动吸附到 2 的次方（2/4/8/16）：
//                              - IntSlider 拖动后立刻 SnapToNearestPowerOfTwo 吸附到有效值
//                              - 有效值表：{ 2, 4, 8, 16 }
// TransCutoutGUI 1.0  初版：CustomEditor 脚本
//                              - 按章节分组绘制：透明溶解 / Dither / 粒子 Alpha / 粒子 Color / 混合 / 渲染设置
//                              - 存档 / 读档 / 重置参数 / 预设按钮（与 Glass_carWindowGUI 一致的存读档模式）
//                              - 存档路径：Library/VicTools/Glass/Custom_TransCutout/<preset>.json
//                              - 预设路径：Packages/com.youdoo.victools/Runtime/Shaders/Custom_TransCutout/<preset>.json
//                              - 处理 4 个 shader_feature toggle 关键字同步：
//                                  _USEDITHER / _USEPARTICLEALPHA / _USEPARTICLECOLOR / _ZWWRITE

using UnityEngine;
using UnityEditor;
using VicTools;

public class TransCutoutGUI : ShaderGUI
{
    private MaterialEditor m_MaterialEditor;
    private MaterialProperty[] m_Properties;

    // 缓存属性
    private MaterialProperty baseMap;
    private MaterialProperty baseColor;
    private MaterialProperty cutoff;
    private MaterialProperty useDither;
    private MaterialProperty ditherTexture;
    private MaterialProperty ditherSize;
    private MaterialProperty useParticleAlpha;
    private MaterialProperty cutoffMin;
    private MaterialProperty cutoffMax;
    private MaterialProperty srcBlend;
    private MaterialProperty dstBlend;
    private MaterialProperty zWrite;
    private MaterialProperty cullMode;

    // 主纹理属性名集合（_BaseMap / _MainTex）已抽到 HeaderStyle.MainTexturePropertyNames 共享，
    // 通过 HeaderStyle.IsMainTexture() / HeaderStyle.ShowLoadTextureDialog() 调用。

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        m_MaterialEditor = materialEditor;
        m_Properties = properties;

        FindProperties();

        // 全局工具栏（存档 / 读档 / 重置 / 预设）
        DrawGlobalSettings();

        // 章节绘制
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawDissolveProperties();
        }
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawDitherSection();
        }
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawParticleAlphaSection();
        }
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawBlendingSection();
        }
        DrawRenderSettings();
    }

    private void FindProperties()
    {
        baseMap           = FindProperty("_BaseMap",          m_Properties);
        baseColor         = FindProperty("_BaseColor",        m_Properties);
        cutoff            = FindProperty("_Cutoff",           m_Properties);
        useDither         = FindProperty("_UseDither",        m_Properties, false);
        ditherTexture     = FindProperty("_DitherTexture",    m_Properties, false);
        ditherSize        = FindProperty("_DitherSize",       m_Properties, false);
        useParticleAlpha  = FindProperty("_UseParticleAlpha", m_Properties, false);
        cutoffMin         = FindProperty("_CutoffMin",        m_Properties, false);
        cutoffMax         = FindProperty("_CutoffMax",        m_Properties, false);
        srcBlend          = FindProperty("_SrcBlend",         m_Properties);
        dstBlend          = FindProperty("_DstBlend",         m_Properties);
        zWrite            = FindProperty("_ZWrite",           m_Properties);
        cullMode          = FindProperty("_Cull",             m_Properties);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 全局工具栏：存档 / 读档 / 重置参数 / 预设
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void DrawGlobalSettings()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(HeaderStyle.Rich("全局设置", HeaderStyle.HeaderTitle), EditorStyle.Get.BoldLabelRichStyle);

        GUI.backgroundColor = new Color(0.3f, 0.8f, 1.0f);
        if (GUILayout.Button("存档", GUILayout.Width(50)))
        {
            EditorApplication.delayCall += SaveMaterialParameters;
        }

        GUI.backgroundColor = new Color(0.5f, 1.0f, 0.5f);
        if (GUILayout.Button("读档 ▾", GUILayout.Width(55)))
        {
            ShowLoadDropdown();
        }

        GUI.backgroundColor = new Color(1.0f, 0.8f, 0.3f);
        if (GUILayout.Button("重置参数", GUILayout.Width(60)))
        {
            EditorApplication.delayCall += ResetMaterialParameters;
        }

        GUI.backgroundColor = new Color(0.9f, 0.7f, 1.0f);
        if (GUILayout.Button("预设 ▾", GUILayout.Width(55)))
        {
            ShowPresetDropdown();
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    /// 显示读档下拉菜单
    private void ShowLoadDropdown()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        string shaderName = material.shader.name.Replace("/", "_");
        string folderPath = "Library/VicTools/Glass/" + shaderName;

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
                string filePath = file;
                menu.AddItem(new GUIContent(fileName), false, () =>
                {
                    EditorApplication.delayCall += () => LoadPresetFile(filePath);
                });
            }
        }

        menu.ShowAsContext();
    }

    /// 显示预设下拉菜单
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
        LoadMaterialParametersFromFile(filePath);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 章节 1：透明溶解属性
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void DrawDissolveProperties()
    {
        GUILayout.Label(HeaderStyle.Rich("1 ▌透明溶解属性 (Transparent Dissolve)", HeaderStyle.Base), EditorStyle.Get.BoldLabelRichStyle);
        m_MaterialEditor.TexturePropertySingleLine(new GUIContent("主纹理 (A = 蒙版)"), baseMap);
        m_MaterialEditor.ColorProperty(baseColor, "基础颜色 (仅 RGB, A 不再控制透明)");
        m_MaterialEditor.RangeProperty(cutoff, "透明阈值 (Alpha Cutoff)");
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 章节 2：Dither 颗粒抖动（_USEDITHER 关键字）
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// 把任意 int 值吸附到最近的 2 的次方（限定在 [2, 16] 范围 = { 2, 4, 8, 16 }）。
    /// 算法：先求第一个 >= v 的 2 的次方（下界），再选上下界中更近的一个。
    /// 等距情况（v=3 → 2|4、v=12 → 8|16）选择下界（更小）以与默认 _DitherSize=4 配合更小的颗粒偏好。
    private static int SnapToNearestPowerOfTwo(int value)
    {
        if (value <= 2) return 2;
        if (value >= 16) return 16;

        // 求第一个 <= v 的 2 的次方（下界 lowerPower）
        int lowerPower = 2;
        while (lowerPower * 2 <= value)
            lowerPower *= 2;

        // 上界 = lowerPower * 2（但不超过 16）
        int upperPower = lowerPower * 2;
        if (upperPower > 16) upperPower = 16;

        int distToLower = value - lowerPower;
        int distToUpper = upperPower - value;

        // 等距时取下界（更小的值，颗粒更细，与默认 _DitherSize=4 的"细颗粒"思路一致）
        return (distToUpper < distToLower) ? upperPower : lowerPower;
    }

    private void DrawDitherSection()
    {
        if (useDither == null) return;

        // 显式监听 toggle 变化，强制同步 keyword
        EditorGUI.BeginChangeCheck();
        HeaderStyle.ShaderProperty(m_MaterialEditor, useDither, "2 ▌Dither 颗粒抖动", HeaderStyle.Base);
        if (EditorGUI.EndChangeCheck())
        {
            foreach (var obj in m_MaterialEditor.targets)
            {
                Material mat = obj as Material;
                if (mat == null) continue;
                if (useDither.floatValue > 0.5f)
                    mat.EnableKeyword("_USEDITHER");
                else
                    mat.DisableKeyword("_USEDITHER");
            }
        }

        if (useDither.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "勾选后用 4x4 Bayer 抖动代替硬 clip（需要 Dither Texture）。\n" +
                "推荐把 AA4.PNG（4x4 Bayer）拖到 Dither Texture 槽。\n" +
                "默认 black 纹理时退化为硬 clip 行为。",
                MessageType.Info);

            if (ditherTexture != null)
            {
                m_MaterialEditor.TexturePropertySingleLine(
                    new GUIContent("Dither Texture (Noise颗粒贴图)"), ditherTexture);
            }

            if (ditherSize != null)
            {
                EditorGUI.BeginChangeCheck();
                int currentSize = Mathf.RoundToInt(ditherSize.floatValue);
                int newSize = EditorGUILayout.IntSlider(
                    new GUIContent("颗粒精细度 (Dither Size)", "自动吸附到 2 的次方（2/4/8/16）"),
                    currentSize, 2, 16);
                if (EditorGUI.EndChangeCheck())
                {
                    int snapped = SnapToNearestPowerOfTwo(newSize);
                    if (snapped != currentSize)
                        ditherSize.floatValue = snapped;
                }

                EditorGUILayout.HelpBox(
                    "拖动 slider 后自动吸附到 2 的次方（仅 2/4/8/16 有效）\n" +
                    "2  = 2x2 Bayer (颗粒最细，渐变层次一般)\n" +
                    "4  = 4x4 Bayer (颗粒细，渐变层次丰富)\n" +
                    "8  = 8x8 Bayer (颗粒中等)\n" +
                    "16 = 16x16 Bayer (颗粒最粗)",
                    MessageType.None);
            }

            EditorGUI.indentLevel--;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 章节 3：粒子 Alpha 驱动（_USEPARTICLEALPHA 关键字）
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void DrawParticleAlphaSection()
    {
        if (useParticleAlpha == null) return;

        EditorGUI.BeginChangeCheck();
        HeaderStyle.ShaderProperty(m_MaterialEditor, useParticleAlpha, "3 ▌粒子 Alpha (ColorOverLifetime)", HeaderStyle.Base);
        if (EditorGUI.EndChangeCheck())
        {
            foreach (var obj in m_MaterialEditor.targets)
            {
                Material mat = obj as Material;
                if (mat == null) continue;
                if (useParticleAlpha.floatValue > 0.5f)
                    mat.EnableKeyword("_USEPARTICLEALPHA");
                else
                    mat.DisableKeyword("_USEPARTICLEALPHA");
            }
        }

        if (useParticleAlpha.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "（注意：开启该选项后不能投影）\n勾选后 _Cutoff 由粒子 vertex color A 通道驱动。\n" +
                "particleAlpha=0 → cutoff = Cutoff Max\n" +
                "particleAlpha=1 → cutoff = Cutoff Min\n" +
                "需要 Particle System Renderer 使用本材质，并在 Color Over Lifetime 模块中调整颜色 A 通道。",
                MessageType.Info);

            if (cutoffMin != null)
                m_MaterialEditor.RangeProperty(cutoffMin, "Cutoff Min (粒子Alpha=1 时)");
            if (cutoffMax != null)
                m_MaterialEditor.RangeProperty(cutoffMax, "Cutoff Max (粒子Alpha=0 时)");

            EditorGUI.indentLevel--;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 章节 4：混合设置（SrcBlend / DstBlend / ZWrite）
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void DrawBlendingSection()
    {
        GUILayout.Label(HeaderStyle.Rich("4 ▌混合设置 (Transparent Blending)", HeaderStyle.Base), EditorStyle.Get.BoldLabelRichStyle);

        m_MaterialEditor.ShaderProperty(srcBlend, "Src Blend");
        m_MaterialEditor.ShaderProperty(dstBlend, "Dst Blend");

        if (zWrite != null)
        {
            EditorGUI.BeginChangeCheck();
            bool zWriteOn = EditorGUILayout.Toggle("深度写入 (ZWrite)", zWrite.floatValue > 0.5f);
            if (EditorGUI.EndChangeCheck())
            {
                zWrite.floatValue = zWriteOn ? 1f : 0f;
                foreach (var obj in m_MaterialEditor.targets)
                {
                    Material mat = obj as Material;
                    if (mat == null) continue;
                    if (zWriteOn)
                        mat.EnableKeyword("_ZWWRITE");
                    else
                        mat.DisableKeyword("_ZWWRITE");
                }
            }
        }

        EditorGUILayout.HelpBox(
            "透明裁剪模式（推荐）：Src Blend=One, Dst Blend=Zero, ZWrite=On\n" +
            "支持透明裁剪阴影，不会被黑色覆盖。\n" +
            "真半透明模式：Src Blend=SrcAlpha, Dst Blend=OneMinusSrcAlpha, ZWrite=Off\n" +
            "真正半透明效果，但阴影投射会有问题。",
            MessageType.Info);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 章节 6：渲染设置（Cull / RenderQueue / Instancing）
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private void DrawRenderSettings()
    {
        GUILayout.Label(HeaderStyle.Rich("5 ▌渲染设置 (Render Settings)", HeaderStyle.Render), EditorStyle.Get.BoldLabelRichStyle);

        m_MaterialEditor.ShaderProperty(cullMode, "剔除模式");

        m_MaterialEditor.RenderQueueField();
        m_MaterialEditor.EnableInstancingField();
        m_MaterialEditor.DoubleSidedGIField();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 存档 / 读档 / 重置
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    /// 获取存档路径
    private string GetPresetPath(string presetName)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return null;

        string shaderName = material.shader.name.Replace("/", "_");
        string folderPath = "Library/VicTools/Glass/" + shaderName;

        if (!System.IO.Directory.Exists(folderPath))
        {
            System.IO.Directory.CreateDirectory(folderPath);
        }

        return folderPath + "/" + presetName + ".json";
    }

    private void SaveMaterialParameters()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        string shaderName = material.shader.name.Replace("/", "_");
        string defaultPath = "Library/VicTools/Glass/" + shaderName;

        if (!System.IO.Directory.Exists(defaultPath))
        {
            System.IO.Directory.CreateDirectory(defaultPath);
        }

        string presetName = EditorUtility.SaveFilePanel(
            "保存 TransCutout 材质参数存档",
            defaultPath,
            "TransCutoutPreset",
            "json"
        );

        if (string.IsNullOrEmpty(presetName)) return;

        string fileName = System.IO.Path.GetFileNameWithoutExtension(presetName);
        SaveMaterialParametersToFile(fileName);
    }

    private void SaveMaterialParametersToFile(string presetName)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        // 构建 MaterialProperty 类型对照表
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

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        bool first = true;

        for (int i = 0; i < propertyCount; i++)
        {
            string propertyName = ShaderUtil.GetPropertyName(shader, i);
            ShaderUtil.ShaderPropertyType propertyType = ShaderUtil.GetPropertyType(shader, i);

            if (!material.HasProperty(propertyName)) continue;

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

        string path = GetPresetPath(presetName);
        System.IO.File.WriteAllText(path, sb.ToString());

        Debug.Log($"TransCutout 材质参数已保存到: {path}（含纹理引用）");
    }

    private static string EscapeJsonString(string str)
    {
        if (string.IsNullOrEmpty(str)) return "";
        return str.Replace("\\", "/");
    }

    private void LoadMaterialParametersFromFile(string filePath, bool isReset = false)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        if (!System.IO.File.Exists(filePath))
        {
            Debug.LogWarning($"存档文件不存在: {filePath}");
            return;
        }

        string json = System.IO.File.ReadAllText(filePath);

        bool hasTextureData = json.Contains("\"path\":");
        bool loadTextures = false;
        bool loadMainTex = false;

        if (hasTextureData && !isReset)
        {
            // 共用对话框：全部读取 / 仅参数 / 排除主纹理
            HeaderStyle.LoadTextureChoice choice = HeaderStyle.ShowLoadTextureDialog();
            loadTextures = choice.LoadTextures;
            loadMainTex  = choice.LoadMainTex;
        }

        Undo.RecordObject(material, "Load TransCutout Material Parameters");

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

            if (valueStr.StartsWith("{"))
            {
                string texJson = valueStr;
                while (!texJson.Contains("}") && lineIdx + 1 < lines.Length)
                {
                    lineIdx++;
                    texJson += lines[lineIdx];
                }

                if (loadTextures)
                {
                    // 主纹理：根据对话框选项决定（loadMainTex=false 时跳过主纹理读取，但 tiling/offset 仍同步）
                    bool isMainTex = HeaderStyle.IsMainTexture(propertyName);
                    if (!isMainTex || loadMainTex)
                    {
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
                            // 路径为空：清除该纹理（让默认值生效）
                            material.SetTexture(propertyName, null);
                        }
                    }
                }

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
                if (float.TryParse(valueStr, out float floatValue))
                {
                    material.SetFloat(propertyName, floatValue);
                }
            }
        }

        if (missingTextures.Count > 0)
        {
            string msg = "以下纹理资源不存在，已跳过：\n\n" + string.Join("\n", missingTextures);
            EditorUtility.DisplayDialog("纹理缺失警告", msg, "确定");
            Debug.LogWarning("[TransCutoutGUI] 读档时部分纹理资源不存在:\n" + string.Join("\n", missingTextures));
        }

        SyncShaderKeywords(material);

        EditorUtility.SetDirty(material);
        if (m_MaterialEditor != null) m_MaterialEditor.Repaint();
        SceneView.RepaintAll();

        string texInfo = loadTextures ? "（含纹理）" : "（仅参数）";
        Debug.Log($"TransCutout 材质参数已从存档加载{texInfo}: {filePath}");
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
        {
            float.TryParse(parts[i].Trim(), out result[i]);
        }
        return result;
    }

    /// 同步所有 shader_feature toggle 对应的 keyword
    private void SyncShaderKeywords(Material material)
    {
        var toggleKeywords = new System.Collections.Generic.Dictionary<string, string>
        {
            { "_UseDither",          "_USEDITHER"          },
            { "_UseParticleAlpha",   "_USEPARTICLEALPHA"   },
            { "_ZWrite",             "_ZWWRITE"            },
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

    private void ResetMaterialParameters()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        string defaultPresetPath = GetPresetPath("Default");

        if (System.IO.File.Exists(defaultPresetPath))
        {
            if (EditorUtility.DisplayDialog("重置参数",
                "将使用Default存档重置参数。\n\n注意：纹理不会被重置。",
                "确定", "取消"))
            {
                LoadMaterialParametersFromFile(defaultPresetPath, true);
            }
        }
        else
        {
            if (EditorUtility.DisplayDialog("创建Default存档",
                "Default存档不存在，将使用Shader默认值创建Default存档。",
                "确定", "取消"))
            {
                Material tempMaterial = new Material(material.shader);

                Shader shader = material.shader;
                int propertyCount = ShaderUtil.GetPropertyCount(shader);

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

                Debug.Log($"TransCutout 材质 Default 存档已创建: {defaultPresetPath}");

                LoadMaterialParametersFromFile(defaultPresetPath, true);
            }
        }
    }
}
