// SpotLightVolume - 轻量探照灯体积雾Shader
// 参考VLB实现：Fresnel边缘羽化 + 深度混合（与场景模型交叠过渡）
Shader "Hidden/VicTools/SpotLightVolume"
{
    Properties
    {
        [HDR] _VolumeColor ("颜色", Color) = (1, 1, 1, 1)
        _Intensity ("强度", Range(0, 2)) = 1
        _StartDistance ("起始距离", Float) = 0
        _MaxDistance ("最长距离", Float) = 10
        _EdgeFade ("边缘羽化", Range(0, 1)) = 0.5
        _EndFade ("末端羽化", Range(0, 1)) = 0.3
        _DepthFadeDistance ("深度混合距离", Range(0, 5)) = 1.5
        _ConeRadiusStart ("锥体起始半径", Float) = 0.001
        _ConeRadiusEnd ("锥体末端半径", Float) = 3

        [HideInInspector] _SrcBlend ("SrcBlend", Int) = 1
        [HideInInspector] _DstBlend ("DstBlend", Int) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SpotLightVolume"
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma shader_feature_local _BLEND_ADDITIVE _BLEND_SOFTADD _BLEND_ALPHA

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 viewDirOS : TEXCOORD1;
                float depthRatio : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float eyeDepth : TEXCOORD4;
                float fogFactor : TEXCOORD5;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _VolumeColor;
                half _Intensity;
                float _StartDistance;
                float _MaxDistance;
                half _EdgeFade;
                half _EndFade;
                half _DepthFadeDistance;
                float _ConeRadiusStart;
                float _ConeRadiusEnd;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionOS = input.positionOS.xyz;

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);

                o.depthRatio = input.uv.x;

                float3 cameraPosOS = TransformWorldToObject(GetCameraPositionWS());
                o.viewDirOS = normalize(input.positionOS.xyz - cameraPosOS);

                // 用于深度混合的屏幕坐标和眼深度
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.eyeDepth = -(TransformWorldToView(posWS).z);

                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float depthRatio = input.depthRatio;
                float depth = depthRatio * _MaxDistance;

                // 起始距离硬裁切
                clip(depth - _StartDistance);

                // 末端羽化
                float endFadeFactor = 1.0 - smoothstep(1.0 - _EndFade, 1.0, depthRatio);

                // === Fresnel边缘羽化 ===
                float2 radialDir = normalize(input.positionOS.xy + 0.0001);
                float coneSlopeRatio = (_ConeRadiusEnd - _ConeRadiusStart) / _MaxDistance;
                float cosSlope = rsqrt(1.0 + coneSlopeRatio * coneSlopeRatio);
                float sinSlope = coneSlopeRatio * cosSlope;
                float3 coneNormal = float3(-radialDir * cosSlope, sinSlope);

                float3 viewDirN = normalize(input.viewDirOS);
                float viewDotAxis = dot(viewDirN, float3(0, 0, 1));
                float factorNearAxisZ = abs(viewDotAxis);

                float fresnel = dot(coneNormal, viewDirN);
                fresnel = saturate(fresnel);
                fresnel = smoothstep(0, 1, fresnel);

                float fresnelPowSide = lerp(1.0, 4.0, _EdgeFade);
                float fresnelPowFront = 0.3;
                float fresnelPow = lerp(fresnelPowSide, fresnelPowFront, factorNearAxisZ);
                float edgeFadeFactor = pow(fresnel, fresnelPow);

                float frontalBoost = smoothstep(0.5, 1.0, factorNearAxisZ);
                edgeFadeFactor = lerp(edgeFadeFactor, 1.0, frontalBoost * 0.7);

                // === 深度羽化（光柱与模型交叠边界柔和过渡）===
                // 当锥面接近场景物体表面时，逐渐降低透明度避免硬边
                float depthFadeFactor = 1.0;
                {
                    float2 screenUV = input.screenPos.xy / input.screenPos.w;
                    float sceneRawDepth = SampleSceneDepth(screenUV);
                    float sceneEyeDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);
                    float diff = sceneEyeDepth - input.eyeDepth;
                    // diff越小说明越接近物体表面 → alpha越小（淡出）
                    depthFadeFactor = saturate(diff / max(_DepthFadeDistance, 0.001));
                }

                // 整合
                half alpha = _Intensity * endFadeFactor * edgeFadeFactor * depthFadeFactor;
                alpha *= 1.0 - depthRatio * 0.3;

                half4 color = half4(_VolumeColor.rgb, 1.0);

                #if _BLEND_ALPHA
                    color.a = saturate(alpha);
                    color.rgb *= color.a;
                #else
                    color.rgb *= saturate(alpha);
                    color.a = 1.0;
                #endif

                color.rgb = MixFog(color.rgb, input.fogFactor);
                return color;
            }
            ENDHLSL
        }
    }

    // Built-in RP 回退
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SpotLightVolumeBuiltin"
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma shader_feature_local _BLEND_ADDITIVE _BLEND_SOFTADD _BLEND_ALPHA

            #include "UnityCG.cginc"

            // 需要深度贴图
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 posOS : TEXCOORD0;
                float3 viewDirOS : TEXCOORD1;
                float depthRatio : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float eyeDepth : TEXCOORD4;
                UNITY_FOG_COORDS(5)
            };

            half4 _VolumeColor;
            half _Intensity;
            float _StartDistance;
            float _MaxDistance;
            half _EdgeFade;
            half _EndFade;
            half _DepthFadeDistance;
            float _ConeRadiusStart;
            float _ConeRadiusEnd;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.posOS = v.vertex.xyz;
                o.depthRatio = v.uv.x;

                float3 cameraPosOS = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                o.viewDirOS = normalize(v.vertex.xyz - cameraPosOS);

                o.screenPos = ComputeScreenPos(o.pos);
                COMPUTE_EYEDEPTH(o.eyeDepth);

                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float depthRatio = i.depthRatio;
                float depth = depthRatio * _MaxDistance;

                clip(depth - _StartDistance);

                float endFadeFactor = 1.0 - smoothstep(1.0 - _EndFade, 1.0, depthRatio);

                // Fresnel边缘羽化
                float2 radialDir = normalize(i.posOS.xy + 0.0001);
                float coneSlopeRatio = (_ConeRadiusEnd - _ConeRadiusStart) / _MaxDistance;
                float cosSlope = rsqrt(1.0 + coneSlopeRatio * coneSlopeRatio);
                float sinSlope = coneSlopeRatio * cosSlope;
                float3 coneNormal = float3(-radialDir * cosSlope, sinSlope);

                float3 viewDirN = normalize(i.viewDirOS);
                float viewDotAxis = dot(viewDirN, float3(0, 0, 1));
                float factorNearAxisZ = abs(viewDotAxis);

                float fresnel = dot(coneNormal, viewDirN);
                fresnel = saturate(fresnel);
                fresnel = smoothstep(0, 1, fresnel);

                float fresnelPowSide = lerp(1.0, 4.0, _EdgeFade);
                float fresnelPowFront = 0.3;
                float fresnelPow = lerp(fresnelPowSide, fresnelPowFront, factorNearAxisZ);
                float edgeFadeFactor = pow(fresnel, fresnelPow);

                float frontalBoost = smoothstep(0.5, 1.0, factorNearAxisZ);
                edgeFadeFactor = lerp(edgeFadeFactor, 1.0, frontalBoost * 0.7);

                // 深度羽化（光柱与模型交叠边界柔和过渡）
                float depthFadeFactor = 1.0;
                {
                    float2 screenUV = i.screenPos.xy / i.screenPos.w;
                    float sceneRawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screenUV);
                    float sceneEyeDepth = LinearEyeDepth(sceneRawDepth);
                    float diff = sceneEyeDepth - i.eyeDepth;
                    depthFadeFactor = saturate(diff / max(_DepthFadeDistance, 0.001));
                }

                half alpha = _Intensity * endFadeFactor * edgeFadeFactor * depthFadeFactor;
                alpha *= 1.0 - depthRatio * 0.3;

                half4 color = half4(_VolumeColor.rgb, 1.0);

                #if _BLEND_ALPHA
                    color.a = saturate(alpha);
                    color.rgb *= color.a;
                #else
                    color.rgb *= saturate(alpha);
                    color.a = 1.0;
                #endif

                UNITY_APPLY_FOG(i.fogCoord, color);
                return color;
            }
            ENDCG
        }
    }

    FallBack Off
}
