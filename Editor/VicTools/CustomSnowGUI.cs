// Custom_Snow.shader GUI v2.0 (2026.05.27)
// - 新增"雪交互管理器"按钮：不存在时创建并关联当前Renderer，存在时选中
// - 新增"创建脚印控制器"按钮：创建Foot对象挂载SnowFootprintMarker

using UnityEngine;
using UnityEditor;
using VicTools;
using System.Runtime.InteropServices;

public class CustomSnowGUI : ShaderGUI
{
    private MaterialEditor m_MaterialEditor;
    private MaterialProperty[] m_Properties;

    // 折叠状态
    private static bool _foldBase = true;
    private static bool _foldLighting = true;
    private static bool _foldSparkle = true;
    private static bool _foldFresnel = true;
    private static bool _foldDeform = true;
    private static bool _foldRender = false;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        m_MaterialEditor = materialEditor;
        m_Properties = properties;

        DrawGlobalSettings();
        EditorGUILayout.Space(4);

        DrawBaseSection();
        DrawLightingSection();
        DrawSparkleSection();
        DrawFresnelSection();
        DrawDeformSection();
        DrawRenderSection();

        EditorGUILayout.Space(8);
        m_MaterialEditor.RenderQueueField();
    }

    // ─── 全局设置（存档/读档/预设） ───
    private void DrawGlobalSettings()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(HeaderStyle.ColoredHeader("Custom/Snow", HeaderStyle.HeaderTitle), EditorStyle.Get.BoldLabelRichStyle);

        GUI.backgroundColor = new Color(0.3f, 0.8f, 1.0f);
        if (GUILayout.Button("存档", GUILayout.Width(50)))
            EditorApplication.delayCall += SaveMaterialParameters;

        GUI.backgroundColor = new Color(0.5f, 1.0f, 0.5f);
        if (GUILayout.Button("读档 ▾", GUILayout.Width(55)))
            ShowLoadDropdown();

        GUI.backgroundColor = new Color(0.9f, 0.7f, 1.0f);
        if (GUILayout.Button("预设 ▾", GUILayout.Width(55)))
            ShowPresetDropdown();

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    // ─── 基础雪面 ───
    private void DrawBaseSection()
    {
        _foldBase = EditorGUILayout.BeginFoldoutHeaderGroup(_foldBase, HeaderStyle.ColoredHeader("基础雪面", HeaderStyle.Base), EditorStyle.Get.FoldoutHeaderRichStyle);
        if (_foldBase)
        {
            EditorGUI.indentLevel++;
            DrawProperty("_BaseColor", "亮面颜色");
            DrawProperty("_ShadowColor", "暗面颜色");
            DrawProperty("_BaseMap", "颜色贴图 (RGB)");
            DrawTextureNoTiling("_NormalMap", "法线贴图");
            DrawProperty("_NormalScale", "法线强度");
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ─── 光照 ───
    private void DrawLightingSection()
    {
        _foldLighting = EditorGUILayout.BeginFoldoutHeaderGroup(_foldLighting, HeaderStyle.ColoredHeader("光照", HeaderStyle.Lighting), EditorStyle.Get.FoldoutHeaderRichStyle);
        if (_foldLighting)
        {
            EditorGUI.indentLevel++;
            DrawProperty("_ShadowSoftness", "阴影柔和度");
            DrawProperty("_ShadowOffset", "明暗分界偏移");
            DrawProperty("_Smoothness", "高光集中度");
            DrawProperty("_SpecularStrength", "高光强度");
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ─── 冰晶闪烁 ───
    private void DrawSparkleSection()
    {
        _foldSparkle = EditorGUILayout.BeginFoldoutHeaderGroup(_foldSparkle, HeaderStyle.ColoredHeader("冰晶闪烁", HeaderStyle.Sparkle), EditorStyle.Get.FoldoutHeaderRichStyle);
        if (_foldSparkle)
        {
            EditorGUI.indentLevel++;
            DrawTextureNoTiling("_SparkleTex", "闪烁贴图 (R=点位 G=蒙版噪波)");
            DrawProperty("_SparkleScale", "闪烁点密度 (Tiling)");
            DrawProperty("_SparkleThreshold", "亮点筛选阈值 (越高越稀疏)");
            DrawProperty("_SparkleIntensity", "闪烁亮度");
            DrawProperty("_SparkleViewDep", "视角灵敏度 (镜头移动触发闪烁)");
            DrawProperty("_SparkleFlickerScale", "蒙版纹理密度 (G通道Tiling)");
            DrawProperty("_SparkleFadeDistance", "可见距离 (远处淡出)");
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ─── 边缘光 ───
    private void DrawFresnelSection()
    {
        _foldFresnel = EditorGUILayout.BeginFoldoutHeaderGroup(_foldFresnel, HeaderStyle.ColoredHeader("边缘光 (Fresnel)", HeaderStyle.Fresnel), EditorStyle.Get.FoldoutHeaderRichStyle);
        if (_foldFresnel)
        {
            EditorGUI.indentLevel++;
            DrawProperty("_FresnelColor", "边缘光颜色");
            DrawProperty("_FresnelPower", "边缘光范围 (越大越窄)");
            DrawProperty("_FresnelStrength", "边缘光强度");
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ─── 雪地交互 (压痕) ───
    private void DrawDeformSection()
    {
        _foldDeform = EditorGUILayout.BeginFoldoutHeaderGroup(_foldDeform, HeaderStyle.ColoredHeader("雪地交互 (压痕)", HeaderStyle.Interaction), EditorStyle.Get.FoldoutHeaderRichStyle);
        if (_foldDeform)
        {
            EditorGUI.indentLevel++;
            DrawProperty("_DeformColor", "压痕颜色");
            DrawProperty("_DeformColorStrength", "压痕染色强度");
            DrawProperty("_DeformEdgeSoftness", "压痕过渡柔和度 (越大边缘越软)");
            EditorGUILayout.HelpBox("凹陷深度和变暗程度由 SnowDeformManager 全局控制", MessageType.Info);
            
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = Color.cyan;
            // 雪交互管理器按钮
            var existingManager = Object.FindObjectOfType<SnowDeformManager>();
            if (GUILayout.Button(existingManager != null ? "选择雪交互管理器" : "创建雪交互管理器"))
            {
                if (existingManager != null)
                {
                    Selection.activeGameObject = existingManager.gameObject;
                }
                else
                {
                    var go = new GameObject("SnowDeformManager");
                    var manager = go.AddComponent<SnowDeformManager>();
                    var selectedGo = Selection.activeGameObject;
                    if (selectedGo != null)
                    {
                        var renderer = selectedGo.GetComponent<Renderer>();
                        if (renderer != null)
                        {
                            manager.snowRenderer = renderer;
                        }
                    }
                    Undo.RegisterCreatedObjectUndo(go, "Create SnowDeformManager");
                    Selection.activeGameObject = go;
                }
            }
            
            GUI.backgroundColor = new Color(0.83f, 0.28f, 0.2f);
            // 创建脚印控制器按钮
            if (GUILayout.Button("创建脚印控制器"))
            {
                // 统计场景中已有的Foot数量，做名称区分
                int existingCount = Object.FindObjectsOfType<SnowFootprintMarker>().Length;
                string footName = existingCount == 0 ? "Foot" : $"Foot_{existingCount + 1}";
                
                var go = new GameObject(footName);
                var marker = go.AddComponent<SnowFootprintMarker>();
                // 自动关联场景中已存在的雪交互管理器
                var mgr = Object.FindObjectOfType<SnowDeformManager>();
                if (mgr != null)
                {
                    marker.manager = mgr;
                }
                Undo.RegisterCreatedObjectUndo(go, "Create SnowFootprintMarker");
                Selection.activeGameObject = go;
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ─── 渲染设置 ───
    private void DrawRenderSection()
    {
        _foldRender = EditorGUILayout.BeginFoldoutHeaderGroup(_foldRender, HeaderStyle.ColoredHeader("渲染设置", HeaderStyle.Render), EditorStyle.Get.FoldoutHeaderRichStyle);
        if (_foldRender)
        {
            EditorGUI.indentLevel++;
            DrawProperty("_Cull", "剔除模式");
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ─── 工具方法 ───
    private void DrawProperty(string propertyName, string label)
    {
        MaterialProperty prop = FindProperty(propertyName, m_Properties, false);
        if (prop != null)
            m_MaterialEditor.ShaderProperty(prop, label);
    }

    /// 绘制纹理属性但不显示 Tiling/Offset
    private void DrawTextureNoTiling(string propertyName, string label)
    {
        MaterialProperty prop = FindProperty(propertyName, m_Properties, false);
        if (prop != null)
            m_MaterialEditor.TexturePropertySingleLine(new GUIContent(label), prop);
    }

    // ─── 存档/读档系统 ───
    private void ShowLoadDropdown()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        string shaderName = material.shader.name.Replace("/", "_");
        string folderPath = "Library/VicTools/Snow/" + shaderName;

        if (!System.IO.Directory.Exists(folderPath))
            System.IO.Directory.CreateDirectory(folderPath);

        string[] files = System.IO.Directory.GetFiles(folderPath, "*.json");
        GenericMenu menu = new GenericMenu();

        if (files.Length == 0)
        {
            menu.AddDisabledItem(new GUIContent("（无存档）"));
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

    private void ShowPresetDropdown()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        string shaderName = material.shader.name.Replace("/", "_");
        string folderPath = "Packages/com.youdoo.victools/Runtime/Shaders/" + shaderName;

        if (!System.IO.Directory.Exists(folderPath))
            System.IO.Directory.CreateDirectory(folderPath);

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

    private string GetPresetPath(string presetName)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return null;

        string shaderName = material.shader.name.Replace("/", "_");
        string folderPath = "Library/VicTools/Snow/" + shaderName;

        if (!System.IO.Directory.Exists(folderPath))
            System.IO.Directory.CreateDirectory(folderPath);

        return folderPath + "/" + presetName + ".json";
    }

    private void SaveMaterialParameters()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        string shaderName = material.shader.name.Replace("/", "_");
        string defaultPath = "Library/VicTools/Snow/" + shaderName;

        if (!System.IO.Directory.Exists(defaultPath))
            System.IO.Directory.CreateDirectory(defaultPath);

        string presetPath = EditorUtility.SaveFilePanel(
            "保存雪地材质参数存档", defaultPath, "SnowPreset", "json");

        if (string.IsNullOrEmpty(presetPath)) return;

        string fileName = System.IO.Path.GetFileNameWithoutExtension(presetPath);
        SaveMaterialParametersToFile(fileName);
    }

    private void SaveMaterialParametersToFile(string presetName)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

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
                    string texPath = tex != null ? AssetDatabase.GetAssetPath(tex) : "";
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
        Debug.Log($"雪地材质参数已保存到: {path}");
    }

    private static string EscapeJsonString(string str)
    {
        if (string.IsNullOrEmpty(str)) return "";
        return str.Replace("\\", "/");
    }

    private void LoadMaterialParametersFromFile(string filePath)
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
        if (hasTextureData)
        {
            loadTextures = EditorUtility.DisplayDialog("读取纹理",
                "存档中包含纹理引用，是否同时读取？\n\n选择「是」将还原纹理及Tiling/Offset\n选择「否」仅读取数值参数",
                "是，读取纹理", "否，仅参数");
        }

        Undo.RecordObject(material, "Load Snow Material Parameters");

        var lines = json.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);

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
                    bool isMainTex = (propertyName == "_BaseMap" || propertyName == "_MainTex");
                    if (!(isMainTex && material.GetTexture(propertyName) != null))
                    {
                        string texPath = ExtractJsonStringValue(texJson, "path");
                        if (!string.IsNullOrEmpty(texPath))
                        {
                            Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                            if (tex != null) material.SetTexture(propertyName, tex);
                        }
                        else
                        {
                            material.SetTexture(propertyName, null);
                        }
                    }
                }

                float[] tilingValues = ExtractJsonFloatArray(texJson, "tiling");
                if (tilingValues != null && tilingValues.Length == 2)
                    material.SetTextureScale(propertyName, new Vector2(tilingValues[0], tilingValues[1]));

                float[] offsetValues = ExtractJsonFloatArray(texJson, "offset");
                if (offsetValues != null && offsetValues.Length == 2)
                    material.SetTextureOffset(propertyName, new Vector2(offsetValues[0], offsetValues[1]));

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
                        float.TryParse(parts[i].Trim(), out values[i]);

                    try { material.SetColor(propertyName, new Color(values[0], values[1], values[2], values[3])); }
                    catch { material.SetVector(propertyName, new Vector4(values[0], values[1], values[2], values[3])); }
                }
            }
            else
            {
                if (float.TryParse(valueStr, out float floatValue))
                    material.SetFloat(propertyName, floatValue);
            }
        }

        EditorUtility.SetDirty(material);
        if (m_MaterialEditor != null) m_MaterialEditor.Repaint();
        SceneView.RepaintAll();
        Debug.Log($"雪地材质参数已从存档加载: {filePath}");
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
}
