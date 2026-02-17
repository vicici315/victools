using UnityEngine;
using UnityEditor;

public class PBR_MobileGUI : ShaderGUI
{
    private MaterialEditor m_MaterialEditor;
    private MaterialProperty[] m_Properties;

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

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        m_MaterialEditor = materialEditor;
        m_Properties = properties;

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
        DrawReflection();
        // EditorGUILayout.Space(5);
        DrawPointLights();
        // EditorGUILayout.Space(5);
        DrawSpotLights();
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
    }

    private void DrawGlobalSettings()
    {
        GUILayout.Label("全局设置", EditorStyles.boldLabel);
        m_MaterialEditor.ShaderProperty(disableEnvironment, "禁用环境光");
        m_MaterialEditor.ShaderProperty(useVerShadow, "使用顶点阴影");
    }

    private void DrawBaseProperties()
    {
        GUILayout.Label("1 ▌基础属性 (Base Properties)", EditorStyles.boldLabel);
        m_MaterialEditor.ColorProperty(baseColor, "基础颜色");
        m_MaterialEditor.TextureProperty(baseMap, "颜色贴图 (RGB)");
    }

    private void DrawMetallicRoughnessAO()
    {
        GUILayout.Label("2 ▌金属度 粗糙度 AO (Metallic Roughness AO)", EditorStyles.boldLabel);
        
        m_MaterialEditor.RangeProperty(metallic, "金属度");
        m_MaterialEditor.RangeProperty(roughness, "粗糙度");
        m_MaterialEditor.RangeProperty(specularScale, "高光强度");
        m_MaterialEditor.RangeProperty(halfLambert, "半兰伯特");
        m_MaterialEditor.RangeProperty(shadowScale, "自身阴影强度");
        m_MaterialEditor.RangeProperty(brightness, "亮度");
        
        if (disableEnvironment.floatValue < 0.5f)
        {
            m_MaterialEditor.VectorProperty(bakedSpecularDirection, "烘焙高光方向");
        }

        m_MaterialEditor.ShaderProperty(useMsaMap, "使用金属度粗糙度贴图");
        
        if (useMsaMap.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            m_MaterialEditor.TextureProperty(metallicGlossMap, "金属度(R) 粗糙度(G) AO(B)");
            EditorGUI.indentLevel--;
            
            m_MaterialEditor.ShaderProperty(useAOMap, "使用 AO(B) 通道");
            
            if (useAOMap.floatValue > 0.5f)
            {
                EditorGUI.indentLevel++;
                m_MaterialEditor.RangeProperty(occlusionContrast, "AO 对比度");
                m_MaterialEditor.RangeProperty(occlusionStrength, "AO 强度");
                m_MaterialEditor.ShaderProperty(previewAOMap, "预览 AO(B) 通道");
                EditorGUI.indentLevel--;
            }
        }
    }

    private void DrawNormalMap()
    {
        // GUILayout.Label("3 ▌法线贴图 (Normal Map)", EditorStyles.boldLabel);
        
        m_MaterialEditor.ShaderProperty(useNormalMap, "使用法线贴图");
        
        if (useNormalMap.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            m_MaterialEditor.TextureProperty(bumpMap, "法线贴图");
            m_MaterialEditor.RangeProperty(bumpScale, "法线强度");
            m_MaterialEditor.ShaderProperty(filpG, "翻转绿色通道");
            m_MaterialEditor.ShaderProperty(debugNormal, "调试法线贴图");
            EditorGUI.indentLevel--;
        }
    }

    private void DrawEmission()
    {
        // GUILayout.Label("4 ▌自发光 (Emission)", EditorStyles.boldLabel);
        
        m_MaterialEditor.ShaderProperty(useEmissionMap, "使用自发光贴图");
        
        if (useEmissionMap.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            m_MaterialEditor.ColorProperty(emissionColor, "自发光颜色");
            m_MaterialEditor.TextureProperty(emissionMap, "自发光贴图");
            m_MaterialEditor.RangeProperty(emissionScale, "自发光缩放");
            m_MaterialEditor.ShaderProperty(invertEmisMap, "反转自发光贴图");
            EditorGUI.indentLevel--;
        }
    }

    private void DrawReflection()
    {
        // GUILayout.Label("5 ▌反射 (Reflection)", EditorStyles.boldLabel);
        
        m_MaterialEditor.ShaderProperty(useReflection, "使用反射贴图");
        
        if (useReflection.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            m_MaterialEditor.TextureProperty(sphericalReflectionMap, "球形反射贴图");
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
        m_MaterialEditor.ShaderProperty(usePointlight, "使用点光源");
        
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
        m_MaterialEditor.ShaderProperty(useSpotlight, "使用聚光灯");
        
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
                m_MaterialEditor.TextureProperty(spotTexture, "聚光灯纹理");
                m_MaterialEditor.RangeProperty(spotTextureContrast, "纹理对比度");
                m_MaterialEditor.RangeProperty(spotTextureSize, "纹理大小");
                m_MaterialEditor.RangeProperty(spotTextureIntensity, "纹理强度");
                EditorGUI.indentLevel--;
            }
            
            EditorGUI.indentLevel--;
        }
    }

    private void DrawPerformance()
    {
        GUILayout.Label("# ▌性能 (Performance)", EditorStyles.boldLabel);
        m_MaterialEditor.ShaderProperty(cullMode, "剔除模式");
    }
}
