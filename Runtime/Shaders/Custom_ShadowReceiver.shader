// ShadowReceiver 12.0 接收阴影材质（等边三角形 120° 软阴影 + 单次 shadow map 比较，无 PCF 开销）

Shader "Custom/ShadowReceiver"
{
    Properties
    {
        _Alpha("Alpha", Range(0, 1)) = 0.8
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 1.0
        _Softness("Softness（纹素数）", Range(0, 5)) = 1.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ShadowReceiver"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // 阴影关键词
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            // 不启用 URP 内置 PCF（_SHADOWS_SOFT），每个采样点只做单次深度比较
            // 柔化效果完全由外层 UV 空间等边三角形核提供（中心 + 3点120°，共 4 次采样）

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
            };

            float4 _MainLightShadowmapTexture_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float _Alpha;
                float _AmbientStrength;
                float _Softness;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // ===== 软阴影 =====
                // 算法：UV 空间等边三角形采样（1 中心 + 3 点 120° 均布），仅 1 次 TransformWorldToShadowCoord
                // 固定权重 2:1:1:1 ÷ 5 → 中心 40%，周边各 20%，羽化更强、归一化正确
                float4 sc = TransformWorldToShadowCoord(IN.positionWS);
                float2 texelSize = _MainLightShadowmapTexture_TexelSize.xy;
                float radius = _Softness * texelSize.x;    // 采样半径（纹素单位）

                // 采样 1：中心点（权重 2）
                float shadow = SAMPLE_TEXTURE2D_SHADOW(
                    _MainLightShadowmapTexture, sampler_LinearClampCompare, sc) * 2.0;
                // 采样 2：0°（右侧，权重 1）
                shadow += SAMPLE_TEXTURE2D_SHADOW(
                    _MainLightShadowmapTexture, sampler_LinearClampCompare,
                    float4(sc.xy + float2(radius, 0), sc.zw));
                // 采样 3：120°（左上，cos120°=-0.5, sin120°≈0.866，权重 1）
                shadow += SAMPLE_TEXTURE2D_SHADOW(
                    _MainLightShadowmapTexture, sampler_LinearClampCompare,
                    float4(sc.xy + float2(-0.5 * radius, 0.866 * radius), sc.zw));
                // 采样 4：240°（左下，cos240°=-0.5, sin240°≈-0.866，权重 1）
                shadow += SAMPLE_TEXTURE2D_SHADOW(
                    _MainLightShadowmapTexture, sampler_LinearClampCompare,
                    float4(sc.xy + float2(-0.5 * radius, -0.866 * radius), sc.zw));

                // 超出阴影贴图范围（sc.z <= 0）时维持无阴影状态
                half inRange = (sc.z > 0.0) ? 1.0 : 0.0;
                shadow = lerp(1.0, shadow / 5.0, inRange);

                half3 ambient = SampleSH(half3(0, 1, 0));
                half3 shadowColor = lerp(half3(0, 0, 0), ambient, _AmbientStrength * 0.25);

                return half4(shadowColor, (1.0 - shadow) * _Alpha);
            }
            ENDHLSL
        }
        // ===== DepthOnly Pass：写入深度用于与其他透明对象的排序 =====
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half DepthFrag(DepthVaryings IN) : SV_TARGET
            {
                return IN.positionCS.z;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
