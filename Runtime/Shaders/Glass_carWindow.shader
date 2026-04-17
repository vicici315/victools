// URP车窗玻璃Shader - 支持透明、菲涅尔反射、球形环境贴图
// 1.2 添加"Reflection Scale"参数，控制反射图的明显度
// 1.3 优化反射，添加反射贴图位移，添加GUI控制，Glass_carWindow添加Ramp渐变贴图，可用于模拟肥皂泡效果
Shader "Custom/Glass_carWindow"
{
    Properties
    {
        [Header(Glass Properties)]
        // [Space(5)]
        [MainColor]_BaseColor ("Base Color", Color) = (0.8, 0.9, 1.0, 0.0)
        _Transparency ("Global Transparency", Range(0, 1)) = 1
        
        [Header(Specular)]
        // [Space(5)]
        _Smoothness ("Smoothness", Range(0.01, 1)) = 0.55
        _SpecularStrength ("Specular Strength", Range(0, 1)) = 0.5
        
        // [Header(Distortion)]
        // [Space(5)]
        [Toggle(_USENORMALMAP)] _UseNormalMap("Use Normal Map", Float) = 0
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0, 1)) = 0.5
        
        // [Header(Reflection)]
        // [Space(5)]
        [Toggle(_USEREFLECTION)] _UseReflection("Use Reflection Map", Float) = 1
        [NoScaleOffset]_SphericalReflectionMap ("Spherical Reflection Map", 2D) = "white" {}
        _ReflectionScale ("Reflection Scale", Range(0.1, 6.0)) = 1.2
        _ReflectionOffset ("Reflection Offset", Range(0, 1)) = 0.5
        _ReflectionBlur ("Max Reflection Blur", Range(0, 6)) = 5.0
        
        [Header(Fresnel)]
        _FresnelPower ("Fresnel Power", Range(0.1, 10)) = 2.5
        _FresnelBias ("Fresnel Bias", Range(0, 1)) = 0.12
        _FresnelScale ("Fresnel Scale", Range(0, 2)) = 1.0
        
        // [Header(Fresnel Ramp)]
        // [Space(5)]
        [Toggle(_USEFRESNELRAMP)] _UseFresnelRamp("Use Fresnel Ramp", Float) = 0
        _FresnelRampTexture ("Fresnel Ramp Texture", 2D) = "white" {}
        _FresnelRampRow ("Fresnel Ramp Row", Range(0.01, 0.99)) = 0.99
        _FresnelRampIntensity ("Fresnel Ramp Intensity", Range(0, 2)) = 0.2
        
        [Header(Render Settings)]
        // [Space(5)]
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
            
            // 使用预乘Alpha混合，让高光以叠加方式显示
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull[_Cull]
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma shader_feature_local _USENORMALMAP
            #pragma shader_feature_local _USEREFLECTION
            #pragma shader_feature_local _USEFRESNELRAMP
            #pragma multi_compile_fog
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 纹理声明应该始终存在，不要放在条件编译中
            TEXTURE2D(_SphericalReflectionMap);
            SAMPLER(sampler_SphericalReflectionMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_FresnelRampTexture);
            SAMPLER(sampler_FresnelRampTexture);

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
                half _ReflectionOffset;
                half _ReflectionScale;
                half _ReflectionBlur;
                float4 _FresnelRampTexture_ST;
                half _FresnelRampRow;
                half _FresnelRampIntensity;
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
                    reflectionVector.x / 4.01 + _ReflectionOffset,
                    reflectionVector.y / 4.01 + _ReflectionOffset
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

            // 快速pow函数 - 使用exp2/log2避免梯度线
            half fastPow(half x, half n) {
                return exp2(n * log2(x));
            }

            // 计算菲涅尔效果
            float CalculateFresnel(float3 normalWS, float3 viewDirWS)
            {
                float fresnel = saturate(dot(normalWS, viewDirWS));
                fresnel = _FresnelBias + _FresnelScale * fastPow(1.0 - fresnel, _FresnelPower);
                return saturate(fresnel);
            }
            #ifdef _USEFRESNELRAMP
            // 采样Fresnel Ramp纹理（使用固定的Fresnel计算）
            // normalWS: 世界空间法线
            // viewDirWS: 世界空间视角方向
            // rampRow: 0-1的行选择值，选择纹理的哪一行
            half3 SampleFresnelRamp(half3 normalWS, half3 viewDirWS, half rampRow)
            {
                // 使用固定的Fresnel计算（不受_FresnelPower等参数影响）
                // 标准Fresnel公式：fresnel = 1 - dot(N, V)
                half fresnelValue = saturate(dot(normalWS, viewDirWS));
                // half fresnelValue = 1.0 - rawFresnel; // 0=中心，1=边缘
                
                // 确保fresnelValue在[0,1]范围内，从左到右完整铺满
                fresnelValue = saturate(fastPow(fresnelValue,1.6));
                
                // 构建UV坐标
                // X轴：固定Fresnel值（从左到右：内到外，0-1完整范围）
                // Y轴：行选择（从下到上）
                half2 rampUV = half2(fresnelValue, rampRow);
                
                // 采样Ramp纹理
                half3 rampColor = SAMPLE_TEXTURE2D(_FresnelRampTexture, sampler_FresnelRampTexture, rampUV).rgb;
                
                return rampColor * _FresnelRampIntensity;
            }
            #endif

            // 玻璃高光 - 使用Phong反射模型（基于反射向量）
            // specular = lightColor * pow(saturate(dot(reflectDir, viewDir)), gloss)
            // 添加能量守恒：smoothness越高，高光越聚焦，能量越集中
            half3 FastSpecular(half3 normalWS, half3 lightDir, half3 viewDirWS, half3 lightColor, half shadowAttenuation, float fresnel)
            {
                // 计算光线的反射向量
                half3 reflectDir = reflect(-lightDir, normalWS);
                
                // 计算反射向量与视线方向的点积
                half RdotV = saturate(dot(reflectDir, viewDirWS));
                
                // 计算光泽度指数（smoothness³ * 512 + 2）
                half smoothnessCubed = _Smoothness * _Smoothness * _Smoothness;
                half gloss = smoothnessCubed * 256.0 + 1.0;
                
                // 使用fastPow计算高光
                half specular = fastPow(max(RdotV, 0.001), gloss);
                
                // 能量守恒归一化（调整后的公式，减少低smoothness时的衰减）
                // 使用 (gloss + 8) / 16 作为归一化因子，提供更平缓的过渡
                half normalization = (gloss + 8.0) / 16.0;
                specular *= normalization;
                
                // 高光受菲涅尔影响（边缘更亮）
                half fresnelBoost = lerp(0.5, 0.8, fresnel);
                
                // 玻璃材质的高光
                return lightColor * specular * shadowAttenuation;
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
            // ● 饱和度调整函数
            half3 AdjustSaturation(half3 color, half saturation) {
                // 计算亮度（使用标准亮度权重）
                half luminance = dot(color, half3(0.299, 0.587, 0.114));
                // 线性插值在原始颜色和灰度之间
                return lerp(half3(luminance, luminance, luminance), color, saturation);
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
                    float3 reflectionColor = SampleSphericalReflection(reflectionVector, lerp(_ReflectionBlur,0,_Smoothness));
                    reflectionColor *= _ReflectionScale;
                    
                    // 混合反射（fresnel后面乘以的值控制反射贴图的突出显示度）
                    // finalColor = lerp(glassColor, reflectionColor*reflectionColor*glassColor, saturate(fresnel*_ReflectionScale));
                    finalColor = reflectionColor*glassColor;
                #endif
                
                // ============================================
                // Fresnel Ramp 渐变叠加
                // ============================================
                #ifdef _USEFRESNELRAMP
                    // 采样Fresnel Ramp纹理（使用固定的Fresnel计算）
                    float3 fresnelRampColor = SampleFresnelRamp(normalWS, viewDirWS, _FresnelRampRow);
                    
                    // 将Ramp颜色叠加到最终颜色上
                    // 使用加法混合，让Ramp颜色增强玻璃效果
                    finalColor += fresnelRampColor;
                #endif
                
                // 获取主光源
                Light mainLight = GetMainLight();
                half3 lightColor = mainLight.color * mainLight.distanceAttenuation;
                finalColor *= lightColor;
                // 获取阴影（玻璃也应该受阴影影响）
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                half shadowAttenuation = mainLight.shadowAttenuation;
                
                // 计算高光（传入fresnel，让边缘高光更亮）
                half3 specular = FastSpecular(normalWS, mainLight.direction, viewDirWS, lightColor, shadowAttenuation, fresnel) *AdjustSaturation(glassColor,0.5)*(fresnel + 0.1)* _SpecularStrength;
                
                // 确保高光可见（调试用）
                // return half4(specular * 10.0, 1.0); // 取消注释此行可以只看高光
                
                finalColor += specular;
                
                // 简单的环境光
                // half3 ambient = half3(0.05, 0.05, 0.05);
                // finalColor += ambient;
                
                // 应用雾效
                finalColor = MixFog(finalColor, input.fogFactor);
                
                // 计算高光亮度（用于透明度）
                half specularLuminance = dot(specular, half3(0.299, 0.587, 0.114));
                
                // 透明度计算：
                // 1. 基础透明度受菲涅尔影响（边缘更不透明）
                half baseAlpha = lerp(_BaseColor.a, 1, fresnel);
                half finalAlpha = saturate(baseAlpha + specularLuminance)*_Transparency;
                
                // 预乘Alpha：将颜色乘以alpha，让高光以叠加方式显示
                // 这样高光在亮背景下不会变暗
                finalColor *= finalAlpha;
                
                return half4(finalColor, finalAlpha);
            }
            ENDHLSL
        }
    }
    
    CustomEditor "Glass_carWindowGUI"
    // FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
