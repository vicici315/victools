// 5.8 添加存档读档功能按钮
// 6.0 支持多个材质球读档
// 6.1 添加【统一阴影】按钮，用于统一设置“自身阴影衰减”值，使场景中阴影保持一致的明暗度（包括PBR_Mobile_Trans）
// 6.3 优化读档，按属性值精确同步 shader 关键字（两个 shader 共用，仅同步各自实际声明的关键字）
// PBR_Mobile.shader7.1 添加"同步设置"按钮，一键同步场景中所有PBR_Mobile.shader与PBR_Mobile_Trans.shader软阴影参数
// PBR_MobileGUI 7.2 添加"查找贴图"按钮，一键自动赋予PBR配套贴图

using UnityEngine;
using UnityEditor;
using VicTools;

public class PBR_MobileGUI : ShaderGUI
{
    private MaterialEditor m_MaterialEditor;
    private MaterialProperty[] m_Properties;
    private bool isTransShader = false; // 标记是否为 Trans 版本的 Shader

    // "仅非主纹理"读档选项下要排除的纹理属性名集合（PBR_MobileGUI 专用）。
    // 合并 HeaderStyle.MainTexturePropertyNames（_BaseMap / _MainTex）共用部分，
    // 再追加 PBR 专用 _MetallicGlossMap（MRA 贴图）、_BumpMap（法线贴图）、_EmissionMap（自发光贴图）。
    // 理由：
    //   _MetallicGlossMap 兼作 金属度(R)/粗糙度(G)/AO(B) 通道，与同名 float 参数互斥，
    //                    切换它会改变 _Metallic/_Roughness/_OcclusionContrast 等参数的语义。
    //   _BumpMap          法线贴图，与 _BumpScale 强绑定，切换它会改变表面光照细节。
    //   _EmissionMap      自发光贴图，与 _EmissionColor/_EmissionScale 共同决定发光效果，
    //                    切换它会改变发光区域与颜色叠加语义。
    // 上述纹理"仅非主纹理"选项下应保留当前设置，不从存档恢复，避免破坏材质当前的视觉表现。
    private static readonly System.Collections.Generic.HashSet<string> PBRMainTexturePropertyNames =
        new System.Collections.Generic.HashSet<string>(HeaderStyle.MainTexturePropertyNames)
        {
            "_MetallicGlossMap",
            "_BumpMap",
            "_EmissionMap"
        };

    // 缓存属性
    private MaterialProperty disableEnvironment;
    private MaterialProperty disableLightColor;
    private MaterialProperty useVerShadow;
    private MaterialProperty baseColor;
    private MaterialProperty baseMap;
    private MaterialProperty metallic;
    private MaterialProperty roughness;
    private MaterialProperty specularScale;
    private MaterialProperty halfLambert;
    private MaterialProperty shadowScale;
    private MaterialProperty useSoftShadow;
    private MaterialProperty softness;
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
    // 性能开关
    private MaterialProperty disableBakedSpecular;
    private MaterialProperty disableIndirectSpecular;
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
        using(new GUILayout.VerticalScope(EditorStyles.helpBox)){
        DrawBaseProperties();
        }
        using(new GUILayout.VerticalScope(EditorStyles.helpBox)){
        DrawMetallicRoughnessAO();
        }
        // EditorGUILayout.Space(5);
        using(new GUILayout.VerticalScope(EditorStyles.helpBox)){
        DrawNormalMap();
        }
        // EditorGUILayout.Space(5);
        using(new GUILayout.VerticalScope(EditorStyles.helpBox)){
        DrawEmission();
        }
        // EditorGUILayout.Space(5);
        
        // 反射功能对两个版本都可用
        using(new GUILayout.VerticalScope(EditorStyles.helpBox)){
        DrawReflection();
        }
        
        // 只在非 Trans 版本显示点光源和聚光灯
        if (!isTransShader)
        {
            using(new GUILayout.VerticalScope(EditorStyles.helpBox)){
            DrawPointLights();
            }
            using(new GUILayout.VerticalScope(EditorStyles.helpBox)){
            DrawSpotLights();
            }
            // EditorGUILayout.Space(5);
        }        
        
        if (isTransShader)
        {
            using(new GUILayout.VerticalScope(EditorStyles.helpBox)){
            DrawAlphaCull();
            }
        }
        // EditorGUILayout.Space(5);
        DrawPerformance();
    }

    private void FindProperties()
    {
        disableEnvironment = FindProperty("_DisableEnvironment", m_Properties, false);
        disableLightColor = FindProperty("_DisableLightColor", m_Properties, false);
        useVerShadow = FindProperty("_UseVerShadow", m_Properties, false);
        useSoftShadow = FindProperty("_UseSoftShadow", m_Properties, false);
        baseColor = FindProperty("_BaseColor", m_Properties);
        baseMap = FindProperty("_BaseMap", m_Properties);
        metallic = FindProperty("_Metallic", m_Properties);
        roughness = FindProperty("_Roughness", m_Properties);
        specularScale = FindProperty("_SpecularScale", m_Properties);
        halfLambert = FindProperty("_HalfLambert", m_Properties);
        shadowScale = FindProperty("_ShadowScale", m_Properties);
        softness = FindProperty("_Softness", m_Properties, false);
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
        // 性能开关（仅 PBR_Mobile 声明，Trans 找不到时为 null）
        disableBakedSpecular = FindProperty("_DisableBakedSpecular", m_Properties, true);
        disableIndirectSpecular = FindProperty("_DisableIndirectSpecular", m_Properties, true);
        cullMode = FindProperty("_Cull", m_Properties);
        _Cutoff = FindProperty("_Cutoff", m_Properties);
        _SrcBlend = FindProperty("_SrcBlend", m_Properties, false);
        _DstBlend = FindProperty("_DstBlend", m_Properties, false);
        _ZWrite = FindProperty("_ZWrite", m_Properties, false);
    }

    private void DrawGlobalSettings()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(HeaderStyle.Rich("全局设置", HeaderStyle.HeaderTitle), EditorStyle.Get.BoldLabelRichStyle);
        // 查找贴图按钮（点击后按材质球名 / 模型对象名 在 Assets 中搜索 _D / _MRA / _N 三种后缀贴图并赋值）
        GUI.backgroundColor = new Color(1.0f, 0.8f, 0.3f); // 黄色背景
        if (GUILayout.Button(new GUIContent("查找贴图",
            "根据材质球名或模型对象名在 Assets 中查找匹配的:\n" +
            "  _D → （颜色贴图）\n" +
            "  _MRA → （MRA 贴图）\n" +
            "  _N → （法线贴图）\n" +
            "找不到则降级到场景中模型对象名查找。"),
            GUILayout.Width(60)))
        {
            EditorApplication.delayCall += FindAndAssignTextures;
        }
        
        // 添加存档按钮
        GUI.backgroundColor = new Color(0.3f, 0.8f, 1.0f); // 蓝色背景
        if (GUILayout.Button(new GUIContent("存档", "保存当前材质参数到文件（不包含纹理和基础颜色）"), GUILayout.Width(50)))
        {
            EditorApplication.delayCall += SaveMaterialParameters;
        }
        
        // 添加读档按钮
        GUI.backgroundColor = new Color(0.5f, 1.0f, 0.5f); // 绿色背景
        if (GUILayout.Button(new GUIContent("读档 ▾", "从文件加载材质参数\n支持批量应用到多个选中的材质"), GUILayout.Width(55)))
        {
            ShowLoadDropdown();
        }
        
        
        // 预设下拉菜单
        GUI.backgroundColor = new Color(0.9f, 0.7f, 1.0f);
        if (GUILayout.Button("预设 ▾", GUILayout.Width(55)))
        {
            ShowPresetDropdown();
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
        
        if (disableLightColor != null)
        {
            m_MaterialEditor.ShaderProperty(disableLightColor, "禁用主光颜色（使用白色）");
        }
        
        if (useVerShadow != null)
        {
            m_MaterialEditor.ShaderProperty(useVerShadow, "使用顶点阴影");
        }
        
        if (useSoftShadow != null)
        {
            EditorGUILayout.BeginHorizontal();
            m_MaterialEditor.ShaderProperty(useSoftShadow, "使用优化软阴影");
            GUI.backgroundColor = new Color(1.0f, 0.6f, 0.3f);
            if (GUILayout.Button(new GUIContent("同步设置", "将当前材质的（使用优化软阴影）与（阴影柔化半径）参数同步到场景中所有使用 PBR_Mobile 及Trans材质"), GUILayout.Width(65)))
            {
                EditorApplication.delayCall += SyncSoftShadowSettings;
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            if (useSoftShadow.floatValue > 0.5f && softness != null)
            {
                EditorGUI.indentLevel++;
                m_MaterialEditor.RangeProperty(softness, "阴影柔化半径");
                EditorGUI.indentLevel--;
            }
        }
    }

    private void DrawBaseProperties()
    {
        GUILayout.Label(HeaderStyle.Rich("1 ▌基础属性 (Base Properties)", HeaderStyle.Base), EditorStyle.Get.BoldLabelRichStyle);
        m_MaterialEditor.ColorProperty(baseColor, "基础颜色");
        m_MaterialEditor.TextureProperty(baseMap, "颜色贴图 (RGB)");
        if (isTransShader)
        {
            m_MaterialEditor.ShaderProperty(_Cutoff, "透明阈值");
        }
    }

    private void DrawMetallicRoughnessAO()
    {
        GUILayout.Label(HeaderStyle.Rich("2 ▌PBR参数 (Metallic、Roughness、AO)", HeaderStyle.Lighting), EditorStyle.Get.BoldLabelRichStyle);
        
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
        
        HeaderStyle.ShaderProperty(m_MaterialEditor, useNormalMap, "3 ▌使用法线贴图", HeaderStyle.Base);
        
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
        
        HeaderStyle.ShaderProperty(m_MaterialEditor, useEmissionMap, "4 ▌使用自发光贴图", HeaderStyle.Lighting);
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
        
        HeaderStyle.ShaderProperty(m_MaterialEditor, useReflection, "5 ▌使用反射贴图", HeaderStyle.Base);
        
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
        HeaderStyle.ShaderProperty(m_MaterialEditor, usePointlight, "6 ▌使用点光源", HeaderStyle.Interaction);
        
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
        HeaderStyle.ShaderProperty(m_MaterialEditor, useSpotlight, "7 ▌使用聚光灯", HeaderStyle.Interaction);
        
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
        // EditorGUILayout.Space(5);
        if (_SrcBlend != null)
            m_MaterialEditor.ShaderProperty(_SrcBlend, "源混合模式");
        if (_DstBlend != null)
            m_MaterialEditor.ShaderProperty(_DstBlend, "目标混合模式");
        if (_ZWrite != null)
            m_MaterialEditor.ShaderProperty(_ZWrite, "深度写入");
    
    }
    private void DrawPerformance()
    {
        EditorGUILayout.Space(5);
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUILayout.Label(HeaderStyle.Rich("# ▌性能 (Performance)", HeaderStyle.Render), EditorStyle.Get.BoldLabelRichStyle);
            m_MaterialEditor.ShaderProperty(cullMode, "剔除模式");

            // 性能开关 - 仅 PBR_Mobile 声明了这些属性，Trans 版本为 null 时自动隐藏
            if (disableBakedSpecular != null)
            {
                m_MaterialEditor.ShaderProperty(disableBakedSpecular, "禁用烘焙高光（省 ALU，仅LIGHTMAP_ON下生效）");
            }
            if (disableIndirectSpecular != null)
            {
                m_MaterialEditor.ShaderProperty(disableIndirectSpecular, "禁用间接高光近似（省 1 次 fastPow+3次 mad）");
            }
        }
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

    /// 显示读档下拉菜单
    private void ShowLoadDropdown()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        string shaderName = material.shader.name.Replace("/", "_");
        string folderPath = "Library/VicTools/PBRM/" + shaderName;

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
                Material mat = material; // 捕获材质引用
                menu.AddItem(new GUIContent(fileName), false, () =>
                {
                    EditorApplication.delayCall += () => LoadFromFile(mat, filePath);
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
                Material mat = material; // 捕获材质引用
                menu.AddItem(new GUIContent(fileName), false, () =>
                {
                    EditorApplication.delayCall += () => LoadFromFile(mat, filePath);
                });
            }
        }
        menu.ShowAsContext();
    }

    /// 从预设文件加载（带纹理提示）
    private void LoadPresetFile(string filePath)
    {
        if (!System.IO.File.Exists(filePath)) return;
        // 从 Selection 获取材质，避免 delayCall 后 m_MaterialEditor 失效
        Material material = null;
        if (m_MaterialEditor != null)
            material = m_MaterialEditor.target as Material;
        if (material == null)
        {
            // fallback: 从 Selection 获取
            if (Selection.activeObject is Material selMat)
                material = selMat;
        }
        if (material == null) return;
        LoadFromFile(material, filePath);
    }

    /// 单材质读档入口（菜单项 / LoadPresetFile 走这里）：弹一次三选项纹理对话框，再加载。
    /// <para>无纹理存档（hasTexData=false，如 Default）跳过弹框，默认全部读取但被 hasTexData 屏蔽掉。</para>
    private void LoadFromFile(Material material, string filePath)
    {
        if (material == null || material.shader == null) return;
        if (!System.IO.File.Exists(filePath))
        {
            Debug.LogWarning($"存档文件不存在: {filePath}");
            return;
        }

        string json = System.IO.File.ReadAllText(filePath);
        bool hasTexData = json.Contains("\"path\":");

        HeaderStyle.LoadTextureChoice choice = hasTexData
            ? HeaderStyle.ShowLoadTextureDialog()
            : new HeaderStyle.LoadTextureChoice(false, false);

        LoadMaterialParametersToMaterial(material, filePath, choice);
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
            
            // 排除基础颜色
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
                    sb.Append(material.GetFloat(propertyName).ToString("G9"));
                    break;

                case ShaderUtil.ShaderPropertyType.TexEnv:
                    var tex = material.GetTexture(propertyName);
                    string texPath = tex != null ? AssetDatabase.GetAssetPath(tex).Replace("\\", "/") : "";
                    var tiling = material.GetTextureScale(propertyName);
                    var offset = material.GetTextureOffset(propertyName);
                    sb.Append($"{{\"path\": \"{texPath}\", \"tiling\": [{tiling.x}, {tiling.y}], \"offset\": [{offset.x}, {offset.y}]}}");
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
                // 弹一次三选项纹理对话框，传给所有材质（避免每个材质都弹框）
                HeaderStyle.LoadTextureChoice choice = HeaderStyle.ShowLoadTextureDialog();
                int applied = 0;
                foreach (Object obj in targets)
                {
                    Material mat = obj as Material;
                    if (mat != null && mat.shader != null)
                    {
                        // 检查shader是否匹配
                        if (mat.shader.name == material.shader.name)
                        {
                            LoadMaterialParametersToMaterial(mat, presetPath, choice);
                            applied++;
                        }
                        else
                        {
                            Debug.LogWarning($"材质 {mat.name} 的Shader不匹配，已跳过。");
                        }
                    }
                }
                
                Debug.Log($"批量读档完成：已应用到 {applied}/{targets.Length} 个材质");
            }
        }
        else
        {
            // 单选模式
            LoadFromFile(material, presetPath);
        }
    }
    
    /// 从文件加载材质参数到指定材质（底层，<paramref name="choice"/> 必传，不弹对话框）
    /// <para>外部入口：</para>
    /// <list type="bullet">
    ///   <item>单材质读档（菜单项 / 预设）→ <see cref="LoadFromFile"/>（弹一次三选项对话框）</item>
    ///   <item>批量读档 → <see cref="LoadMaterialParameters"/>（弹一次三选项对话框后传入）</item>
    /// </list>
    private void LoadMaterialParametersToMaterial(Material material, string filePath, HeaderStyle.LoadTextureChoice choice)
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

        // 检测是否包含纹理数据，配合 choice 决定是否实际读取
        bool hasTexData = json.Contains("\"path\":");
        bool loadTex     = hasTexData && choice.LoadTextures;
        bool loadMainTex = hasTexData && choice.LoadMainTex;
        
        // 使用简单的JSON解析
        var lines = json.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        
        for (int li = 0; li < lines.Length; li++)
        {
            string trimmed = lines[li].Trim().TrimEnd(',');
            int colonIndex = trimmed.IndexOf(':');
            if (colonIndex < 0) continue;
            // Substring(0, colonIndex) 可以写成更简洁的 [..colonIndex]，建议用后者，如果需要兼容 C# 7.3 或更早：必须保留 Substring(0, colonIndex)
            string propertyName = trimmed.Substring(0, colonIndex).Trim().Trim('"');
            string valueStr = trimmed.Substring(colonIndex + 1).Trim();

            if (!material.HasProperty(propertyName)) continue;

            // 排除基础颜色
            if (propertyName == "_BaseColor") continue;

            if (valueStr.StartsWith("{"))
            {
                // 纹理类型
                string texJson = valueStr;
                while (!texJson.Contains("}") && li + 1 < lines.Length) { li++; texJson += lines[li]; }

                // Tiling/Offset 始终读取
                float[] t = ExtractTexFloats(texJson, "tiling");
                if (t != null && t.Length == 2) material.SetTextureScale(propertyName, new Vector2(t[0], t[1]));
                float[] o = ExtractTexFloats(texJson, "offset");
                if (o != null && o.Length == 2) material.SetTextureOffset(propertyName, new Vector2(o[0], o[1]));

                if (!loadTex) continue;

                // PBR 专用主纹理：仅非主纹理选项下跳过，保留当前设置。
                // PBRMainTexturePropertyNames 包含 _BaseMap / _MainTex / _MetallicGlossMap。
                if (PBRMainTexturePropertyNames.Contains(propertyName) && !loadMainTex) continue;

                string texPath = ExtractTexString(texJson, "path");
                if (!string.IsNullOrEmpty(texPath))
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                    if (tex != null) material.SetTexture(propertyName, tex);
                }
                else
                {
                    material.SetTexture(propertyName, null);
                }
            }
            else if (valueStr.StartsWith("["))
            {
                // 数组类型（Color或Vector）
                string[] parts = valueStr.Trim('[', ']').Split(',');
                if (parts.Length == 4)
                {
                    float[] values = new float[4];
                    for (int i = 0; i < 4; i++)
                    {
                        float.TryParse(parts[i].Trim(), out values[i]);
                    }
                    
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
        
        // 刷新材质
        EditorUtility.SetDirty(material);
        
        // 按属性值精确同步关键字，避免重赋 shader 导致关键字被重置
        SyncMaterialKeywords(material);
    }

    private static string ExtractTexString(string json, string key)
    {
        int idx = json.IndexOf("\"" + key + "\":");
        if (idx < 0) return null;
        int s = json.IndexOf('"', idx + key.Length + 3);
        if (s < 0) return null;
        int e = json.IndexOf('"', s + 1);
        return e > s ? json.Substring(s + 1, e - s - 1) : null;
    }

    private static float[] ExtractTexFloats(string json, string key)
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
    
    /// 按属性值精确同步 shader 关键字（两个 shader 共用，仅同步各自实际声明的关键字）
    private void SyncMaterialKeywords(Material mat)
    {
        void SyncToggle(string prop, string keyword)
        {
            if (!mat.HasProperty(prop)) return;
            if (mat.GetFloat(prop) > 0.5f) mat.EnableKeyword(keyword);
            else mat.DisableKeyword(keyword);
        }

        SyncToggle("_DisableEnvironment", "_DISABLEENVIRONMENT");
        SyncToggle("_DisableLightColor",  "_DISABLELIGHTCOLOR");
        SyncToggle("_UseVerShadow",       "_USEVERSHADOW");
        SyncToggle("_UseSoftShadow",      "_USESOFTSHADOW");
        SyncToggle("_UseMsaMap",          "_USEMSAMAP");
        SyncToggle("_UseAOMap",           "_USEAOMAP");
        SyncToggle("_PreviewAOMap",       "_PREVIEWAO");
        SyncToggle("_UseNormalMap",       "_NORMALMAP");
        SyncToggle("_FilpG",              "_FILPG");
        SyncToggle("_DebugNormal",        "_DEBUGNORMAL");
        SyncToggle("_UseEmissionMap",     "_USEEMISSIONMAP");
        SyncToggle("_InvertEmisMap",      "_INVERTEMISMAP");
        SyncToggle("_UseReflection",      "_USEREFLECTION");
        SyncToggle("_UsePointlight",      "_USEPOINTLIGHT");
        SyncToggle("_UseSpotlight",       "_USESPOTLIGHT");
        SyncToggle("_UseSpotTexture",     "_USESPOTTEXTURE");
        SyncToggle("_DisableBakedSpecular",     "_DISABLEBAKEDSPECULAR");
        SyncToggle("_DisableIndirectSpecular",  "_DISABLEINDIRECTSPECULAR");
        // _ZWrite 关键字由 shader 的 [Toggle(_ZWWRITE)] 自动同步，但为保险起见在读档/重置时也手动同步一次
        SyncToggle("_ZWrite",                  "_ZWWRITE");
    }

    /// 查找贴图：根据材质球名 / 模型对象名 在 Assets 中搜索并赋值 PBR 纹理。
    /// <para>规则：</para>
    /// <list type="bullet">
    ///   <item>候选名（按优先级）：① 材质球名（去掉 .mat） ② 场景中使用此材质的所有 Renderer 的 GameObject 名（去重）</item>
    ///   <item>后缀对照：_D → _BaseMap，_MRA → _MetallicGlossMap，_N → _BumpMap，
    ///   <b>_E → _EmissionMap（仅当 _UseEmissionMap 开启时参与查找）</b></item>
    ///   <item>匹配优先级（评分）：1000 精确匹配 > 500 子串匹配 > 200 同时包含 baseName+suffix > 100 仅包含 baseName</item>
    ///   <item>找到 MRA / 法线贴图后自动启用对应 toggle（_UseMsaMap / _UseNormalMap）并同步 keyword；
    ///   自发光 toggle 由用户预先决定，仅同步 _USEEMISSIONMAP keyword</item>
    /// </list>
    private void FindAndAssignTextures()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;

        // 收集候选基础名称（按优先级：材质球 → 模型对象）
        string materialBaseName = System.IO.Path.GetFileNameWithoutExtension(material.name);
        var searchSources = new System.Collections.Generic.List<System.Tuple<string, string>>();
        searchSources.Add(new System.Tuple<string, string>(materialBaseName, "材质球"));

        // 查找场景中使用此材质的所有 GameObject 名（去重）
        var seenModelNames = new System.Collections.Generic.HashSet<string>();
        Renderer[] renderers = Object.FindObjectsOfType<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            foreach (Material mat in renderer.sharedMaterials)
            {
                if (mat == material)
                {
                    string goName = renderer.gameObject.name;
                    if (seenModelNames.Add(goName))
                    {
                        searchSources.Add(new System.Tuple<string, string>(goName, "模型"));
                    }
                    break;
                }
            }
        }

        // 后缀 → 纹理参数映射
        // _E（自发光贴图）仅在 _UseEmissionMap 开启时才参与查找
        var suffixMap = new System.Collections.Generic.Dictionary<string, string>
        {
            { "_D",   "_BaseMap" },
            { "_MRA", "_MetallicGlossMap" },
            { "_N",   "_BumpMap" }
        };

        // 自发光开关：仅当用户已勾选 _UseEmissionMap 时才查找并赋值 _E 贴图
        bool useEmission = material.HasProperty("_UseEmissionMap")
            && material.GetFloat("_UseEmissionMap") > 0.5f;
        if (useEmission && material.HasProperty("_EmissionMap"))
        {
            suffixMap.Add("_E", "_EmissionMap");
        }

        var logBuilder = new System.Text.StringBuilder();
        logBuilder.AppendLine($"[查找贴图] 材质: {material.name}");

        Undo.RecordObject(material, "查找贴图");

        foreach (var kvp in suffixMap)
        {
            string suffix = kvp.Key;
            string paramName = kvp.Value;
            if (!material.HasProperty(paramName)) continue;

            // 逐个候选查找，按第一个命中就退出（保证优先级：材质球 > 模型）
            Texture foundTex = null;
            string foundLabel = null;
            foreach (var source in searchSources)
            {
                string baseName = source.Item1;
                string sourceLabel = source.Item2;
                if (string.IsNullOrEmpty(baseName)) continue;

                string foundPath = FindBestTextureForBaseName(baseName, suffix);
                if (!string.IsNullOrEmpty(foundPath))
                {
                    Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(foundPath);
                    if (tex != null)
                    {
                        foundTex = tex;
                        foundLabel = $"{sourceLabel} ({baseName}) → {foundPath}";
                        break;
                    }
                }
            }

            if (foundTex != null)
            {
                material.SetTexture(paramName, foundTex);
                logBuilder.AppendLine($"  ✅ {paramName} ← {foundLabel}");
            }
            else
            {
                logBuilder.AppendLine($"  ❌ {paramName} ({suffix}): 未找到匹配贴图");
            }
        }

        // 找到贴图后自动启用对应 toggle 并同步 keyword
        if (material.GetTexture("_MetallicGlossMap") != null && material.HasProperty("_UseMsaMap"))
        {
            material.SetFloat("_UseMsaMap", 1.0f);
            material.EnableKeyword("_USEMSAMAP");
            logBuilder.AppendLine("  ⚙️ 自动启用 _UseMsaMap");
        }
        if (material.GetTexture("_BumpMap") != null && material.HasProperty("_UseNormalMap"))
        {
            material.SetFloat("_UseNormalMap", 1.0f);
            material.EnableKeyword("_NORMALMAP");
            logBuilder.AppendLine("  ⚙️ 自动启用 _UseNormalMap");
        }
        // 自发光：_UseEmissionMap 已开启时，仅同步 keyword（保持用户勾选状态）
        if (material.GetTexture("_EmissionMap") != null && material.HasProperty("_UseEmissionMap"))
        {
            material.EnableKeyword("_USEEMISSIONMAP");
            logBuilder.AppendLine("  ⚙️ 同步 _USEEMISSIONMAP keyword");
        }

        SyncMaterialKeywords(material);
        EditorUtility.SetDirty(material);

        Debug.Log(logBuilder.ToString());
        SceneView.RepaintAll();
    }

    /// 在 Assets 中按给定基础名称 + 后缀搜索最佳匹配贴图。
    /// <para>评分：1000 精确匹配 (baseName + suffix) > 500 子串匹配 > 200 同时包含 baseName 和 suffix >
    /// 150 包含核心名 + suffix > 100 仅包含 baseName > 50 仅包含核心名</para>
    /// <para>「核心名」：去掉对象名常见前缀 (FQ_ / T_ / MAT_ / M_) 与末尾版本号 (_01 / _01 (1) / (1) 等)，
    /// 用于处理 "FQ_yuanzhuxingrongqi_01 (1)" → "T_yuanzhuxingrongqi_D" 这类相似但不精确的匹配。</para>
    private string FindBestTextureForBaseName(string baseName, string suffix)
    {
        if (string.IsNullOrEmpty(baseName)) return null;

        string fileNameSuffix = "_" + suffix.TrimStart('_'); // "_D" / "_MRA" / "_N"
        string expectedExact  = baseName + fileNameSuffix;

        // 提取核心名（去掉前缀 / 末尾版本号），用于相似匹配
        string coreName = ExtractCoreName(baseName);

        // AssetDatabase 搜索：优先用核心名（更宽松），并清理 FindAssets 的关键字分隔符（空格 / 括号）
        string searchKey = !string.IsNullOrEmpty(coreName)
            ? coreName
            : SanitizeForFindAssets(baseName);
        string[] guids = AssetDatabase.FindAssets($"{searchKey} {fileNameSuffix} t:Texture");

        string bestPath = null;
        int bestScore = -1;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) continue;
            string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(fileName)) continue;

            int score = 0;

            // 1000 = 精确匹配 case-insensitive
            if (string.Equals(fileName, expectedExact, System.StringComparison.OrdinalIgnoreCase))
            {
                score = 1000;
            }
            // 500 = 包含 expectedExact 子串
            else if (fileName.IndexOf(expectedExact, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score = 500;
            }
            // 200 = 同时包含 baseName 和 fileNameSuffix
            else if (fileName.IndexOf(baseName, System.StringComparison.OrdinalIgnoreCase) >= 0
                  && fileName.IndexOf(fileNameSuffix, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score = 200;
            }
            // 150 = 同时包含 核心名 和 fileNameSuffix（处理 FQ_yuanzhuxingrongqi_01 (1) → T_yuanzhuxingrongqi_D）
            else if (!string.IsNullOrEmpty(coreName)
                  && fileName.IndexOf(coreName, System.StringComparison.OrdinalIgnoreCase) >= 0
                  && fileName.IndexOf(fileNameSuffix, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score = 150;
            }
            // 100 = 仅包含 baseName（说明 Unity 搜索只命中了 baseName 部分）
            else if (fileName.IndexOf(baseName, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score = 100;
            }
            // 50 = 仅包含核心名（最宽松匹配）
            else if (!string.IsNullOrEmpty(coreName)
                  && fileName.IndexOf(coreName, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score = 50;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestPath = path;
            }
        }

        return bestScore > 0 ? bestPath : null;
    }

    /// 提取对象名的核心部分（去掉常见前缀 FQ_/T_/MAT_/M_；去掉末尾版本号 _01 / _01 (1) / (1) 等）。
    /// 用于相似匹配，例如：
    ///   "FQ_yuanzhuxingrongqi_01 (1)" → "yuanzhuxingrongqi"
    ///   "T_yuanzhuxingrongqi_D" 包含 "yuanzhuxingrongqi"，可匹配。
    /// 提取后不足 4 个字符返回空（避免过短核心名引发误匹配）。
    private static string ExtractCoreName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        // 1) 去掉常见前缀
        string[] prefixes = { "FQ_", "T_", "MAT_", "M_" };
        foreach (var p in prefixes)
        {
            if (name.StartsWith(p, System.StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(p.Length);
                break;
            }
        }

        // 2) 去掉末尾 " (数字)"
        int parenIdx = name.LastIndexOf(" (");
        if (parenIdx > 0 && name.EndsWith(")", System.StringComparison.Ordinal))
        {
            string inside = name.Substring(parenIdx + 2, name.Length - parenIdx - 3);
            if (IsAllDigits(inside))
            {
                name = name.Substring(0, parenIdx);
            }
        }

        // 3) 去掉末尾 "_数字"
        int lastUnderscore = name.LastIndexOf('_');
        if (lastUnderscore > 0)
        {
            string tail = name.Substring(lastUnderscore + 1);
            if (IsAllDigits(tail))
            {
                name = name.Substring(0, lastUnderscore);
            }
        }

        return name.Length >= 4 ? name : "";
    }

    /// 清理 name 用于 AssetDatabase.FindAssets 搜索（去除空格、括号、点等会被 FindAssets 当作关键字分隔符的字符）。
    private static string SanitizeForFindAssets(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// 判断字符串是否全为数字（空字符串返回 false）。
    private static bool IsAllDigits(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
        {
            if (!char.IsDigit(c)) return false;
        }
        return true;
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

    /// 同步场景中所有同时使用 PBR_Mobile 和 PBR_Mobile_Trans Shader 的材质的优化软阴影设置
    /// 包括 _UseSoftShadow toggle 和 _Softness 柔化半径
    private void SyncSoftShadowSettings()
    {
        Material sourceMat = m_MaterialEditor.target as Material;
        if (sourceMat == null) return;
        
        float sourceUseSoftShadow = sourceMat.GetFloat("_UseSoftShadow");
        float sourceSoftness = sourceMat.GetFloat("_Softness");

        // 同时匹配 PBR_Mobile 和 PBR_Mobile_Trans 两种 shader
        string shaderName = sourceMat.shader.name;
        string shaderNameTrans = shaderName + "_Trans";    // Custom/PBR_Mobile → Custom/PBR_Mobile_Trans
        if (shaderName.EndsWith("_Trans"))
            shaderNameTrans = shaderName;                  // 当前已是 Trans，保持
        string shaderNameBase = shaderName.EndsWith("_Trans") ? shaderName.Substring(0, shaderName.Length - 6) : shaderName;

        Shader baseShader = Shader.Find(shaderNameBase);
        Shader transShader = Shader.Find(shaderNameTrans);

        var renderers = Object.FindObjectsOfType<Renderer>();
        var processedMaterials = new System.Collections.Generic.HashSet<Material>();
        int modifiedCount = 0;

        foreach (var renderer in renderers)
        {
            foreach (var mat in renderer.sharedMaterials)
            {
                if (mat == null || mat == sourceMat) continue;
                Shader matShader = mat.shader;
                if ((baseShader != null && matShader == baseShader) ||
                    (transShader != null && matShader == transShader))
                {
                    // matched
                }
                else
                {
                    continue;
                }
                if (!processedMaterials.Add(mat)) continue;

                Undo.RecordObject(mat, "Sync Soft Shadow Settings");
                mat.SetFloat("_UseSoftShadow", sourceUseSoftShadow);
                mat.SetFloat("_Softness", sourceSoftness);

                if (sourceUseSoftShadow > 0.5f)
                    mat.EnableKeyword("_USESOFTSHADOW");
                else
                    mat.DisableKeyword("_USESOFTSHADOW");

                EditorUtility.SetDirty(mat);
                modifiedCount++;
            }
        }

        if (modifiedCount > 0)
        {
            Debug.Log($"同步设置完成：已将 {modifiedCount} 个材质(PBR_Mobile + PBR_Mobile_Trans)的优化软阴影参数同步（_UseSoftShadow={sourceUseSoftShadow}, _Softness={sourceSoftness:F3}）");
            SceneView.RepaintAll();
        }
        else
        {
            Debug.LogWarning("场景中没有找到使用 PBR_Mobile 或 PBR_Mobile_Trans Shader 的其他材质");
        }
    }
}
