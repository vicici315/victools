// Grass.shader GUI 控制脚本
// 参考 Glass_carWindowGUI 的控制逻辑实现
using UnityEngine;
using UnityEditor;

public class GrassGUI : ShaderGUI
{
    private MaterialEditor m_MaterialEditor;
    private MaterialProperty[] m_Properties;

    // 缓存属性
    private MaterialProperty tessellation;
    private MaterialProperty topColor;
    private MaterialProperty bottomColor;
    private MaterialProperty colorBias;
    private MaterialProperty baseMap;
    private MaterialProperty alphaCutoff;
    private MaterialProperty bladeMinHeight;
    private MaterialProperty translucentGain;
    private MaterialProperty bladeWidth;
    private MaterialProperty bladeWidthRandom;
    private MaterialProperty bladeMinWidth;
    private MaterialProperty bladeHeight;
    private MaterialProperty bladeHeightRandom;
    private MaterialProperty bladeForward;
    private MaterialProperty bladeCurve;
    private MaterialProperty bendRotationRandom;
    private MaterialProperty windDistortionMap;
    private MaterialProperty windFrequency;
    private MaterialProperty windStrength;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        m_MaterialEditor = materialEditor;
        m_Properties = properties;
        FindProperties();

        DrawGlobalSettings();

        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            DrawTessellation();
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            DrawShading();
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            DrawBlade();
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            DrawWind();

        DrawRenderSettings();
    }

    private void FindProperties()
    {
        tessellation = FindProperty("_TessellationUniform", m_Properties);
        topColor = FindProperty("_TopColor", m_Properties);
        bottomColor = FindProperty("_BottomColor", m_Properties);
        colorBias = FindProperty("_ColorBias", m_Properties);
        baseMap = FindProperty("_BaseMap", m_Properties);
        alphaCutoff = FindProperty("_AlphaCutoff", m_Properties);
        bladeMinHeight = FindProperty("_BladeMinHeight", m_Properties);
        translucentGain = FindProperty("_TranslucentGain", m_Properties);
        bladeWidth = FindProperty("_BladeWidth", m_Properties);
        bladeWidthRandom = FindProperty("_BladeWidthRandom", m_Properties);
        bladeMinWidth = FindProperty("_BladeMinWidth", m_Properties);
        bladeHeight = FindProperty("_BladeHeight", m_Properties);
        bladeHeightRandom = FindProperty("_BladeHeightRandom", m_Properties);
        bladeForward = FindProperty("_BladeForward", m_Properties);
        bladeCurve = FindProperty("_BladeCurve", m_Properties);
        bendRotationRandom = FindProperty("_BendRotationRandom", m_Properties);
        windDistortionMap = FindProperty("_WindDistortionMap", m_Properties);
        windFrequency = FindProperty("_WindFrequency", m_Properties);
        windStrength = FindProperty("_WindStrength", m_Properties);
    }

    private void DrawGlobalSettings()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("草地着色器", EditorStyles.boldLabel);

        GUI.backgroundColor = new Color(0.3f, 0.8f, 1.0f);
        if (GUILayout.Button("存档", GUILayout.Width(50)))
            EditorApplication.delayCall += SavePreset;

        GUI.backgroundColor = new Color(0.5f, 1.0f, 0.5f);
        if (GUILayout.Button("读档", GUILayout.Width(50)))
            EditorApplication.delayCall += LoadPreset;

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    private void DrawTessellation()
    {
        GUILayout.Label("1 ▌细分密度 (Tessellation)", EditorStyles.boldLabel);
        m_MaterialEditor.RangeProperty(tessellation, "细分等级");
        EditorGUILayout.HelpBox("值越大草叶越密，性能消耗越高。建议 1~8 用于移动端。", MessageType.Info);
    }

    private void DrawShading()
    {
        GUILayout.Label("2 ▌着色 (Shading)", EditorStyles.boldLabel);
        m_MaterialEditor.ColorProperty(topColor, "顶部颜色");
        m_MaterialEditor.ColorProperty(bottomColor, "底部颜色（与贴图混合）");
        m_MaterialEditor.RangeProperty(colorBias, "颜色倾向（0=纯色, 1=贴图）");
        m_MaterialEditor.TexturePropertySingleLine(new GUIContent("草地颜色贴图 (RGB=颜色, A=长宽比)"), baseMap);

        // Tiling/Offset
        if (baseMap.textureValue != null)
        {
            Material mat = m_MaterialEditor.target as Material;
            if (mat != null)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                Vector2 tiling = EditorGUILayout.Vector2Field("Tiling", mat.GetTextureScale("_BaseMap"));
                Vector2 offset = EditorGUILayout.Vector2Field("Offset", mat.GetTextureOffset("_BaseMap"));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(mat, "Change BaseMap Tiling/Offset");
                    mat.SetTextureScale("_BaseMap", tiling);
                    mat.SetTextureOffset("_BaseMap", offset);
                    EditorUtility.SetDirty(mat);
                }
                EditorGUI.indentLevel--;
            }
        }

        m_MaterialEditor.RangeProperty(alphaCutoff, "Alpha 剔除阈值");
        m_MaterialEditor.ShaderProperty(bladeMinHeight, "最小高度剔除");
        m_MaterialEditor.RangeProperty(translucentGain, "半透明增益");
        EditorGUILayout.HelpBox("Alpha < 剔除阈值 → 不生成草叶\nAlpha 越大 → 草越高越窄\n计算高度 < 最小高度 → 也不生成", MessageType.Info);
    }

    private void DrawBlade()
    {
        GUILayout.Label("3 ▌草叶形状 (Blade)", EditorStyles.boldLabel);
        m_MaterialEditor.RangeProperty(bladeWidth, "宽度");
        m_MaterialEditor.RangeProperty(bladeWidthRandom, "宽度随机");
        m_MaterialEditor.RangeProperty(bladeMinWidth, "最小宽度");
        m_MaterialEditor.FloatProperty(bladeHeight, "高度");
        m_MaterialEditor.FloatProperty(bladeHeightRandom, "高度随机");
        m_MaterialEditor.FloatProperty(bladeForward, "前倾量");
        m_MaterialEditor.RangeProperty(bladeCurve, "弯曲度");
        m_MaterialEditor.RangeProperty(bendRotationRandom, "朝向随机");
    }

    private void DrawWind()
    {
        GUILayout.Label("4 ▌风力 (Wind)", EditorStyles.boldLabel);
        m_MaterialEditor.TexturePropertySingleLine(new GUIContent("风力扰动贴图"), windDistortionMap);
        m_MaterialEditor.ShaderProperty(windFrequency, "风力频率 (XY)");
        m_MaterialEditor.FloatProperty(windStrength, "风力强度");
    }

    private void DrawRenderSettings()
    {
        EditorGUILayout.Space(5);
        GUILayout.Label("5 ▌渲染设置", EditorStyles.boldLabel);
        m_MaterialEditor.RenderQueueField();
        m_MaterialEditor.EnableInstancingField();
        m_MaterialEditor.DoubleSidedGIField();
    }

    // ═══════════════════════════════════════════
    //  存档 / 读档
    // ═══════════════════════════════════════════
    private string GetPresetFolder()
    {
        string folder = "Library/VicTools/Grass";
        if (!System.IO.Directory.Exists(folder))
            System.IO.Directory.CreateDirectory(folder);
        return folder;
    }

    private void SavePreset()
    {
        Material mat = m_MaterialEditor.target as Material;
        if (mat == null || mat.shader == null) return;

        string path = EditorUtility.SaveFilePanel("保存草地材质参数", GetPresetFolder(), "GrassPreset", "json");
        if (string.IsNullOrEmpty(path)) return;

        Shader shader = mat.shader;
        int count = ShaderUtil.GetPropertyCount(shader);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        bool first = true;

        for (int i = 0; i < count; i++)
        {
            string name = ShaderUtil.GetPropertyName(shader, i);
            if (!mat.HasProperty(name)) continue;
            var type = ShaderUtil.GetPropertyType(shader, i);

            if (!first) sb.AppendLine(",");
            first = false;
            sb.Append($"  \"{name}\": ");

            switch (type)
            {
                case ShaderUtil.ShaderPropertyType.Color:
                    var c = mat.GetColor(name);
                    sb.Append($"[{c.r}, {c.g}, {c.b}, {c.a}]");
                    break;
                case ShaderUtil.ShaderPropertyType.Vector:
                    var v = mat.GetVector(name);
                    sb.Append($"[{v.x}, {v.y}, {v.z}, {v.w}]");
                    break;
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:
                    sb.Append(mat.GetFloat(name).ToString());
                    break;
                case ShaderUtil.ShaderPropertyType.TexEnv:
                    var tex = mat.GetTexture(name);
                    string texPath = tex != null ? AssetDatabase.GetAssetPath(tex).Replace("\\", "/") : "";
                    var tiling = mat.GetTextureScale(name);
                    var offset = mat.GetTextureOffset(name);
                    sb.Append($"{{\"path\": \"{texPath}\", \"tiling\": [{tiling.x}, {tiling.y}], \"offset\": [{offset.x}, {offset.y}]}}");
                    break;
            }
        }

        sb.AppendLine();
        sb.AppendLine("}");
        System.IO.File.WriteAllText(path, sb.ToString());
        Debug.Log($"草地材质参数已保存: {path}");
    }

    private void LoadPreset()
    {
        Material mat = m_MaterialEditor.target as Material;
        if (mat == null) return;

        string path = EditorUtility.OpenFilePanel("加载草地材质参数", GetPresetFolder(), "json");
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;

        string json = System.IO.File.ReadAllText(path);
        bool hasTexData = json.Contains("\"path\":");
        bool loadTex = hasTexData && EditorUtility.DisplayDialog("读取纹理",
            "存档中包含纹理引用，是否同时读取？", "是", "否，仅参数");

        Undo.RecordObject(mat, "Load Grass Preset");
        var lines = json.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);

        for (int li = 0; li < lines.Length; li++)
        {
            string line = lines[li].Trim().TrimEnd(',');
            int colon = line.IndexOf(':');
            if (colon < 0) continue;

            string propName = line.Substring(0, colon).Trim().Trim('"');
            string val = line.Substring(colon + 1).Trim();
            if (!mat.HasProperty(propName)) continue;

            if (val.StartsWith("{"))
            {
                if (!loadTex) continue;
                string texJson = val;
                while (!texJson.Contains("}") && li + 1 < lines.Length) { li++; texJson += lines[li]; }

                string texPath = ExtractString(texJson, "path");
                if (!string.IsNullOrEmpty(texPath))
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                    if (tex != null) mat.SetTexture(propName, tex);
                }
                else mat.SetTexture(propName, null);

                float[] t = ExtractFloats(texJson, "tiling");
                if (t != null && t.Length == 2) mat.SetTextureScale(propName, new Vector2(t[0], t[1]));
                float[] o = ExtractFloats(texJson, "offset");
                if (o != null && o.Length == 2) mat.SetTextureOffset(propName, new Vector2(o[0], o[1]));
            }
            else if (val.StartsWith("["))
            {
                string[] parts = val.Trim('[', ']').Split(',');
                if (parts.Length == 4)
                {
                    float[] v = new float[4];
                    for (int i = 0; i < 4; i++) float.TryParse(parts[i].Trim(), out v[i]);
                    mat.SetColor(propName, new Color(v[0], v[1], v[2], v[3]));
                }
            }
            else
            {
                if (float.TryParse(val, out float f)) mat.SetFloat(propName, f);
            }
        }

        EditorUtility.SetDirty(mat);
        m_MaterialEditor?.Repaint();
        SceneView.RepaintAll();
        Debug.Log($"草地材质参数已加载: {path}");
    }

    private static string ExtractString(string json, string key)
    {
        int idx = json.IndexOf("\"" + key + "\":");
        if (idx < 0) return null;
        int s = json.IndexOf('"', idx + key.Length + 3);
        if (s < 0) return null;
        int e = json.IndexOf('"', s + 1);
        return e > s ? json.Substring(s + 1, e - s - 1) : null;
    }

    private static float[] ExtractFloats(string json, string key)
    {
        int idx = json.IndexOf("\"" + key + "\":");
        if (idx < 0) return null;
        int s = json.IndexOf('[', idx);
        int e = json.IndexOf(']', s);
        if (s < 0 || e < 0) return null;
        string[] parts = json.Substring(s + 1, e - s - 1).Split(',');
        float[] r = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++) float.TryParse(parts[i].Trim(), out r[i]);
        return r;
    }
}
