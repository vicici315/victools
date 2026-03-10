// v1.1 Texture材质UI控制（支持渲染模式设置，修复Undo问题）
using UnityEngine;
using UnityEditor;

public class CustomTextureGUI : ShaderGUI
{
    public enum RenderMode
    {
        Opaque,
        Cutout,
        Transparent
    }

    private MaterialProperty mainTex;
    private MaterialProperty color;
    private MaterialProperty contrast;
    private MaterialProperty brightness;
    private MaterialProperty useAlphaClip;
    private MaterialProperty cutoff;
    private MaterialProperty useAlphaBlend;
    private MaterialProperty cullMode;
    private MaterialProperty srcBlend;
    private MaterialProperty dstBlend;
    private MaterialProperty zWrite;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        // 查找属性
        mainTex = FindProperty("_MainTex", properties);
        color = FindProperty("_Color", properties);
        contrast = FindProperty("_Contrast", properties);
        brightness = FindProperty("_Brightness", properties);
        useAlphaClip = FindProperty("_UseAlphaClip", properties);
        cutoff = FindProperty("_Cutoff", properties);
        useAlphaBlend = FindProperty("_UseAlphaBlend", properties);
        cullMode = FindProperty("_Cull", properties);
        srcBlend = FindProperty("_SrcBlend", properties);
        dstBlend = FindProperty("_DstBlend", properties);
        zWrite = FindProperty("_ZWrite", properties);

        Material material = materialEditor.target as Material;

        // 渲染模式选择
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("渲染模式", EditorStyles.boldLabel);
        
        RenderMode currentMode = GetRenderMode(material);
        RenderMode newMode = (RenderMode)EditorGUILayout.EnumPopup("Render Mode", currentMode);
        
        // 只在用户主动更改时才设置，并记录 Undo
        if (newMode != currentMode)
        {
            Undo.RecordObject(material, "Change Render Mode");
            SetRenderMode(material, newMode);
            EditorUtility.SetDirty(material);
        }

        // 透明度设置（仅在 Cutout 模式显示）
        if (currentMode == RenderMode.Cutout)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("透明度设置", EditorStyles.boldLabel);
            materialEditor.RangeProperty(cutoff, "Alpha 裁剪阈值");
        }
        
        // 透明模式设置
        if (currentMode == RenderMode.Transparent)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("透明模式设置", EditorStyles.boldLabel);
            
            EditorGUILayout.HelpBox("云朵透明效果建议：\n1. 调整渲染队列避免闪烁\n2. 确保物体不要重叠太多\n3. 使用合适的混合模式（SrcAlpha、One）", MessageType.Info);
            
            // 渲染队列微调
            int currentQueue = material.renderQueue;
            int baseTransparentQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            int queueOffset = currentQueue - baseTransparentQueue;
            
            EditorGUILayout.LabelField($"当前渲染队列: {currentQueue}");
            int newQueueOffset = EditorGUILayout.IntSlider("队列偏移 (解决闪烁)", queueOffset, -50, 50);
            
            if (newQueueOffset != queueOffset)
            {
                Undo.RecordObject(material, "Change Render Queue");
                material.renderQueue = baseTransparentQueue + newQueueOffset;
                EditorUtility.SetDirty(material);
            }
        }
        
        EditorGUILayout.Space();
        
        // 纹理设置
        EditorGUILayout.LabelField("纹理设置", EditorStyles.boldLabel);
        materialEditor.TexturePropertySingleLine(new GUIContent("纹理"), mainTex);
        
        // 显示 Tiling 和 Offset
        materialEditor.TextureScaleOffsetProperty(mainTex);
        
        materialEditor.ColorProperty(color, "颜色叠加");
        materialEditor.RangeProperty(contrast, "对比度");
        materialEditor.RangeProperty(brightness, "亮度");

        // 渲染设置
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("渲染设置", EditorStyles.boldLabel);
        materialEditor.ShaderProperty(cullMode, "剔除模式");
        
        // 高级设置（折叠）
        EditorGUILayout.Space();
        bool showAdvanced = EditorGUILayout.Foldout(
            EditorPrefs.GetBool("CustomTexture_ShowAdvanced", false), 
            "高级设置"
        );
        EditorPrefs.SetBool("CustomTexture_ShowAdvanced", showAdvanced);
        
        if (showAdvanced)
        {
            EditorGUI.indentLevel++;
            materialEditor.ShaderProperty(srcBlend, "源混合模式");
            materialEditor.ShaderProperty(dstBlend, "目标混合模式");
            materialEditor.ShaderProperty(zWrite, "深度写入");
            EditorGUI.indentLevel--;
        }
    }

    private RenderMode GetRenderMode(Material material)
    {
        // 优先检查关键字，因为它们直接影响渲染
        if (material.IsKeywordEnabled("_ALPHABLEND_ON"))
            return RenderMode.Transparent;
        if (material.IsKeywordEnabled("_ALPHATEST_ON"))
            return RenderMode.Cutout;
            
        // 如果关键字都没有启用，检查属性值作为备用
        float useAlphaClip = material.GetFloat("_UseAlphaClip");
        float useAlphaBlend = material.GetFloat("_UseAlphaBlend");
        
        if (useAlphaBlend > 0.5f)
            return RenderMode.Transparent;
        if (useAlphaClip > 0.5f)
            return RenderMode.Cutout;
            
        return RenderMode.Opaque;
    }

    private void SetRenderMode(Material material, RenderMode mode)
    {
        switch (mode)
        {
            case RenderMode.Opaque:
                // 设置属性值，Unity 会自动管理关键字
                material.SetFloat("_UseAlphaClip", 0);
                material.SetFloat("_UseAlphaBlend", 0);
                // 手动确保关键字正确
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.SetOverrideTag("RenderType", "Opaque");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                material.SetFloat("_ZWrite", 1.0f);
                break;

            case RenderMode.Cutout:
                material.SetFloat("_UseAlphaClip", 1);
                material.SetFloat("_UseAlphaBlend", 0);
                // 手动确保关键字正确
                material.EnableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                material.SetFloat("_ZWrite", 1.0f);
                break;

            case RenderMode.Transparent:
                material.SetFloat("_UseAlphaClip", 0);
                material.SetFloat("_UseAlphaBlend", 1);
                // 手动确保关键字正确
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.SetOverrideTag("RenderType", "Transparent");
                
                // 使用标准透明队列
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                // 关闭深度写入以获得正确的透明效果
                material.SetFloat("_ZWrite", 0.0f);
                break;
        }
        
        // 强制刷新材质，确保所有更改立即生效
        material.shader = material.shader;
    }
}
