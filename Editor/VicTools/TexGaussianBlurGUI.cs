// TexGaussianBlur_HLSL.shader 自定义材质编辑器
// 集成单帧捕获功能，无需额外挂载脚本

using UnityEngine;
using UnityEditor;
using VicTools;

public class TexGaussianBlurGUI : ShaderGUI
{
    // 属性缓存
    private MaterialProperty performanceMode;
    private MaterialProperty blurSize;
    private MaterialProperty pixelSize;
    private MaterialProperty sigma;
    private MaterialProperty sampleCount;
    private MaterialProperty invertDirection;
    private MaterialProperty useSceneColor;
    private MaterialProperty singleFrame;
    private MaterialProperty capturedSceneTex;
    private MaterialProperty blurSourceTex;
    private MaterialProperty texExposure;
    private MaterialProperty contrast;
    private MaterialProperty useDistortion;
    private MaterialProperty distortionTex;
    private MaterialProperty distortionStrength;
    private MaterialProperty distortionSpeed;

    // 单帧捕获参数
    private static int s_CaptureDelayFrames = 2;
    private static float s_ResolutionScale = 0.5f;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        FindProperties(properties);

        EditorGUILayout.Space(4);
        DrawBlurSettings(materialEditor);
        EditorGUILayout.Space(4);
        DrawSceneColorSettings(materialEditor);
        EditorGUILayout.Space(4);
        DrawSourceTexture(materialEditor);
        EditorGUILayout.Space(4);
        DrawColorAdjust(materialEditor);
        EditorGUILayout.Space(4);
        DrawDistortion(materialEditor);
        EditorGUILayout.Space(8);
        DrawRenderSettings(materialEditor);
    }

    private void FindProperties(MaterialProperty[] props)
    {
        performanceMode = FindProperty("_PerformanceMode", props, false);
        blurSize = FindProperty("_BlurSize", props);
        pixelSize = FindProperty("_PixelSize", props);
        sigma = FindProperty("_Sigma", props);
        sampleCount = FindProperty("_SampleCount", props);
        invertDirection = FindProperty("_InvertDirection", props);
        useSceneColor = FindProperty("_UseSceneColor", props, false);
        singleFrame = FindProperty("_SingleFrame", props, false);
        capturedSceneTex = FindProperty("_CapturedSceneTex", props, false);
        blurSourceTex = FindProperty("_BlurSourceTex", props);
        texExposure = FindProperty("_TexExposure", props);
        contrast = FindProperty("_Contrast", props);
        useDistortion = FindProperty("_UseDistortion", props, false);
        distortionTex = FindProperty("_DistortionTex", props, false);
        distortionStrength = FindProperty("_DistortionStrength", props, false);
        distortionSpeed = FindProperty("_DistortionSpeed", props, false);
    }

    private void DrawBlurSettings(MaterialEditor editor)
    {
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUILayout.Label(HeaderStyle.Rich("模糊设置", HeaderStyle.Sparkle), EditorStyle.Get.BoldLabelRichStyle);

            if (performanceMode != null)
            {
                editor.ShaderProperty(performanceMode, new GUIContent("性能模式",
                    "开启后使用 Kawase 快速采样(14次)，关闭使用完整高斯"));
            }

            editor.RangeProperty(blurSize, "模糊大小");
            editor.RangeProperty(pixelSize, "像素大小");

            // 质量模式参数
            bool isPerfMode = performanceMode != null && performanceMode.floatValue > 0.5f;
            if (!isPerfMode)
            {
                editor.RangeProperty(sigma, "Sigma (高斯权重)");
                editor.RangeProperty(sampleCount, "采样数量");
            }
            else
            {
                EditorGUILayout.HelpBox("性能模式下采样数固定为14次，Sigma 和 Sample Count 无效", MessageType.Info);
            }

            editor.ShaderProperty(invertDirection, "翻转方向");
        }
    }

    private void DrawSceneColorSettings(MaterialEditor editor)
    {
        if (useSceneColor == null) return;

        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUILayout.Label(HeaderStyle.Rich("场景颜色", HeaderStyle.Base), EditorStyle.Get.BoldLabelRichStyle);
            editor.ShaderProperty(useSceneColor, new GUIContent("使用场景颜色",
                "开启后模糊对象为场景不透明物体渲染结果（需 URP 开启 Opaque Texture）"));

            if (useSceneColor.floatValue > 0.5f)
            {
                EditorGUI.indentLevel++;

                if (singleFrame != null)
                {
                    editor.ShaderProperty(singleFrame, new GUIContent("单帧捕获",
                        "开启后只在游戏启动时捕获一帧场景颜色，之后不再实时获取"));

                    if (singleFrame.floatValue > 0.5f)
                    {
                        DrawSingleFrameSettings(editor);
                    }
                }

                // 传递参数按钮（无论是否勾选单帧都显示）
                EditorGUILayout.Space(2);
                Material mat = editor.target as Material;
                if (mat != null)
                {
                    // 从选中的 GameObject 或正在检视的 Renderer 中检测组件
                    GameObject[] targets = Selection.gameObjects;
                    
                    // 如果没有选中 GameObject，尝试从正在检视的 Renderer 获取
                    if (targets == null || targets.Length == 0)
                    {
                        var activeGO = Selection.activeGameObject;
                        if (activeGO != null)
                            targets = new[] { activeGO };
                    }

                    if (targets != null && targets.Length > 0)
                    {
                        bool allHaveComponent = true;
                        foreach (var go in targets)
                        {
                            if (go.GetComponent<SingleFrameBlurCapture>() == null)
                            {
                                allHaveComponent = false;
                                break;
                            }
                        }

                        if (allHaveComponent)
                        {
                            GUI.backgroundColor = new Color(0.5f, 1.0f, 0.5f);
                            if (GUILayout.Button("传递模糊参数"))
                            {
                                SyncBlurParamsToSelection(mat);
                            }
                            GUI.backgroundColor = Color.white;
                        }
                        else
                        {
                            if (GUILayout.Button("为选中物体添加 SingleFrameBlurCapture"))
                            {
                                AddCaptureComponentToSelection(mat);
                            }
                        }
                    }
                }

                EditorGUI.indentLevel--;
            }
        }
    }

    private void DrawSingleFrameSettings(MaterialEditor editor)
    {
        EditorGUI.indentLevel++;
        EditorGUILayout.Space(2);

        s_CaptureDelayFrames = EditorGUILayout.IntSlider(
            new GUIContent("捕获延迟帧数", "等待几帧后再捕获，确保场景渲染完成"),
            s_CaptureDelayFrames, 1, 10);

        s_ResolutionScale = EditorGUILayout.Slider(
            new GUIContent("分辨率缩放", "降低 RT 分辨率以节省显存"),
            s_ResolutionScale, 0.25f, 1f);

        EditorGUILayout.Space(2);

        // 编辑器预览：手动捕获
        if (Application.isPlaying)
        {
            GUI.backgroundColor = new Color(1.0f, 0.9f, 0.3f);
            if (GUILayout.Button("立即捕获一帧"))
            {
                Material m = editor.target as Material;
                if (m != null) SyncBlurParamsToSelection(m);
                CaptureNow(editor);
            }
            GUI.backgroundColor = Color.white;
        }

        EditorGUI.indentLevel--;
    }

    private void DrawSourceTexture(MaterialEditor editor)
    {
        // 如果已开启场景颜色且非单帧模式，不需要显示源纹理
        bool sceneColorActive = useSceneColor != null && useSceneColor.floatValue > 0.5f;
        bool singleFrameActive = singleFrame != null && singleFrame.floatValue > 0.5f;

        if (sceneColorActive && !singleFrameActive) return;

        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUILayout.Label(HeaderStyle.Rich("模糊源纹理", HeaderStyle.Base), EditorStyle.Get.BoldLabelRichStyle);

            if (sceneColorActive && singleFrameActive)
            {
                // 单帧模式下显示捕获的纹理（只读）
                if (capturedSceneTex != null)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    editor.TexturePropertySingleLine(new GUIContent("捕获的场景纹理"), capturedSceneTex);
                    EditorGUI.EndDisabledGroup();
                }
            }
            else
            {
                editor.TexturePropertySingleLine(new GUIContent("模糊源纹理"), blurSourceTex);
            }
        }
    }

    private void DrawColorAdjust(MaterialEditor editor)
    {
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUILayout.Label(HeaderStyle.Rich("颜色调整", HeaderStyle.Base), EditorStyle.Get.BoldLabelRichStyle);
            editor.RangeProperty(texExposure, "曝光");
            editor.RangeProperty(contrast, "对比度");
        }
    }

    private void DrawDistortion(MaterialEditor editor)
    {
        if (useDistortion == null) return;

        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUILayout.Label(HeaderStyle.Rich("扰动", HeaderStyle.Sparkle), EditorStyle.Get.BoldLabelRichStyle);
            editor.ShaderProperty(useDistortion, "使用扰动");

            if (useDistortion.floatValue > 0.5f)
            {
                EditorGUI.indentLevel++;
                if (distortionTex != null)
                    editor.TexturePropertySingleLine(new GUIContent("扰动纹理"), distortionTex);
                if (distortionStrength != null)
                    editor.RangeProperty(distortionStrength, "扰动强度");
                if (distortionSpeed != null)
                    editor.RangeProperty(distortionSpeed, "扰动速度");
                EditorGUI.indentLevel--;
            }
        }
    }

    private void DrawRenderSettings(MaterialEditor editor)
    {
        GUILayout.Label(HeaderStyle.Rich("渲染设置", HeaderStyle.Render), EditorStyle.Get.BoldLabelRichStyle);
        editor.RenderQueueField();
        editor.EnableInstancingField();
        editor.DoubleSidedGIField();
    }

    // ====== 单帧捕获辅助功能 ======

    private static void AddCaptureComponentToSelection(Material mat)
    {
        float materialBlurSize = mat.GetFloat("_BlurSize");
        float materialPixelSize = mat.GetFloat("_PixelSize");
        float materialSigma = mat.GetFloat("_Sigma");
        int materialSampleCount = Mathf.RoundToInt(mat.GetFloat("_SampleCount"));
        bool perfMode = mat.IsKeywordEnabled("_PERFORMANCE_MODE");

        foreach (var go in Selection.gameObjects)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) continue;

            var capture = go.GetComponent<SingleFrameBlurCapture>();
            if (capture == null)
            {
                capture = Undo.AddComponent<SingleFrameBlurCapture>(go);
                Debug.Log($"已为 {go.name} 添加 SingleFrameBlurCapture 组件");
            }

            Undo.RecordObject(capture, "Sync Blur Params");
            capture.captureDelayFrames = s_CaptureDelayFrames;
            capture.resolutionScale = s_ResolutionScale;
            capture.blurSize = materialBlurSize;
            capture.pixelSize = materialPixelSize;
            capture.sigma = materialSigma;
            capture.sampleCount = materialSampleCount;
            capture.usePerformanceMode = perfMode;
            EditorUtility.SetDirty(capture);
        }
    }

    private static void SyncBlurParamsToSelection(Material mat)
    {
        float materialBlurSize = mat.GetFloat("_BlurSize");
        float materialPixelSize = mat.GetFloat("_PixelSize");
        float materialSigma = mat.GetFloat("_Sigma");
        int materialSampleCount = Mathf.RoundToInt(mat.GetFloat("_SampleCount"));
        bool perfMode = mat.IsKeywordEnabled("_PERFORMANCE_MODE");

        foreach (var go in Selection.gameObjects)
        {
            var capture = go.GetComponent<SingleFrameBlurCapture>();
            if (capture == null) continue;

            Undo.RecordObject(capture, "Sync Blur Params");
            capture.captureDelayFrames = s_CaptureDelayFrames;
            capture.resolutionScale = s_ResolutionScale;
            capture.blurSize = materialBlurSize;
            capture.pixelSize = materialPixelSize;
            capture.sigma = materialSigma;
            capture.sampleCount = materialSampleCount;
            capture.usePerformanceMode = perfMode;
            EditorUtility.SetDirty(capture);
        }
        Debug.Log($"已传递模糊参数: BlurSize={materialBlurSize:F1}, PixelSize={materialPixelSize:F2}, Sigma={materialSigma:F1}, SampleCount={materialSampleCount}, PerfMode={perfMode}");
    }

    private void CaptureNow(MaterialEditor editor)
    {
        Material mat = editor.target as Material;
        if (mat == null) return;

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("未找到主相机，无法捕获");
            return;
        }

        int width = Mathf.Max(64, Mathf.RoundToInt(cam.pixelWidth * s_ResolutionScale));
        int height = Mathf.Max(64, Mathf.RoundToInt(cam.pixelHeight * s_ResolutionScale));

        RenderTexture sceneRT = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        sceneRT.name = "EditorSingleFrameCapture";

        // 临时隐藏使用该材质的所有 Renderer
        var allRenderers = Object.FindObjectsOfType<Renderer>();
        var disabledRenderers = new System.Collections.Generic.List<Renderer>();
        foreach (var r in allRenderers)
        {
            if (r.sharedMaterial == mat && r.enabled)
            {
                r.enabled = false;
                disabledRenderers.Add(r);
            }
        }

        var originalRT = cam.targetTexture;
        cam.targetTexture = sceneRT;
        cam.Render();
        cam.targetTexture = originalRT;

        // 恢复
        foreach (var r in disabledRenderers)
            r.enabled = true;

        // 模糊处理（与主材质算法完全一致）
        Shader blurShader = Shader.Find("Hidden/SingleFrameBlur");
        if (blurShader != null)
        {
            Material blurMat = new Material(blurShader);
            blurMat.SetFloat("_BlurSize", mat.GetFloat("_BlurSize"));
            blurMat.SetFloat("_PixelSize", mat.GetFloat("_PixelSize"));
            blurMat.SetFloat("_Sigma", mat.GetFloat("_Sigma"));
            blurMat.SetFloat("_SampleCount", mat.GetFloat("_SampleCount"));
            blurMat.SetFloat("_UsePerformanceMode", mat.IsKeywordEnabled("_PERFORMANCE_MODE") ? 1f : 0f);
            blurMat.SetFloat("_ResolutionScale", s_ResolutionScale);

            // 单次 Blit 完成全部模糊
            RenderTexture result = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            result.name = "EditorBlurredCapture";
            Graphics.Blit(sceneRT, result, blurMat);

            RenderTexture.ReleaseTemporary(sceneRT);
            Object.DestroyImmediate(blurMat);

            mat.SetTexture("_CapturedSceneTex", result);
            Debug.Log("已捕获并模糊一帧场景颜色到材质");
        }
        else
        {
            RenderTexture.ReleaseTemporary(sceneRT);
            Debug.LogWarning("未找到 Hidden/SingleFrameBlur shader，无法模糊");
        }
    }
}
