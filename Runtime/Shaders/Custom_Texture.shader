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
        Tags 
        { 
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 100

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
            
            // 不需要雾效
            // #pragma multi_compile_fog
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Contrast;
                half _Brightness;
                half _Cutoff;
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
                // 采样纹理
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                
                // 应用颜色叠加
                half4 col = texColor * _Color;
                
                // 应用对比度调整
                // 对比度公式：(color - 0.5) * contrast + 0.5
                col.rgb = saturate((col.rgb - 0.5) * _Contrast + 0.5);
                
                // 应用亮度调整
                col.rgb *= _Brightness;
                
                // Alpha Clip（用于硬边透明，如树叶）
                #ifdef _ALPHATEST_ON
                    clip(col.a - _Cutoff);
                #endif
                
                // Alpha Blend（用于半透明，如云朵）
                #ifdef _ALPHABLEND_ON
                    // 保持 alpha 通道用于混合
                    return col;
                #else
                    // 不透明模式，alpha 设为 1
                    return half4(col.rgb, 1.0);
                #endif
            }
            ENDHLSL
        }
    }
    
    // 自定义编辑器，用于自动设置渲染模式
    CustomEditor "CustomTextureGUI"
}
