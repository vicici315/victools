Shader "Custom/AddTrans"
{
    Properties
    {
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        [HDR] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 1
        [Enum(Off,0,On,1)] _ZWrite("ZWrite", Float) = 1
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        
        ZWrite [_ZWrite]
        Cull Back
        Blend [_SrcBlend] [_DstBlend]
        
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            
            HLSLPROGRAM
            
            #pragma vertex vert
            #pragma fragment frag
            
            // SRP Batcher兼容性
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            // 纹理和采样器定义
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            // 材质属性
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _BaseColor;
                // float _SrcBlend;
                // float _DstBlend;
            CBUFFER_END
            
            // 顶点输入结构
            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            // 顶点输出结构
            struct Varyings
            {
                float2 uv           : TEXCOORD0;
                float4 positionCS   : SV_POSITION;
                float4 color        : COLOR;
                
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            
            // 顶点着色器
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                
                return output;
            }
            
            // 片段着色器
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                
                // 采样纹理
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                
                // 应用基础颜色和顶点颜色
                half4 finalColor = texColor * _BaseColor * input.color;
                
                return finalColor;
            }
            
            ENDHLSL
        }
        
    }
    
    FallBack "Hidden/Universal Render Pipeline/Unlit"
}
