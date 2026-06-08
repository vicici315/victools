// ============================================================
// TextureGaussianBlur_HLSL v1.0 — 高斯模糊材质 Shader
// ============================================================
// 功能概述：
//   对指定纹理或场景颜色进行高斯模糊，适用于毛玻璃、背景虚化等效果。
//
// 模糊源（二选一）：
//   1. _BlurSourceTex — 用户指定的静态纹理（使用模型UV采样）
//   2. _CameraOpaqueTexture — URP 场景不透明颜色（屏幕空间采样，需开启 Opaque Texture）
//
// 模式选项：
//   - Performance Mode（性能模式）：Kawase 风格固定14次采样，适合实时渲染
//   - Quality Mode（质量模式）：完整高斯核采样，SampleCount 可调，适合高画质需求
//   - Single Frame（单帧捕获）：启动时捕获一帧场景颜色并预模糊，
//     之后 shader 仅做1次纹理采样，性能几乎无损
//
// 附加功能：
//   - 扰动（Distortion）：法线贴图驱动的UV偏移动画
//   - 曝光/对比度调节
//   - 方向翻转
//
// 辅助文件：
//   - Editor/VicTools/TexGaussianBlurGUI.cs — 自定义材质面板（CustomEditor）
//   - Runtime/Scripts/SingleFrameBlurCapture.cs — 单帧捕获运行时组件
//     （自动排除自身、GPU预模糊、需在Inspector拖入BlurShader引用）
//   - Runtime/Shaders/SingleFrameBlur.shader — 单帧捕获用的内部Blit模糊Shader
//
// 使用注意：
//   - 使用场景颜色时渲染队列需为 Transparent（已默认设置）
//   - 单帧模式需在 Renderer 上挂载 SingleFrameBlurCapture 组件
//   - URP 设置中需开启 Opaque Texture（Pipeline Asset → General）
// ============================================================

Shader "Custom/Blur/TextureGaussianBlur_HLSL"
{
    Properties
    {
        [Toggle(_PERFORMANCE_MODE)] _PerformanceMode ("Performance Mode (Kawase Blur)", Float) = 0
        _BlurSize ("Blur Size", Range(0.0, 11.0)) = 4.0
        _PixelSize ("Pixel Size", Range(0.25, 4.0)) = 1.0
        _Sigma ("Sigma", Range(0.1, 5.0)) = 2.7
        _SampleCount ("Sample Count", Range(1, 12)) = 6
        [Toggle] _InvertDirection ("Invert Direction", Float) = 0
        [Toggle(_USE_SCENE_COLOR)] _UseSceneColor ("Use Scene Color (when no source tex)", Float) = 0
        [Toggle(_SINGLE_FRAME)] _SingleFrame ("Single Frame Capture", Float) = 0
        [HideInInspector][NoScaleOffset] _CapturedSceneTex ("Captured Scene Texture", 2D) = "white" {}
        [NoScaleOffset] _BlurSourceTex ("Blur Source Texture", 2D) = "white" {}
        _TexExposure ("Blur Texture Exposure", Range(0.1, 1.0)) = 0.7
        _Contrast ("Contrast", Range(0.0, 2.0)) = 1.0
        
        [Toggle(_USE_DISTORTION)] _UseDistortion ("Use Distortion", Float) = 0
        _DistortionTex ("Distortion Texture", 2D) = "bump" {}
        _DistortionStrength ("Distortion Strength", Range(0.0, 0.1)) = 0.01
        _DistortionSpeed ("Distortion Speed", Range(0.0, 5.0)) = 1.0
    }
    
    SubShader
    {
        Tags
        {
            "QUEUE" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        
        ZWrite On
        Blend SrcAlpha OneMinusSrcAlpha
        
        Pass
        {
            Name "Universal Forward"
            
            HLSLPROGRAM
            
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _USE_DISTORTION
            #pragma shader_feature_local _USE_SCENE_COLOR
            #pragma shader_feature_local _SINGLE_FRAME
            #pragma shader_feature_local _PERFORMANCE_MODE
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"
            
            TEXTURE2D(_BlurSourceTex);
            SAMPLER(sampler_BlurSourceTex);
            float4 _BlurSourceTex_TexelSize;
            
            #if defined(_USE_SCENE_COLOR) && defined(_SINGLE_FRAME)
            TEXTURE2D(_CapturedSceneTex);
            SAMPLER(sampler_CapturedSceneTex);
            float4 _CapturedSceneTex_TexelSize;
            #endif
            
            #if defined(_USE_SCENE_COLOR) && !defined(_SINGLE_FRAME)
            TEXTURE2D(_CameraOpaqueTexture);
            SAMPLER(sampler_CameraOpaqueTexture);
            float4 _CameraOpaqueTexture_TexelSize;
            #endif
            
            #ifdef _USE_DISTORTION
            TEXTURE2D(_DistortionTex);
            SAMPLER(sampler_DistortionTex);
            #endif
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };
                        
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                #ifdef _USE_DISTORTION
                float2 distortionUV : TEXCOORD3;
                #endif
                #ifdef _USE_SCENE_COLOR
                float4 screenPos : TEXCOORD4;
                #endif
            };
                        
            CBUFFER_START(UnityPerMaterial)
                float _BlurSize;
                float _PixelSize;
                float _Sigma;
                float _SampleCount;
                float _InvertDirection;
                float _TexExposure;
                float _Contrast;
                #ifdef _USE_DISTORTION
                float4 _DistortionTex_ST;
                float  _DistortionStrength;
                float  _DistortionSpeed;
                #endif
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);
                
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.uv = input.uv;
                
                #ifdef _USE_DISTORTION
                output.distortionUV = TRANSFORM_TEX(input.uv, _DistortionTex);
                #endif
                
                #ifdef _USE_SCENE_COLOR
                output.screenPos = ComputeScreenPos(output.positionCS);
                #endif
                
                return output;
            }
                        
            float GaussianWeight(float r2, float sigma)
            {
                return exp(-r2 / (2.0 * sigma * sigma));
            }

            #if defined(_USE_SCENE_COLOR) && !defined(_SINGLE_FRAME)
            // 场景颜色实时路径：Kawase 风格采样（14次采样）
            half4 SampleSceneBlurOptimized(float2 centerUV, float2 texelOffset)
            {
                float2 off1 = texelOffset;
                float2 off2 = texelOffset * 2.0;
                float2 off3 = texelOffset * 3.0;
                
                half4 color = 0;
                color += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, centerUV) * 4.0;
                color += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, centerUV + float2(off1.x, 0)) * 2.0;
                color += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, centerUV - float2(off1.x, 0)) * 2.0;
                color += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, centerUV + float2(0, off1.y)) * 2.0;
                color += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, centerUV - float2(0, off1.y)) * 2.0;
                color += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, centerUV + float2(off2.x, off2.y)) * 1.0;
                color += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, centerUV - float2(off2.x, off2.y)) * 1.0;
                color += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, centerUV + float2(off2.x, -off2.y)) * 1.0;
                color += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, centerUV - float2(off2.x, -off2.y)) * 1.0;
                color += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, centerUV + float2(off3.x, 0)) * 0.5;
                color += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, centerUV - float2(off3.x, 0)) * 0.5;
                color += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, centerUV + float2(0, off3.y)) * 0.5;
                color += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, centerUV - float2(0, off3.y)) * 0.5;
                return color / 18.0;
            }
            #endif
            
            // 纹理优化路径：Kawase 风格采样（用于 _BlurSourceTex）
            half4 SampleTexBlurOptimized(float2 centerUV, float2 texelOffset)
            {
                float2 off1 = texelOffset;
                float2 off2 = texelOffset * 2.0;
                float2 off3 = texelOffset * 3.0;
                
                half4 color = 0;
                color += SAMPLE_TEXTURE2D(_BlurSourceTex, sampler_BlurSourceTex, centerUV) * 4.0;
                color += SAMPLE_TEXTURE2D(_BlurSourceTex, sampler_BlurSourceTex, centerUV + float2(off1.x, 0)) * 2.0;
                color += SAMPLE_TEXTURE2D(_BlurSourceTex, sampler_BlurSourceTex, centerUV - float2(off1.x, 0)) * 2.0;
                color += SAMPLE_TEXTURE2D(_BlurSourceTex, sampler_BlurSourceTex, centerUV + float2(0, off1.y)) * 2.0;
                color += SAMPLE_TEXTURE2D(_BlurSourceTex, sampler_BlurSourceTex, centerUV - float2(0, off1.y)) * 2.0;
                color += SAMPLE_TEXTURE2D(_BlurSourceTex, sampler_BlurSourceTex, centerUV + float2(off2.x, off2.y)) * 1.0;
                color += SAMPLE_TEXTURE2D(_BlurSourceTex, sampler_BlurSourceTex, centerUV - float2(off2.x, off2.y)) * 1.0;
                color += SAMPLE_TEXTURE2D(_BlurSourceTex, sampler_BlurSourceTex, centerUV + float2(off2.x, -off2.y)) * 1.0;
                color += SAMPLE_TEXTURE2D(_BlurSourceTex, sampler_BlurSourceTex, centerUV - float2(off2.x, -off2.y)) * 1.0;
                color += SAMPLE_TEXTURE2D(_BlurSourceTex, sampler_BlurSourceTex, centerUV + float2(off3.x, 0)) * 0.5;
                color += SAMPLE_TEXTURE2D(_BlurSourceTex, sampler_BlurSourceTex, centerUV - float2(off3.x, 0)) * 0.5;
                color += SAMPLE_TEXTURE2D(_BlurSourceTex, sampler_BlurSourceTex, centerUV + float2(0, off3.y)) * 0.5;
                color += SAMPLE_TEXTURE2D(_BlurSourceTex, sampler_BlurSourceTex, centerUV - float2(0, off3.y)) * 0.5;
                return color / 18.0;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                
                // 计算扰动偏移
                float2 distortionOffset = 0;
                #ifdef _USE_DISTORTION
                {
                    float2 distUV = input.distortionUV + _Time.y * _DistortionSpeed * 0.1;
                    float2 distortion = SAMPLE_TEXTURE2D(_DistortionTex, sampler_DistortionTex, distUV).rg * 2.0 - 1.0;
                    distortionOffset = distortion * _DistortionStrength;
                }
                #endif
                
                half4 finalColor;
                
                #ifdef _USE_SCENE_COLOR
                {
                    float2 baseUV = input.screenPos.xy / input.screenPos.w + distortionOffset;
                    
                    if (_InvertDirection > 0.5)
                        baseUV.y = 1.0 - baseUV.y;
                    
                    #ifdef _SINGLE_FRAME
                        // 单帧模式：纹理已预模糊，直接采样即可（1次采样）
                        finalColor = SAMPLE_TEXTURE2D(_CapturedSceneTex, sampler_CapturedSceneTex, baseUV);
                    #else
                        // 实时模式：每帧采样 _CameraOpaqueTexture
                        float2 texelOffset = _CameraOpaqueTexture_TexelSize.xy * _BlurSize * _PixelSize;
                        
                        #ifdef _PERFORMANCE_MODE
                            finalColor = SampleSceneBlurOptimized(baseUV, texelOffset);
                        #else
                            int sampleCount = (int)_SampleCount;
                            half4 accumulatedColor = 0;
                            float totalWeight = 0.0;
                            for (int y = -sampleCount; y <= sampleCount; y++)
                            {
                                for (int x = -sampleCount; x <= sampleCount; x++)
                                {
                                    float2 sampleUV = clamp(baseUV + float2(x, y) * texelOffset, 0.0, 1.0);
                                    half4 color = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, sampleUV);
                                    float r2 = float(x * x + y * y);
                                    float weight = GaussianWeight(r2, _Sigma);
                                    accumulatedColor += color * weight;
                                    totalWeight += weight;
                                }
                            }
                            finalColor = accumulatedColor / totalWeight;
                        #endif
                    #endif
                }
                #else
                {
                    float2 baseUV = uv + distortionOffset;
                    
                    if (_InvertDirection > 0.5)
                        baseUV.y = 1.0 - baseUV.y;
                    
                    float4 texelSize = float4(
                        1.0 / _BlurSourceTex_TexelSize.z,
                        1.0 / _BlurSourceTex_TexelSize.w,
                        _BlurSourceTex_TexelSize.z,
                        _BlurSourceTex_TexelSize.w
                    );
                    float2 texelOffset = texelSize.xy * _BlurSize * _PixelSize;
                    
                    #ifdef _PERFORMANCE_MODE
                        // 性能模式：Kawase 14次采样
                        finalColor = SampleTexBlurOptimized(baseUV, texelOffset);
                    #else
                        // 质量模式：完整高斯
                        int sampleCount = (int)_SampleCount;
                        half4 accumulatedColor = 0;
                        float totalWeight = 0.0;
                        for (int y = -sampleCount; y <= sampleCount; y++)
                        {
                            for (int x = -sampleCount; x <= sampleCount; x++)
                            {
                                float2 sampleUV = clamp(baseUV + float2(x, y) * texelOffset, 0.0, 1.0);
                                half4 color = SAMPLE_TEXTURE2D(_BlurSourceTex, sampler_BlurSourceTex, sampleUV);
                                float r2 = float(x * x + y * y);
                                float weight = GaussianWeight(r2, _Sigma);
                                accumulatedColor += color * weight;
                                totalWeight += weight;
                            }
                        }
                        finalColor = accumulatedColor / totalWeight;
                    #endif
                }
                #endif

                finalColor.rgb *= _TexExposure;
                finalColor.rgb = lerp(half3(0.5, 0.5, 0.5), finalColor.rgb, _Contrast);
                
                return half4(finalColor.rgb, 1.0);
            }
            
            ENDHLSL
        }
    }
    CustomEditor "TexGaussianBlurGUI"
}
