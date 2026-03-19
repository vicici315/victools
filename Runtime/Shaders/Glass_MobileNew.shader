// Glass_MobileNew.v2.0 完善折射效果，玻璃固有色受Fresnel控制，优化高光效果
Shader "Custom/Glass_MobileNew"
{
    Properties
    {
        [Header(Glass Properties)]
        [Space(5)]
        [MainColor]_BaseColor ("Base Color", Color) = (1,1,1,0.5)
        _Transparency ("Global Transparency", Range(0, 1)) = 0.98
        
        [Header(Specular)]
        [Space(5)]
        _Smoothness ("Smoothness", Range(0.01, 1)) = 0.88
        _SpecularStrength ("Specular Strength", Range(0, 1)) = 0.8
        _SceneBlurStrength ("Scene Blur Strength", Range(0, 1)) = 1
        
        [Header(Distortion)]
        [Space(5)]
        [Toggle(_USE_NORMAL_MAP)] _UseNormalMap ("Use Normal Map", Float) = 0
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0, 2)) = 0.6
        
        [Header(Refraction)]
        [Space(5)]
        [Toggle(_USE_REFRACTION)] _UseRefraction ("Use Refraction", Float) = 1
        _RefractionStrength ("Refraction Strength", Range(-1.81, 1.81)) = -0.3
        
        [Header(Reflection)]
        [Space(5)]
        [Toggle(_USE_REFLECTION)] _UseReflection("Use Reflection Map", Float) = 0
        [NoScaleOffset]_SphericalReflectionMap ("Spherical Reflection Map", 2D) = "white" {}
        _ReflectionScale ("Reflection Scale", Range(0.0, 2.0)) = 1.0
        _ReflectionBlur ("Max Reflection Blur", Range(0, 6)) = 6.0
        
        [Header(Fresnel)]
        [Space(5)]
        _FresnelPower ("Fresnel Power", Range(0.1, 10)) = 1.86
        _FresnelBias ("Fresnel Bias", Range(0, 1)) = 0.072
        _FresnelScale ("Fresnel Scale", Range(0, 2)) = 1.2
        
        [Header(Render Settings)]
        [Space(5)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent+100"  // 使用更高的队列值确保正确排序
            "IgnoreProjector"="True"   // 忽略投影器，避免阴影投射
            "DisableBatching"="False"
        }

        // 主渲染Pass（只保留基本功能）
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            Blend One OneMinusSrcAlpha  // 标准透明混合模式
            ZWrite Off  // 透明对象不写入深度
            Cull [_Cull]
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // URP关键多编译指令（简化版）
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            
            
            // 基础功能开关（按性能影响排序）
            #pragma shader_feature_local _USE_NORMAL_MAP
            #pragma shader_feature_local _USE_REFRACTION
            #pragma shader_feature_local _USE_REFLECTION
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            
            // 函数前向声明
            inline half3 SampleSceneColorBlurred(float2 uv, float blurAmount);
            
            // 手动模糊采样函数 - 使用多次采样模拟模糊效果
            inline half3 SampleSceneColorBlurred(float2 uv, float blurAmount)
            {
                half3 result;
                
                // 如果模糊量很小，直接返回原始采样
                if (blurAmount < 0.01)
                {
                    result = SampleSceneColor(uv).rgb;
                }
                else
                {
                    // 计算采样偏移（基于屏幕空间像素大小）
                    float2 texelSize = _ScreenParams.zw - 1.0; // 1/width, 1/height
                    float2 offset = texelSize * blurAmount;
                    
                    // 使用9点采样进行模糊（优化的高斯模糊）
                    half3 color = half3(0, 0, 0);
                    
                    // 中心权重
                    color += SampleSceneColor(uv).rgb * 0.25;
                    
                    // 4个主方向
                    color += SampleSceneColor(uv + float2(offset.x, 0)).rgb * 0.125;
                    color += SampleSceneColor(uv + float2(-offset.x, 0)).rgb * 0.125;
                    color += SampleSceneColor(uv + float2(0, offset.y)).rgb * 0.125;
                    color += SampleSceneColor(uv + float2(0, -offset.y)).rgb * 0.125;
                    
                    // 4个对角线方向
                    color += SampleSceneColor(uv + float2(offset.x, offset.y)).rgb * 0.0625;
                    color += SampleSceneColor(uv + float2(-offset.x, offset.y)).rgb * 0.0625;
                    color += SampleSceneColor(uv + float2(offset.x, -offset.y)).rgb * 0.0625;
                    color += SampleSceneColor(uv + float2(-offset.x, -offset.y)).rgb * 0.0625;
                    
                    result = color;
                }
                
                return result;
            }
            
            // 纹理声明应该始终存在，不要放在条件编译中
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_SphericalReflectionMap);
            SAMPLER(sampler_SphericalReflectionMap);
            
            half fastPow(half x, half n) {
                return exp2(n * log2(x)); // 在某些GPU上更快
            }
            
            // 快速球形UV映射
            #ifdef _USE_REFLECTION
            float2 fastSphericalUV(float3 reflectionVector)
            {
                reflectionVector = normalize(reflectionVector);
                return float2(
                    reflectionVector.x / 4.01 + 0.5,
                    reflectionVector.y / 4.01 + 0.5
                );
            }
            
            // 采样球形反射贴图
            float3 SampleSphericalReflection(float3 reflectionVector, float blur)
            {
                float2 uv = fastSphericalUV(reflectionVector);
                float3 reflectionColor = SAMPLE_TEXTURE2D_LOD(_SphericalReflectionMap, sampler_SphericalReflectionMap, uv, blur).rgb;
                return reflectionColor;
            }
            #endif
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float3 tangentWS : TEXCOORD4;
                float3 bitangentWS : TEXCOORD5;
                float fogFactor : TEXCOORD6;
                float3 positionWS : TEXCOORD7;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Transparency;
                half _Smoothness;
                half _SpecularStrength;
                half _SceneBlurStrength;
                half _BumpScale;
                half _RefractionStrength;
                half _ReflectionScale;
                half _ReflectionBlur;
                half _FresnelPower;
                half _FresnelBias;
                half _FresnelScale;
                half _ShadowStrength;
                float4 _BumpMap_ST;
            CBUFFER_END

            // ● FastSpecular函数 - 来自Glass_carWindow.shader的优化高光计算
            half3 FastSpecular(half3 normalWS, half3 lightDir, half3 viewDirWS, half3 lightColor, half shadowAttenuation, float fresnel)
            {
                // 计算光线的反射向量
                half3 reflectDir = reflect(-lightDir, normalWS);
                
                // 计算反射向量与视线方向的点积
                half RdotV = saturate(dot(reflectDir, viewDirWS));
                
                // 计算光泽度指数（smoothness³ * 512 + 2）
                half smoothnessCubed = _Smoothness * _Smoothness * _Smoothness;
                half gloss = smoothnessCubed * 512.0 + 2.0;
                
                // 使用fastPow计算高光
                half specular = fastPow(max(RdotV, 0.001), gloss);
                
                // 能量守恒归一化（调整后的公式，减少低smoothness时的衰减）
                // 使用 (gloss + 8) / 16 作为归一化因子，提供更平缓的过渡
                half normalization = (gloss + 8.0) / 16.0;
                specular *= normalization;
                
                // 高光受菲涅尔影响（边缘更亮）
                half fresnelBoost = lerp(0.5, 0.8, fresnel);
                
                // 玻璃材质的高光
                return lightColor * specular * shadowAttenuation * fresnelBoost;
            }

            // 计算菲涅尔效果（与Glass_carWindow.shader保持一致）
            float CalculateFresnel(float3 normalWS, float3 viewDirWS)
            {
                float fresnel = saturate(dot(normalWS, viewDirWS));
                fresnel = _FresnelBias + _FresnelScale * pow(1.0 - fresnel, _FresnelPower);
                return saturate(fresnel);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                
                OUT.positionCS = vertexInput.positionCS;
                OUT.positionWS = vertexInput.positionWS;
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BumpMap);
                
                OUT.normalWS = normalInput.normalWS;
                OUT.tangentWS = normalInput.tangentWS;
                OUT.bitangentWS = normalInput.bitangentWS;
                OUT.viewDirWS = GetCameraPositionWS() - vertexInput.positionWS;
                OUT.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                
                return OUT;
            }

            
            half4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                float3 viewDirWS = normalize(IN.viewDirWS);
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                
                // 法线贴图采样（可选）
                half3 normalTS = half3(0, 0, 1); // 默认法线
                #ifdef _USE_NORMAL_MAP
                    normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv));
                    normalTS.xy *= _BumpScale;
                    normalTS.z = sqrt(1 - saturate(dot(normalTS.xy, normalTS.xy)));
                    
                    float3x3 TBN = float3x3(
                        normalize(IN.tangentWS),
                        normalize(IN.bitangentWS),
                        normalWS
                    );
                    normalWS = normalize(mul(normalTS, TBN));
                #endif
                
                // 菲涅尔效应（使用统一的计算函数）
                half fresnel = CalculateFresnel(normalWS, viewDirWS);
                // 折射效果 - 以物体中心为轴心进行扭曲缩放
                float2 finalScreenUV = screenUV;
                
                #ifdef _USE_REFRACTION
                    // 计算物体中心（对象空间原点）在屏幕空间的位置
                    float3 objectCenterWS = TransformObjectToWorld(float3(0, 0, 0));
                    float4 objectCenterCS = TransformWorldToHClip(objectCenterWS);
                    float2 objectCenterScreenUV = objectCenterCS.xy / objectCenterCS.w * 0.5 + 0.5; // 正确转换到屏幕UV坐标
                    
                    // 计算从中心到当前像素的方向向量
                    float2 directionFromCenter = screenUV - objectCenterScreenUV;
                    
                    // 使用法线强度调制扭曲效果（法线越偏离，扭曲越强）
                    half normalDistortion = 0;
                    #ifdef _USE_NORMAL_MAP
                        normalDistortion = length(normalTS.xy) * _BumpScale;
                    #endif
                    
                    // 以中心为轴心进行径向扭曲缩放
                    // _RefractionStrength > 0: 向外扩张（放大效果）
                    // _RefractionStrength < 0: 向内收缩（缩小效果）
                    float refractionScale = 1.0 + _RefractionStrength * (fresnel + 0.3) * (1.0 + normalDistortion);
                    float2 scaledDirection = directionFromCenter * refractionScale;
                    finalScreenUV = objectCenterScreenUV + scaledDirection;
                    
                    // 确保UV坐标在有效范围内
                    finalScreenUV = saturate(finalScreenUV);
                #endif
                
                // 根据光滑度计算模糊级别（光滑度越低，模糊越强）
                // _Smoothness: 0 = 粗糙（最大模糊），1 = 光滑（无模糊）
                // _SceneBlurStrength: 控制整体模糊强度
                float blurAmount = (1.0 - _Smoothness) * _SceneBlurStrength * 6.0; // 模糊强度范围 0-3
                half3 sceneColor = SampleSceneColorBlurred(finalScreenUV, blurAmount);
                
                // 光照计算（优化版）
                Light mainLight = GetMainLight();
                
                // 优化：预计算光照强度，避免重复计算
                half3 lightColor = mainLight.color * mainLight.distanceAttenuation;
                half shadowAttenuation = mainLight.shadowAttenuation;
                
                half3 specular = FastSpecular(normalWS, mainLight.direction, viewDirWS, lightColor, shadowAttenuation, fresnel) * (fresnel+0.12) * _SpecularStrength;
                
                // 优化：直接计算最终玻璃基础颜色
                half3 glassBaseColor = lerp(_BaseColor.rgb * sceneColor, sceneColor, fresnel*0.8);
                
                // 反射计算（优化版）
                half3 reflectionColor = half3(0, 0, 0);
                #ifdef _USE_REFLECTION
                    // 优化：预计算反射向量和模糊度
                    float3 reflectionVector = reflect(-viewDirWS, normalWS);
                    float reflectionBlur = lerp(_ReflectionBlur, 0, _Smoothness);
                    
                    // 优化：合并反射颜色计算和强度应用
                    reflectionColor = SampleSphericalReflection(reflectionVector, reflectionBlur);
                    
                    // 优化：直接应用菲涅尔混合，避免中间变量
                    glassBaseColor = lerp(glassBaseColor, glassBaseColor+ reflectionColor * (_ReflectionScale), (fresnel*_ReflectionScale));
                #endif
                
                // 优化：预计算高光亮度，避免重复计算
                half specularLuminance = dot(specular, half3(0.299, 0.587, 0.114));
                half3 enhancedSpecular = specular * _SpecularStrength;
                

                half3 finalColor = glassBaseColor;
                // 优化：合并场景颜色和高光增强，减少一次加法运算
                finalColor += enhancedSpecular;
                
                // 应用雾效
                finalColor = MixFog(finalColor, IN.fogFactor);
                
                // 优化：预计算基础透明度，避免在最终计算中重复lerp
                half baseAlpha = lerp(_Transparency, 1, fresnel);
                half finalAlpha = saturate(baseAlpha + specularLuminance) * _Transparency;
                
                return half4(finalColor, finalAlpha);
            }
            ENDHLSL
        }
        
        // ● 阴影投射Pass（优化简化版）
        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}
            
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            float3 _LightDirection;
            float4 _ShadowBias; 
            
            struct Attributes 
            { 
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct Varyings 
            { 
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                positionWS = positionWS + _LightDirection * _ShadowBias.x;
                
                output.positionCS = TransformWorldToHClip(positionWS);
                
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target 
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }
        
    }
    
    // 禁用阴影投射
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}