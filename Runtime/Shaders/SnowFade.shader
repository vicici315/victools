// 雪地痕迹淡出 Shader（Hidden，仅供 SnowDeformManager 内部使用）
// 每帧对 RT 做微量衰减，模拟雪慢慢填平痕迹

Shader "Hidden/SnowFade"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float _FadeAmount; // 每帧衰减量

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half existing = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).r;
                // 减去衰减量，clamp 到 0
                half faded = max(0, existing - _FadeAmount);
                return half4(faded, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
