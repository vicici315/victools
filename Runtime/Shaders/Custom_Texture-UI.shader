// Texture-UI 1.0 基于 Custom/Texture 改编的UI专用版本
// 与 Custom/Texture 共享 CustomTextureGUI 编辑器脚本
// 差异：
//   - 去掉 URP 光照管线 LightMode，适配 Canvas 渲染
//   - 顶点着色器融合 Image.color（vertex.color）× 材质 _Color
//   - SubShader 级 Stencil 块，支持 Mask 组件裁剪
//   - _ClipRect 裁剪，支持 RectMask2D / ScrollRect
//   - 默认 Transparent 模式（_UseAlphaBlend=1, SrcAlpha/OneMinusSrcAlpha, ZWrite Off）

Shader "Custom/Texture-UI"
{
    Properties
    {
        [Header(Texture Settings)]
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _Color ("Color Tint", Color) = (1,1,1,1)
        _Contrast ("Contrast", Range(0.1, 3.0)) = 1.0
        _Brightness ("Brightness", Range(0.0, 2.0)) = 1.0

        [Header(Transparency)]
        [Toggle(_ALPHATEST_ON)] _UseAlphaClip ("Use Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [Toggle(_ALPHABLEND_ON)] _UseAlphaBlend ("Use Alpha Blend", Float) = 1

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Toggle] _ZWrite ("Z Write", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Plane"
        }
        LOD 100

        // ── UI Mask 兼容（Mask 组件依赖 Stencil）──
        Stencil
        {
            Ref 0
            Comp Always
            Pass Keep
        }

        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Cull [_Cull]

        Pass
        {
            Name "Default"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _ALPHABLEND_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _Color;
                half   _Contrast;
                half   _Brightness;
                half   _Cutoff;
                // RectMask2D / ScrollRect 自动注入
                float4 _ClipRect;
                float  _UIMaskSoftnessX;
                float  _UIMaskSoftnessY;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                // Image.color（vertex color）× 材质 _Color → 支持运行时 tint 叠加
                output.color = input.color * _Color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 col = texColor * input.color;

                // 对比度
                col.rgb = saturate((col.rgb - 0.5) * _Contrast + 0.5);
                // 亮度
                col.rgb *= _Brightness;

                // RectMask2D / ScrollRect 软裁剪
                half2 clipDelta = (_ClipRect.zw - _ClipRect.xy) * 0.5;
                half2 clipCenter = (_ClipRect.xy + _ClipRect.zw) * 0.5;
                half2 clipMask = saturate(clipDelta - abs(input.positionCS.xy - clipCenter));
                col.a *= clipMask.x * clipMask.y;

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
    }

    CustomEditor "CustomTextureGUI"
}
