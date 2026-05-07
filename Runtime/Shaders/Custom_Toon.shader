// CustomToon 1.0 卡通材质第一版，单阶明暗，带亮部边缘光；优化高光算法；添加暗部边缘光控制；优化暗部边缘光效果
// CustomToon 1.1 添加法线贴图支持
// CustomToon 1.2 优化_DarkRimThreshold参数背部边缘光控制
// CustomToon 1.3 添加GUI控制，添加自发光参数，添加明暗段数控制，高光算法优化
// CustomToon 1.4 添加【明暗对比】参数；优化自身阴影过暗受环境光影响；添加【自身阴影】选项

Shader "Custom/Toon"
{
    Properties
    {
        [Toggle(_RECEIVE_SHADOWS)] _ReceiveShadows("自身阴影", Float) = 0
        _Color("Color", Color) = (0.87,0.87,0.87,1)
        _MainTex("Main Texture", 2D) = "white" {}
        [Normal] _NormalMap("Normal Map", 2D) = "bump" {}
        _NormalScale("Normal Scale", Range(0, 2)) = 1.0
        _AmbientColor("Ambient Color", Color) = (0.4,0.4,0.4,1)
        [HDR]
        _SpecularColor("Specular Color", Color) = (0.9,0.9,0.9,1)
        _Glossiness("Smoothness", Range(0, 52)) = 24
        // [Header(Terminator Gradient)]
        _ToonSteps("Toon Steps (明暗段数)", Range(1, 10)) = 1
        _ToonSmooth("Toon Smooth (段间柔化)", Range(0, 0.5)) = 0.0
        _ToonOffset("Toon Offset (明暗偏移)", Range(-1, 1)) = -0.4
        _ToonCompression("Toon Compression (明暗压缩)", Range(0.1, 5)) = 3.7
        _ToonContrast("Toon Contrast (明暗对比)", Range(0.001, 3)) = 1.25
        [HDR]
        _RimColor("Rim Color", Color) = (1,1,1,1)
        _RimAmount("Rim Amount", Range(0, 1)) = 0.7
        _RimThreshold("Rim Threshold", Range(0, 1)) = 0.3
        [HDR]
        _DarkRimColor("Dark Rim Color", Color) = (0.25,0.35,0.74,0.5)
        _DarkRimAmount("Dark Rim Amount", Range(0, 1)) = 0.6
        _DarkRimThreshold("Dark Rim Threshold", Range(0.1, 1)) = 0.2
        [Header(Emission)]
        [Toggle(_USEEMISSIONMAP)] _UseEmissionMap("Use Emission Map", Float) = 0
        [HDR] _EmissionColor("Emission Color", Color) = (1,1,1,1)
        _EmissionMap("Emission Map", 2D) = "white" {}
        _EmissionScale("Emission Scale", Range(0, 3)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_local _ _USEEMISSIONMAP
            #pragma multi_compile_local _ _RECEIVE_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_ST;
                float4 _NormalMap_ST;
                float  _NormalScale;
                float4 _AmbientColor;
                float4 _SpecularColor;
                float _Glossiness;
                float4 _RimColor;
                float _RimAmount;
                float _RimThreshold;
                float4 _DarkRimColor;
                float _DarkRimAmount;
                float _DarkRimThreshold;
                float _ToonSteps;
                float _ToonSmooth;
                float _ToonOffset;
                float _ToonCompression;
                float _ToonContrast;
                float4 _EmissionColor;
                float _EmissionScale;
            CBUFFER_END

            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap);  SAMPLER(sampler_NormalMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float3 tangentWS   : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
                float3 viewDirWS   : TEXCOORD5;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs  = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   normInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS  = posInputs.positionCS;
                OUT.positionWS   = posInputs.positionWS;
                OUT.normalWS     = normInputs.normalWS;
                OUT.tangentWS    = normInputs.tangentWS;
                OUT.bitangentWS  = normInputs.bitangentWS;
                OUT.viewDirWS    = GetWorldSpaceViewDir(posInputs.positionWS);
                OUT.uv           = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }
			half fastPow(half x, half n) {
                return exp2(n * log2(x)); 
            }
            half4 frag(Varyings IN) : SV_Target
            {
                // 法线贴图解码，构建 TBN 矩阵转换到世界空间
                half4 normalSample = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, IN.uv);
                half3 normalTS = UnpackNormalScale(normalSample, _NormalScale);
                float3x3 TBN = float3x3(normalize(IN.tangentWS), normalize(IN.bitangentWS), normalize(IN.normalWS));
                float3 normal = normalize(mul(normalTS, TBN));

                float3 viewDir = normalize(IN.viewDirWS);

                // 获取主光源
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 lightDir = normalize(mainLight.direction);

                // 卡通光照：阶梯化 NdotL
                // _ToonOffset 偏移明暗分界线位置，_ToonCompression 压缩/扩展明暗范围
                float NdotL = dot(lightDir, normal);
                #ifdef _RECEIVE_SHADOWS
                    float shadow = mainLight.shadowAttenuation;
                #else
                    float shadow = 1.0;
                #endif
                // 将 NdotL 从 [-1,1] 映射到 [0,1]，应用偏移和压缩
                float halfLambert = saturate((NdotL * 0.5 + 0.5 + _ToonOffset) * _ToonCompression);
                // 量化为离散阶梯
                float stepped = floor(halfLambert * _ToonSteps) / _ToonSteps;
                float nextStep = min(stepped + 1.0 / _ToonSteps, 1.0);
                // 段间柔化：在阶梯边缘做 smoothstep 过渡
                float edge = frac(halfLambert * _ToonSteps);
                float toonLight = lerp(stepped, nextStep, smoothstep(0.5 - _ToonSmooth, 0.5 + _ToonSmooth, edge));
                // 明暗对比：用 pow 曲线压暗中低段，值越大暗部越暗，亮部基本不变
                toonLight = fastPow(toonLight, _ToonContrast);
                float softShadow = shadow;
                float lightIntensity = toonLight * softShadow;
                float4 light = lightIntensity * float4(mainLight.color, 1);

                // 高光：卡通风格 smoothstep 硬切
                float3 reflectDir = reflect(-lightDir, normal);
                float RdotV = saturate(dot(reflectDir, viewDir));
                // _Glossiness（光滑度）：值越高高光越小越锐利，值越低越大越模糊
                // specSize：高光区域大小（光滑度越高区域越小）
                float specSize = exp2(lerp(3.0, 8.0, saturate(_Glossiness / 52.0)));
                float specPow = fastPow(RdotV, specSize);
                // 过渡宽度：光滑度越高越锐利
                float specEdge = lerp(0.3, 0.02, saturate(_Glossiness / 52.0));
                float specularIntensitySmooth = smoothstep(0.5 - specEdge, 0.5 + specEdge, specPow) * toonLight * softShadow;
                // 能量守恒：高光面积越大强度越低，面积越小强度越高
                float energyNorm = (specSize + 2.0) / (2.0 * 3.14159);
                specularIntensitySmooth *= saturate(energyNorm / 8.0);
                float4 specular = specularIntensitySmooth * _SpecularColor;

                // 亮部边缘光 (Rim)
                float rimDot = 1 - dot(viewDir, normal);
                float rimIntensity = rimDot * fastPow(NdotL, _RimThreshold);
                rimIntensity = smoothstep(_RimAmount - 0.01, _RimAmount + 0.01, rimIntensity);
                float4 rim = rimIntensity * _RimColor * softShadow;

                // 暗部边缘光：借鉴亮部边缘光算法，边缘光结尾两头变细，但作用于暗部区域
                // _DarkRimThreshold 控制边缘光的强度阈值（类似_RimThreshold）
                float darkNdotL = saturate(-NdotL); // 取反，只在背光面生效
                float darkRimIntensity = rimDot * fastPow(darkNdotL, _DarkRimThreshold);
                darkRimIntensity = smoothstep(_DarkRimAmount - 0.01, _DarkRimAmount + 0.01, darkRimIntensity);
                darkRimIntensity *= (1.0 - toonLight); // 确保只在暗部显示

                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                float4 baseColor = (light + _AmbientColor * (1.0 - lightIntensity) + specular + rim) * _Color * texColor;
                // 自发光
                #ifdef _USEEMISSIONMAP
                    half3 emissionMap = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv).rgb;
                    half3 emission = emissionMap * _EmissionColor.rgb * _EmissionScale;
                    baseColor.rgb += emission;
                #endif
                // 暗部边缘光覆盖贴图，A 通道控制透明度
                float darkRimAlpha = darkRimIntensity * _DarkRimColor.a;
                return lerp(baseColor, float4(_DarkRimColor.rgb, 1), darkRimAlpha);
            }
            ENDHLSL
        }

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
                
                // #if UNITY_REVERSED_Z
                //     output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                // #else
                //     output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                // #endif
                
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

    CustomEditor "CustomToonGUI"
}
