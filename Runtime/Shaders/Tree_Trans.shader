// ============================================================================
// Tree_Trans 1.1 植被透明材质，虚拟光照，ShadowMap 只带投影，Noise 纹理风动
// ============================================================================
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
        // ForwardLit
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);         SAMPLER(sampler_BaseMap);
            TEXTURE2D(_ShadowNormalMap); SAMPLER(sampler_ShadowNormalMap);
            TEXTURE2D(_RampMap);         SAMPLER(sampler_RampMap);
            TEXTURE2D(_WindNoiseTex);    SAMPLER(sampler_WindNoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
                half   _HalfLambert;
                half4  _ShadowColor;
                half   _ShadowStrength;
                half   _ShadowSoftness;
                half   _VirtualShadowBias;
                half   _ShadowBrightness;
                float4 _ShadowNormalMap_ST;
                half   _ShadowNormalScale;
                half   _WindSpeed;
                half   _WindStrength;
                float4 _WindDirection;
                half   _WindRadius;
                float4 _WindNoiseTex_ST;
                half   _WindNoiseScale;
                half   _WindNoiseStrength;
                half   _RampStrength;
                half   _RampRow;
                half   _RampHeight;
                half   _RampOffset;
            CBUFFER_END

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
                float4 positionCS    : SV_POSITION;
                float2 uv            : TEXCOORD0;
                float3 positionWS    : TEXCOORD1;
                half3  normalWS      : TEXCOORD2;
                half   fogFactor     : TEXCOORD3;
                half4  tangentWS     : TEXCOORD4;
                half   heightGradient : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // 风力：主摆动 + noise 纹理局部随机抖动
            float3 ApplyWind(float3 positionOS)
            {
                #ifdef _WIND
                    half dist = length(positionOS.xyz);
                    half radialWeight = saturate(dist / _WindRadius);
                    radialWeight *= radialWeight;

                    float time = _Time.y * _WindSpeed;
                    float3 worldPos = TransformObjectToWorld(positionOS);

                    // 主摆动：两层正弦叠加
                    float phase = dot(worldPos.xz, float2(0.7, 0.3));
                    float sway = sin(time + phase) * 0.6 + sin(time * 2.3 + phase * 1.5) * 0.25;

                    // Noise 纹理采样：世界坐标 XZ * 缩放 + 时间滚动
                    // 产生局部随机抖动，每片叶子的偏移量不同
                    float2 noiseUV = worldPos.xz * _WindNoiseScale + time * 0.15;
                    half noiseSample = SAMPLE_TEXTURE2D_LOD(_WindNoiseTex, sampler_WindNoiseTex, noiseUV, 0).r;
                    half noiseValue = (noiseSample * 2.0 - 1.0) * _WindNoiseStrength;
                    sway += noiseValue;

                    float3 windDir = normalize(_WindDirection.xyz);
                    positionOS.xyz += windDir * sway * _WindStrength * radialWeight;
                #endif
                return positionOS;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posOS = ApplyWind(input.positionOS.xyz);
                VertexPositionInputs vertexInput = GetVertexPositionInputs(posOS);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS  = TransformObjectToWorldNormal(input.normalOS);
                half sign = input.tangentOS.w * GetOddNegativeScale();
                output.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz), sign);
                output.uv        = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                // _RampOffset 偏移渐变底部起始高度：正值让渐变从更高处开始，负值从更低处开�?
                output.heightGradient = saturate((posOS.y - _RampOffset) / _RampHeight);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 albedo  = baseMap * _BaseColor;
                clip(albedo.a - _Cutoff);

                half3 rampColor = SAMPLE_TEXTURE2D(_RampMap, sampler_RampMap, half2(input.heightGradient, _RampRow)).rgb;
                albedo.rgb = lerp(albedo.rgb, albedo.rgb * rampColor, _RampStrength);

                half3 normalWS = normalize(input.normalWS);

                float2 shadowNormalUV = input.uv * _ShadowNormalMap_ST.xy + _ShadowNormalMap_ST.zw;
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_ShadowNormalMap, sampler_ShadowNormalMap, shadowNormalUV));
                normalTS.xy *= _ShadowNormalScale;
                normalTS = normalize(normalTS);

                half3 tangentWS = normalize(input.tangentWS.xyz);
                half3 bitangentWS = cross(normalWS, tangentWS) * input.tangentWS.w;
                half3 perturbedNormal = normalize(
                    normalTS.x * tangentWS +
                    normalTS.y * bitangentWS +
                    normalTS.z * normalWS
                );

                Light mainLight = GetMainLight();

                half NdotL_biased = dot(perturbedNormal, mainLight.direction) + _VirtualShadowBias;
                half shadowMask = smoothstep(-_ShadowSoftness, _ShadowSoftness, NdotL_biased);

                half3 fullShadowTint = lerp(_ShadowColor.rgb, half3(1, 1, 1), shadowMask);
                half3 shadowTint = lerp(half3(1, 1, 1), fullShadowTint * _ShadowBrightness, _ShadowStrength);

                half NdotL_raw = dot(normalWS, mainLight.direction);
                half halfLambert = NdotL_raw * (1.0 - _HalfLambert) + _HalfLambert;
                half3 diffuse = mainLight.color * halfLambert;

                half3 ambient = SampleSH(normalWS);

                half3 finalColor = albedo.rgb * (diffuse + ambient) * shadowTint;
                finalColor = MixFog(finalColor, input.fogFactor);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // ====================================================================
        // DepthOnly
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);
            TEXTURE2D(_WindNoiseTex); SAMPLER(sampler_WindNoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
                half   _HalfLambert;
                half4  _ShadowColor;
                half   _ShadowStrength;
                half   _ShadowSoftness;
                half   _VirtualShadowBias;
                half   _ShadowBrightness;
                float4 _ShadowNormalMap_ST;
                half   _ShadowNormalScale;
                half   _WindSpeed;
                half   _WindStrength;
                float4 _WindDirection;
                half   _WindRadius;
                float4 _WindNoiseTex_ST;
                half   _WindNoiseScale;
                half   _WindNoiseStrength;
                half   _RampStrength;
                half   _RampRow;
                half   _RampHeight;
                half   _RampOffset;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings  { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float3 ApplyWind(float3 positionOS)
            {
                #ifdef _WIND
                    half dist = length(positionOS.xyz);
                    half radialWeight = saturate(dist / _WindRadius);
                    radialWeight *= radialWeight;
                    float time = _Time.y * _WindSpeed;
                    float3 worldPos = TransformObjectToWorld(positionOS);
                    float phase = dot(worldPos.xz, float2(0.7, 0.3));
                    float sway = sin(time + phase) * 0.6 + sin(time * 2.3 + phase * 1.5) * 0.25;
                    float2 noiseUV = worldPos.xz * _WindNoiseScale + time * 0.15;
                    half noiseSample = SAMPLE_TEXTURE2D_LOD(_WindNoiseTex, sampler_WindNoiseTex, noiseUV, 0).r;
                    sway += (noiseSample * 2.0 - 1.0) * _WindNoiseStrength;
                    float3 windDir = normalize(_WindDirection.xyz);
                    positionOS.xyz += windDir * sway * _WindStrength * radialWeight;
                #endif
                return positionOS;
            }

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
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // ====================================================================
        // ShadowCaster
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);
            TEXTURE2D(_WindNoiseTex); SAMPLER(sampler_WindNoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
                half   _HalfLambert;
                half4  _ShadowColor;
                half   _ShadowStrength;
                half   _ShadowSoftness;
                half   _VirtualShadowBias;
                half   _ShadowBrightness;
                float4 _ShadowNormalMap_ST;
                half   _ShadowNormalScale;
                half   _WindSpeed;
                half   _WindStrength;
                float4 _WindDirection;
                half   _WindRadius;
                float4 _WindNoiseTex_ST;
                half   _WindNoiseScale;
                half   _WindNoiseStrength;
                half   _RampStrength;
                half   _RampRow;
                half   _RampHeight;
                half   _RampOffset;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings  { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float3 ApplyWind(float3 positionOS)
            {
                #ifdef _WIND
                    half dist = length(positionOS.xyz);
                    half radialWeight = saturate(dist / _WindRadius);
                    radialWeight *= radialWeight;
                    float time = _Time.y * _WindSpeed;
                    float3 worldPos = TransformObjectToWorld(positionOS);
                    float phase = dot(worldPos.xz, float2(0.7, 0.3));
                    float sway = sin(time + phase) * 0.6 + sin(time * 2.3 + phase * 1.5) * 0.25;
                    float2 noiseUV = worldPos.xz * _WindNoiseScale + time * 0.15;
                    half noiseSample = SAMPLE_TEXTURE2D_LOD(_WindNoiseTex, sampler_WindNoiseTex, noiseUV, 0).r;
                    sway += (noiseSample * 2.0 - 1.0) * _WindNoiseStrength;
                    float3 windDir = normalize(_WindDirection.xyz);
                    positionOS.xyz += windDir * sway * _WindStrength * radialWeight;
                #endif
                return positionOS;
            }

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 posOS = ApplyWind(input.positionOS.xyz);
                float3 posWS = TransformObjectToWorld(posOS);
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
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
