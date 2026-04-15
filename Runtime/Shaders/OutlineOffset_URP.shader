// OutlineZOffset1.1 HLSL版本，去除不需要pass
Shader "Custom/Outline/OutlineZOffset"
{
    Properties
    {
        [KeywordEnum(VertexColor,Tangent,UV1,UV2,UV3,UV4)]_OutlineSource ("Source", int) = 0
        // _InTBN: 启用后，平滑法线数据被视为存储在 TBN（切线）空间中，shader 会将其变换回 Object Space 再使用。
        // 关闭时，数据直接当作 Object Space 法线使用，无需变换。
        // 通常配合外部工具（如 Houdini/脚本）烘焙平滑法线到顶点色或 UV 时使用：
        //   - 若烘焙工具输出的是切线空间法线 → 开启
        //   - 若烘焙工具输出的是模型空间法线 → 关闭
        [Toggle(_INTBN)]_InTBN ("Store In TBN Space", float) = 0
        [KeywordEnum(Object,World,View,Clip)]_OutlineSpace ("Space", int) = 0
        _outline_color("outline color", Color) = (0.0, 0.0, 0.0, 0.0)
        _OutlineWidth("Width", Range(0, 0.2)) = 0.02
        _OutlineOffsetFactor ("Offset Factor", float) = 50
        _OutlineOffsetUnits ("Offset Units", float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Cull Front
            Offset [_OutlineOffsetFactor],[_OutlineOffsetUnits]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _OUTLINESOURCE_VERTEXCOLOR _OUTLINESOURCE_TANGENT _OUTLINESOURCE_UV1 _OUTLINESOURCE_UV2 _OUTLINESOURCE_UV3 _OUTLINESOURCE_UV4
            #pragma shader_feature_local _INTBN
            #pragma shader_feature_local _OUTLINESPACE_OBJECT _OUTLINESPACE_WORLD _OUTLINESPACE_VIEW _OUTLINESPACE_CLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _OutlineWidth;
                float4 _outline_color;
                float _OutlineOffsetFactor;
                float _OutlineOffsetUnits;
            CBUFFER_END

            float3 UnpackNormalRG(float2 packedNormal)
            {
                float3 n;
                n.xy = packedNormal * 2.0 - 1.0;
                n.z  = sqrt(1.0 - saturate(dot(n.xy, n.xy)));
                return n;
            }

            float3 GetSmoothNormalOS(float3 source, float3x3 OtoT)
            {
            #if defined(_OUTLINESOURCE_TANGENT)
                return source;
            #else
                #if defined(_INTBN)
                    #if defined(_OUTLINESOURCE_VERTEXCOLOR)
                        return normalize(mul(source, OtoT));
                    #else
                        float3 smoothNormalTS = UnpackNormalRG(source.rg);
                        return normalize(mul(smoothNormalTS, OtoT));
                    #endif
                #else
                    return source;
                #endif
            #endif
            }

            float4 ExpandAlongNormal(float4 positionOS, float3 normalOS)
            {
                float4 positionCS = (float4)0;
            #if defined(_OUTLINESPACE_OBJECT)
                positionOS.xyz += normalOS * _OutlineWidth;
                positionCS = TransformObjectToHClip(positionOS.xyz);
            #elif defined(_OUTLINESPACE_WORLD)
                float3 positionWS = TransformObjectToWorld(positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(normalOS);
                positionWS       += normalWS * _OutlineWidth;
                positionCS        = TransformWorldToHClip(positionWS);
            #elif defined(_OUTLINESPACE_VIEW)
                float4 positionVS = mul(UNITY_MATRIX_MV, positionOS);
                float3 normalVS   = mul((float3x3)UNITY_MATRIX_IT_MV, normalOS);
                positionVS.xy    += normalize(normalVS).xy * _OutlineWidth;
                positionCS        = mul(UNITY_MATRIX_P, positionVS);
            #elif defined(_OUTLINESPACE_CLIP)
                positionCS = TransformObjectToHClip(positionOS.xyz);
                float3 normalCS  = mul((float3x3)UNITY_MATRIX_MVP, normalOS);
                float2 screenOff = normalize(normalCS.xy) * (_ScreenParams.zw - 1.0) * positionCS.w;
                positionCS.xy   += screenOff * max(_ScreenParams.x, _ScreenParams.y) * _OutlineWidth * positionCS.z;
            #else
                positionOS.xyz += normalOS * _OutlineWidth;
                positionCS = TransformObjectToHClip(positionOS.xyz);
            #endif
                return positionCS;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
            #if defined(_OUTLINESOURCE_VERTEXCOLOR)
                float4 color      : COLOR;
            #elif defined(_OUTLINESOURCE_UV1)
                float3 uv1        : TEXCOORD1;
            #elif defined(_OUTLINESOURCE_UV2)
                float3 uv2        : TEXCOORD2;
            #elif defined(_OUTLINESOURCE_UV3)
                float3 uv3        : TEXCOORD3;
            #elif defined(_OUTLINESOURCE_UV4)
                float3 uv4        : TEXCOORD4;
            #endif
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 bitangentOS = cross(IN.normalOS, IN.tangentOS.xyz) * IN.tangentOS.w;
                float3x3 OtoT = float3x3(IN.tangentOS.xyz, bitangentOS, IN.normalOS);

                float3 smoothNormalOS = IN.normalOS;
            #if defined(_OUTLINESOURCE_VERTEXCOLOR)
                smoothNormalOS = GetSmoothNormalOS(IN.color.xyz, OtoT);
            #elif defined(_OUTLINESOURCE_TANGENT)
                smoothNormalOS = GetSmoothNormalOS(IN.tangentOS.xyz, OtoT);
            #elif defined(_OUTLINESOURCE_UV1)
                smoothNormalOS = GetSmoothNormalOS(IN.uv1, OtoT);
            #elif defined(_OUTLINESOURCE_UV2)
                smoothNormalOS = GetSmoothNormalOS(IN.uv2, OtoT);
            #elif defined(_OUTLINESOURCE_UV3)
                smoothNormalOS = GetSmoothNormalOS(IN.uv3, OtoT);
            #elif defined(_OUTLINESOURCE_UV4)
                smoothNormalOS = GetSmoothNormalOS(IN.uv4, OtoT);
            #endif

                OUT.positionCS = ExpandAlongNormal(IN.positionOS, smoothNormalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_TARGET
            {
                return _outline_color;
            }

            ENDHLSL
        }
    }
}
