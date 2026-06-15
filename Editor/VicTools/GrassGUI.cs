// Grass.shader GUI 控制脚本
// 参考 Glass_carWindowGUI 的控制逻辑实现
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Vic.Runtime;

public class GrassGUI : ShaderGUI
{
    private MaterialEditor m_MaterialEditor;
    private MaterialProperty[] m_Properties;

    // 缓存属性
    private MaterialProperty tessellation;
    private MaterialProperty topColor;
    private MaterialProperty bottomColor;
    private MaterialProperty gradientOffset;
    private MaterialProperty colorBias;
    private MaterialProperty baseMap;
    private MaterialProperty alphaCutoff;
    private MaterialProperty bladeMinHeight;
    private MaterialProperty translucentGain;
    private MaterialProperty bladeWidth;
    private MaterialProperty bladeBottomWidth;
    private MaterialProperty bladeWidthRandom;
    private MaterialProperty bladeMinWidth;
    private MaterialProperty bladeHeight;
    private MaterialProperty bladeHeightRandom;
    private MaterialProperty bladeForward;
    private MaterialProperty bladeCurve;
    private MaterialProperty bladeSegments;
    private MaterialProperty bendRotationRandom;
    private MaterialProperty bladeRootSink;
    private MaterialProperty windDistortionMap;
    private MaterialProperty windFrequency;
    private MaterialProperty windStrength;
    private MaterialProperty bladeOverlayTex;
    private MaterialProperty bladeOverlayIntensity;
    private MaterialProperty bladeOverlayAlphaClip;
    private MaterialProperty useBillboard;
    private MaterialProperty useBladeOverlay;
    private MaterialProperty grassFadeStart;
    private MaterialProperty grassFadeEnd;

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
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            DrawDistanceCulling();
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            DrawInteraction();

        DrawRenderSettings();
    }

    private void FindProperties()
    {
        tessellation = FindProperty("_TessellationUniform", m_Properties);
        topColor = FindProperty("_TopColor", m_Properties);
        bottomColor = FindProperty("_BottomColor", m_Properties);
        gradientOffset = FindProperty("_GradientOffset", m_Properties);
        colorBias = FindProperty("_ColorBias", m_Properties);
        baseMap = FindProperty("_BaseMap", m_Properties);
        alphaCutoff = FindProperty("_AlphaCutoff", m_Properties);
        bladeMinHeight = FindProperty("_BladeMinHeight", m_Properties);
        translucentGain = FindProperty("_ShadowScale", m_Properties);
        bladeWidth = FindProperty("_BladeWidth", m_Properties);
        bladeBottomWidth = FindProperty("_BladeBottomWidth", m_Properties);
        bladeWidthRandom = FindProperty("_BladeWidthRandom", m_Properties);
        bladeMinWidth = FindProperty("_BladeMinWidth", m_Properties);
        bladeHeight = FindProperty("_BladeHeight", m_Properties);
        bladeHeightRandom = FindProperty("_BladeHeightRandom", m_Properties);
        bladeForward = FindProperty("_BladeForward", m_Properties);
        bladeCurve = FindProperty("_BladeCurve", m_Properties);
        bladeSegments = FindProperty("_BladeSegments", m_Properties);
        bendRotationRandom = FindProperty("_BendRotationRandom", m_Properties);
        bladeRootSink = FindProperty("_BladeRootSink", m_Properties);
        windDistortionMap = FindProperty("_WindDistortionMap", m_Properties);
        windFrequency = FindProperty("_WindFrequency", m_Properties);
        windStrength = FindProperty("_WindStrength", m_Properties);
        bladeOverlayTex = FindProperty("_BladeOverlayTex", m_Properties);
        bladeOverlayIntensity = FindProperty("_BladeOverlayIntensity", m_Properties);
        bladeOverlayAlphaClip = FindProperty("_BladeOverlayAlphaClip", m_Properties);
        useBillboard = FindProperty("_UseBillboard", m_Properties);
        useBladeOverlay = FindProperty("_UseBladeOverlay", m_Properties);
        grassFadeStart = FindProperty("_GrassFadeStart", m_Properties);
        grassFadeEnd = FindProperty("_GrassFadeEnd", m_Properties);
    }

    private void DrawGlobalSettings()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("草地着色器", EditorStyles.boldLabel);

        GUI.backgroundColor = new Color(0.3f, 0.8f, 1.0f);
        if (GUILayout.Button("存档", GUILayout.Width(50)))
            EditorApplication.delayCall += SavePreset;

        GUI.backgroundColor = new Color(0.5f, 1.0f, 0.5f);
        if (GUILayout.Button("读档 ▾", GUILayout.Width(55)))
            ShowLoadDropdown();

        GUI.backgroundColor = new Color(0.9f, 0.7f, 1.0f);
        if (GUILayout.Button("预设 ▾", GUILayout.Width(55)))
            ShowPresetDropdown();

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    private void ShowLoadDropdown()
    {
        Material mat = m_MaterialEditor.target as Material;
        if (mat == null || mat.shader == null) return;

        string shaderName = mat.shader.name.Replace("/", "_");
        string folderPath = "Library/VicTools/Grass";

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
        Material mat = m_MaterialEditor.target as Material;
        if (mat == null || mat.shader == null) return;

        string shaderName = mat.shader.name.Replace("/", "_");
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

        string json = System.IO.File.ReadAllText(filePath);
        bool hasTexData = json.Contains("\"path\":");
        bool loadTex = hasTexData && EditorUtility.DisplayDialog("读取纹理",
            "预设中包含纹理引用，是否同时读取？", "是", "否，仅参数");

        Material mat = m_MaterialEditor.target as Material;
        if (mat == null) return;

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
        if (mat.HasProperty("_UseBladeOverlay"))
        {
            if (mat.GetFloat("_UseBladeOverlay") > 0.5f)
                mat.EnableKeyword("_BLADE_OVERLAY_ON");
            else
                mat.DisableKeyword("_BLADE_OVERLAY_ON");
        }
        m_MaterialEditor?.Repaint();
        SceneView.RepaintAll();
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
        m_MaterialEditor.RangeProperty(gradientOffset, "渐变偏移（负=底色多, 正=顶色多）");

        GUI.backgroundColor = new Color(0.65f, 0.75f, 1.0f);
        if (GUILayout.Button("换色（对调顶部/底部颜色）", GUILayout.Height(22)))
        {
            Material mat = m_MaterialEditor.target as Material;
            if (mat != null)
            {
                Undo.RecordObject(mat, "Swap Top/Bottom Color");
                Color tmp = topColor.colorValue;
                topColor.colorValue = bottomColor.colorValue;
                bottomColor.colorValue = tmp;
                EditorUtility.SetDirty(mat);
            }
        }
        GUI.backgroundColor = Color.white;
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
        m_MaterialEditor.RangeProperty(translucentGain, "阴影强度");
        EditorGUILayout.HelpBox("Alpha < 剔除阈值 → 不生成草叶\nAlpha 越大 → 草越高越窄\n计算高度 < 最小高度 → 也不生成", MessageType.Info);
    }

    private void DrawBlade()
    {
        GUILayout.Label("3 ▌草叶形态 (Blade)", EditorStyles.boldLabel);
        m_MaterialEditor.RangeProperty(bladeWidth, "宽度");
        m_MaterialEditor.RangeProperty(bladeBottomWidth, "底部宽度");
        m_MaterialEditor.RangeProperty(bladeWidthRandom, "宽度随机");
        m_MaterialEditor.RangeProperty(bladeMinWidth, "最小宽度");
        m_MaterialEditor.FloatProperty(bladeHeight, "高度");
        m_MaterialEditor.FloatProperty(bladeHeightRandom, "高度随机");
        m_MaterialEditor.FloatProperty(bladeForward, "前倾量");
        m_MaterialEditor.RangeProperty(bladeCurve, "弯曲度");
        m_MaterialEditor.RangeProperty(bladeSegments, "草体段数（1~3，3段有尖角）");
        m_MaterialEditor.RangeProperty(bendRotationRandom, "朝向随机");
        m_MaterialEditor.RangeProperty(bladeRootSink, "根部下沉");

        EditorGUILayout.Space(4);
        GUILayout.Label("草体透贴", EditorStyles.miniBoldLabel);

        EditorGUI.BeginChangeCheck();
        bool overlayOn = useBladeOverlay.floatValue > 0.5f;
        overlayOn = EditorGUILayout.Toggle("使用草体贴图", overlayOn);
        if (EditorGUI.EndChangeCheck())
        {
            useBladeOverlay.floatValue = overlayOn ? 1.0f : 0.0f;
            foreach (var obj in m_MaterialEditor.targets)
            {
                Material m = obj as Material;
                if (m == null) continue;
                if (overlayOn)
                    m.EnableKeyword("_BLADE_OVERLAY_ON");
                else
                    m.DisableKeyword("_BLADE_OVERLAY_ON");
            }
        }

        if (overlayOn)
        {
            m_MaterialEditor.TexturePropertySingleLine(new GUIContent("透贴纹理 (RGB=颜色, A=透明度)"), bladeOverlayTex);
            m_MaterialEditor.RangeProperty(bladeOverlayIntensity, "纹理强度");
            m_MaterialEditor.RangeProperty(bladeOverlayAlphaClip, "Alpha Clip 阈值");

            EditorGUI.BeginChangeCheck();
            m_MaterialEditor.ShaderProperty(useBillboard, "使用公告板");
            if (EditorGUI.EndChangeCheck())
            {
                if (useBillboard.floatValue > 0.5f)
                {
                    foreach (var obj in m_MaterialEditor.targets)
                    {
                        Material m = obj as Material;
                        if (m != null) m.SetFloat("_Cull", 2); // Cull Back
                        //m.SetFloat("_Cull", billboardOn ? 2 : 0); // 2=Back, 0=Off 三元表达式，公告板开启时设为 2，否则设为 0
                    }
                }
            }
            EditorGUILayout.HelpBox("透贴纹理会平展贴到每个草体上。UV使用草体高度映射，适用 1~2段草体。\n公告板模式：面片始终面向摄像机。", MessageType.Info);
        }
    }

    private void DrawWind()
    {
        GUILayout.Label("4 ▌风力 (Wind)", EditorStyles.boldLabel);
        m_MaterialEditor.TexturePropertySingleLine(new GUIContent("风力扰动贴图"), windDistortionMap);
        m_MaterialEditor.ShaderProperty(windFrequency, "风力频率 (XY)");
        m_MaterialEditor.FloatProperty(windStrength, "风力强度");
    }

    private void DrawDistanceCulling()
    {
        GUILayout.Label("5 ▌距离剔除 (Distance Culling)", EditorStyles.boldLabel);
        m_MaterialEditor.FloatProperty(grassFadeStart, "开始衰减距离");
        m_MaterialEditor.FloatProperty(grassFadeEnd, "完全剔除距离");
        EditorGUILayout.HelpBox("摄像机距离 < 开始衰减 → 全密度\n开始衰减 ~ 完全剔除 → 线性降低细分\n> 完全剔除 → 不生成草叶（细分=0）", MessageType.Info);
    }

    private void DrawInteraction()
    {
        GUILayout.Label("6 ▌交互控制 (Interaction)", EditorStyles.boldLabel);

        // 检查场景中是否已有 Controller
        var controller = Object.FindObjectOfType<GrassInteractionController>();

        if (controller != null)
        {
            EditorGUILayout.HelpBox($"场景中已存在交互控制器: {controller.gameObject.name}\n交互点数量: {controller.interactors.Count}/{GrassInteractionController.MaxInteractors}", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.8f, 1.0f, 0.8f);
            if (GUILayout.Button("选中交互控制器", GUILayout.Height(24)))
            {
                Selection.activeGameObject = controller.gameObject;
                EditorGUIUtility.PingObject(controller.gameObject);
            }

            GUI.backgroundColor = new Color(1.0f, 0.9f, 0.6f);
            if (GUILayout.Button("添加交互点", GUILayout.Height(24)))
            {
                CreateInteractorUnderController(controller);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            EditorGUILayout.HelpBox("场景中没有草地交互控制器。\n点击下方按钮创建控制器，然后添加交互对象。", MessageType.Warning);

            GUI.backgroundColor = new Color(0.4f, 1.0f, 0.6f);
            if (GUILayout.Button("创建交互控制器", GUILayout.Height(28)))
            {
                CreateInteractionController();
            }
            GUI.backgroundColor = Color.white;
        }
    }

    private void CreateInteractionController()
    {
        GameObject controllerObj = new GameObject("GrassInteractionController");
        var controller = controllerObj.AddComponent<GrassInteractionController>();

        // 创建一个默认交互点作为子对象
        CreateInteractorUnderController(controller);

        Undo.RegisterCreatedObjectUndo(controllerObj, "Create Grass Interaction Controller");
        Selection.activeGameObject = controllerObj;
        EditorGUIUtility.PingObject(controllerObj);

        // 标记场景为已修改
        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(controllerObj.scene);

        Debug.Log("[GrassGUI] 已创建草地交互控制器，包含一个默认交互点。");
    }

    private void CreateInteractorUnderController(GrassInteractionController controller)
    {
        GameObject interactorObj = new GameObject($"GrassInteractor_{controller.interactors.Count}");
        interactorObj.transform.SetParent(controller.transform);
        interactorObj.transform.localPosition = Vector3.zero;

        var interactor = interactorObj.AddComponent<GrassInteractor>();
        controller.interactors.Add(interactor);

        Undo.RegisterCreatedObjectUndo(interactorObj, "Create Grass Interactor");
        Selection.activeGameObject = interactorObj;

        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(interactorObj.scene);
    }

    private void DrawRenderSettings()
    {
        EditorGUILayout.Space(5);
        GUILayout.Label("7 ▌渲染设置", EditorStyles.boldLabel);

        // 单双面渲染选项
        Material mat = m_MaterialEditor.target as Material;
        if (mat != null)
        {
            EditorGUI.BeginChangeCheck();
            float cullVal = mat.GetFloat("_Cull");
            int cullInt = (int)cullVal;
            string[] cullOptions = { "双面 (Off)", "正面剔除 (Front)", "背面剔除 (Back)" };
            cullInt = EditorGUILayout.Popup("面渲染模式", cullInt, cullOptions);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var obj in m_MaterialEditor.targets)
                {
                    Material m = obj as Material;
                    if (m != null) m.SetFloat("_Cull", cullInt);
                }
            }
        }

        m_MaterialEditor.RenderQueueField();
        m_MaterialEditor.EnableInstancingField();
        m_MaterialEditor.DoubleSidedGIField();
    }

    // ══════════════════════════════════════════════
    //  存档 / 读档
    // ══════════════════════════════════════════════
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

        // 读档后同步 keyword 状态
        if (mat.HasProperty("_UseBladeOverlay"))
        {
            if (mat.GetFloat("_UseBladeOverlay") > 0.5f)
                mat.EnableKeyword("_BLADE_OVERLAY_ON");
            else
                mat.DisableKeyword("_BLADE_OVERLAY_ON");
        }

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
