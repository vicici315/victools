// Custom/Hair ShaderGUI - 分组显示 + 存档/读档
using UnityEngine;
using UnityEditor;

public class CustomHairGUI : ShaderGUI
{
    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        Material material = materialEditor.target as Material;

        DrawPresetBar(material);

        // ── 基础 ──
        EditorGUILayout.LabelField("基础", EditorStyles.boldLabel);
        materialEditor.TexturePropertySingleLine(new GUIContent("Albedo"), FindProperty("_BaseMap", properties));
        materialEditor.TextureScaleOffsetProperty(FindProperty("_BaseMap", properties));
        materialEditor.ShaderProperty(FindProperty("_Color", properties), "颜色");
        materialEditor.ShaderProperty(FindProperty("_Cutoff", properties), "Alpha 裁剪 (主体)");
        materialEditor.ShaderProperty(FindProperty("_TransCutoff", properties), "Alpha 裁剪 (边缘起始)");
        materialEditor.ShaderProperty(FindProperty("_ShadowCutoff", properties), "阴影 Alpha 裁剪");
        materialEditor.ShaderProperty(FindProperty("_ShadowBiasScale", properties), "阴影偏移");

        // ── 法线 ──
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("法线", EditorStyles.boldLabel);
        materialEditor.TexturePropertySingleLine(new GUIContent("法线贴图"), FindProperty("_NormalMap", properties));
        materialEditor.ShaderProperty(FindProperty("_NormalScale", properties), "法线强度");

        // ── 各向异性高光 ──
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("各向异性高光 (Kajiya-Kay)", EditorStyles.boldLabel);
        materialEditor.TexturePropertySingleLine(new GUIContent("Shift Map (R通道)"), FindProperty("_ShiftMap", properties));
        materialEditor.ShaderProperty(FindProperty("_HairDirRotate", properties), "方向旋转");
        EditorGUILayout.Space(3);
        EditorGUILayout.LabelField("主高光", EditorStyles.miniLabel);
        materialEditor.ShaderProperty(FindProperty("_SpecColor1", properties), "  颜色");
        materialEditor.ShaderProperty(FindProperty("_SpecPower1", properties), "  锐度");
        materialEditor.ShaderProperty(FindProperty("_SpecShift1", properties), "  偏移");
        EditorGUILayout.Space(3);
        EditorGUILayout.LabelField("副高光", EditorStyles.miniLabel);
        materialEditor.ShaderProperty(FindProperty("_SpecColor2", properties), "  颜色");
        materialEditor.ShaderProperty(FindProperty("_SpecPower2", properties), "  锐度");
        materialEditor.ShaderProperty(FindProperty("_SpecShift2", properties), "  偏移");

        // ── 环境光 & 边缘光 ──
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("环境光 & 边缘光", EditorStyles.boldLabel);
        materialEditor.ShaderProperty(FindProperty("_AmbientColor", properties), "环境光颜色");
        materialEditor.ShaderProperty(FindProperty("_RimPower", properties), "边缘光衰减");
        materialEditor.ShaderProperty(FindProperty("_RimIntensity", properties), "边缘光强度");

        // ── 渲染 ──
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("渲染设置", EditorStyles.boldLabel);
        materialEditor.ShaderProperty(FindProperty("_Cull", properties), "剔除模式");
    }

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

        if (doSave) { SavePreset(material); GUIUtility.ExitGUI(); }
        if (doLoad) { LoadPreset(material); GUIUtility.ExitGUI(); }
    }

    private string GetPresetDir(Material material)
    {
        string shaderName = material.shader.name.Replace("/", "_");
        string dir = "Library/VicTools/Hair/" + shaderName;
        if (!System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    private void SavePreset(Material material)
    {
        string dir = GetPresetDir(material);
        string path = EditorUtility.SaveFilePanel("保存 Hair 材质存档", dir, "HairPreset", "json");
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
                default: sb.Append("null"); break;
            }
            sb.AppendLine(i < count - 1 ? "," : "");
        }
        sb.AppendLine("}");
        System.IO.File.WriteAllText(path, sb.ToString());
        Debug.Log($"[CustomHairGUI] 存档已保存：{path}");
    }

    private void LoadPreset(Material material)
    {
        string dir = GetPresetDir(material);
        string path = EditorUtility.OpenFilePanel("加载 Hair 材质存档", dir, "json");
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;

        bool loadTex = EditorUtility.DisplayDialog("读取纹理", "是否同时读取纹理参数？\n选择「否」将只读取数值参数。", "是", "否");
        string json = System.IO.File.ReadAllText(path);
        Undo.RecordObject(material, "加载 Hair 材质预设");

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
                    if (ca != null && ca.Length >= 4) material.SetColor(propName, new Color(ca[0], ca[1], ca[2], ca[3]));
                    break;
                case ShaderUtil.ShaderPropertyType.Vector:
                    float[] va = ExtractFloatArray(json, propName);
                    if (va != null && va.Length >= 4) material.SetVector(propName, new Vector4(va[0], va[1], va[2], va[3]));
                    break;
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:
                    float fv = ExtractFloat(json, propName, float.NaN);
                    if (!float.IsNaN(fv)) material.SetFloat(propName, fv);
                    break;
                case ShaderUtil.ShaderPropertyType.TexEnv:
                    if (!loadTex) break;
                    string texPath = ExtractTexPath(json, propName);
                    if (!string.IsNullOrEmpty(texPath))
                    {
                        Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                        if (tex != null) material.SetTexture(propName, tex);
                    }
                    float[] tiling = ExtractSubFloatArray(json, propName, "tiling");
                    float[] offset = ExtractSubFloatArray(json, propName, "offset");
                    if (tiling != null && tiling.Length >= 2) material.SetTextureScale(propName, new Vector2(tiling[0], tiling[1]));
                    if (offset != null && offset.Length >= 2) material.SetTextureOffset(propName, new Vector2(offset[0], offset[1]));
                    break;
            }
        }
        EditorUtility.SetDirty(material);
        Debug.Log($"[CustomHairGUI] 存档已加载：{path}");
    }

    // ── JSON 解析 ──
    static float[] ExtractFloatArray(string json, string key)
    {
        string p = $"\"{key}\": ["; int s = json.IndexOf(p); if (s < 0) return null;
        s += p.Length; int e = json.IndexOf(']', s); if (e < 0) return null;
        string[] parts = json.Substring(s, e - s).Split(',');
        var r = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r[i])) return null;
        return r;
    }
    static float ExtractFloat(string json, string key, float fb)
    {
        string p = $"\"{key}\": "; int s = json.IndexOf(p); if (s < 0) return fb;
        s += p.Length; int e = s;
        while (e < json.Length && (char.IsDigit(json[e]) || json[e] == '.' || json[e] == '-' || json[e] == 'E' || json[e] == 'e' || json[e] == '+')) e++;
        return float.TryParse(json.Substring(s, e - s), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fb;
    }
    static string ExtractTexPath(string json, string key)
    {
        string p = $"\"{key}\": {{\"path\": \""; int s = json.IndexOf(p); if (s < 0) return null;
        s += p.Length; int e = json.IndexOf('"', s); return e < 0 ? null : json.Substring(s, e - s);
    }
    static float[] ExtractSubFloatArray(string json, string key, string sub)
    {
        string p = $"\"{key}\": {{"; int bs = json.IndexOf(p); if (bs < 0) return null;
        int be = json.IndexOf('}', bs); if (be < 0) return null;
        return ExtractFloatArray(json.Substring(bs, be - bs) + "}", sub);
    }
}
