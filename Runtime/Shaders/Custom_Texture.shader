// Texture 1.2 纯色透明材质（可用于2D云朵透贴）修给变体固定代码
// Texture 1.3 不再依赖 DepthOnlyPass.hlsl，改为自己实现的轻量 pass，CBUFFER 用 _MainTex_ST 保持一致

Shader "Custom/Texture"
{
    Properties
    {
        [Header(Texture Settings)]
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        [MainColor] _Color ("Color Tint", Color) = (1,1,1,1)
        _Contrast ("Contrast", Range(0.1, 3.0)) = 1.0
        _Brightness ("Brightness", Range(0.0, 2.0)) = 1.0

        [Header(Transparency)]
        [Toggle(_ALPHATEST_ON)] _UseAlphaClip ("Use Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [Toggle(_ALPHABLEND_ON)] _UseAlphaBlend ("Use Alpha Blend", Float) = 0

        [Header(Render Settings)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Toggle] _ZWrite ("Z Write", Float) = 1
    }

    SubShader
    {
        // RenderType/Queue 由 CustomTextureGUI 在运行时通过 material.SetOverrideTag 控制
        // 这里给一个合理的默认值
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 100

        // ── 主 Forward Pass ──
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // 透明度选项
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _ALPHABLEND_ON
            
            // multi_compile_local 保证所有变体都被打包，不会因引用方式被剥离
            // #pragma multi_compile_local _ _ALPHATEST_ON
            // #pragma multi_compile_local _ _ALPHABLEND_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half   _Contrast;
                half   _Brightness;
                half   _Cutoff;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 col = texColor * _Color;

                // 对比度
                col.rgb = saturate((col.rgb - 0.5) * _Contrast + 0.5);
                // 亮度
                col.rgb *= _Brightness;

                #ifdef _ALPHATEST_ON
                    clip(col.a - _Cutoff);
                #endif

                #ifdef _ALPHABLEND_ON
                    return col;
                #else
                    return half4(col.rgb, 1.0);
                #endif
            }
            ENDHLSL
        }

        // ── DepthOnly Pass：AlphaTest 模式下深度预写入 ──
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
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half   _Contrast;
                half   _Brightness;
                half   _Cutoff;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                #ifdef _ALPHATEST_ON
                    half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a * _Color.a;
                    clip(alpha - _Cutoff);
                #endif
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }

    CustomEditor "CustomTextureGUI"
}
