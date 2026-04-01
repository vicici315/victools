// FurShell_MobileGUI 1.1 - 毛发shader的GUI控制脚本
// 1.1 添加Tiling/Offset控制，优化按钮风格
using UnityEngine;
using UnityEditor;
using System.IO;

public class FurShell_MobileGUI : ShaderGUI
{
    private MaterialEditor m_MaterialEditor;
    private MaterialProperty[] m_Properties;

    // 缓存属性
    private MaterialProperty useDistanceAtten;
    private MaterialProperty useVerShadow;
    private MaterialProperty useSelfShadow;
    private MaterialProperty baseColor;
    private MaterialProperty useAlpha;
    private MaterialProperty baseMap;
    private MaterialProperty furMap;
    private MaterialProperty noiseMap;
    private MaterialProperty noiseBendStrength;
    private MaterialProperty shellAmount;
    private MaterialProperty furLength;
    private MaterialProperty alphaOffset;
    private MaterialProperty alphaCutout;
    private MaterialProperty furScale;
    private MaterialProperty occlusion;
    private MaterialProperty useTouch;
    private MaterialProperty useWind;
    private MaterialProperty baseMove;
    private MaterialProperty windFreq;
    private MaterialProperty windMove;
    private MaterialProperty faceViewProdThresh;
    private MaterialProperty touchPosition;
    private MaterialProperty touchRadius;
    private MaterialProperty maxDepression;
    private MaterialProperty useWindCone;
    private MaterialProperty windConePosition;
    private MaterialProperty windConeDirection;
    private MaterialProperty windConeAngle;
    private MaterialProperty windConeRange;
    private MaterialProperty windConeFrequencyBoost;

    // 存档路径
    private const string SAVE_FOLDER = "Library/VicTools/FurShell/";

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        m_MaterialEditor = materialEditor;
        m_Properties = properties;

        // 查找所有属性
        FindProperties();

        // 绘制存档/读档/重置按钮
        DrawArchiveButtons();

        EditorGUILayout.Space(10);

        // 绘制各个分组
        DrawRenderSettings();
        EditorGUILayout.Space(5);
        DrawBaseProperties();
        EditorGUILayout.Space(5);
        DrawFurProperties();
        EditorGUILayout.Space(5);
        DrawWindSettings();
        EditorGUILayout.Space(5);
        DrawTouchSettings();
        EditorGUILayout.Space(5);
        DrawWindConeSettings();
        EditorGUILayout.Space(5);
        DrawAdvancedSettings();
    }

    private void FindProperties()
    {
        useDistanceAtten = FindProperty("_UseDistanceAtten", m_Properties, false);
        useVerShadow = FindProperty("_UseVerShadow", m_Properties, false);
        useSelfShadow = FindProperty("_UseSelfShadow", m_Properties, false);
        baseColor = FindProperty("_BaseColor", m_Properties, false);
        useAlpha = FindProperty("_UseAlpha", m_Properties, false);
        baseMap = FindProperty("_BaseMap", m_Properties, false);
        furMap = FindProperty("_FurMap", m_Properties, false);
        noiseMap = FindProperty("_NoiseMap", m_Properties, false);
        noiseBendStrength = FindProperty("_NoiseBendStrength", m_Properties, false);
        shellAmount = FindProperty("_ShellAmount", m_Properties, false);
        furLength = FindProperty("_FurLength", m_Properties, false);
        alphaOffset = FindProperty("_AlphaOffset", m_Properties, false);
        alphaCutout = FindProperty("_AlphaCutout", m_Properties, false);
        furScale = FindProperty("_FurScale", m_Properties, false);
        occlusion = FindProperty("_Occlusion", m_Properties, false);
        useTouch = FindProperty("_UseTouch", m_Properties, false);
        useWind = FindProperty("_UseWind", m_Properties, false);
        baseMove = FindProperty("_BaseMove", m_Properties, false);
        windFreq = FindProperty("_WindFreq", m_Properties, false);
        windMove = FindProperty("_WindMove", m_Properties, false);
        faceViewProdThresh = FindProperty("_FaceViewProdThresh", m_Properties, false);
        touchPosition = FindProperty("_TouchPosition", m_Properties, false);
        touchRadius = FindProperty("_TouchRadius", m_Properties, false);
        maxDepression = FindProperty("_MaxDepression", m_Properties, false);
        useWindCone = FindProperty("_UseWindCone", m_Properties, false);
        windConePosition = FindProperty("_WindConePosition", m_Properties, false);
        windConeDirection = FindProperty("_WindConeDirection", m_Properties, false);
        windConeAngle = FindProperty("_WindConeAngle", m_Properties, false);
        windConeRange = FindProperty("_WindConeRange", m_Properties, false);
        windConeFrequencyBoost = FindProperty("_WindConeFrequencyBoost", m_Properties, false);
    }

    private void DrawArchiveButtons()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("毛发设置", EditorStyles.boldLabel);
        
        // 添加存档按钮
        GUI.backgroundColor = new Color(0.3f, 0.8f, 1.0f); // 蓝色背景
        if (GUILayout.Button("存档", GUILayout.Width(50)))
        {
            EditorApplication.delayCall += SaveParameters;
        }
        
        // 添加读档按钮
        GUI.backgroundColor = new Color(0.5f, 1.0f, 0.5f); // 绿色背景
        if (GUILayout.Button("读档", GUILayout.Width(50)))
        {
            EditorApplication.delayCall += LoadParameters;
        }
        
        // 添加重置按钮
        GUI.backgroundColor = new Color(1.0f, 0.8f, 0.3f); // 黄色背景
        if (GUILayout.Button("重置参数", GUILayout.Width(60)))
        {
            EditorApplication.delayCall += ResetParameters;
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    private void DrawRenderSettings()
    {
        EditorGUILayout.LabelField("渲染设置", EditorStyles.boldLabel);
        
        if (useDistanceAtten != null)
            m_MaterialEditor.ShaderProperty(useDistanceAtten, "使用距离衰减");
        if (useVerShadow != null)
            m_MaterialEditor.ShaderProperty(useVerShadow, "使用顶点阴影");
        if (useSelfShadow != null)
            m_MaterialEditor.ShaderProperty(useSelfShadow, "使用自身阴影");
    }

    private void DrawBaseProperties()
    {
        EditorGUILayout.LabelField("基础属性", EditorStyles.boldLabel);
        
        if (baseColor != null)
            m_MaterialEditor.ShaderProperty(baseColor, "基础颜色");
        if (useAlpha != null)
            m_MaterialEditor.ShaderProperty(useAlpha, "使用Alpha");
        if (baseMap != null)
        {
            // 使用TextureProperty显示完整的贴图控制（包含Tiling和Offset）
            m_MaterialEditor.TextureProperty(baseMap, "基础贴图");
        }
        if (furMap != null)
            m_MaterialEditor.TexturePropertySingleLine(new GUIContent("毛发贴图"), furMap);
        if (noiseMap != null)
        {
            m_MaterialEditor.TextureProperty(noiseMap, "弯曲Noise贴图");
            EditorGUILayout.HelpBox("RG通道控制毛发横向弯曲偏移，推荐使用低频noise纹理", MessageType.Info);
        }
        if (noiseBendStrength != null)
            m_MaterialEditor.ShaderProperty(noiseBendStrength, "弯曲强度");
    }

    private void DrawFurProperties()
    {
        EditorGUILayout.LabelField("毛发属性", EditorStyles.boldLabel);
        
        if (shellAmount != null)
            m_MaterialEditor.ShaderProperty(shellAmount, "毛发层数");
        if (furLength != null)
            m_MaterialEditor.ShaderProperty(furLength, "毛发长度");
        if (alphaOffset != null)
            m_MaterialEditor.ShaderProperty(alphaOffset, "Alpha偏移");
        if (alphaCutout != null)
            m_MaterialEditor.ShaderProperty(alphaCutout, "Alpha裁剪");
        if (furScale != null)
            m_MaterialEditor.ShaderProperty(furScale, "毛发密度");
        if (occlusion != null)
            m_MaterialEditor.ShaderProperty(occlusion, "光影遮蔽");
    }

    private void DrawWindSettings()
    {
        
        EditorGUILayout.HelpBox("xyz: 基础移动方向, w: 移动因子指数", MessageType.Info);
        m_MaterialEditor.ShaderProperty(baseMove, "基础移动");
        EditorGUILayout.LabelField("风力设置", EditorStyles.boldLabel);
        
        if (useWind != null)
            m_MaterialEditor.ShaderProperty(useWind, "使用风力");
        
        if (useWind != null && useWind.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            if (windFreq != null)
            {
                EditorGUILayout.HelpBox("xyz: 风频率向量, 值越大变化越快", MessageType.Info);
                m_MaterialEditor.ShaderProperty(windFreq, "风频率");
            }
            if (windMove != null)
            {
                EditorGUILayout.HelpBox("xyz: 风移动幅度, w: 空间频率", MessageType.Info);
                m_MaterialEditor.ShaderProperty(windMove, "风移动");
            }
            EditorGUI.indentLevel--;
        }
    }

    private void DrawTouchSettings()
    {
        EditorGUILayout.LabelField("触摸设置", EditorStyles.boldLabel);
        
        if (useTouch != null)
            m_MaterialEditor.ShaderProperty(useTouch, "使用触摸");
        
        if (useTouch != null && useTouch.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            
            // 添加挂载脚本按钮
            GUI.backgroundColor = new Color(0.7f, 0.9f, 1.0f); // 浅蓝色
            if (GUILayout.Button("挂载脚本", GUILayout.Width(100)))
            {
                AttachTouchDeformationController();
            }
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.Space(5);
            
            if (touchPosition != null)
            {
                m_MaterialEditor.ShaderProperty(touchPosition, "触摸位置");
                EditorGUILayout.HelpBox("xyz: 世界空间位置, w: 强度", MessageType.Info);
            }
            if (touchRadius != null)
                m_MaterialEditor.ShaderProperty(touchRadius, "触摸半径");
            if (maxDepression != null)
                m_MaterialEditor.ShaderProperty(maxDepression, "最大凹陷度");
            EditorGUI.indentLevel--;
        }
    }

    private void DrawWindConeSettings()
    {
        EditorGUILayout.LabelField("圆锥风力设置", EditorStyles.boldLabel);
        
        // 手动绘制Toggle，完全控制其行为
        if (useWindCone != null)
        {
            Material material = m_MaterialEditor.target as Material;
            
            // 从材质直接读取实际值
            float actualValue = 0.0f;
            if (material != null && material.HasProperty("_UseWindCone"))
            {
                actualValue = material.GetFloat("_UseWindCone");
            }
            
            EditorGUI.BeginChangeCheck();
            bool currentValue = actualValue > 0.5f;
            bool newValue = EditorGUILayout.Toggle("使用圆锥风力", currentValue);
            
            if (EditorGUI.EndChangeCheck())
            {
                if (material != null)
                {
                    Undo.RecordObject(material, "Toggle Wind Cone");
                    material.SetFloat("_UseWindCone", newValue ? 1.0f : 0.0f);
                    useWindCone.floatValue = newValue ? 1.0f : 0.0f;
                    
                    // 如果关闭圆锥风力，重置相关参数到默认值
                    if (!newValue)
                    {
                        material.SetVector("_WindConePosition", new Vector4(0, 0, 0, 1));
                        material.SetVector("_WindConeDirection", new Vector4(0, 1, 0, 0));
                        material.SetFloat("_WindConeAngle", 30.0f);
                        material.SetFloat("_WindConeRange", 5.0f);
                        material.SetFloat("_WindConeFrequencyBoost", 2.0f);
                        Debug.Log("已关闭圆锥风力并重置相关参数");
                    }
                    
                    // 调试信息
                    Debug.Log($"UseWindCone设置为: {(newValue ? 1.0f : 0.0f)}, 材质值: {material.GetFloat("_UseWindCone")}");
                    
                    // 如果启用圆锥风力，自动启用风力
                    if (newValue && useWind != null)
                    {
                        float windValue = material.GetFloat("_UseWind");
                        if (windValue < 0.5f)
                        {
                            material.SetFloat("_UseWind", 1.0f);
                            useWind.floatValue = 1.0f;
                            Debug.Log("圆锥风力需要启用风力，已自动启用'使用风力'选项");
                        }
                    }
                    
                    EditorUtility.SetDirty(material);
                    m_MaterialEditor.Repaint();
                }
            }
            
            // 检查是否启用了风力，如果没有则显示警告
            if (currentValue && material != null)
            {
                float windValue = material.GetFloat("_UseWind");
                if (windValue < 0.5f)
                {
                    EditorGUILayout.HelpBox("圆锥风力需要启用'使用风力'才能生效！", MessageType.Warning);
                }
            }
            
            // 检查场景中是否有WindConeController在控制材质
            if (Application.isPlaying || !Application.isPlaying)
            {
                WindConeController[] controllers = GameObject.FindObjectsOfType<WindConeController>();
                if (controllers.Length > 0)
                {
                    bool hasActiveController = false;
                    foreach (var controller in controllers)
                    {
                        if (controller.enabled && controller.enableWindCone)
                        {
                            hasActiveController = true;
                            break;
                        }
                    }
                    
                    if (hasActiveController)
                    {
                        EditorGUILayout.HelpBox("场景中有WindConeController脚本正在控制圆锥风力参数。如需手动控制，请禁用WindConeController组件或取消勾选其enableWindCone选项。", MessageType.Info);
                    }
                }
            }
        }
        
        // 使用材质的实际值来判断是否显示子选项
        Material mat = m_MaterialEditor.target as Material;
        bool isEnabled = false;
        if (mat != null && mat.HasProperty("_UseWindCone"))
        {
            isEnabled = mat.GetFloat("_UseWindCone") > 0.5f;
        }
        
        if (isEnabled)
        {
            EditorGUI.indentLevel++;
            
            // 添加创建圆锥风场按钮
            GUI.backgroundColor = new Color(0.5f, 1.0f, 0.7f);
            if (GUILayout.Button("创建圆锥风场", GUILayout.Width(120)))
            {
                CreateWindCone();
            }
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.Space(5);
            
            // 手动绘制Vector4参数
            if (windConePosition != null && mat != null)
            {
                EditorGUI.BeginChangeCheck();
                Vector4 pos = mat.GetVector("_WindConePosition");
                pos = EditorGUILayout.Vector4Field("圆锥位置", pos);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(mat, "Change Wind Cone Position");
                    mat.SetVector("_WindConePosition", pos);
                    windConePosition.vectorValue = pos;
                    EditorUtility.SetDirty(mat);
                }
                EditorGUILayout.HelpBox("xyz: 圆锥中心位置, w: 强度倍增", MessageType.Info);
            }
            
            if (windConeDirection != null && mat != null)
            {
                EditorGUI.BeginChangeCheck();
                Vector4 dir = mat.GetVector("_WindConeDirection");
                dir = EditorGUILayout.Vector4Field("圆锥方向", dir);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(mat, "Change Wind Cone Direction");
                    mat.SetVector("_WindConeDirection", dir);
                    windConeDirection.vectorValue = dir;
                    EditorUtility.SetDirty(mat);
                }
                EditorGUILayout.HelpBox("xyz: 圆锥方向向量", MessageType.Info);
            }
            
            if (windConeAngle != null && mat != null)
            {
                EditorGUI.BeginChangeCheck();
                float angle = mat.GetFloat("_WindConeAngle");
                angle = EditorGUILayout.Slider("圆锥角度", angle, 0.0f, 90.0f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(mat, "Change Wind Cone Angle");
                    mat.SetFloat("_WindConeAngle", angle);
                    windConeAngle.floatValue = angle;
                    EditorUtility.SetDirty(mat);
                }
            }
            
            if (windConeRange != null && mat != null)
            {
                EditorGUI.BeginChangeCheck();
                float range = mat.GetFloat("_WindConeRange");
                range = EditorGUILayout.FloatField("圆锥范围", range);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(mat, "Change Wind Cone Range");
                    mat.SetFloat("_WindConeRange", range);
                    windConeRange.floatValue = range;
                    EditorUtility.SetDirty(mat);
                }
            }
            
            if (windConeFrequencyBoost != null && mat != null)
            {
                EditorGUI.BeginChangeCheck();
                float boost = mat.GetFloat("_WindConeFrequencyBoost");
                boost = EditorGUILayout.Slider("频率加成", boost, 0.0f, 10.0f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(mat, "Change Wind Cone Frequency Boost");
                    mat.SetFloat("_WindConeFrequencyBoost", boost);
                    windConeFrequencyBoost.floatValue = boost;
                    EditorUtility.SetDirty(mat);
                }
            }
            
            EditorGUI.indentLevel--;
        }
    }

    private void DrawAdvancedSettings()
    {
        EditorGUILayout.LabelField("高级设置", EditorStyles.boldLabel);
        
        if (faceViewProdThresh != null)
            m_MaterialEditor.ShaderProperty(faceViewProdThresh, "方向阈值");
    }

    // 存档功能 - 使用文件选择对话框
    private void SaveParameters()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;
        
        string shaderName = material.shader.name.Replace("/", "_");
        string defaultPath = SAVE_FOLDER + shaderName;
        
        // 确保目录存在
        if (!Directory.Exists(defaultPath))
        {
            Directory.CreateDirectory(defaultPath);
        }
        
        // 弹出文件保存对话框
        string presetPath = EditorUtility.SaveFilePanel(
            "保存毛发材质参数",
            defaultPath,
            "FurPreset",
            "json"
        );
        
        if (string.IsNullOrEmpty(presetPath)) return;
        
        // 构建JSON
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        
        // 保存所有参数
        AppendFloat(sb, "_UseDistanceAtten", useDistanceAtten, material);
        AppendFloat(sb, "_UseVerShadow", useVerShadow, material);
        AppendFloat(sb, "_UseSelfShadow", useSelfShadow, material);
        AppendColor(sb, "_BaseColor", baseColor, material);
        AppendFloat(sb, "_UseAlpha", useAlpha, material);
        AppendFloat(sb, "_ShellAmount", shellAmount, material);
        AppendFloat(sb, "_FurLength", furLength, material);
        AppendFloat(sb, "_AlphaOffset", alphaOffset, material);
        AppendFloat(sb, "_AlphaCutout", alphaCutout, material);
        AppendFloat(sb, "_FurScale", furScale, material);
        AppendFloat(sb, "_Occlusion", occlusion, material);
        AppendFloat(sb, "_UseTouch", useTouch, material);
        AppendFloat(sb, "_UseWind", useWind, material);
        AppendVector(sb, "_BaseMove", baseMove, material);
        AppendVector(sb, "_WindFreq", windFreq, material);
        AppendVector(sb, "_WindMove", windMove, material);
        AppendFloat(sb, "_FaceViewProdThresh", faceViewProdThresh, material);
        AppendVector(sb, "_TouchPosition", touchPosition, material);
        AppendFloat(sb, "_TouchRadius", touchRadius, material);
        AppendFloat(sb, "_MaxDepression", maxDepression, material);
        AppendFloat(sb, "_UseWindCone", useWindCone, material);
        AppendVector(sb, "_WindConePosition", windConePosition, material);
        AppendVector(sb, "_WindConeDirection", windConeDirection, material);
        AppendFloat(sb, "_WindConeAngle", windConeAngle, material);
        AppendFloat(sb, "_WindConeRange", windConeRange, material);
        AppendFloat(sb, "_WindConeFrequencyBoost", windConeFrequencyBoost, material);
        AppendFloat(sb, "_NoiseBendStrength", noiseBendStrength, material, false);
        if (material.HasProperty("_BaseMap"))
        {
            Vector2 scale = material.GetTextureScale("_BaseMap");
            Vector2 offset = material.GetTextureOffset("_BaseMap");
            sb.AppendLine(",");
            sb.Append($"  \"_BaseMap_Scale\": [{scale.x}, {scale.y}],");
            sb.AppendLine();
            sb.Append($"  \"_BaseMap_Offset\": [{offset.x}, {offset.y}]");
        }
        
        if (material.HasProperty("_NoiseMap"))
        {
            Vector2 scale = material.GetTextureScale("_NoiseMap");
            Vector2 offset = material.GetTextureOffset("_NoiseMap");
            sb.AppendLine(",");
            sb.Append($"  \"_NoiseMap_Scale\": [{scale.x}, {scale.y}],");
            sb.AppendLine();
            sb.Append($"  \"_NoiseMap_Offset\": [{offset.x}, {offset.y}]");
        }
        
        sb.AppendLine();
        sb.AppendLine("}");
        
        // 保存到文件
        File.WriteAllText(presetPath, sb.ToString());
        
        Debug.Log($"毛发材质参数已保存到: {presetPath}");
        EditorUtility.DisplayDialog("存档成功", $"参数已保存到:\n{presetPath}", "确定");
    }

    // 读档功能 - 使用文件选择对话框
    private void LoadParameters()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;
        
        string shaderName = material.shader.name.Replace("/", "_");
        string defaultPath = SAVE_FOLDER + shaderName;
        
        // 确保目录存在
        if (!Directory.Exists(defaultPath))
        {
            Directory.CreateDirectory(defaultPath);
        }
        
        // 弹出文件选择对话框
        string presetPath = EditorUtility.OpenFilePanel(
            "加载毛发材质参数",
            defaultPath,
            "json"
        );
        
        if (string.IsNullOrEmpty(presetPath)) return;
        
        if (!File.Exists(presetPath))
        {
            EditorUtility.DisplayDialog("读档失败", "文件不存在", "确定");
            return;
        }
        
        // 记录撤销操作
        Undo.RecordObject(material, "Load Fur Material Parameters");
        
        // 读取JSON
        string json = File.ReadAllText(presetPath);
        
        // 简单的JSON解析
        var lines = json.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            if (line.Contains(":"))
            {
                string[] parts = line.Split(':');
                if (parts.Length < 2) continue;
                
                string key = parts[0].Trim().Trim('"', ',', ' ');
                string value = parts[1].Trim().Trim(',', ' ');
                
                // 解析不同类型的值
                if (value.StartsWith("["))
                {
                    // Vector或Color
                    value = value.Trim('[', ']');
                    string[] values = value.Split(',');
                    
                    if (key == "_BaseMap_Scale" && values.Length >= 2)
                    {
                        Vector2 scale = new Vector2(
                            float.Parse(values[0].Trim()),
                            float.Parse(values[1].Trim())
                        );
                        material.SetTextureScale("_BaseMap", scale);
                    }
                    else if (key == "_BaseMap_Offset" && values.Length >= 2)
                    {
                        Vector2 offset = new Vector2(
                            float.Parse(values[0].Trim()),
                            float.Parse(values[1].Trim())
                        );
                        material.SetTextureOffset("_BaseMap", offset);
                    }
                    else if (key == "_NoiseMap_Scale" && values.Length >= 2)
                    {
                        Vector2 scale = new Vector2(
                            float.Parse(values[0].Trim()),
                            float.Parse(values[1].Trim())
                        );
                        material.SetTextureScale("_NoiseMap", scale);
                    }
                    else if (key == "_NoiseMap_Offset" && values.Length >= 2)
                    {
                        Vector2 offset = new Vector2(
                            float.Parse(values[0].Trim()),
                            float.Parse(values[1].Trim())
                        );
                        material.SetTextureOffset("_NoiseMap", offset);
                    }
                    else if (values.Length == 4)
                    {
                        // Color或Vector4
                        if (material.HasProperty(key))
                        {
                            Vector4 vec = new Vector4(
                                float.Parse(values[0].Trim()),
                                float.Parse(values[1].Trim()),
                                float.Parse(values[2].Trim()),
                                float.Parse(values[3].Trim())
                            );
                            
                            // 判断是Color还是Vector
                            Shader shader = material.shader;
                            int propIndex = shader.FindPropertyIndex(key);
                            if (propIndex >= 0)
                            {
                                var propType = shader.GetPropertyType(propIndex);
                                if (propType == UnityEngine.Rendering.ShaderPropertyType.Color)
                                {
                                    material.SetColor(key, new Color(vec.x, vec.y, vec.z, vec.w));
                                }
                                else
                                {
                                    material.SetVector(key, vec);
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Float
                    if (material.HasProperty(key))
                    {
                        float floatValue;
                        if (float.TryParse(value, out floatValue))
                        {
                            material.SetFloat(key, floatValue);
                        }
                    }
                }
            }
        }
        
        // 同步shader keywords
        UpdateShaderKeywords(material);
        
        // 清除不需要的keywords（_UseTouch和_UseWindCone现在使用ToggleUI）
        material.DisableKeyword("_USETOUCH");
        material.DisableKeyword("_USEWINDCONE");
        
        EditorUtility.SetDirty(material);
        
        // 刷新材质编辑器
        if (m_MaterialEditor != null)
        {
            m_MaterialEditor.Repaint();
        }
        
        // 刷新场景视图
        SceneView.RepaintAll();
        
        Debug.Log($"毛发材质参数已从 {presetPath} 加载");
        // EditorUtility.DisplayDialog("读档成功", "参数已恢复", "确定");
    }
    
    // 更新shader keywords
    private void UpdateShaderKeywords(Material material)
    {
        if (material == null) return;
        
        // 根据参数值更新keywords
        SetKeyword(material, "_USEDISTANCEATTEN", material.GetFloat("_UseDistanceAtten") > 0.5f);
        SetKeyword(material, "_USEVERSHADOW", material.GetFloat("_UseVerShadow") > 0.5f);
        SetKeyword(material, "_USESELFSHADOW", material.GetFloat("_UseSelfShadow") > 0.5f);
        // _UseTouch 和 _UseWindCone 使用 ToggleUI，不需要keyword
    }
    
    // 设置shader keyword
    private void SetKeyword(Material material, string keyword, bool enabled)
    {
        if (enabled)
            material.EnableKeyword(keyword);
        else
            material.DisableKeyword(keyword);
    }

    // 重置功能
    private void ResetParameters()
    {
        if (!EditorUtility.DisplayDialog("重置参数", "确定要重置所有参数到默认值吗？", "确定", "取消"))
            return;

        Material material = m_MaterialEditor.target as Material;
        if (material == null) return;
        
        // 记录撤销操作
        Undo.RecordObject(material, "Reset Fur Material Parameters");

        // 清除不需要的keywords（_UseTouch和_UseWindCone现在使用ToggleUI）
        material.DisableKeyword("_USETOUCH");
        material.DisableKeyword("_USEWINDCONE");

        // 重置为shader中定义的默认值
        SetFloat(useDistanceAtten, material, 0.0f);
        SetFloat(useVerShadow, material, 1.0f);
        SetFloat(useSelfShadow, material, 1.0f);
        SetColor(baseColor, material, Color.white);
        SetFloat(useAlpha, material, 1.0f);
        SetFloat(shellAmount, material, 8.0f);
        SetFloat(furLength, material, 0.004f);
        SetFloat(alphaOffset, material, 0.06f);
        SetFloat(alphaCutout, material, 0.3f);
        SetFloat(furScale, material, 1.0f);
        SetFloat(occlusion, material, 0.23f);
        SetFloat(useTouch, material, 0.0f);
        SetFloat(useWind, material, 0.0f);
        SetVector(baseMove, material, new Vector4(0.9f, -1.0f, 0.0f, 2.6f));
        SetVector(windFreq, material, new Vector4(2.5f, 0.7f, 0.9f, 1.0f));
        SetVector(windMove, material, new Vector4(1.2f, 0.3f, 0.2f, 2.0f));
        SetFloat(faceViewProdThresh, material, 0.001f);
        SetVector(touchPosition, material, new Vector4(0, 0, 0, 1));
        SetFloat(touchRadius, material, 1.0f);
        SetFloat(maxDepression, material, 1.3f);
        SetFloat(useWindCone, material, 0.0f);
        SetVector(windConePosition, material, new Vector4(0, 0, 0, 1));
        SetVector(windConeDirection, material, new Vector4(0, 1, 0, 0));
        SetFloat(windConeAngle, material, 30.0f);
        SetFloat(windConeRange, material, 5.0f);
        SetFloat(windConeFrequencyBoost, material, 2.0f);
        SetFloat(noiseBendStrength, material, 0.3f);
        
        // 重置贴图的Tiling和Offset
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTextureScale("_BaseMap", Vector2.one);
            material.SetTextureOffset("_BaseMap", Vector2.zero);
        }
        if (material.HasProperty("_NoiseMap"))
        {
            material.SetTextureScale("_NoiseMap", Vector2.one);
            material.SetTextureOffset("_NoiseMap", Vector2.zero);
        }
        
        // 更新需要的keywords
        UpdateShaderKeywords(material);
        
        EditorUtility.SetDirty(material);
        EditorUtility.DisplayDialog("重置成功", "所有参数已重置为默认值", "确定");
    }

    // JSON构建辅助方法
    private void AppendFloat(System.Text.StringBuilder sb, string name, MaterialProperty prop, Material material, bool addComma = true)
    {
        if (prop != null && material.HasProperty(name))
        {
            sb.Append($"  \"{name}\": {material.GetFloat(name)}");
            if (addComma) sb.Append(",");
            sb.AppendLine();
        }
    }

    private void AppendColor(System.Text.StringBuilder sb, string name, MaterialProperty prop, Material material, bool addComma = true)
    {
        if (prop != null && material.HasProperty(name))
        {
            Color c = material.GetColor(name);
            sb.Append($"  \"{name}\": [{c.r}, {c.g}, {c.b}, {c.a}]");
            if (addComma) sb.Append(",");
            sb.AppendLine();
        }
    }

    private void AppendVector(System.Text.StringBuilder sb, string name, MaterialProperty prop, Material material, bool addComma = true)
    {
        if (prop != null && material.HasProperty(name))
        {
            Vector4 v = material.GetVector(name);
            sb.Append($"  \"{name}\": [{v.x}, {v.y}, {v.z}, {v.w}]");
            if (addComma) sb.Append(",");
            sb.AppendLine();
        }
    }

    private void SetFloat(MaterialProperty prop, Material material, float value)
    {
        if (prop != null)
            material.SetFloat(prop.name, value);
    }

    private void SetColor(MaterialProperty prop, Material material, Color value)
    {
        if (prop != null)
            material.SetColor(prop.name, value);
    }

    private void SetVector(MaterialProperty prop, Material material, Vector4 value)
    {
        if (prop != null)
            material.SetVector(prop.name, value);
    }
    
    // 挂载TouchDeformationController脚本到使用该材质的对象
    private void AttachTouchDeformationController()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null) return;
        
        // 查找场景中所有使用该材质的Renderer
        Renderer[] allRenderers = GameObject.FindObjectsOfType<Renderer>();
        int attachedCount = 0;
        int alreadyHasCount = 0;
        
        foreach (Renderer renderer in allRenderers)
        {
            // 检查是否使用了该材质
            bool usesMaterial = false;
            foreach (Material mat in renderer.sharedMaterials)
            {
                if (mat == material)
                {
                    usesMaterial = true;
                    break;
                }
            }
            
            if (usesMaterial)
            {
                GameObject obj = renderer.gameObject;
                
                // 检查是否已经有该脚本
                if (obj.GetComponent<TouchDeformationController>() != null)
                {
                    alreadyHasCount++;
                    continue;
                }
                
                // 挂载脚本
                Undo.AddComponent<TouchDeformationController>(obj);
                attachedCount++;
                Debug.Log($"已为 {obj.name} 挂载 TouchDeformationController 脚本");
            }
        }
        
        // 显示结果
        string message = "";
        if (attachedCount > 0)
            message += $"成功挂载 {attachedCount} 个对象\n";
        if (alreadyHasCount > 0)
            message += $"{alreadyHasCount} 个对象已有该脚本\n";
        if (attachedCount == 0 && alreadyHasCount == 0)
            message = "场景中没有找到使用该材质的对象";
        
        EditorUtility.DisplayDialog("挂载脚本", message.TrimEnd(), "确定");
    }
    
    // 创建圆锥风场对象
    private void CreateWindCone()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null) return;
        
        // 创建WindZone对象
        GameObject windZoneObj = new GameObject("WindCone_" + material.name);
        
        // 添加WindZone组件
        WindZone windZone = windZoneObj.AddComponent<WindZone>();
        windZone.mode = WindZoneMode.Directional;
        windZone.windMain = 1.0f;
        windZone.windTurbulence = 0.5f;
        windZone.windPulseMagnitude = 0.5f;
        windZone.windPulseFrequency = 0.25f;
        
        // 添加WindConeController脚本
        WindConeController controller = windZoneObj.AddComponent<WindConeController>();
        
        // 从材质读取初始参数
        if (material.HasProperty("_WindConePosition"))
        {
            Vector4 pos = material.GetVector("_WindConePosition");
            windZoneObj.transform.position = new Vector3(pos.x, pos.y, pos.z);
        }
        
        if (material.HasProperty("_WindConeDirection"))
        {
            Vector4 dir = material.GetVector("_WindConeDirection");
            Vector3 direction = new Vector3(dir.x, dir.y, dir.z);
            if (direction != Vector3.zero)
            {
                windZoneObj.transform.rotation = Quaternion.LookRotation(direction);
            }
        }
        
        // 注册撤销操作
        Undo.RegisterCreatedObjectUndo(windZoneObj, "Create Wind Cone");
        
        // 选中新创建的对象
        Selection.activeGameObject = windZoneObj;
        
        Debug.Log($"已创建圆锥风场对象: {windZoneObj.name}");
        EditorUtility.DisplayDialog("创建成功", 
            $"已创建圆锥风场对象: {windZoneObj.name}\n" +
            "请在场景中调整位置和方向\n" +
            "WindConeController脚本会自动更新材质参数", 
            "确定");
    }
}
