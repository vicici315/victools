// Custom Hair Shader - URP
// Kajiya-Kay 各向异性双高光 + Shift Map
// 双 Pass：Pass1 不透明主体（完整光照）+ Pass2 半透明边缘（简化光照）
// Custom_Hair 1.2 性能优化：Pass2 简化光照，减少纹理采样和 normalize 调用，头发进入阴影区域保留微小高光
Shader "Custom/Hair"
{
    Properties
    {
        [Header(Base)]
        _BaseMap("Albedo", 2D) = "white" {}
        _Color("Color Tint", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff (主体)", Range(0, 1)) = 0.542
        _TransCutoff("Trans Cutoff (边缘起始)", Range(0, 1)) = 0.17
        _ShadowCutoff("Shadow Alpha Cutoff", Range(0, 1)) = 0.202
        _ShadowBiasScale("Shadow Bias Scale", Range(0, 0.1)) = 0.001

        [Header(Normal)]
        [Normal] _NormalMap("Normal Map", 2D) = "bump" {}
        _NormalScale("Normal Scale", Range(0, 2)) = 0.8

        [Header(Anisotropic Specular)]
        _ShiftMap("Shift Map (R通道)", 2D) = "gray" {}
        _HairDirRotate("Hair Dir Rotate", Range(-180, 180)) = -13
        [HDR] _SpecColor1("Primary Specular Color", Color) = (1,1,1,1)
        _SpecPower1("Primary Specular Power", Range(1, 256)) = 155
        _SpecShift1("Primary Shift", Range(-1, 1)) = 0.418
        [HDR] _SpecColor2("Secondary Specular Color", Color) = (0.5,0.5,0.5,1)
        _SpecPower2("Secondary Specular Power", Range(1, 256)) = 50
        _SpecShift2("Secondary Shift", Range(-1, 1)) = -0.052

        [Header(Ambient and Rim)]
        [HDR] _AmbientColor("Ambient Color", Color) = (0.3,0.3,0.3,1)
        _RimPower("Rim Power", Range(1, 8)) = 6.39
        _RimIntensity("Rim Intensity", Range(0, 1)) = 0.258

        [Header(Render Settings)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4  _Color;
        half   _Cutoff;
        half   _TransCutoff;
        half   _ShadowCutoff;
        half   _ShadowBiasScale;
        half   _NormalScale;
        half   _HairDirRotate;
        half4  _SpecColor1;
        half   _SpecPower1;
        half   _SpecShift1;
        half4  _SpecColor2;
        half   _SpecPower2;
        half   _SpecShift2;
        half4  _AmbientColor;
        half   _RimPower;
        half   _RimIntensity;
    CBUFFER_END

    TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
    TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
    TEXTURE2D(_ShiftMap);  SAMPLER(sampler_ShiftMap);
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
        }

        // ══════════════════════════════════════
        // Pass 1: 不透明主体（完整光照）
        // ══════════════════════════════════════
        Pass
        {
            Name "HairOpaque"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertFull
            #pragma fragment fragOpaque
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS  : POSITION;
                float3 normalOS    : NORMAL;
                float4 tangentOS   : TANGENT;
                float2 uv          : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                half3  normalWS    : TEXCOORD2;
                half3  tangentWS   : TEXCOORD3;
                half3  bitangentWS : TEXCOORD4;
            };

            Varyings vertFull(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.positionCS  = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.normalWS    = normInputs.normalWS;
                OUT.tangentWS   = normInputs.tangentWS;
                OUT.bitangentWS = normInputs.bitangentWS;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            // Kajiya-Kay：用 exp2/log2 替代 pow，移动端更友好
            half StrandSpec(half3 T, half3 H, half exponent)
            {
                half TdotH = dot(T, H);
                half sinTH = sqrt(max(0.001, 1.0 - TdotH * TdotH));
                return exp2(exponent * log2(max(sinTH, 0.001)));
            }

            half3 ShiftT(half3 T, half3 N, half shift)
            {
                return normalize(T + N * shift);
            }

            half4 fragOpaque(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _Color;
                clip(tex.a - _Cutoff);

                // 法线
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, IN.uv), _NormalScale);
                half3x3 TBN = half3x3(normalize(IN.tangentWS), normalize(IN.bitangentWS), normalize(IN.normalWS));
                half3 N = normalize(mul(normalTS, TBN));

                // Shift Map
                half shiftTex = SAMPLE_TEXTURE2D(_ShiftMap, sampler_ShiftMap, IN.uv).r - 0.5;

                // 发丝方向：bitangent + 旋转微调
                half3 hairBase = normalize(IN.bitangentWS);
                half rotRad = radians(_HairDirRotate);
                half cosR = cos(rotRad);
                half sinR = sin(rotRad);
                half3 T = normalize(hairBase * cosR + cross(N, hairBase) * sinR);

                // 光照
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half3 L = mainLight.direction;
                half3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                half3 H = normalize(L + V);

                half NdotL = saturate(dot(N, L) * 0.5 + 0.5);
                half shadow = mainLight.shadowAttenuation;

                // 漫反射
                half3 diffuse = tex.rgb * mainLight.color * (NdotL * shadow);

                // 双高光（阴影区域保留部分高光，避免完全消失）
                half3 T1 = ShiftT(T, N, _SpecShift1 + shiftTex);
                half3 T2 = ShiftT(T, N, _SpecShift2 + shiftTex);
                half specShadow = max(shadow, 0.07);
                half3 specular = (_SpecColor1.rgb * StrandSpec(T1, H, _SpecPower1)
                                + _SpecColor2.rgb * StrandSpec(T2, H, _SpecPower2)) * specShadow;

                // 环境光（阴影区域补偿）
                half lightFactor = NdotL * shadow;
                half3 ambient = _AmbientColor.rgb * tex.rgb * (1.0 - lightFactor);

                // 边缘光（用法线贴图的 N，颜色由环境光控制）
                half rim = exp2(_RimPower * log2(max(1.0 - saturate(dot(N, V)), 0.001))) * _RimIntensity;

                return half4(diffuse + specular + ambient + rim * _AmbientColor.rgb, 1.0);
            }
            ENDHLSL
        }

        // ══════════════════════════════════════
        // Pass 2: 半透明边缘（简化光照：无法线贴图、无 ShiftMap、单高光）
        // ══════════════════════════════════════
        Pass
        {
            Name "HairTransparent"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull [_Cull]
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vertSimple
            #pragma fragment fragTrans
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS  : POSITION;
                float3 normalOS    : NORMAL;
                float4 tangentOS   : TANGENT;
                float2 uv          : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                half3  normalWS    : TEXCOORD2;
                half3  tangentWS   : TEXCOORD3;
                half3  bitangentWS : TEXCOORD4;
            };

            Varyings vertSimple(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.positionCS  = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.normalWS    = normInputs.normalWS;
                OUT.tangentWS   = normInputs.tangentWS;
                OUT.bitangentWS = normInputs.bitangentWS;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 fragTrans(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _Color;
                clip(tex.a - _TransCutoff);
                clip(_Cutoff - tex.a - 0.001);

                // 采样法线贴图让边缘光不平
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, IN.uv), _NormalScale);
                half3x3 TBN = half3x3(normalize(IN.tangentWS), normalize(IN.bitangentWS), normalize(IN.normalWS));
                half3 N = normalize(mul(normalTS, TBN));

                half3 T = normalize(IN.bitangentWS);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half3 L = mainLight.direction;
                half3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                half3 H = normalize(L + V);

                half NdotL = saturate(dot(N, L) * 0.5 + 0.5);
                half shadow = mainLight.shadowAttenuation;

                // 漫反射
                half3 diffuse = tex.rgb * mainLight.color * (NdotL * shadow);

                // 单高光（阴影区域保留部分）
                half TdotH = dot(T, H);
                half sinTH = sqrt(max(0.001, 1.0 - TdotH * TdotH));
                half spec = exp2(_SpecPower1 * 0.5 * log2(max(sinTH, 0.001))) * max(shadow, 0.2);
                half3 specular = _SpecColor1.rgb * spec;

                // 环境光
                half3 ambient = _AmbientColor.rgb * tex.rgb * (1.0 - NdotL * shadow);

                // 边缘光（用法线贴图的 N，颜色由环境光控制）
                half rim = exp2(_RimPower * log2(max(1.0 - saturate(dot(N, V)), 0.001))) * _RimIntensity;

                return half4(diffuse + specular + ambient + rim * _AmbientColor.rgb, tex.a);
            }
            ENDHLSL
        }

        // ══════════════════════════════════════
        // Shadow Caster
        // ══════════════════════════════════════
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                posWS += _LightDirection * _ShadowBiasScale;
                posWS += normWS * (_ShadowBiasScale * 0.5);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _Color.a;
                clip(alpha - _ShadowCutoff);
                return 0;
            }
            ENDHLSL
        }

        // ══════════════════════════════════════
        // Depth Only
        // ══════════════════════════════════════
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 DepthFrag(Varyings IN) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _Color.a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    CustomEditor "CustomHairGUI"
}
