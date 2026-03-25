// Glass_carWindow.shader GUI控制脚本
// 基于PBR_MobileGUI的控制逻辑实现
using UnityEngine;
using UnityEditor;

public class Glass_carWindowGUI : ShaderGUI
{
    private MaterialEditor m_MaterialEditor;
    private MaterialProperty[] m_Properties;

    // 缓存属性
    private MaterialProperty baseColor;
    private MaterialProperty transparency;
    private MaterialProperty smoothness;
    private MaterialProperty specularStrength;
    private MaterialProperty sceneBlurStrength; // Glass_MobileNew独有
    private MaterialProperty useNormalMap;
    private MaterialProperty bumpMap;
    private MaterialProperty bumpScale;
    private MaterialProperty useRefraction; // Glass_MobileNew独有
    private MaterialProperty refractionStrength; // Glass_MobileNew独有
    private MaterialProperty useReflection;
    private MaterialProperty sphericalReflectionMap;
    private MaterialProperty reflectionScale;
    private MaterialProperty reflectionOffset; // Glass_carWindow独有
    private MaterialProperty reflectionBlur;
    private MaterialProperty fresnelPower;
    private MaterialProperty fresnelBias;
    private MaterialProperty fresnelScale;
    private MaterialProperty useFresnelRamp; // Glass_carWindow独有
    private MaterialProperty fresnelRampTexture; // Glass_carWindow独有
    private MaterialProperty fresnelRampRow; // Glass_carWindow独有
    private MaterialProperty fresnelRampIntensity; // Glass_carWindow独有
    private MaterialProperty cullMode;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        m_MaterialEditor = materialEditor;
        m_Properties = properties;

        // 查找所有属性
        FindProperties();

        // 绘制 GUI
        DrawGlobalSettings();
        DrawGlassProperties();
        DrawSpecular();
        DrawDistortion();
        DrawRefraction(); // Glass_MobileNew独有
        DrawReflection();
        DrawFresnel();
        DrawFresnelRamp(); // Glass_carWindow独有
        DrawRenderSettings();
    }

    private void FindProperties()
    {
        baseColor = FindProperty("_BaseColor", m_Properties);
        transparency = FindProperty("_Transparency", m_Properties);
        smoothness = FindProperty("_Smoothness", m_Properties);
        specularStrength = FindProperty("_SpecularStrength", m_Properties);
        sceneBlurStrength = FindProperty("_SceneBlurStrength", m_Properties, false); // Glass_MobileNew独有
        useNormalMap = FindProperty("_UseNormalMap", m_Properties, false);
        bumpMap = FindProperty("_BumpMap", m_Properties);
        bumpScale = FindProperty("_BumpScale", m_Properties);
        useRefraction = FindProperty("_UseRefraction", m_Properties, false); // Glass_MobileNew独有
        refractionStrength = FindProperty("_RefractionStrength", m_Properties, false); // Glass_MobileNew独有
        useReflection = FindProperty("_UseReflection", m_Properties, false);
        sphericalReflectionMap = FindProperty("_SphericalReflectionMap", m_Properties);
        reflectionScale = FindProperty("_ReflectionScale", m_Properties);
        reflectionOffset = FindProperty("_ReflectionOffset", m_Properties, false); // Glass_carWindow独有
        reflectionBlur = FindProperty("_ReflectionBlur", m_Properties);
        fresnelPower = FindProperty("_FresnelPower", m_Properties);
        fresnelBias = FindProperty("_FresnelBias", m_Properties);
        fresnelScale = FindProperty("_FresnelScale", m_Properties);
        useFresnelRamp = FindProperty("_UseFresnelRamp", m_Properties, false); // Glass_carWindow独有
        fresnelRampTexture = FindProperty("_FresnelRampTexture", m_Properties, false); // Glass_carWindow独有
        fresnelRampRow = FindProperty("_FresnelRampRow", m_Properties, false); // Glass_carWindow独有
        fresnelRampIntensity = FindProperty("_FresnelRampIntensity", m_Properties, false); // Glass_carWindow独有
        cullMode = FindProperty("_Cull", m_Properties);
    }

    private void DrawGlobalSettings()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("全局设置", EditorStyles.boldLabel);
        
        // 添加存档按钮
        GUI.backgroundColor = new Color(0.3f, 0.8f, 1.0f); // 蓝色背景
        if (GUILayout.Button("存档", GUILayout.Width(50)))
        {
            EditorApplication.delayCall += SaveMaterialParameters;
        }
        
        // 添加读档按钮
        GUI.backgroundColor = new Color(0.5f, 1.0f, 0.5f); // 绿色背景
        if (GUILayout.Button("读档", GUILayout.Width(50)))
        {
            EditorApplication.delayCall += LoadMaterialParameters;
        }
        
        // 添加重置按钮
        GUI.backgroundColor = new Color(1.0f, 0.8f, 0.3f); // 黄色背景
        if (GUILayout.Button("重置参数", GUILayout.Width(60)))
        {
            EditorApplication.delayCall += ResetMaterialParameters;
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    private void DrawGlassProperties()
    {
        GUILayout.Label("1 ▌玻璃属性 (Glass Properties)", EditorStyles.boldLabel);
        m_MaterialEditor.ColorProperty(baseColor, "基础颜色");
        m_MaterialEditor.RangeProperty(transparency, "全局透明度");
    }

    private void DrawSpecular()
    {
        GUILayout.Label("2 ▌高光 (Specular)", EditorStyles.boldLabel);
        m_MaterialEditor.RangeProperty(smoothness, "光滑度");
        m_MaterialEditor.RangeProperty(specularStrength, "高光强度");
        
        // Glass_MobileNew独有参数
        if (sceneBlurStrength != null)
        {
            m_MaterialEditor.RangeProperty(sceneBlurStrength, "场景模糊强度");
        }
    }

    private void DrawDistortion()
    {
        m_MaterialEditor.ShaderProperty(useNormalMap, "3 ▌使用法线贴图 (扭曲)");
        
        if (useNormalMap.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            m_MaterialEditor.TexturePropertySingleLine(new GUIContent("法线贴图"), bumpMap);
            
            // 显示法线贴图的Tiling和Offset参数
            if (bumpMap.textureValue != null)
            {
                EditorGUI.indentLevel++;
                
                // 获取材质
                Material material = m_MaterialEditor.target as Material;
                if (material != null)
                {
                    // 获取当前的Tiling和Offset
                    Vector2 tiling = material.GetTextureScale("_BumpMap");
                    Vector2 offset = material.GetTextureOffset("_BumpMap");
                    
                    EditorGUI.BeginChangeCheck();
                    
                    // 显示Tiling
                    tiling = EditorGUILayout.Vector2Field("Tiling", tiling);
                    
                    // 显示Offset
                    offset = EditorGUILayout.Vector2Field("Offset", offset);
                    
                    if (EditorGUI.EndChangeCheck())
                    {
                        // 记录Undo
                        Undo.RecordObject(material, "Change Normal Map Tiling/Offset");
                        
                        // 应用修改
                        material.SetTextureScale("_BumpMap", tiling);
                        material.SetTextureOffset("_BumpMap", offset);
                        
                        EditorUtility.SetDirty(material);
                    }
                }
                
                EditorGUI.indentLevel--;
            }
            
            m_MaterialEditor.RangeProperty(bumpScale, "法线强度");
            EditorGUI.indentLevel--;
        }
    }

    private void DrawRefraction()
    {
        // Glass_MobileNew独有的折射功能
        if (useRefraction != null && refractionStrength != null)
        {
            m_MaterialEditor.ShaderProperty(useRefraction, "4 ▌使用折射");
            
            if (useRefraction.floatValue > 0.5f)
            {
                EditorGUI.indentLevel++;
                m_MaterialEditor.RangeProperty(refractionStrength, "折射强度");
                EditorGUI.indentLevel--;
            }
        }
    }

    private void DrawReflection()
    {
        // 根据是否有折射功能来决定标题编号
        string sectionNumber = (useRefraction != null) ? "5" : "4";
        m_MaterialEditor.ShaderProperty(useReflection, $"{sectionNumber} ▌使用反射贴图");
        
        if (useReflection.floatValue > 0.5f)
        {
            EditorGUI.indentLevel++;
            m_MaterialEditor.TexturePropertySingleLine(new GUIContent("球形反射贴图"), sphericalReflectionMap);
            m_MaterialEditor.RangeProperty(reflectionScale, "反射强度");
            
            // Glass_carWindow独有参数
            if (reflectionOffset != null)
            {
                m_MaterialEditor.RangeProperty(reflectionOffset, "反射偏移");
            }
            
            m_MaterialEditor.RangeProperty(reflectionBlur, "最大反射模糊");
            EditorGUI.indentLevel--;
        }
    }

    private void DrawFresnel()
    {
        // 根据是否有折射功能来决定标题编号
        string sectionNumber = (useRefraction != null) ? "6" : "5";
        GUILayout.Label($"{sectionNumber} ▌菲涅尔 (Fresnel)", EditorStyles.boldLabel);
        m_MaterialEditor.RangeProperty(fresnelPower, "全局强度");
        m_MaterialEditor.RangeProperty(fresnelBias, "中心偏移");
        m_MaterialEditor.RangeProperty(fresnelScale, "边缘缩放");
    }

    private void DrawFresnelRamp()
    {
        // Glass_carWindow独有的菲涅尔渐变贴图功能
        if (useFresnelRamp != null && fresnelRampTexture != null && 
            fresnelRampRow != null && fresnelRampIntensity != null)
        {
            // 根据是否有折射功能来决定标题编号
            string sectionNumber = (useRefraction != null) ? "7" : "6";
            m_MaterialEditor.ShaderProperty(useFresnelRamp, $"{sectionNumber} ▌使用菲涅尔渐变贴图");
            
            if (useFresnelRamp.floatValue > 0.5f)
            {
                EditorGUI.indentLevel++;
                
                // 添加说明文字
                EditorGUILayout.HelpBox("• 横向(X轴)：从左到右对应菲涅尔从内到外（使用固定Fresnel计算）\n• 纵向(Y轴)：不同行代表不同的渐变效果\n• 渐变行选择值：0.01=底部，0.99=顶部\n• 固定Fresnel：不受菲涅尔参数影响，确保0-1完整范围", MessageType.Info);
                
                m_MaterialEditor.TexturePropertySingleLine(new GUIContent("菲涅尔渐变贴图"), fresnelRampTexture);
                m_MaterialEditor.RangeProperty(fresnelRampRow, "渐变行选择");
                m_MaterialEditor.RangeProperty(fresnelRampIntensity, "渐变强度");
                EditorGUI.indentLevel--;
            }
        }
    }

    private void DrawRenderSettings()
    {
        // 根据是否有折射功能来决定标题编号
        // string sectionNumber = (useRefraction != null) ? "8" : "7";
        // GUILayout.Label($"{sectionNumber} ▌渲染设置 (Render Settings)", EditorStyles.boldLabel);
        GUILayout.Label("7 ▌渲染设置 (Render Settings)", EditorStyles.boldLabel);
        m_MaterialEditor.ShaderProperty(cullMode, "剔除模式");
    }

    /// 获取材质参数存档路径
    private string GetPresetPath(string presetName)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return null;
        
        string shaderName = material.shader.name.Replace("/", "_");
        string folderPath = "Library/VicTools/Glass/" + shaderName;
        
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
        string defaultPath = "Library/VicTools/Glass/" + shaderName;
        
        // 确保目录存在
        if (!System.IO.Directory.Exists(defaultPath))
        {
            System.IO.Directory.CreateDirectory(defaultPath);
        }
        
        // 弹出输入框让用户输入存档名称
        string presetName = EditorUtility.SaveFilePanel(
            "保存玻璃材质参数存档",
            defaultPath,
            "GlassPreset",
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
            
            // 排除纹理
            if (propertyType == ShaderUtil.ShaderPropertyType.TexEnv) continue;
            
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
        
        Debug.Log($"玻璃材质参数已保存到: {path}");
    }
    
    /// 读档材质参数
    private void LoadMaterialParameters()
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;
        
        string shaderName = material.shader.name.Replace("/", "_");
        string defaultPath = "Library/VicTools/Glass/" + shaderName;
        
        // 确保目录存在
        if (!System.IO.Directory.Exists(defaultPath))
        {
            System.IO.Directory.CreateDirectory(defaultPath);
        }
        
        // 弹出文件选择框
        string presetPath = EditorUtility.OpenFilePanel(
            "加载玻璃材质参数存档",
            defaultPath,
            "json"
        );
        
        if (string.IsNullOrEmpty(presetPath)) return;
        
        LoadMaterialParametersFromFile(presetPath);
    }
    
    /// 从文件加载材质参数
    private void LoadMaterialParametersFromFile(string filePath)
    {
        Material material = m_MaterialEditor.target as Material;
        if (material == null || material.shader == null) return;
        
        if (!System.IO.File.Exists(filePath))
        {
            Debug.LogWarning($"存档文件不存在: {filePath}");
            return;
        }
        
        // 记录撤销操作
        Undo.RecordObject(material, "Load Glass Material Parameters");
        
        // 读取JSON
        string json = System.IO.File.ReadAllText(filePath);
        
        // 手动解析JSON
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
        
        // 刷新材质编辑器
        if (m_MaterialEditor != null)
        {
            m_MaterialEditor.Repaint();
        }
        
        // 刷新场景视图
        SceneView.RepaintAll();
        
        // 强制更新shader关键字
        material.shader = material.shader;
        
        Debug.Log($"玻璃材质参数已从存档加载: {filePath}");
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
                    
                    // 排除纹理
                    if (propertyType == ShaderUtil.ShaderPropertyType.TexEnv) continue;
                    
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
                
                Debug.Log($"玻璃材质Default存档已创建: {defaultPresetPath}");
                
                // 加载Default存档
                LoadMaterialParametersFromFile(defaultPresetPath);
            }
        }
    }
}
