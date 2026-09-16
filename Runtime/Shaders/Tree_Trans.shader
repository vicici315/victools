// ============================================================================
// Tree_Trans 1.1 植被透明材质，虚拟光照，ShadowMap 只带投影，Noise 纹理风动
//
// 重构说明：共享片段已抽取到 Tree_Trans_Common.hlsl（CBUFFER、纹理、ApplyWind、
// alpha clip、Ramp、法线扰动、虚拟阴影 tint）。每个 Pass 的 HLSLPROGRAM 内
// 只保留本 Pass 特有的 vert/frag 与少量本地 helpers，体量与职责都大幅收敛。
// 视觉行为/编译变体与重构前 100% 等价。
// ============================================================================
// Tree_Trans 2.0 共享片段已抽取到 Tree_Trans_Common.hlsl（CBUFFER、纹理、ApplyWind、
// alpha clip、Ramp、法线扰动、虚拟阴影 tint）；添加GUI。
Shader "Custom/Tree_Trans"
{
    Properties
    {
        [Header(Base)]
        [MainColor] _BaseColor ("Base Color", Color) = (0.45, 0.75, 0.15, 1)
        [MainTexture] _BaseMap ("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.541

        [Header(Lighting)]
        _HalfLambert ("Half Lambert", Range(0, 1)) = 0.0

        [Header(Virtual Shadow)]
        _ShadowColor ("Shadow Color", Color) = (0.15, 0.25, 0.05, 1)
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.868
        _ShadowSoftness ("Shadow Softness", Range(0.01, 1)) = 0.657
        _VirtualShadowBias ("Shadow Bias", Range(-1, 1)) = -0.63
        _ShadowBrightness ("Shadow Brightness", Range(0, 1)) = 1.0
        [Normal] _ShadowNormalMap ("Shadow Normal Map", 2D) = "bump" {}
        _ShadowNormalScale ("Shadow Normal Scale", Range(0, 5)) = 2.5

        [Header(Ramp Gradient)]
        [NoScaleOffset] _RampMap ("Ramp Map (Left=Bottom, Right=Top)", 2D) = "white" {}
        _RampStrength ("Ramp Strength", Range(0, 1)) = 0.784
        _RampRow ("Ramp Row Select", Range(0.01, 0.99)) = 0.1
        _RampOffset ("Ramp Bottom Offset", Range(-10, 10)) = 0.0
        _RampHeight ("Ramp Height Range", Range(0.1, 20)) = 0.5

        [Header(Wind)]
        [Toggle(_WIND)] _UseWind ("Enable Wind", Float) = 1
        _WindSpeed ("Wind Speed", Range(0, 20)) = 1.8
        _WindStrength ("Wind Strength", Range(0, 0.5)) = 0.218
        _WindDirection ("Wind Direction (XZ)", Vector) = (0.12, 0, 0.13, 0.97)
        _WindRadius ("Wind Radius", Range(0.1, 10)) = 6.46
        _WindNoiseTex ("Wind Noise Texture", 2D) = "gray" {}
        _WindNoiseScale ("Wind Noise Scale", Range(0.01, 2)) = 2.0
        _WindNoiseStrength ("Wind Noise Strength", Range(0, 1)) = 0.611

        [Header(Options)]
        [Enum(Off,0,Front,1,Back,2)] _Cull ("Cull Mode", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "AlphaTest"
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        // ====================================================================
        // ForwardLit：主光照 Pass（虚拟阴影 + Half Lambert + Ramp + Wind）
        // ====================================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _WIND
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Tree_Trans_Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS     : SV_POSITION;
                float2 uv             : TEXCOORD0;
                float3 positionWS     : TEXCOORD1;
                half3  normalWS       : TEXCOORD2;
                half   fogFactor      : TEXCOORD3;
                half4  tangentWS      : TEXCOORD4;
                half   heightGradient : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posOS = ApplyWind(input.positionOS.xyz);
                VertexPositionInputs vertexInput = GetVertexPositionInputs(posOS);
                output.positionCS     = vertexInput.positionCS;
                output.positionWS     = vertexInput.positionWS;
                output.normalWS       = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS      = half4(TransformObjectToWorldDir(input.tangentOS.xyz),
                                              input.tangentOS.w * GetOddNegativeScale());
                output.uv             = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor      = ComputeFogFactor(vertexInput.positionCS.z);
                output.heightGradient = ComputeHeightGradient(posOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // 1) BaseColor + Alpha clip
                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 albedo  = baseMap * _BaseColor;
                clip(albedo.a - _Cutoff);

                // 2) 高度 ramp 颜色
                albedo.rgb = ApplyHeightRamp(albedo.rgb, input.heightGradient);

                // 3) Shadow normal 扰动 + 虚拟阴影 tint
                half3 N           = normalize(input.normalWS);
                half3 perturbedN  = PerturbNormal(input.uv, N, input.tangentWS.xyz, input.tangentWS.w);
                Light mainLight   = GetMainLight();
                half3 shadowTint  = ComputeVirtualShadowTint(perturbedN, mainLight.direction);

                // 4) Half Lambert diffuse + SH ambient
                half  halfLambert = dot(N, mainLight.direction) * (1.0 - _HalfLambert) + _HalfLambert;
                half3 diffuse     = mainLight.color * halfLambert;
                half3 ambient     = SampleSH(N);

                // 5) 合成 + fog
                half3 finalColor = albedo.rgb * (diffuse + ambient) * shadowTint;
                finalColor       = MixFog(finalColor, input.fogFactor);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // ====================================================================
        // DepthOnly：深度预 Pass（仅 alpha clip，影响深度图）
        // ====================================================================
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
            #pragma shader_feature_local _WIND
            #pragma multi_compile_instancing

            #include "Tree_Trans_Common.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings  { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 posOS = ApplyWind(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(posOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                clip(GetBaseAlpha(input.uv) - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // ====================================================================
        // ShadowCaster：阴影投射 Pass（仅 alpha clip，影响其他物体投到此树的影）
        // ====================================================================
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
            #pragma shader_feature_local _WIND
            #pragma multi_compile_instancing

            #include "Tree_Trans_Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings  { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 posOS    = ApplyWind(input.positionOS.xyz);
                float3 posWS    = TransformObjectToWorld(posOS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, normalWS, _MainLightPosition.xyz));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                clip(GetBaseAlpha(input.uv) - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
    CustomEditor "TreeTransGUI"
}
