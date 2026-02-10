Shader "Custom/Outline"
{
    Properties
    {
        _outline_scale("outline scale", Range(1.0, 1.8)) = 1.1
        _outline_color("outline color", Color) = (0.0, 0.0, 0.0, 0.0)
        [Toggle]_use_object_center("Use Object Center", Float) = 0
        _center_offset("Center Offset", Vector) = (0.0, 0.0, 0.0, 0.0)
        [Toggle]_use_edge_detection("Use Edge Noise", Float) = 0
        _edge_threshold("Edge Threshold", Range(0.0, 1.0)) = 0.7
        _edge_smooth("Edge Smooth", Range(1.0, 3.0)) = 1.1
    }
    
    SubShader
    {
        Tags
        {
            "QUEUE" = "Geometry"
            "RenderType" = "Opaque"
            "DisableBatching" = "False"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
//            "ShaderGraphShader" = "true"
            "ShaderGraphTargetId" = "UniversalUnlitSubTarget"
        }
        
        // Universal Forward Pass
        Pass
        {
            Name "Universal Forward"
            Tags
            {
                "QUEUE" = "Geometry"
                "RenderType" = "Opaque"
                "DisableBatching" = "False"
                "RenderPipeline" = "UniversalPipeline"
                "UniversalMaterialType" = "Unlit"
//                "ShaderGraphShader" = "true"
                "ShaderGraphTargetId" = "UniversalUnlitSubTarget"
            }
            
            Cull Front
            ZWrite On
            ZTest LEqual
            Offset -1, -1 // Offset 0, -1将深度值向前推，确保描边在深度测试中优先于Decal

            
            HLSLPROGRAM
            
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };
            
            CBUFFER_START(UnityPerMaterial)
                float _outline_scale;
                float4 _outline_color;
                float _use_object_center;
                float4 _center_offset;
                float _use_edge_detection;
                float _edge_threshold;
                float _edge_smooth;
            CBUFFER_END
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                // 应用轮廓效果
                float3 outlinePositionOS;
                
                if (_use_object_center > 0.5)
                {
                    // 模式1：使用物体中心作为缩放中心
                    // 以指定的中心点偏移进行缩放
                    float3 center = _center_offset.xyz;
                    outlinePositionOS = center + (input.positionOS.xyz - center) * _outline_scale *0.94;
                }
                else
                {
                    // 模式2：使用顶点法线方向偏移
                    // 沿法线方向偏移顶点位置
                    float3 offset = input.normalOS * (_outline_scale - 1.0) * 0.2;
                    
                    // 如果启用边界检测，则只对边界顶点应用偏移
                    if (_use_edge_detection > 0.5)
                    {
                        // 计算世界空间法线
                        float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                        
                        // 计算世界空间位置
                        float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                        
                        // 计算视角方向（从顶点到相机）
                        // 获取相机位置（在URP中，_WorldSpaceCameraPos是内置变量）
                        float3 cameraPosWS = _WorldSpaceCameraPos;
                        float3 viewDirWS = normalize(cameraPosWS - positionWS);
                        
                        // 计算法线与视角方向的点积
                        float dotNV = dot(normalWS, viewDirWS);
                        
                        // 轮廓边检测：法线与视角方向接近垂直时是轮廓边
                        // 使用阈值来控制检测灵敏度
                        float edgeFactor = 1.4 - abs(dotNV);
                        edgeFactor = smoothstep(_edge_threshold, _edge_smooth, edgeFactor);
                        
                        // 只对检测到的边界应用偏移
                        offset *= edgeFactor;
                    }
                    
                    outlinePositionOS = input.positionOS.xyz + offset;
                }
                
                // 计算世界空间位置
                float3 positionWS = TransformObjectToWorld(outlinePositionOS);
                output.positionWS = positionWS;
                
                // 计算裁剪空间位置
                output.positionCS = TransformWorldToHClip(positionWS);
                
                // 转换法线到世界空间
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // 直接返回轮廓颜色
                return _outline_color;
            }
            
            ENDHLSL
        }
        
    }
    
//    Fallback "Hidden/Universal Render Pipeline/FallbackError"
//    CustomEditor "UnityEditor.ShaderGraph.GenericShaderGraphMaterialGUI"
}
