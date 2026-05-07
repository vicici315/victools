// Custom/Toon ShaderGUI - 分组显示 + 存档/读档功能
using UnityEngine;
using UnityEditor;

public class CustomToonGUI : ShaderGUI
{
    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        Material material = materialEditor.target as Material;

        // ── 存档 / 读档 ──
        DrawPresetBar(material);

        // ── 基础设置 ──
        EditorGUILayout.Space(6);
        materialEditor.ShaderProperty(FindProperty("_ReceiveShadows", properties), "自身阴影");

        // ── 基础颜色 ──
        EditorGUILayout.LabelField("基础颜色", EditorStyles.boldLabel);
        materialEditor.ShaderProperty(FindProperty("_Color", properties), "颜色");
        materialEditor.TexturePropertySingleLine(new GUIContent("主贴图"), FindProperty("_MainTex", properties));
        materialEditor.TextureScaleOffsetProperty(FindProperty("_MainTex", properties));
        materialEditor.ShaderProperty(FindProperty("_AmbientColor", properties), "环境光颜色");

        // ── 法线 ──
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("法线贴图", EditorStyles.boldLabel);
        materialEditor.TexturePropertySingleLine(new GUIContent("法线贴图"), FindProperty("_NormalMap", properties));
        materialEditor.ShaderProperty(FindProperty("_NormalScale", properties), "法线强度");

        // ── 高光 ──
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("高光", EditorStyles.boldLabel);
        materialEditor.ShaderProperty(FindProperty("_SpecularColor", properties), "高光颜色");
        materialEditor.ShaderProperty(FindProperty("_Glossiness", properties), "光滑度");

        // ── 明暗交界线 ──
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("明暗交界线", EditorStyles.boldLabel);
        materialEditor.ShaderProperty(FindProperty("_ToonSteps", properties), "段数 (1=硬切)");
        materialEditor.ShaderProperty(FindProperty("_ToonSmooth", properties), "段间柔化");
        materialEditor.ShaderProperty(FindProperty("_ToonOffset", properties), "明暗偏移");
        materialEditor.ShaderProperty(FindProperty("_ToonCompression", properties), "明暗压缩");
        materialEditor.ShaderProperty(FindProperty("_ToonContrast", properties), "明暗对比");


        // ── 亮部边缘光 ──
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("亮部边缘光 (Rim)", EditorStyles.boldLabel);
        materialEditor.ShaderProperty(FindProperty("_RimColor", properties), "边缘光颜色");
        materialEditor.ShaderProperty(FindProperty("_RimAmount", properties), "边缘光范围");
        materialEditor.ShaderProperty(FindProperty("_RimThreshold", properties), "边缘光阈值");

        // ── 暗部边缘光 ──
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("暗部边缘光", EditorStyles.boldLabel);
        materialEditor.ShaderProperty(FindProperty("_DarkRimColor", properties), "暗部边缘光颜色");
        materialEditor.ShaderProperty(FindProperty("_DarkRimAmount", properties), "暗部边缘光范围");
        materialEditor.ShaderProperty(FindProperty("_DarkRimThreshold", properties), "暗部边缘光阈值");

        // ── 自发光 ──
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("自发光", EditorStyles.boldLabel);
        materialEditor.ShaderProperty(FindProperty("_UseEmissionMap", properties), "启用自发光贴图");
        materialEditor.ShaderProperty(FindProperty("_EmissionColor", properties), "自发光颜色");
        materialEditor.TexturePropertySingleLine(new GUIContent("自发光贴图"), FindProperty("_EmissionMap", properties));
        materialEditor.ShaderProperty(FindProperty("_EmissionScale", properties), "自发光强度");
    }

    // ═══════════════════════════════════════════
    //  存档 / 读档
    // ═══════════════════════════════════════════
    private void DrawPresetBar(Material material)
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.5f, 0.9f, 0.6f);
        bool doSave = GUILayout.Button("存档", GUILayout.Height(22));
        GUI.backgroundColor = new Color(0.5f, 0.75f, 1f);
        bool doLoad = GUILayout.Button("读档", GUILayout.Height(22));
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4);

        // 在布局结束后再执行弹窗操作，避免 EndLayoutGroup 错误
        if (doSave) { SavePreset(material); GUIUtility.ExitGUI(); }
        if (doLoad) { LoadPreset(material); GUIUtility.ExitGUI(); }
    }

    private string GetPresetDir(Material material)
    {
        string shaderName = material.shader.name.Replace("/", "_");
        string dir = "Library/VicTools/Toon/" + shaderName;
        if (!System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    private void SavePreset(Material material)
    {
        string dir = GetPresetDir(material);
        string path = EditorUtility.SaveFilePanel("保存 Toon 材质存档", dir, "ToonPreset", "json");
        if (string.IsNullOrEmpty(path)) return;

        Shader shader = material.shader;
        int count = ShaderUtil.GetPropertyCount(shader);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");

        for (int i = 0; i < count; i++)
        {
            string propName = ShaderUtil.GetPropertyName(shader, i);
            var propType = ShaderUtil.GetPropertyType(shader, i);
            if (!material.HasProperty(propName)) continue;

            sb.Append($"  \"{propName}\": ");
            switch (propType)
            {
                case ShaderUtil.ShaderPropertyType.Color:
                    Color c = material.GetColor(propName);
                    sb.Append($"[{c.r}, {c.g}, {c.b}, {c.a}]");
                    break;
                case ShaderUtil.ShaderPropertyType.Vector:
                    Vector4 v = material.GetVector(propName);
                    sb.Append($"[{v.x}, {v.y}, {v.z}, {v.w}]");
                    break;
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:
                    sb.Append(material.GetFloat(propName).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case ShaderUtil.ShaderPropertyType.TexEnv:
                    Texture tex = material.GetTexture(propName);
                    string texPath = tex != null ? AssetDatabase.GetAssetPath(tex).Replace("\\", "/") : "";
                    Vector2 tiling = material.GetTextureScale(propName);
                    Vector2 offset = material.GetTextureOffset(propName);
                    sb.Append($"{{\"path\": \"{texPath}\", \"tiling\": [{tiling.x}, {tiling.y}], \"offset\": [{offset.x}, {offset.y}]}}");
                    break;
                default:
                    sb.Append("null");
                    break;
            }
            if (i < count - 1) sb.AppendLine(",");
            else sb.AppendLine();
        }

        sb.AppendLine("}");
        System.IO.File.WriteAllText(path, sb.ToString());
        Debug.Log($"[CustomToonGUI] 存档已保存：{path}");
    }

    private void LoadPreset(Material material)
    {
        string dir = GetPresetDir(material);
        string path = EditorUtility.OpenFilePanel("加载 Toon 材质存档", dir, "json");
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;

        bool loadTextures = EditorUtility.DisplayDialog("读取纹理",
            "是否同时读取纹理参数？\n选择「否」将只读取数值参数。", "是", "否");

        string json = System.IO.File.ReadAllText(path);
        Undo.RecordObject(material, "加载 Toon 材质预设");

        Shader shader = material.shader;
        int count = ShaderUtil.GetPropertyCount(shader);

        for (int i = 0; i < count; i++)
        {
            string propName = ShaderUtil.GetPropertyName(shader, i);
            var propType = ShaderUtil.GetPropertyType(shader, i);
            if (!material.HasProperty(propName)) continue;

            switch (propType)
            {
                case ShaderUtil.ShaderPropertyType.Color:
                    float[] ca = ExtractFloatArray(json, propName);
                    if (ca != null && ca.Length >= 4)
                        material.SetColor(propName, new Color(ca[0], ca[1], ca[2], ca[3]));
                    break;
                case ShaderUtil.ShaderPropertyType.Vector:
                    float[] va = ExtractFloatArray(json, propName);
                    if (va != null && va.Length >= 4)
                        material.SetVector(propName, new Vector4(va[0], va[1], va[2], va[3]));
                    break;
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:
                    float fv = ExtractFloat(json, propName, float.NaN);
                    if (!float.IsNaN(fv)) material.SetFloat(propName, fv);
                    break;
                case ShaderUtil.ShaderPropertyType.TexEnv:
                    if (!loadTextures) break;
                    string texPath = ExtractTexPath(json, propName);
                    if (!string.IsNullOrEmpty(texPath))
                    {
                        Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                        if (tex != null) material.SetTexture(propName, tex);
                    }
                    float[] tiling = ExtractSubFloatArray(json, propName, "tiling");
                    float[] offset = ExtractSubFloatArray(json, propName, "offset");
                    if (tiling != null && tiling.Length >= 2)
                        material.SetTextureScale(propName, new Vector2(tiling[0], tiling[1]));
                    if (offset != null && offset.Length >= 2)
                        material.SetTextureOffset(propName, new Vector2(offset[0], offset[1]));
                    break;
            }
        }

        EditorUtility.SetDirty(material);
        Debug.Log($"[CustomToonGUI] 存档已加载：{path}");
    }

    // ── JSON 解析工具 ──
    private static float[] ExtractFloatArray(string json, string key)
    {
        string pattern = $"\"{key}\": [";
        int start = json.IndexOf(pattern);
        if (start < 0) return null;
        start += pattern.Length;
        int end = json.IndexOf(']', start);
        if (end < 0) return null;
        string[] parts = json.Substring(start, end - start).Split(',');
        var result = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out result[i])) return null;
        return result;
    }

    private static float ExtractFloat(string json, string key, float fallback)
    {
        string pattern = $"\"{key}\": ";
        int start = json.IndexOf(pattern);
        if (start < 0) return fallback;
        start += pattern.Length;
        int end = start;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-' || json[end] == 'E' || json[end] == 'e' || json[end] == '+')) end++;
        return float.TryParse(json.Substring(start, end - start), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
    }

    private static string ExtractTexPath(string json, string key)
    {
        string pattern = $"\"{key}\": {{\"path\": \"";
        int start = json.IndexOf(pattern);
        if (start < 0) return null;
        start += pattern.Length;
        int end = json.IndexOf('"', start);
        return end < 0 ? null : json.Substring(start, end - start);
    }

    private static float[] ExtractSubFloatArray(string json, string key, string subKey)
    {
        string pattern = $"\"{key}\": {{";
        int blockStart = json.IndexOf(pattern);
        if (blockStart < 0) return null;
        int blockEnd = json.IndexOf('}', blockStart);
        if (blockEnd < 0) return null;
        string block = json.Substring(blockStart, blockEnd - blockStart) + "}";
        return ExtractFloatArray(block, subKey);
    }
}
