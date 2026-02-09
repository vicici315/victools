Shader "Custom/Glass_MobileNew"
{
    Properties
    {
        [Header((Glass Color))]
        [MainColor]_GlassColor ("Glass Color", Color) = (1,1,1,0.5)
        _Diffuse ("Diffuse", Range(0, 1)) = 0.8
        _Fresnel ("Fresnel", Range(0, 8)) = 0.6
        _Transparency ("Transparency", Range(0, 1)) = 0.8
        _LayerBlendFactor ("Layer Blend Factor", Range(0, 2)) = 1.0
        
        [Header((Refraction))]
        _RefractionStrength ("Refraction Strength", Range(-0.1, 0.1)) = 0.02
        [Toggle(_DISABLE_REFRACTION)] _DisableRefraction ("Disable Refraction", Float) = 0
        
        [Header((Normal Map))]
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale      ("Normal Strength", Range(0, 2)) = 0.6
        
        [Header((Specular))]
        _Roughness     ("Roughness", Range(0, 1)) = 0.3
        _Specular       ("Specular", Range(0, 16)) = 3
        
        [Header((Performance))]
        [Enum(Off,0,Front,1,Back,2)] _Cull ("Cull Mode", Float) = 2   // 默认背面裁剪 Back
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
            
            // 折射控制
            #pragma shader_feature _DISABLE_REFRACTION
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            
            half fastPow(half x, half n) {
                return exp2(n * log2(x)); // 在某些GPU上更快
            }
            
            // ● 简化的高光计算 - 针对玻璃材质优化
            half3 SimpleSpecular(half3 normalWS, half3 lightDir, half3 viewDir, half roughness, half3 lightColor)
            {
                half3 halfDir = normalize(lightDir + viewDir);
                half NdotH = saturate(dot(normalWS, halfDir));
                roughness = 1 - roughness;
                half shininess = 2.0 + roughness * roughness * roughness * 512.0;

                half specular = fastPow(max(NdotH, 0.001), shininess) * roughness;
                return lightColor * specular * 2.0;
            }
            
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

            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _GlassColor;
                half _Fresnel;
                half _Transparency;
                half _LayerBlendFactor;
                half _RefractionStrength;
                half _BumpScale;
                half _Roughness;
                half _Specular;
                float4 _BumpMap_ST;
                half _Diffuse;
            CBUFFER_END

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
            
            // 计算多层透明混合
            half3 CalculateMultiLayerTransparency(half3 baseColor, half3 sceneColor, half transparency, half layerBlendFactor, half fresnel)
            {
                half baseAlpha = transparency * _GlassColor.a;
                half fresnelAlpha = lerp(baseAlpha, 1.0, fresnel);
                half blendFactor = saturate(layerBlendFactor * (fresnel*1.82));
                
                half3 blendedColor = sceneColor * baseColor * fresnelAlpha;
                blendedColor = lerp(blendedColor, (blendFactor*baseColor), fresnel);
                
                return blendedColor;
            }
            
            half4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                float3 viewDirWS = normalize(IN.viewDirWS);
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                
                // 法线贴图采样
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv));
                normalTS.xy *= _BumpScale;
                normalTS.z = sqrt(1 - saturate(dot(normalTS.xy, normalTS.xy)));
                
                float3x3 TBN = float3x3(
                    normalize(IN.tangentWS),
                    normalize(IN.bitangentWS),
                    normalWS
                );
                normalWS = normalize(mul(normalTS, TBN));
                
                // 折射效果
                half3 sceneColor;
                #ifdef _DISABLE_REFRACTION
                    sceneColor = SampleSceneColor(screenUV).rgb;
                #else
                    half2 refractionOffset = normalWS.xy * _RefractionStrength;
                    sceneColor = SampleSceneColor(screenUV + refractionOffset).rgb;
                #endif
                
                // 光照计算（简化版，无阴影投射）
                Light mainLight = GetMainLight();
                half3 lightColor = mainLight.color * mainLight.distanceAttenuation;
                
                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half3 diffuse = lightColor * NdotL;
                half3 specular = SimpleSpecular(normalWS, mainLight.direction, viewDirWS, _Roughness, lightColor) * (_GlassColor.rgb+0.2);
                
                // 菲涅尔效应
                half fresnel = 1.0 - saturate(dot(viewDirWS, normalWS));
                fresnel = fastPow(fresnel, _Fresnel);
                
                // 计算基础玻璃颜色
                half3 glassBaseColor = _GlassColor.rgb * saturate(diffuse+_Diffuse);
                
                // 应用多层透明混合
                half3 finalColor = CalculateMultiLayerTransparency(
                    glassBaseColor, 
                    sceneColor, 
                    _Transparency, 
                    _LayerBlendFactor, 
                    fresnel
                );
                
                // 增强场景颜色可见性
                finalColor = finalColor + specular * _Specular * sceneColor;
                
                // 应用雾效
                finalColor = MixFog(finalColor, IN.fogFactor);
                
                // 计算最终透明度
                half finalAlpha = _GlassColor.a * _Transparency;
                finalAlpha = lerp(finalAlpha, 1.0, fresnel * 0.2); // 边缘稍微增加不透明度
                
                return half4(finalColor, finalAlpha);
            }
            ENDHLSL
        }
        
        // ● 阴影投射Pass（优化简化版）

        
    }
    
    // 禁用阴影投射
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}