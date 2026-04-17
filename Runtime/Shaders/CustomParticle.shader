// CustomParticle 1.0 水质感粒子材质，粒子系统控制透明
// CustomParticle 1.1 添加反射、法线、Fresnel
// CustomParticle 1.2 去除（SoftParticle）参数，默认值设置

Shader "Custom/Fx/CustomParticle"
{
    Properties
    {
        [Header(Main Texture)]
        _MainTex ("Main Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)

        [Header(UV Animation)]
        _ScrollSpeed ("UV Scroll Speed (XY)", Vector) = (0, 0, 0, 0)

        [Header(Wet Decal)]
        // 勾选后：R通道蒙版压暗地面 + 反射高光模拟水面
        [Toggle(_WETDECAL_ON)] _UseWetDecal ("Wet Decal (R Mask)", Float) = 0
        _WetStrength ("Wet Darkness", Range(0, 1)) = 0.5

        [Header(Wet Reflection)]
        [Toggle(_USEREFLECTION)] _UseReflection ("Use Reflection Map", Float) = 0
        [NoScaleOffset] _ReflectionMap ("Spherical Reflection Map", 2D) = "white" {}
        _ReflectionStrength ("Reflection Strength", Range(0, 2)) = 1.8
        _Smoothness ("Smoothness", Range(0, 1)) = 0.9

        [Header(Wet Normal)]
        [Toggle(_USENORMALMAP)] _UseNormalMap ("Use Normal Map", Float) = 0
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0, 2)) = 0.85

        [Header(Fresnel)]
        _FresnelPower ("Fresnel Power", Range(0.1, 10)) = 4.2
        _FresnelBias  ("Fresnel Bias",  Range(0, 1))    = 0.25
        _FresnelScale ("Fresnel Scale", Range(0, 2))    = 1.25

        [Header(Blend Mode Non Wet)]
        [KeywordEnum(Additive, AlphaBlend)] _BlendMode ("Blend Mode", Float) = 1

        [Header(Rendering)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull [_Cull]

        Pass
        {
            Name "CustomParticle"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local _WETDECAL_ON
            #pragma shader_feature_local _USEREFLECTION
            #pragma shader_feature_local _USENORMALMAP
            #pragma shader_feature_local _BLENDMODE_ADDITIVE _BLENDMODE_ALPHABLEND

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);      SAMPLER(sampler_MainTex);
            TEXTURE2D(_ReflectionMap); SAMPLER(sampler_ReflectionMap);
            TEXTURE2D(_BumpMap);       SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _MainTex_ST;
                half4 _Color;
                half4 _ScrollSpeed;
                half  _WetStrength;
                half  _ReflectionStrength;
                half  _Smoothness;

                half  _BumpScale;
                half  _FresnelPower;
                half  _FresnelBias;
                half  _FresnelScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                half4  color      : COLOR;
                half2  uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                half2  uv         : TEXCOORD0;
                half   fogFactor  : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float3 tangentWS  : TEXCOORD3;
                float3 bitangentWS: TEXCOORD4;
                float3 viewDirWS  : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // 球形反射UV（Matcap 风格，视空间法线映射）
            half2 SphericalReflectionUV(half3 normalWS, half3 viewDirWS)
            {
                half3 r = reflect(-viewDirWS, normalWS);
                // 转到视空间做球形映射
                half3 viewSpaceR = half3(
                    dot(r, UNITY_MATRIX_V[0].xyz),
                    dot(r, UNITY_MATRIX_V[1].xyz),
                    dot(r, UNITY_MATRIX_V[2].xyz)
                );
                return viewSpaceR.xy / (2.0h * (sqrt(viewSpaceR.z + 1.0h) + 0.001h)) + 0.5h;
            }

            half CalculateFresnel(half3 normalWS, half3 viewDirWS)
            {
                half NdotV = saturate(dot(normalWS, viewDirWS));
                return saturate(_FresnelBias + _FresnelScale * pow(1.0h - NdotV, _FresnelPower));
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   nrmInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS   = posInputs.positionCS;
                output.color        = input.color;
                output.uv           = TRANSFORM_TEX(input.uv, _MainTex) + _Time.y * _ScrollSpeed.xy;
                output.fogFactor    = ComputeFogFactor(output.positionCS.z);
                output.normalWS     = nrmInputs.normalWS;
                output.tangentWS    = nrmInputs.tangentWS;
                output.bitangentWS  = nrmInputs.bitangentWS;
                output.viewDirWS    = GetCameraPositionWS() - posInputs.positionWS;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                half particleAlpha = input.color.a;

                // ── 打湿模式 ──────────────────────────────────────────────
                #if defined(_WETDECAL_ON)
                {
                    half3 normalWS  = normalize(input.normalWS);
                    half3 viewDirWS = normalize(input.viewDirWS);

                    // 法线贴图扰动（可选）
                    #if defined(_USENORMALMAP)
                    {
                        half2 bumpUV = TRANSFORM_TEX(input.uv, _MainTex);
                        half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, bumpUV));
                        normalTS.xy *= _BumpScale;
                        normalTS.z   = sqrt(1.0h - saturate(dot(normalTS.xy, normalTS.xy)));
                        half3x3 TBN  = half3x3(
                            normalize(input.tangentWS),
                            normalize(input.bitangentWS),
                            normalWS
                        );
                        normalWS = normalize(mul(normalTS, TBN));
                    }
                    #endif

                    // Fresnel：掠射角反射更强
                    half fresnel = CalculateFresnel(normalWS, viewDirWS);

                    // R通道蒙版 × 粒子alpha → 打湿区域透明度
                    half mask = tex.r * particleAlpha;

                    // 压暗色：_Color 越深压暗越强，_WetStrength 控制整体强度
                    half3 wetColor = _Color.rgb * input.color.rgb;

                    // 反射叠加（可选）
                    #if defined(_USEREFLECTION)
                    {
                        half2 reflUV = SphericalReflectionUV(normalWS, viewDirWS);
                        half3 reflColor = SAMPLE_TEXTURE2D_LOD(
                            _ReflectionMap, sampler_ReflectionMap, reflUV, 0).rgb;
                        // 反射强度受 Fresnel 和 Smoothness 控制
                        half reflFactor = fresnel * _ReflectionStrength * _Smoothness;
                        wetColor += reflColor * reflFactor;
                    }
                    #endif

                    // 主光高光（简单 Blinn-Phong，让水面有光泽感）
                    Light mainLight = GetMainLight();
                    half3 halfDir   = normalize(mainLight.direction + viewDirWS);
                    half  NdotH     = saturate(dot(normalWS, halfDir));
                    half  gloss     = exp2(_Smoothness * 10.0h + 1.0h);
                    half  spec      = pow(NdotH, gloss) * _Smoothness * fresnel;
                    wetColor += mainLight.color * spec * _ReflectionStrength;

                    // 最终 alpha：蒙版控制形状，_WetStrength 控制压暗强度
                    // Blend SrcAlpha OneMinusSrcAlpha：每个粒子独立覆盖，不互相叠乘
                    half finalAlpha = mask * _WetStrength;
                    return half4(MixFog(wetColor, input.fogFactor), finalAlpha);
                }

                // ── 普通模式 ──────────────────────────────────────────────
                #elif defined(_BLENDMODE_ADDITIVE)
                {
                    half3 col   = tex.rgb * _Color.rgb * input.color.rgb;
                    half  alpha = tex.a * _Color.a * particleAlpha;
                    return half4(MixFog(col, input.fogFactor), alpha);
                }
                #else // AlphaBlend
                {
                    half3 col   = tex.rgb * _Color.rgb * input.color.rgb;
                    half  alpha = tex.a * _Color.a * particleAlpha;
                    return half4(MixFog(col, input.fogFactor), alpha);
                }
                #endif
            }
            ENDHLSL
        }
    }

    FallBack "Particles/Additive"
    CustomEditor "CustomParticleGUI"
}
