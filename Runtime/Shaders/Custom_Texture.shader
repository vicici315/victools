// Texture 1.2 纯色透明材质（可用于2D云朵透贴）修给变体固定代码
// Texture 1.3 不再依赖 DepthOnlyPass.hlsl，改为自己实现的轻量 pass，CBUFFER 用 _BaseMap_ST 保持一致
// Texture 1.4 透明处理完全移至DepthOnly Pass中，适配 DepthPrimingMode=Forced。
//              Forward pass 不再做 alpha clip，由 DepthOnly pre-pass 统一剔除，保证像素集与深度集一致。
// Texture 1.5 Cutout 模式下启用 PreZ：GUI 将 _ZWrite 自动设为 0，Forward pass 不再二次写深度，
//              由 DepthOnly pre-pass 写带 alpha clip 的深度，配合 URP DepthPrimingMode=Forced 实现早 Z 剔除。
//              要求 URP Asset 启用 Depth Priming（推荐 Forced）。
// Texture 1.6 修复 Cutout + PreZ 环境下透贴像素显示为不透明的 bug：
//              当物体前没有 opaque 几何时，DepthOnly clip 掉的像素在 depth buffer 中没有深度，
//              Forward pass 不 clip + ZTest LEqual 与 clear 值比较会通过 → 被错误绘制。
//              现在 Forward 重新做 clip（与 DepthOnly 完全一致），由 DepthOnly 提供早 Z 收益。
//              修正 SubShader 默认 Tags，避免 Queue 与 RenderType 矛盾影响 URP PreZ 判断。
Shader "Custom/Texture"
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
        [Toggle(_ALPHABLEND_ON)] _UseAlphaBlend ("Use Alpha Blend", Float) = 0

        // [Header(Render Settings)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Toggle] _ZWrite ("Z Write", Float) = 1
    }

    SubShader
    {
        // RenderType/Queue 由 CustomTextureGUI 在运行时通过 material.SetOverrideTag / material.renderQueue 控制
        // ShaderLab 的 Tags 是编译期常量，无法用 shader 变量参数化。
        // 默认值给 Opaque/Geometry：与 _UseAlphaClip=0、_UseAlphaBlend=0 的初始状态一致，
        // 避免 URP 误以为材质在 AlphaTest 队列而影响 PreZ 调度。
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 100

        // ── 主 Forward Pass ──
        // Cutout 模式下 _ZWrite 由 GUI 自动设为 0：深度由 DepthOnly pre-pass 写入，
        // 本 pass 不再写深度，配合 URP DepthPrimingMode=Forced 启用 PreZ（被前向深度遮挡的像素早 Z 阶段被剔除）。
        // Forward 仍做 alpha clip：兜底"物体前没有 opaque"场景下的透贴剔除；
        // alpha clip 与 PreZ 互不冲突——PreZ 收益来自被前向深度早 Z 拒绝的像素，与 alpha test 无关。
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

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
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
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 col = texColor * _Color;

                // 对比度
                col.rgb = saturate((col.rgb - 0.5) * _Contrast + 0.5);
                // 亮度
                col.rgb *= _Brightness;

                // Cutout 模式必须在 Forward 也做 alpha clip：
                // PreZ 环境下，物体前若没有 opaque 几何，DepthOnly clip 掉的像素在 depth buffer
                // 中没有深度值，ZTest LEqual 与 clear 值比较会通过 → 透贴像素被错误绘制。
                // 因此 Forward 必须 clip 来兜底，PreZ 收益由 DepthOnly 写深度 + Early-Z 拒绝
                // 被前向遮挡的像素来获得，alpha clip 不影响 PreZ 收益。
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

        // ── DepthOnly Pass：alpha clip 唯一权威，适配 DepthPrimingMode=Forced ──
        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
                "RenderPipeline" = "UniversalPipeline"
            }

            ZWrite On
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 必须与 Forward pass 字段顺序严格一致，维持 SRP Batcher 兼容
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
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
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                // 与 Forward pass 保持完全一致的 alpha 计算：col.a = _BaseMap.a * _Color.a
                // 保证 depth pre-pass 写出的深度形状与 forward 像素集完全对齐
                #ifdef _ALPHATEST_ON
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _Color.a;
                    clip(alpha - _Cutoff);
                #endif
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }

    CustomEditor "CustomTextureGUI"
}