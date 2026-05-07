// CustomTextureGUI v1.2 添加存档/读档功能，路径结构与 Glass_carWindowGUI 一致（Library/VicTools/Texture/{ShaderName}/）
// CustomTextureGUI v1.3 修复存读档排序参数bug

using UnityEngine;
using UnityEditor;

public class CustomTextureGUI : ShaderGUI
{
    public enum RenderMode { Opaque, Cutout, Transparent }

    private MaterialProperty mainTex;
    private MaterialProperty color;
    private MaterialProperty contrast;
    private MaterialProperty brightness;
    private MaterialProperty cutoff;
    private MaterialProperty cullMode;
    private MaterialProperty srcBlend;
    private MaterialProperty dstBlend;
    private MaterialProperty zWrite;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        mainTex    = FindProperty("_MainTex",      properties);
        color      = FindProperty("_Color",        properties);
        contrast   = FindProperty("_Contrast",     properties);
        brightness = FindProperty("_Brightness",   properties);
        cutoff     = FindProperty("_Cutoff",       properties);
        cullMode   = FindProperty("_Cull",         properties);
        srcBlend   = FindProperty("_SrcBlend",     properties);
        dstBlend   = FindProperty("_DstBlend",     properties);
        zWrite     = FindProperty("_ZWrite",       properties);

        Material material = materialEditor.target as Material;

        // ── 存档 / 读档 ──
        DrawPresetBar(material);

        // ── 渲染模式 ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("渲染模式", EditorStyles.boldLabel);

        RenderMode currentMode = GetRenderMode(material);
        RenderMode newMode = (RenderMode)EditorGUILayout.EnumPopup("Render Mode", currentMode);

        if (newMode != currentMode)
        {
            Undo.RecordObject(material, "Change Render Mode");
            SetRenderMode(material, newMode);
            EditorUtility.SetDirty(material);
        }

        if (currentMode == RenderMode.Cutout)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("透明度设置", EditorStyles.boldLabel);
            materialEditor.RangeProperty(cutoff, "Alpha 裁剪阈值");
        }

        if (currentMode == RenderMode.Transparent)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("透明模式设置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("云朵透明效果建议：\n1. 调整渲染队列避免闪烁\n2. 确保物体不要重叠太多\n3. 使用合适的混合模式", MessageType.Info);

            int currentQueue = material.renderQueue;
            int baseQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            int queueOffset = currentQueue - baseQueue;
            EditorGUILayout.LabelField($"当前渲染队列: {currentQueue}");
            int newOffset = EditorGUILayout.IntSlider("队列偏移 (解决闪烁)", queueOffset, -50, 50);
            if (newOffset != queueOffset)
            {
                Undo.RecordObject(material, "Change Render Queue");
                material.renderQueue = baseQueue + newOffset;
                EditorUtility.SetDirty(material);
            }
        }

        // ── 纹理设置 ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("纹理设置", EditorStyles.boldLabel);
        materialEditor.TexturePropertySingleLine(new GUIContent("纹理"), mainTex);
        materialEditor.TextureScaleOffsetProperty(mainTex);
        materialEditor.ColorProperty(color, "颜色叠加");
        materialEditor.RangeProperty(contrast, "对比度");
        materialEditor.RangeProperty(brightness, "亮度");

        // ── 渲染设置 ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("渲染设置", EditorStyles.boldLabel);
        materialEditor.ShaderProperty(cullMode, "剔除模式");

        // ── 高级设置 ──
        EditorGUILayout.Space();
        bool showAdvanced = EditorGUILayout.Foldout(
            EditorPrefs.GetBool("CustomTexture_ShowAdvanced", false), "高级设置");
        EditorPrefs.SetBool("CustomTexture_ShowAdvanced", showAdvanced);
        if (showAdvanced)
        {
            EditorGUI.indentLevel++;
            materialEditor.ShaderProperty(srcBlend, "源混合模式");
            materialEditor.ShaderProperty(dstBlend, "目标混合模式");
            materialEditor.ShaderProperty(zWrite,   "深度写入");
            EditorGUI.indentLevel--;
        }
    }

    // ═══════════════════════════════════════════
    //  存档 / 读档 UI
    // ═══════════════════════════════════════════
    private void DrawPresetBar(Material material)
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.5f, 0.9f, 0.6f);
        if (GUILayout.Button("存档", GUILayout.Height(22)))
            EditorApplication.delayCall += () => SavePreset(material);

        GUI.backgroundColor = new Color(0.5f, 0.75f, 1f);
        if (GUILayout.Button("读档", GUILayout.Height(22)))
            EditorApplication.delayCall += () => LoadPreset(material);

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(2);
    }

    private string GetPresetDir(Material material)
    {
        string shaderName = material.shader.name.Replace("/", "_");
        string dir = "Library/VicTools/Texture/" + shaderName;
        if (!System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    private void SavePreset(Material material)
    {
        string dir = GetPresetDir(material);
        string path = EditorUtility.SaveFilePanel("保存材质参数存档", dir, "TexturePreset", "json");
        if (string.IsNullOrEmpty(path)) return;

        Shader shader = material.shader;
        int count = ShaderUtil.GetPropertyCount(shader);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");

        // 额外保存 renderQueue 和渲染模式关键字（不在 shader 属性列表里）
        sb.AppendLine($"  \"__renderQueue\": {material.renderQueue},");
        sb.AppendLine($"  \"__renderMode\": \"{GetRenderMode(material)}\",");

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

            // 最后一个属性不加逗号
            if (i < count - 1) sb.AppendLine(",");
            else sb.AppendLine();
        }

        sb.AppendLine("}");
        System.IO.File.WriteAllText(path, sb.ToString());
        Debug.Log($"[CustomTextureGUI] 存档已保存：{path}");
    }

    private void LoadPreset(Material material)
    {
        string dir = GetPresetDir(material);
        string path = EditorUtility.OpenFilePanel("加载材质参数存档", dir, "json");
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;

        bool loadTextures = EditorUtility.DisplayDialog("读取纹理",
            "是否同时读取纹理参数？\n选择「否」将只读取数值参数。", "是", "否");

        string json = System.IO.File.ReadAllText(path);
        Undo.RecordObject(material, "加载材质预设");

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

        // 读取 renderQueue
        float rq = ExtractFloat(json, "__renderQueue", float.NaN);

        // 只同步关键字和 RenderType tag，不调用 SetRenderMode（避免覆盖已读入的混合参数）
        string modeStr = ExtractStringValue(json, "__renderMode");
        if (!string.IsNullOrEmpty(modeStr) && System.Enum.TryParse(modeStr, out RenderMode savedMode))
            SyncKeywordsOnly(material, savedMode);

        // SyncKeywordsOnly 内 shader 重赋值会重置 renderQueue，需在其后再还原
        if (!float.IsNaN(rq)) material.renderQueue = (int)rq;

        EditorUtility.SetDirty(material);
        Debug.Log($"[CustomTextureGUI] 存档已加载：{path}");
    }

    // ── 只同步关键字和 RenderType tag，不覆盖混合/剔除等参数 ──
    private static void SyncKeywordsOnly(Material material, RenderMode mode)
    {
        switch (mode)
        {
            case RenderMode.Opaque:
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.SetOverrideTag("RenderType", "Opaque");
                break;
            case RenderMode.Cutout:
                material.EnableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                break;
            case RenderMode.Transparent:
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.SetOverrideTag("RenderType", "Transparent");
                break;
        }
        material.shader = material.shader; // 强制刷新
    }

    // ── 轻量 JSON 解析工具 ──
    private static string ExtractStringValue(string json, string key)
    {
        string pattern = $"\"{key}\": \"";
        int start = json.IndexOf(pattern);
        if (start < 0) return null;
        start += pattern.Length;
        int end = json.IndexOf('"', start);
        return end < 0 ? null : json.Substring(start, end - start);
    }
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
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-')) end++;
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
        string block = json.Substring(blockStart, blockEnd - blockStart);
        return ExtractFloatArray(block + "}", subKey);
    }

    // ═══════════════════════════════════════════
    //  渲染模式
    // ═══════════════════════════════════════════
    private RenderMode GetRenderMode(Material material)
    {
        if (material.IsKeywordEnabled("_ALPHABLEND_ON")) return RenderMode.Transparent;
        if (material.IsKeywordEnabled("_ALPHATEST_ON"))  return RenderMode.Cutout;

        if (material.GetFloat("_UseAlphaBlend") > 0.5f) return RenderMode.Transparent;
        if (material.GetFloat("_UseAlphaClip")  > 0.5f) return RenderMode.Cutout;

        return RenderMode.Opaque;
    }

    private void SetRenderMode(Material material, RenderMode mode)
    {
        switch (mode)
        {
            case RenderMode.Opaque:
                material.SetFloat("_UseAlphaClip",  0);
                material.SetFloat("_UseAlphaBlend", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.SetOverrideTag("RenderType", "Opaque");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                material.SetFloat("_ZWrite", 1f);
                break;

            case RenderMode.Cutout:
                material.SetFloat("_UseAlphaClip",  1);
                material.SetFloat("_UseAlphaBlend", 0);
                material.EnableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                material.SetFloat("_ZWrite", 1f);
                break;

            case RenderMode.Transparent:
                material.SetFloat("_UseAlphaClip",  0);
                material.SetFloat("_UseAlphaBlend", 1);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                break;
        }

        material.shader = material.shader; // 强制刷新
    }
}
