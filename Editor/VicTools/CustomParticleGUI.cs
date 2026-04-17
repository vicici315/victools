// CustomParticle.shader GUI控制脚本
// 存读档包含纹理等所有参数
using UnityEngine;
using UnityEditor;

public class CustomParticleGUI : ShaderGUI
{
    private MaterialEditor m_MaterialEditor;
    private MaterialProperty[] m_Properties;

    // 缓存属性
    private MaterialProperty mainTex;
    private MaterialProperty color;
    private MaterialProperty scrollSpeed;
    private MaterialProperty useWetDecal;
    private MaterialProperty wetStrength;
    private MaterialProperty useReflection;
    private MaterialProperty reflectionMap;
    private MaterialProperty reflectionStrength;
    private MaterialProperty smoothness;
    private MaterialProperty useNormalMap;
    private MaterialProperty bumpMap;
    private MaterialProperty bumpScale;
    private MaterialProperty fresnelPower;
    private MaterialProperty fresnelBias;
    private MaterialProperty fresnelScale;
    private MaterialProperty blendMode;
    private MaterialProperty cullMode;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        m_MaterialEditor = materialEditor;
        m_Properties = properties;

        FindProperties();

        DrawGlobalSettings();
        DrawMainTexture();
        DrawUVAnimation();
        DrawWetDecal();
        DrawBlendMode();
        DrawRenderSettings();
    }

    private void FindProperties()
    {
        mainTex = FindProperty("_MainTex", m_Properties);
        color = FindProperty("_Color", m_Properties);
        scrollSpeed = FindProperty("_ScrollSpeed", m_Properties);
        useWetDecal = FindProperty("_UseWetDecal", m_Properties);
        wetStrength = FindProperty("_WetStrength", m_Properties);
        useReflection = FindProperty("_UseReflection", m_Properties);
        reflectionMap = FindProperty("_ReflectionMap", m_Properties);
        reflectionStrength = FindProperty("_ReflectionStrength", m_Properties);
        smoothness = FindProperty("_Smoothness", m_Properties);
        useNormalMap = FindProperty("_UseNormalMap", m_Properties);
        bumpMap = FindProperty("_BumpMap", m_Properties);
        bumpScale = FindProperty("_BumpScale", m_Properties);
        fresnelPower = FindProperty("_FresnelPower", m_Properties);
        fresnelBias = FindProperty("_FresnelBias", m_Properties);
        fresnelScale = FindProperty("_FresnelScale", m_Properties);
        blendMode = FindProperty("_BlendMode", m_Properties);
        cullMode = FindProperty("_Cull", m_Properties);
    }

    private void DrawGlobalSettings()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("全局设置", EditorStyles.boldLabel);

        GUI.backgroundColor = new Color(0.3f, 0.8f, 1.0f);
        if (GUILayout.Button("存档", GUILayout.Width(50)))
        {
            EditorApplication.delayCall += SaveMaterialParameters;
        }

        GUI.backgroundColor = new Color(0.5f, 1.0f, 0.5f);
        if (GUILayout.Button("读档", GUILayout.Width(50)))
        {
            EditorApplication.delayCall += LoadMaterialParameters;
        }

        GUI.backgroundColor = new Color(1.0f, 0.8f, 0.3f);
        if (GUILayout.Button("重置参数", GUILayout.Width(60)))
        {
            EditorApplication.delayCall += ResetMaterialParameters;
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    private void DrawMainTexture()
    {
        GUILayout.Label("1 ▌主贴图 (Main Texture)", EditorStyles.boldLabel);
        m_MaterialEditor.TexturePropertySingleLine(new GUIContent("主贴图"), mainTex);

        Material material = m_MaterialEditor.target as Material;
        if (material != null)
        {
            EditorGUI.indentLevel++;
            Vector2 tiling = material.GetTextureScale("_MainTex");
            Vector2 offset = material.GetTextureOffset("_MainTex");

            EditorGUI.BeginChangeCheck();
            tiling = EditorGUILayout.Vector2Field("Tiling", tiling);
            offset = EditorGUILayout.Vector2Field("Offset", offset);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(material, "Change MainTex Tiling/Offset");
                material.SetTextureScale("_MainTex", tiling);
                material.SetTextureOffset("_MainTex", offset);
                EditorUtility.SetDirty(material);
            }
            EditorGUI.indentLevel--;
        }

        m_MaterialEditor.ColorProperty(color, "颜色 (HDR)");
    }

    private void DrawUVAnimation()
    {
        GUILayout.Label("2 ▌UV动画 (UV Animation)", EditorStyles.boldLabel);
        m_MaterialEditor.VectorProperty(scrollSpeed, "UV滚动速度 (XY)");
    }

    private void DrawWetDecal()
    {
        m_MaterialEditor.ShaderProperty(useWetDecal, "3 ▌打湿贴花 (Wet Decal)");

        if (useWetDecal.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            m_MaterialEditor.RangeProperty(wetStrength, "透明度");

            EditorGUILayout.Space(4);
            m_MaterialEditor.ShaderProperty(useReflection, "使用反射贴图");
            if (useReflection.floatValue > 0.5f)
            {
                EditorGUI.indentLevel++;
                m_MaterialEditor.TexturePropertySingleLine(new GUIContent("球形反射贴图"), reflectionMap);
                m_MaterialEditor.RangeProperty(reflectionStrength, "反射强度");
                m_MaterialEditor.RangeProperty(smoothness, "光滑度");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            m_MaterialEditor.ShaderProperty(useNormalMap, "使用法线贴图");
            if (useNormalMap.floatValue > 0.5f)
            {
                EditorGUI.indentLevel++;
                m_MaterialEditor.TexturePropertySingleLine(new GUIContent("法线贴图"), bumpMap);
                m_MaterialEditor.RangeProperty(bumpScale, "法线强度");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            GUILayout.Label("菲涅尔 (Fresnel)", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            m_MaterialEditor.RangeProperty(fresnelPower, "全局强度");
            m_MaterialEditor.RangeProperty(fresnelBias, "中心偏移");
            m_MaterialEditor.RangeProperty(fresnelScale, "边缘缩放");
            EditorGUI.indentLevel--;

            EditorGUI.indentLevel--;
        }
    }

    private void DrawBlendMode()
    {
        GUILayout.Label("4 ▌混合模式 (非打湿)", EditorStyles.boldLabel);
        m_MaterialEditor.ShaderProperty(blendMode, "混合模式");
    }

    private void DrawRenderSettings()
    {
        GUILayout.Label("5 ▌渲染设置 (Rendering)", EditorStyles.boldLabel);
        m_MaterialEditor.ShaderProperty(cullMode, "剔除模式");

        // 渲染排序
        Material material = m_MaterialEditor.target as Material;
        if (material != null)
        {
            EditorGUI.BeginChangeCheck();
            int renderQueue = EditorGUILayout.IntField("Render Queue", material.renderQueue);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(material, "Change Render Queue");
                material.renderQueue = renderQueue;
                EditorUtility.SetDirty(material);
            }
        }

        m_MaterialEditor.RenderQueueField();
    }

    // ── 存读档（包含纹理） ──────────────────────────────────────

    private string GetPresetPath(string presetName)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return null;

        string shaderName = material.shader.name.Replace("/", "_");
        string folderPath = "Library/VicTools/CustomParticle/" + shaderName;

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
        string defaultPath = "Library/VicTools/CustomParticle/" + shaderName;

        if (!System.IO.Directory.Exists(defaultPath))
        {
            System.IO.Directory.CreateDirectory(defaultPath);
        }

        string presetPath = EditorUtility.SaveFilePanel(
            "保存材质参数存档", defaultPath, "CustomParticlePreset", "json");

        if (string.IsNullOrEmpty(presetPath)) return;

        SaveMaterialParametersToFile(presetPath, material);
    }

    private void SaveMaterialParametersToFile(string filePath, Material material)
    {
        Shader shader = material.shader;
        int propertyCount = ShaderUtil.GetPropertyCount(shader);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        bool first = true;

        for (int i = 0; i < propertyCount; i++)
        {
            string propName = ShaderUtil.GetPropertyName(shader, i);
            ShaderUtil.ShaderPropertyType propType = ShaderUtil.GetPropertyType(shader, i);

            if (!material.HasProperty(propName)) continue;

            if (!first) sb.AppendLine(",");
            first = false;

            switch (propType)
            {
                case ShaderUtil.ShaderPropertyType.Color:
                    Color c = material.GetColor(propName);
                    sb.Append($"  \"{propName}\": {{ \"type\": \"color\", \"value\": [{c.r}, {c.g}, {c.b}, {c.a}] }}");
                    break;

                case ShaderUtil.ShaderPropertyType.Vector:
                    Vector4 v = material.GetVector(propName);
                    sb.Append($"  \"{propName}\": {{ \"type\": \"vector\", \"value\": [{v.x}, {v.y}, {v.z}, {v.w}] }}");
                    break;

                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:
                    sb.Append($"  \"{propName}\": {{ \"type\": \"float\", \"value\": {material.GetFloat(propName)} }}");
                    break;

                case ShaderUtil.ShaderPropertyType.TexEnv:
                    Texture tex = material.GetTexture(propName);
                    string texPath = tex != null ? AssetDatabase.GetAssetPath(tex) : "";
                    Vector2 tiling = material.GetTextureScale(propName);
                    Vector2 offset = material.GetTextureOffset(propName);
                    sb.Append($"  \"{propName}\": {{ \"type\": \"texture\", \"path\": \"{EscapeJson(texPath)}\", " +
                              $"\"tiling\": [{tiling.x}, {tiling.y}], \"offset\": [{offset.x}, {offset.y}] }}");
                    break;
            }
        }

        sb.AppendLine();
        sb.AppendLine("}");

        System.IO.File.WriteAllText(filePath, sb.ToString());
        Debug.Log($"材质参数已保存到: {filePath}");
    }

    private void LoadMaterialParameters()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        string shaderName = material.shader.name.Replace("/", "_");
        string defaultPath = "Library/VicTools/CustomParticle/" + shaderName;

        if (!System.IO.Directory.Exists(defaultPath))
        {
            System.IO.Directory.CreateDirectory(defaultPath);
        }

        string presetPath = EditorUtility.OpenFilePanel(
            "加载材质参数存档", defaultPath, "json");

        if (string.IsNullOrEmpty(presetPath)) return;

        // 弹出选项：是否同时读取纹理
        int choice = EditorUtility.DisplayDialogComplex(
            "读档选项",
            "是否同时读取纹理参数？",
            "读取全部（含纹理）",   // 0
            "取消",                 // 1
            "仅读取数值参数");      // 2

        if (choice == 1) return; // 取消

        bool loadTextures = (choice == 0);
        LoadMaterialParametersFromFile(presetPath, loadTextures);
    }

    private void LoadMaterialParametersFromFile(string filePath, bool loadTextures = true)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        if (!System.IO.File.Exists(filePath))
        {
            Debug.LogWarning($"存档文件不存在: {filePath}");
            return;
        }

        Undo.RecordObject(material, "Load CustomParticle Material Parameters");

        string json = System.IO.File.ReadAllText(filePath);
        // 简单逐行解析
        var lines = json.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (!trimmed.Contains("\"type\"")) continue;

            // 提取属性名
            int firstQuote = trimmed.IndexOf('"');
            int secondQuote = trimmed.IndexOf('"', firstQuote + 1);
            if (firstQuote < 0 || secondQuote < 0) continue;
            string propName = trimmed.Substring(firstQuote + 1, secondQuote - firstQuote - 1);

            if (!material.HasProperty(propName)) continue;

            if (trimmed.Contains("\"type\": \"float\""))
            {
                // float: "value": 0.5
                int valueIdx = trimmed.LastIndexOf("\"value\":");
                if (valueIdx < 0) continue;
                string valStr = trimmed.Substring(valueIdx + 9).Trim().TrimEnd('}', ',', ' ');
                if (float.TryParse(valStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float f))
                {
                    material.SetFloat(propName, f);
                }
            }
            else if (trimmed.Contains("\"type\": \"color\"") || trimmed.Contains("\"type\": \"vector\""))
            {
                // [r, g, b, a]
                int bracketStart = trimmed.IndexOf('[');
                int bracketEnd = trimmed.IndexOf(']');
                if (bracketStart < 0 || bracketEnd < 0) continue;
                string arrStr = trimmed.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                string[] parts = arrStr.Split(',');
                if (parts.Length == 4)
                {
                    float[] vals = new float[4];
                    for (int i = 0; i < 4; i++)
                        float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out vals[i]);

                    if (trimmed.Contains("\"color\""))
                        material.SetColor(propName, new Color(vals[0], vals[1], vals[2], vals[3]));
                    else
                        material.SetVector(propName, new Vector4(vals[0], vals[1], vals[2], vals[3]));
                }
            }
            else if (trimmed.Contains("\"type\": \"texture\""))
            {
                if (!loadTextures) continue;
                // 纹理路径
                int pathStart = trimmed.IndexOf("\"path\": \"") + 9;
                int pathEnd = trimmed.IndexOf('"', pathStart);
                string texPath = trimmed.Substring(pathStart, pathEnd - pathStart);
                texPath = UnescapeJson(texPath);

                if (!string.IsNullOrEmpty(texPath))
                {
                    Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                    if (tex != null) material.SetTexture(propName, tex);
                }
                else
                {
                    material.SetTexture(propName, null);
                }

                // Tiling
                int tilingStart = trimmed.IndexOf("\"tiling\": [") + 11;
                int tilingEnd = trimmed.IndexOf(']', tilingStart);
                if (tilingStart > 10 && tilingEnd > tilingStart)
                {
                    string[] tp = trimmed.Substring(tilingStart, tilingEnd - tilingStart).Split(',');
                    if (tp.Length == 2)
                    {
                        float.TryParse(tp[0].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float tx);
                        float.TryParse(tp[1].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float ty);
                        material.SetTextureScale(propName, new Vector2(tx, ty));
                    }
                }

                // Offset
                int offsetStart = trimmed.IndexOf("\"offset\": [") + 11;
                int offsetEnd = trimmed.IndexOf(']', offsetStart);
                if (offsetStart > 10 && offsetEnd > offsetStart)
                {
                    string[] op = trimmed.Substring(offsetStart, offsetEnd - offsetStart).Split(',');
                    if (op.Length == 2)
                    {
                        float.TryParse(op[0].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float ox);
                        float.TryParse(op[1].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float oy);
                        material.SetTextureOffset(propName, new Vector2(ox, oy));
                    }
                }
            }
        }

        SyncShaderKeywords(material);
        EditorUtility.SetDirty(material);
        if (m_MaterialEditor != null) m_MaterialEditor.Repaint();
        SceneView.RepaintAll();

        Debug.Log($"材质参数已从存档加载: {filePath}");
    }

    private void SyncShaderKeywords(Material material)
    {
        var toggleKeywords = new System.Collections.Generic.Dictionary<string, string>
        {
            { "_UseWetDecal",   "_WETDECAL_ON"    },
            { "_UseReflection", "_USEREFLECTION"   },
            { "_UseNormalMap",  "_USENORMALMAP"    },
        };

        foreach (var pair in toggleKeywords)
        {
            if (!material.HasProperty(pair.Key)) continue;

            if (material.GetFloat(pair.Key) > 0.5f)
                material.EnableKeyword(pair.Value);
            else
                material.DisableKeyword(pair.Value);
        }

        // BlendMode keyword
        if (material.HasProperty("_BlendMode"))
        {
            float mode = material.GetFloat("_BlendMode");
            material.DisableKeyword("_BLENDMODE_ADDITIVE");
            material.DisableKeyword("_BLENDMODE_ALPHABLEND");
            if (mode < 0.5f)
                material.EnableKeyword("_BLENDMODE_ADDITIVE");
            else
                material.EnableKeyword("_BLENDMODE_ALPHABLEND");
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
                "将使用Default存档重置所有参数（含纹理）。", "确定", "取消"))
            {
                LoadMaterialParametersFromFile(defaultPresetPath);
            }
        }
        else
        {
            if (EditorUtility.DisplayDialog("创建Default存档",
                "Default存档不存在，将使用Shader默认值创建。", "确定", "取消"))
            {
                Material tempMat = new Material(material.shader);
                SaveMaterialParametersToFile(defaultPresetPath, tempMat);
                Object.DestroyImmediate(tempMat);
                Debug.Log($"Default存档已创建: {defaultPresetPath}");
                LoadMaterialParametersFromFile(defaultPresetPath);
            }
        }
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string UnescapeJson(string s)
    {
        return s.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}
