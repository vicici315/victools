// 5.8 添加存档读档功能按钮
// 6.0 支持多个材质球读档
// 6.1 添加【统一阴影】按钮，用于统一设置“自身阴影衰减”值，使场景中阴影保持一致的明暗度（包括PBR_Mobile_Trans）
using UnityEngine;
using UnityEditor;

public class PBR_MobileGUI : ShaderGUI
{
    private MaterialEditor m_MaterialEditor;
    private MaterialProperty[] m_Properties;
    private bool isTransShader = false; // 标记是否为 Trans 版本的 Shader

    // 缓存属性
    private MaterialProperty disableEnvironment;
    private MaterialProperty useVerShadow;
    private MaterialProperty baseColor;
    private MaterialProperty baseMap;
    private MaterialProperty metallic;
    private MaterialProperty roughness;
    private MaterialProperty specularScale;
    private MaterialProperty halfLambert;
    private MaterialProperty shadowScale;
    private MaterialProperty brightness;
    private MaterialProperty bakedSpecularDirection;
    private MaterialProperty useMsaMap;
    private MaterialProperty metallicGlossMap;
    private MaterialProperty useAOMap;
    private MaterialProperty occlusionContrast;
    private MaterialProperty occlusionStrength;
    private MaterialProperty previewAOMap;
    private MaterialProperty useNormalMap;
    private MaterialProperty bumpMap;
    private MaterialProperty bumpScale;
    private MaterialProperty filpG;
    private MaterialProperty debugNormal;
    private MaterialProperty useEmissionMap;
    private MaterialProperty emissionColor;
    private MaterialProperty emissionMap;
    private MaterialProperty emissionScale;
    private MaterialProperty invertEmisMap;
    private MaterialProperty useReflection;
    private MaterialProperty sphericalReflectionMap;
    private MaterialProperty reflectionStrength;
    private MaterialProperty reflectionBlur;
    private MaterialProperty reflectionFresnelPower;
    private MaterialProperty reflectionFresnelBias;
    private MaterialProperty usePointlight;
    private MaterialProperty pointLightIntensity;
    private MaterialProperty pointLightRangeMultiplier;
    private MaterialProperty pointLightFalloff;
    private MaterialProperty pointLightAmount;
    private MaterialProperty useSpotlight;
    private MaterialProperty spotLightIntensity;
    private MaterialProperty spotLightRangeMultiplier;
    private MaterialProperty spotLightFalloff;
    private MaterialProperty spotLightAmount;
    private MaterialProperty useSpotTexture;
    private MaterialProperty spotTexture;
    private MaterialProperty spotTextureContrast;
    private MaterialProperty spotTextureSize;
    private MaterialProperty spotTextureIntensity;
    private MaterialProperty cullMode;
    private MaterialProperty _Cutoff;
    private MaterialProperty _SrcBlend;
    private MaterialProperty _DstBlend;
    private MaterialProperty _ZWrite;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        m_MaterialEditor = materialEditor;
        m_Properties = properties;

        // 检测是否为 Trans 版本的 Shader
        Material material = materialEditor.target as Material;
        if (material != null && material.shader != null)
        {
            isTransShader = material.shader.name.Contains("PBR_Mobile_Trans");
        }

        // 查找所有属性
        FindProperties();

        // 绘制 GUI
        DrawGlobalSettings();
        
        // EditorGUILayout.Space(5);
        DrawBaseProperties();
        DrawMetallicRoughnessAO();
        // EditorGUILayout.Space(5);
        DrawNormalMap();
        // EditorGUILayout.Space(5);
        DrawEmission();
        // EditorGUILayout.Space(5);
        
        // 反射功能对两个版本都可用
        DrawReflection();
        
        // 只在非 Trans 版本显示点光源和聚光灯
        if (!isTransShader)
        {
            DrawPointLights();
            DrawSpotLights();
            // EditorGUILayout.Space(5);
        }        
        
        if (isTransShader)
        {
            DrawAlphaCull();
        }
        EditorGUILayout.Space(5);
        DrawPerformance();
    }

    private void FindProperties()
    {
        disableEnvironment = FindProperty("_DisableEnvironment", m_Properties, false);
        useVerShadow = FindProperty("_UseVerShadow", m_Properties, false);
        baseColor = FindProperty("_BaseColor", m_Properties);
        baseMap = FindProperty("_BaseMap", m_Properties);
        metallic = FindProperty("_Metallic", m_Properties);
        roughness = FindProperty("_Roughness", m_Properties);
        specularScale = FindProperty("_SpecularScale", m_Properties);
        halfLambert = FindProperty("_HalfLambert", m_Properties);
        shadowScale = FindProperty("_ShadowScale", m_Properties);
        brightness = FindProperty("_Brightness", m_Properties);
        bakedSpecularDirection = FindProperty("_BakedSpecularDirection", m_Properties);
        useMsaMap = FindProperty("_UseMsaMap", m_Properties, false);
        metallicGlossMap = FindProperty("_MetallicGlossMap", m_Properties);
        useAOMap = FindProperty("_UseAOMap", m_Properties, false);
        occlusionContrast = FindProperty("_OcclusionContrast", m_Properties);
        occlusionStrength = FindProperty("_OcclusionStrength", m_Properties);
        previewAOMap = FindProperty("_PreviewAOMap", m_Properties, false);
        useNormalMap = FindProperty("_UseNormalMap", m_Properties, false);
        bumpMap = FindProperty("_BumpMap", m_Properties);
        bumpScale = FindProperty("_BumpScale", m_Properties);
        filpG = FindProperty("_FilpG", m_Properties, false);
        debugNormal = FindProperty("_DebugNormal", m_Properties, false);
        useEmissionMap = FindProperty("_UseEmissionMap", m_Properties, false);
        emissionColor = FindProperty("_EmissionColor", m_Properties);
        emissionMap = FindProperty("_EmissionMap", m_Properties);
        emissionScale = FindProperty("_EmissionScale", m_Properties);
        invertEmisMap = FindProperty("_InvertEmisMap", m_Properties, false);
        useReflection = FindProperty("_UseReflection", m_Properties, false);
        sphericalReflectionMap = FindProperty("_SphericalReflectionMap", m_Properties);
        reflectionStrength = FindProperty("_ReflectionStrength", m_Properties);
        reflectionBlur = FindProperty("_ReflectionBlur", m_Properties);
        reflectionFresnelPower = FindProperty("_ReflectionFresnelPower", m_Properties);
        reflectionFresnelBias = FindProperty("_ReflectionFresnelBias", m_Properties);
        usePointlight = FindProperty("_UsePointlight", m_Properties, false);
        pointLightIntensity = FindProperty("_PointLightIntensity", m_Properties);
        pointLightRangeMultiplier = FindProperty("_PointLightRangeMultiplier", m_Properties);
        pointLightFalloff = FindProperty("_PointLightFalloff", m_Properties);
        pointLightAmount = FindProperty("_PointLightAmount", m_Properties);
        useSpotlight = FindProperty("_UseSpotlight", m_Properties, false);
        spotLightIntensity = FindProperty("_SpotLightIntensity", m_Properties);
        spotLightRangeMultiplier = FindProperty("_SpotLightRangeMultiplier", m_Properties);
        spotLightFalloff = FindProperty("_SpotLightFalloff", m_Properties);
        spotLightAmount = FindProperty("_SpotLightAmount", m_Properties);
        useSpotTexture = FindProperty("_UseSpotTexture", m_Properties, false);
        spotTexture = FindProperty("_SpotTexture", m_Properties);
        spotTextureContrast = FindProperty("_SpotTextureContrast", m_Properties);
        spotTextureSize = FindProperty("_SpotTextureSize", m_Properties);
        spotTextureIntensity = FindProperty("_SpotTextureIntensity", m_Properties);
        cullMode = FindProperty("_Cull", m_Properties);
        _Cutoff = FindProperty("_Cutoff", m_Properties);
        _SrcBlend = FindProperty("_SrcBlend", m_Properties, false);
        _DstBlend = FindProperty("_DstBlend", m_Properties, false);
        _ZWrite = FindProperty("_ZWrite", m_Properties, false);
    }

    private void DrawGlobalSettings()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("全局设置", EditorStyles.boldLabel);
        
        // 添加存档按钮
        GUI.backgroundColor = new Color(0.3f, 0.8f, 1.0f); // 蓝色背景
        if (GUILayout.Button(new GUIContent("存档", "保存当前材质参数到文件（不包含纹理和基础颜色）"), GUILayout.Width(50)))
        {
            EditorApplication.delayCall += SaveMaterialParameters;
        }
        
        // 添加读档按钮
        GUI.backgroundColor = new Color(0.5f, 1.0f, 0.5f); // 绿色背景
        if (GUILayout.Button(new GUIContent("读档", "从文件加载材质参数\n支持批量应用到多个选中的材质"), GUILayout.Width(50)))
        {
            EditorApplication.delayCall += LoadMaterialParameters;
        }
        
        // 添加重置按钮
        GUI.backgroundColor = new Color(1.0f, 0.8f, 0.3f); // 黄色背景
        if (GUILayout.Button(new GUIContent("重置参数", "重置材质参数为Default存档或Shader默认值"), GUILayout.Width(60)))
        {
            EditorApplication.delayCall += ResetMaterialParameters;
        }
        
        // 添加统一阴影按钮
        GUI.backgroundColor = new Color(1.0f, 0.6f, 0.3f); // 橙色背景
        if (GUILayout.Button(new GUIContent("统一阴影", "根据当前对象的Static状态\n统一设置场景中相同类型对象的【自身阴影衰减】参数"), GUILayout.Width(60)))
        {
            EditorApplication.delayCall += UnifyShadowScale;
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
        
        // 只在非 Trans 版本显示禁用环境光选项
        if (disableEnvironment != null)
        {
            m_MaterialEditor.ShaderProperty(disableEnvironment, "禁用环境光");
        }
        
        if (useVerShadow != null)
        {
            m_MaterialEditor.ShaderProperty(useVerShadow, "使用顶点阴影");
        }
    }

    private void DrawBaseProperties()
    {
        GUILayout.Label("1 ▌基础属性 (Base Properties)", EditorStyles.boldLabel);
        m_MaterialEditor.ColorProperty(baseColor, "基础颜色");
        m_MaterialEditor.TextureProperty(baseMap, "颜色贴图 (RGB)");
        if (isTransShader)
        {
            m_MaterialEditor.ShaderProperty(_Cutoff, "透明阈值");
        }
    }

    private void DrawMetallicRoughnessAO()
    {
        GUILayout.Label("2 ▌PBR参数 (Metallic、Roughness、AO)", EditorStyles.boldLabel);
        
        m_MaterialEditor.RangeProperty(metallic, "金属度");
        m_MaterialEditor.RangeProperty(roughness, "粗糙度");
        m_MaterialEditor.RangeProperty(specularScale, "高光强度");
        m_MaterialEditor.RangeProperty(halfLambert, "半兰伯特");
        m_MaterialEditor.RangeProperty(shadowScale, "自身阴影衰减");
        m_MaterialEditor.RangeProperty(brightness, "亮度");
        
        // 只在非 Trans 版本显示烘焙高光方向
        if (disableEnvironment != null && disableEnvironment.floatValue < 0.5f && bakedSpecularDirection != null)
        {
            m_MaterialEditor.VectorProperty(bakedSpecularDirection, "烘焙高光方向");
        }
        
        if (useMsaMap != null)
        {
            m_MaterialEditor.ShaderProperty(useMsaMap, "  使用金属度粗糙度(MRA贴图)");
        }
        
        if (useMsaMap.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("金属度(R) 粗糙度(G) AO(B) 基础色蒙版(A)", MessageType.Info);
            m_MaterialEditor.TexturePropertySingleLine(new GUIContent("MRA贴图"),metallicGlossMap);
            EditorGUI.indentLevel--;
            
            m_MaterialEditor.ShaderProperty(useAOMap, "  使用 AO(B) 通道");
            
            if (useAOMap.floatValue > 0.5f)
            {
                EditorGUI.indentLevel++;
                m_MaterialEditor.RangeProperty(occlusionContrast, "AO 对比度");
                m_MaterialEditor.RangeProperty(occlusionStrength, "AO 强度");
                m_MaterialEditor.ShaderProperty(previewAOMap, "预览 AO(B) 通道");
                EditorGUI.indentLevel--;
            }
            else
            {
                // 确保关键字被禁用
                foreach (Material mat in m_MaterialEditor.targets)
                {
                    mat.DisableKeyword("_USEAOMAP");
                    mat.DisableKeyword("_PREVIEWAO");
                }
            }
        }
        else
        {
            // 当 useMsaMap 关闭时，自动禁用 useAOMap 和相关关键字
            if (useAOMap.floatValue > 0.5f)
            {
                useAOMap.floatValue = 0;
            }
            if (previewAOMap.floatValue > 0.5f)
            {
                previewAOMap.floatValue = 0;
            }
            
            // 确保关键字被禁用
            foreach (Material mat in m_MaterialEditor.targets)
            {
                mat.DisableKeyword("_USEAOMAP");
                mat.DisableKeyword("_PREVIEWAO");
            }
        }
    }

    private void DrawNormalMap()
    {
        // GUILayout.Label("3 ▌法线贴图 (Normal Map)", EditorStyles.boldLabel);
        
        m_MaterialEditor.ShaderProperty(useNormalMap, "3 ▌使用法线贴图");
        
        if (useNormalMap.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            m_MaterialEditor.TexturePropertySingleLine(new GUIContent("法线贴图"), bumpMap);
            m_MaterialEditor.RangeProperty(bumpScale, "法线强度");
            
            if (filpG != null)
            {
                m_MaterialEditor.ShaderProperty(filpG, "翻转绿色通道");
            }
            
            // 只在非 Trans 版本显示调试法线贴图
            if (!isTransShader && debugNormal != null)
            {
                m_MaterialEditor.ShaderProperty(debugNormal, "调试法线贴图");
            }
            
            EditorGUI.indentLevel--;
        }
    }

    private void DrawEmission()
    {
        // GUILayout.Label("4 ▌自发光 (Emission)", EditorStyles.boldLabel);
        
        m_MaterialEditor.ShaderProperty(useEmissionMap, "4 ▌使用自发光贴图");
        
        if (useEmissionMap.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            // 将自发光颜色和贴图显示在同一行
            m_MaterialEditor.TexturePropertyWithHDRColor(new GUIContent("自发光"), emissionMap, emissionColor, false);
            m_MaterialEditor.RangeProperty(emissionScale, "自发光强度");
            m_MaterialEditor.ShaderProperty(invertEmisMap, "反转自发光贴图");
            EditorGUI.indentLevel--;
        }
    }

    private void DrawReflection()
    {
        // GUILayout.Label("5 ▌反射 (Reflection)", EditorStyles.boldLabel);
        
        m_MaterialEditor.ShaderProperty(useReflection, "5 ▌使用反射贴图");
        
        if (useReflection.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            m_MaterialEditor.TexturePropertySingleLine(new GUIContent("球形反射贴图"),sphericalReflectionMap);
            m_MaterialEditor.RangeProperty(reflectionStrength, "反射强度");
            m_MaterialEditor.RangeProperty(reflectionBlur, "反射模糊");
            m_MaterialEditor.RangeProperty(reflectionFresnelPower, "菲涅尔强度");
            m_MaterialEditor.RangeProperty(reflectionFresnelBias, "菲涅尔偏移");
            EditorGUI.indentLevel--;
        }
    }

    private void DrawPointLights()
    {
        // GUILayout.Label("6 ▌自定义点光源 (Custom Point Lights)", EditorStyles.boldLabel);
        m_MaterialEditor.ShaderProperty(usePointlight, "6 ▌使用点光源");
        
        if (usePointlight.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            m_MaterialEditor.RangeProperty(pointLightIntensity, "点光源强度");
            m_MaterialEditor.RangeProperty(pointLightRangeMultiplier, "范围倍增器");
            m_MaterialEditor.RangeProperty(pointLightFalloff, "衰减强度");
            m_MaterialEditor.RangeProperty(pointLightAmount, "光源数量");
            EditorGUI.indentLevel--;
        }
    }

    private void DrawSpotLights()
    {
        // GUILayout.Label("7 ▌自定义聚光灯 (Custom Spot Lights)", EditorStyles.boldLabel);
        m_MaterialEditor.ShaderProperty(useSpotlight, "7 ▌使用聚光灯");
        
        if (useSpotlight.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            m_MaterialEditor.RangeProperty(spotLightIntensity, "聚光灯强度");
            m_MaterialEditor.RangeProperty(spotLightRangeMultiplier, "范围倍增器");
            m_MaterialEditor.RangeProperty(spotLightFalloff, "衰减强度");
            m_MaterialEditor.RangeProperty(spotLightAmount, "光源数量");
            
            EditorGUILayout.Space(5);
            m_MaterialEditor.ShaderProperty(useSpotTexture, "使用聚光灯纹理");
            
            if (useSpotTexture.floatValue > 0.5f)
            {
                EditorGUI.indentLevel++;
                m_MaterialEditor.TexturePropertySingleLine(new GUIContent("聚光灯纹理"), spotTexture);
                m_MaterialEditor.RangeProperty(spotTextureContrast, "纹理对比度");
                m_MaterialEditor.RangeProperty(spotTextureSize, "纹理大小");
                m_MaterialEditor.RangeProperty(spotTextureIntensity, "纹理强度");
                EditorGUI.indentLevel--;
            }
            
            EditorGUI.indentLevel--;
        }
    }

    private void DrawAlphaCull()
    {
        EditorGUILayout.Space(5);
        if (_SrcBlend != null)
            m_MaterialEditor.ShaderProperty(_SrcBlend, "源混合模式");
        if (_DstBlend != null)
            m_MaterialEditor.ShaderProperty(_DstBlend, "目标混合模式");
        if (_ZWrite != null)
            m_MaterialEditor.ShaderProperty(_ZWrite, "深度写入");
    
    }
    private void DrawPerformance()
    {
        // GUILayout.Label("# ▌性能 (Performance)", EditorStyles.boldLabel);
        m_MaterialEditor.ShaderProperty(cullMode, "剔除模式");
    }

    /// 获取材质参数存档路径
    private string GetPresetPath(string presetName)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return null;
        
        string shaderName = material.shader.name.Replace("/", "_");
        string folderPath = "Library/VicTools/PBRM/" + shaderName;
        
        if (!System.IO.Directory.Exists(folderPath))
        {
            System.IO.Directory.CreateDirectory(folderPath);
        }
        
        return folderPath + "/" + presetName + ".json";
    }
    
    /// 存档材质参数（排除纹理）
    private void SaveMaterialParameters()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;
        
        string shaderName = material.shader.name.Replace("/", "_");
        string defaultPath = "Library/VicTools/PBRM/" + shaderName;
        
        // 确保目录存在
        if (!System.IO.Directory.Exists(defaultPath))
        {
            System.IO.Directory.CreateDirectory(defaultPath);
        }
        
        // 弹出输入框让用户输入存档名称
        string presetName = EditorUtility.SaveFilePanel(
            "保存材质参数存档",
            defaultPath,
            "MaterialPreset",
            "json"
        );
        
        if (string.IsNullOrEmpty(presetName)) return;
        
        // 提取文件名（不含扩展名）
        string fileName = System.IO.Path.GetFileNameWithoutExtension(presetName);
        
        SaveMaterialParametersToFile(fileName);
    }
    
    /// 保存材质参数到指定文件
    private void SaveMaterialParametersToFile(string presetName)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;
        
        Shader shader = material.shader;
        int propertyCount = ShaderUtil.GetPropertyCount(shader);
        
        // 手动构建JSON
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        bool first = true;
        
        for (int i = 0; i < propertyCount; i++)
        {
            string propertyName = ShaderUtil.GetPropertyName(shader, i);
            ShaderUtil.ShaderPropertyType propertyType = ShaderUtil.GetPropertyType(shader, i);
            
            if (!material.HasProperty(propertyName)) continue;
            
            // 排除纹理和基础颜色
            if (propertyType == ShaderUtil.ShaderPropertyType.TexEnv) continue;
            if (propertyName == "_BaseColor") continue;
            
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
            }
        }
        
        sb.AppendLine();
        sb.AppendLine("}");
        
        // 保存到文件
        string path = GetPresetPath(presetName);
        System.IO.File.WriteAllText(path, sb.ToString());
        
        Debug.Log($"材质参数已保存到: {path}");
    }
    
    /// 读档材质参数
    private void LoadMaterialParameters()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;
        
        string shaderName = material.shader.name.Replace("/", "_");
        string defaultPath = "Library/VicTools/PBRM/" + shaderName;
        
        // 确保目录存在
        if (!System.IO.Directory.Exists(defaultPath))
        {
            System.IO.Directory.CreateDirectory(defaultPath);
        }
        
        // 弹出文件选择框
        string presetPath = EditorUtility.OpenFilePanel(
            "加载材质参数存档",
            defaultPath,
            "json"
        );
        
        if (string.IsNullOrEmpty(presetPath)) return;
        
        // 获取所有选中的材质
        Object[] targets = m_MaterialEditor.targets;
        
        if (targets.Length > 1)
        {
            // 多选模式：批量应用
            if (EditorUtility.DisplayDialog("批量读档", 
                $"将对 {targets.Length} 个材质应用此存档。\n\n注意：纹理和基础颜色不会被修改。", 
                "确定", "取消"))
            {
                foreach (Object obj in targets)
                {
                    Material mat = obj as Material;
                    if (mat != null && mat.shader != null)
                    {
                        // 检查shader是否匹配
                        if (mat.shader.name == material.shader.name)
                        {
                            LoadMaterialParametersToMaterial(mat, presetPath);
                        }
                        else
                        {
                            Debug.LogWarning($"材质 {mat.name} 的Shader不匹配，已跳过。");
                        }
                    }
                }
                
                Debug.Log($"批量读档完成：已应用到 {targets.Length} 个材质");
            }
        }
        else
        {
            // 单选模式
            LoadMaterialParametersFromFile(presetPath);
        }
    }
    
    /// 从文件加载材质参数到指定材质
    private void LoadMaterialParametersToMaterial(Material material, string filePath)
    {
        if (material == null || material.shader == null) return;
        
        if (!System.IO.File.Exists(filePath))
        {
            Debug.LogWarning($"存档文件不存在: {filePath}");
            return;
        }
        
        // 记录撤销操作
        Undo.RecordObject(material, "Load Material Parameters");
        
        // 读取JSON
        string json = System.IO.File.ReadAllText(filePath);
        
        // 使用简单的JSON解析
        var lines = json.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            if (line.Contains(":"))
            {
                // 简单解析 "propertyName": value
                string trimmed = line.Trim().TrimEnd(',');
                int colonIndex = trimmed.IndexOf(':');
                if (colonIndex < 0) continue;
                
                string propertyName = trimmed.Substring(0, colonIndex).Trim().Trim('"');
                string valueStr = trimmed.Substring(colonIndex + 1).Trim();
                
                if (!material.HasProperty(propertyName)) continue;
                
                // 排除基础颜色
                if (propertyName == "_BaseColor") continue;
                
                // 判断值类型
                if (valueStr.StartsWith("["))
                {
                    // 数组类型（Color或Vector）
                    valueStr = valueStr.Trim('[', ']');
                    string[] parts = valueStr.Split(',');
                    if (parts.Length == 4)
                    {
                        float[] values = new float[4];
                        for (int i = 0; i < 4; i++)
                        {
                            float.TryParse(parts[i].Trim(), out values[i]);
                        }
                        
                        // 尝试设置为Color或Vector
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
                else
                {
                    // Float类型
                    if (float.TryParse(valueStr, out float floatValue))
                    {
                        material.SetFloat(propertyName, floatValue);
                    }
                }
            }
        }
        
        // 刷新材质
        EditorUtility.SetDirty(material);
        
        // 强制更新shader关键字
        material.shader = material.shader;
    }
    
    /// 从文件加载材质参数（单个材质）
    private void LoadMaterialParametersFromFile(string filePath)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null) return;
        
        LoadMaterialParametersToMaterial(material, filePath);
        
        // 刷新材质编辑器
        if (m_MaterialEditor != null)
        {
            m_MaterialEditor.Repaint();
        }
        
        // 刷新场景视图
        SceneView.RepaintAll();
        
        Debug.Log($"材质参数已从存档加载: {filePath}");
    }
    
    /// 重置材质参数为默认值（使用Default存档或shader默认值）
    private void ResetMaterialParameters()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;
        
        string defaultPresetPath = GetPresetPath("Default");
        
        // 检查Default存档是否存在
        if (System.IO.File.Exists(defaultPresetPath))
        {
            // 使用Default存档
            if (EditorUtility.DisplayDialog("重置参数", 
                "将使用Default存档重置参数。\n\n注意：纹理不会被重置。", 
                "确定", "取消"))
            {
                LoadMaterialParametersFromFile(defaultPresetPath);
            }
        }
        else
        {
            // Default存档不存在，创建它
            if (EditorUtility.DisplayDialog("创建Default存档", 
                "Default存档不存在，将使用Shader默认值创建Default存档。\n\n注意：纹理不会被保存。", 
                "确定", "取消"))
            {
                // 创建临时材质以获取shader默认值
                Material tempMaterial = new Material(material.shader);
                
                Shader shader = material.shader;
                int propertyCount = ShaderUtil.GetPropertyCount(shader);
                
                // 创建参数字典
                System.Collections.Generic.Dictionary<string, object> parameters = new System.Collections.Generic.Dictionary<string, object>();
                
                for (int i = 0; i < propertyCount; i++)
                {
                    string propertyName = ShaderUtil.GetPropertyName(shader, i);
                    ShaderUtil.ShaderPropertyType propertyType = ShaderUtil.GetPropertyType(shader, i);
                    
                    if (!tempMaterial.HasProperty(propertyName)) continue;
                    
                    // 排除纹理和基础颜色
                    if (propertyType == ShaderUtil.ShaderPropertyType.TexEnv) continue;
                    if (propertyName == "_BaseColor") continue;
                    
                    switch (propertyType)
                    {
                        case ShaderUtil.ShaderPropertyType.Color:
                            Color color = tempMaterial.GetColor(propertyName);
                            parameters[propertyName] = new float[] { color.r, color.g, color.b, color.a };
                            break;
                            
                        case ShaderUtil.ShaderPropertyType.Vector:
                            Vector4 vector = tempMaterial.GetVector(propertyName);
                            parameters[propertyName] = new float[] { vector.x, vector.y, vector.z, vector.w };
                            break;
                            
                        case ShaderUtil.ShaderPropertyType.Float:
                        case ShaderUtil.ShaderPropertyType.Range:
                            parameters[propertyName] = tempMaterial.GetFloat(propertyName);
                            break;
                    }
                }
                
                // 序列化为JSON（手动构建简单JSON）
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("{");
                bool first = true;
                foreach (var kvp in parameters)
                {
                    if (!first) sb.AppendLine(",");
                    first = false;
                    
                    sb.Append("  \"" + kvp.Key + "\": ");
                    
                    if (kvp.Value is float[])
                    {
                        float[] arr = (float[])kvp.Value;
                        sb.Append($"[{arr[0]}, {arr[1]}, {arr[2]}, {arr[3]}]");
                    }
                    else
                    {
                        sb.Append(kvp.Value.ToString());
                    }
                }
                sb.AppendLine();
                sb.AppendLine("}");
                
                // 保存Default存档
                System.IO.File.WriteAllText(defaultPresetPath, sb.ToString());
                
                Object.DestroyImmediate(tempMaterial);
                
                Debug.Log($"Default存档已创建: {defaultPresetPath}");
                
                // 加载Default存档
                LoadMaterialParametersFromFile(defaultPresetPath);
            }
        }
    }
    
    /// 统一场景中所有PBR_Mobile材质的自身阴影衰减参数
    /// 根据当前选中对象的Static状态，只统一相同类型（静态或非静态）的对象
    private void UnifyShadowScale()
    {
        Material currentMaterial = m_MaterialEditor.target as Material;
        if (currentMaterial == null || !currentMaterial.HasProperty("_ShadowScale"))
        {
            Debug.LogWarning("当前材质没有 _ShadowScale 参数");
            return;
        }
        
        // 获取当前材质的 _ShadowScale 值
        float targetShadowScale = currentMaterial.GetFloat("_ShadowScale");
        
        // 查找当前材质所属的GameObject，判断其Static状态
        bool currentIsStatic = false;
        GameObject currentGameObject = null;
        
        // 在场景中查找使用当前材质的对象
        Renderer[] allRenderers = Object.FindObjectsOfType<Renderer>();
        foreach (Renderer renderer in allRenderers)
        {
            if (renderer.sharedMaterials != null)
            {
                foreach (Material mat in renderer.sharedMaterials)
                {
                    if (mat == currentMaterial)
                    {
                        currentGameObject = renderer.gameObject;
                        
                        // 检查当前对象的Static状态
                        UnityEditor.StaticEditorFlags staticFlags = UnityEditor.GameObjectUtility.GetStaticEditorFlags(currentGameObject);
                        bool hasStaticFlags = ((int)staticFlags) != 0;
                        bool isStaticProperty = currentGameObject.isStatic;
                        
                        currentIsStatic = hasStaticFlags || isStaticProperty;
                        break;
                    }
                }
                if (currentGameObject != null) break;
            }
        }
        
        string staticTypeText = currentIsStatic ? "静态" : "非静态";
        Debug.Log($"当前材质所属对象: {(currentGameObject != null ? currentGameObject.name : "未知")}, 类型: {staticTypeText}");
        Debug.Log($"将统一设置所有{staticTypeText}对象的自身阴影衰减参数为: {targetShadowScale:F3}");
        
        // 查找场景中所有使用 PBR_Mobile 或 PBR_Mobile_Trans shader 的材质
        string[] targetShaderNames = new string[]
        {
            "Custom/PBR_Mobile",
            "Custom/PBR_Mobile_Trans"
        };
        
        System.Collections.Generic.HashSet<Material> processedMaterials = new System.Collections.Generic.HashSet<Material>();
        int modifiedCount = 0;
        int skippedCount = 0;
        
        foreach (Renderer renderer in allRenderers)
        {
            GameObject go = renderer.gameObject;
            if (go == null) continue;
            
            // 检查GameObject的Static状态
            UnityEditor.StaticEditorFlags staticFlags = UnityEditor.GameObjectUtility.GetStaticEditorFlags(go);
            bool hasStaticFlags = ((int)staticFlags) != 0;
            bool isStaticProperty = go.isStatic;
            bool objectIsStatic = hasStaticFlags || isStaticProperty;
            
            // 根据当前材质所属对象的Static状态，只处理相同类型的对象
            if (objectIsStatic != currentIsStatic)
            {
                skippedCount++;
                continue;
            }
            
            Material[] materials = renderer.sharedMaterials;
            
            foreach (Material mat in materials)
            {
                if (mat == null || mat.shader == null) continue;
                
                // 检查是否已处理过此材质（避免重复）
                if (processedMaterials.Contains(mat)) continue;
                
                // 检查是否是目标 shader
                bool isTargetShader = false;
                foreach (string shaderName in targetShaderNames)
                {
                    if (mat.shader.name == shaderName)
                    {
                        isTargetShader = true;
                        break;
                    }
                }
                
                if (isTargetShader && mat.HasProperty("_ShadowScale"))
                {
                    // 记录撤销操作
                    Undo.RecordObject(mat, "Unify Shadow Scale");
                    
                    // 设置 _ShadowScale 值
                    mat.SetFloat("_ShadowScale", targetShadowScale);
                    
                    // 标记材质为已修改
                    EditorUtility.SetDirty(mat);
                    
                    processedMaterials.Add(mat);
                    modifiedCount++;
                }
            }
        }
        
        if (modifiedCount > 0)
        {
            Debug.Log($"统一阴影完成：已将 {modifiedCount} 个{staticTypeText}对象的材质自身阴影衰减参数设置为 {targetShadowScale:F3}");
            Debug.Log($"跳过了 {skippedCount} 个{(currentIsStatic ? "非静态" : "静态")}对象");
            
            // 刷新场景视图
            SceneView.RepaintAll();
        }
        else
        {
            Debug.LogWarning($"场景中没有找到符合条件的{staticTypeText}对象材质。跳过了 {skippedCount} 个{(currentIsStatic ? "非静态" : "静态")}对象");
        }
    }
}
