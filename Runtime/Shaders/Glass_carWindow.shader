// URP车窗玻璃Shader - 支持透明、菲涅尔反射、球形环境贴图
Shader "Custom/Glass_carWindow"
{
    Properties
    {
        [Header(Glass Properties)]
        [Space(5)]
        _BaseColor ("Glass Tint Color", Color) = (0.8, 0.9, 1.0, 0.3)
        _Transparency ("Transparency", Range(0, 1)) = 0.1
        
        [Header(Specular)]
        [Space(5)]
        _Smoothness ("Smoothness", Range(0.01, 1)) = 0.85
        _SpecularStrength ("Specular Strength", Range(0, 2)) = 1.0
        
        [Header(Distortion)]
        [Space(5)]
        [Toggle(_USENORMALMAP)] _UseNormalMap("Use Normal Map", Float) = 0
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0, 1)) = 0.5
        
        [Header(Reflection)]
        [Space(5)]
        [Toggle(_USEREFLECTION)] _UseReflection("Use Reflection Map", Float) = 1
        [NoScaleOffset]_SphericalReflectionMap ("Spherical Reflection Map", 2D) = "white" {}
        _ReflectionStrength ("Reflection Strength", Range(0, 12)) = 1.0
        _ReflectionBlur ("Reflection Blur", Range(0, 6)) = 0.0
        
        [Header(Fresnel)]
        _FresnelPower ("Fresnel Power", Range(0.1, 10)) = 2.5
        _FresnelBias ("Fresnel Bias", Range(0, 1)) = 0.12
        _FresnelScale ("Fresnel Scale", Range(0, 2)) = 1.0
        
        [Header(Render Settings)]
        [Space(5)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull[_Cull]
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma shader_feature_local _USENORMALMAP
            #pragma shader_feature_local _USEREFLECTION
            #pragma multi_compile_fog
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 纹理声明应该始终存在，不要放在条件编译中
            TEXTURE2D(_SphericalReflectionMap);
            SAMPLER(sampler_SphericalReflectionMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Transparency;
                half _Smoothness;
                half _SpecularStrength;
                half _FresnelPower;
                half _FresnelBias;
                half _FresnelScale;
                float4 _BumpMap_ST;
                half _BumpScale;
                half _ReflectionStrength;
                half _ReflectionBlur;
            CBUFFER_END

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
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 viewDirWS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                
                #ifdef _USENORMALMAP
                float4 tangentWS : TEXCOORD5;
                #endif
            };

            // 快速球形UV映射（来自PBR_Mobile）
            #ifdef _USEREFLECTION
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

            // 计算菲涅尔效果
            float CalculateFresnel(float3 normalWS, float3 viewDirWS)
            {
                float fresnel = saturate(dot(normalWS, viewDirWS));
                fresnel = _FresnelBias + _FresnelScale * pow(1.0 - fresnel, _FresnelPower);
                return saturate(fresnel);
            }

            // 快速pow函数 - 使用exp2/log2避免梯度线
            half fastPow(half x, half n) {
                return exp2(n * log2(x));
            }

            // 玻璃高光 - 使用Phong反射模型（基于反射向量）
            // specular = lightColor * pow(saturate(dot(reflectDir, viewDir)), gloss)
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
                
                // 高光受菲涅尔影响（边缘更亮）
                half fresnelBoost = lerp(0.7, 1.0, fresnel);
                
                // 玻璃材质的高光
                return lightColor * specular * _SpecularStrength * shadowAttenuation * fresnelBoost;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.uv = input.uv;
                output.normalWS = normalInput.normalWS;
                output.viewDirWS = GetCameraPositionWS() - vertexInput.positionWS;
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                
                #ifdef _USENORMALMAP
                output.tangentWS = float4(normalInput.tangentWS, input.tangentOS.w);
                #endif
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 归一化向量
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);
                
                // 法线贴图扰动
                #ifdef _USENORMALMAP
                    float2 bumpUV = TRANSFORM_TEX(input.uv, _BumpMap);
                    half4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, bumpUV);
                    half3 normalTS = UnpackNormal(normalSample);
                    normalTS.xy *= _BumpScale;
                    normalTS = normalize(normalTS);
                    
                    float3 tangentWS = normalize(input.tangentWS.xyz);
                    float3 bitangentWS = cross(normalWS, tangentWS) * input.tangentWS.w;
                    float3x3 TBN = float3x3(tangentWS, bitangentWS, normalWS);
                    normalWS = normalize(mul(normalTS, TBN));
                #endif
                
                // 计算菲涅尔效果
                float fresnel = CalculateFresnel(normalWS, viewDirWS);
                
                // 混合玻璃颜色和反射
                half3 glassColor = _BaseColor.rgb;
                half3 finalColor = glassColor;
                
                #ifdef _USEREFLECTION
                    // 计算反射向量
                    float3 reflectionVector = reflect(-viewDirWS, normalWS);
                    
                    // 采样球形反射贴图
                    float3 reflectionColor = SampleSphericalReflection(reflectionVector, _ReflectionBlur) * (glassColor * 1.2);
                    reflectionColor *= _ReflectionStrength;
                    
                    // 混合反射
                    finalColor = lerp(glassColor, reflectionColor, fresnel);
                    // finalColor = reflectionColor;
                #endif
                
                // 获取主光源
                Light mainLight = GetMainLight();
                half3 lightColor = mainLight.color * mainLight.distanceAttenuation;
                finalColor *= lightColor;
                // 获取阴影（玻璃也应该受阴影影响）
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                half shadowAttenuation = mainLight.shadowAttenuation;
                
                // 计算高光（传入fresnel，让边缘高光更亮）
                half3 specular = FastSpecular(normalWS, mainLight.direction, viewDirWS, lightColor, shadowAttenuation, fresnel);
                
                // 确保高光可见（调试用）
                // return half4(specular * 10.0, 1.0); // 取消注释此行可以只看高光
                
                finalColor += specular;
                
                // 简单的环境光
                half3 ambient = half3(0.051, 0.051, 0.051);
                finalColor += ambient;
                
                // 应用雾效
                finalColor = MixFog(finalColor, input.fogFactor);
                
                // 计算高光亮度（用于透明度）
                half specularLuminance = dot(specular, half3(0.299, 0.587, 0.114));
                // return specularLuminance;
                // 透明度计算：
                // 1. 基础透明度受菲涅尔影响（边缘更不透明）
                half baseAlpha = lerp(_Transparency, 1.0, fresnel * 0.9);
                
                return half4(finalColor, saturate(baseAlpha + specularLuminance*0.46));
            }
            ENDHLSL
        }
    }
    
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
