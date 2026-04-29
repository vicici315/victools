// ShadowReceiver 1.0 接收阴影材质，支持环境光

Shader "Custom/ShadowReceiver"
{
    Properties
    {
        _Alpha("Alpha", Range(0, 1)) = 0.8
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Geometry+1"
            // 支持MSAA抗锯齿
            "IgnoreProjector" = "True"
        }

        // ── 接收阴影 Pass ──
        Pass
        {
            Name "ShadowReceiver"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // URP 阴影关键字 - 启用软阴影和级联阴影
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            // 启用MSAA支持
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

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

            CBUFFER_START(UnityPerMaterial)
                float _Alpha;
                float _AmbientStrength;
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
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float shadow = mainLight.shadowAttenuation;

                // 阴影颜色：纯黑 lerp 到少量环境光，避免死黑但不会过亮
                half3 ambient = SampleSH(half3(0, 1, 0));
                half3 shadowColor = lerp(half3(0, 0, 0), ambient, _AmbientStrength * 0.25);

                // 有阴影处不透明叠暗色，无阴影处完全透明
                return half4(shadowColor, (1.0 - shadow) * _Alpha);
            }
            ENDHLSL
        }

        // ── 投射阴影 Pass（让自身也能投影） ──
        // Pass
        // {
        //     Name "ShadowCaster"
        //     Tags { "LightMode" = "ShadowCaster" }

        //     ZWrite On
        //     ZTest LEqual
        //     ColorMask 0

        //     HLSLPROGRAM
        //     #pragma vertex ShadowPassVertex
        //     #pragma fragment ShadowPassFragment
        //     #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

        //     #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        //     #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
        //     ENDHLSL
        // }
    }

    FallBack Off
}
