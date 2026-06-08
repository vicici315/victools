// 单帧捕获用内部 Blit 模糊 Shader
// 算法与主材质 TexGaussianBlur_HLSL 完全一致
// 支持质量模式（完整高斯核）和性能模式（Kawase 14次采样）
Shader "Hidden/SingleFrameBlur"
{
    Properties
    {
        _MainTex ("", 2D) = "white" {}
        _BlurSize ("Blur Size", Float) = 4.0
        _PixelSize ("Pixel Size", Float) = 1.0
        _Sigma ("Sigma", Float) = 2.7
        _SampleCount ("Sample Count", Float) = 6
        _UsePerformanceMode ("Performance Mode", Float) = 0
    }
    
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;
            float _BlurSize;
            float _PixelSize;
            float _Sigma;
            float _SampleCount;
            float _UsePerformanceMode;
            float _ResolutionScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv = v.uv;
                return o;
            }

            float GaussianWeight(float r2, float sigma)
            {
                return exp(-r2 / (2.0 * sigma * sigma));
            }

            half4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                // 补偿分辨率缩放：RT 缩小后 texelSize 变大，需乘以 scale 还原
                float2 offset = _MainTex_TexelSize.xy * _BlurSize * _PixelSize * _ResolutionScale;

                if (_UsePerformanceMode > 0.5)
                {
                    // 性能模式：Kawase 风格14次采样（与主材质一致）
                    float2 off1 = offset;
                    float2 off2 = offset * 2.0;
                    float2 off3 = offset * 3.0;
                    
                    half4 color = 0;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * 4.0;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(off1.x, 0)) * 2.0;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(off1.x, 0)) * 2.0;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, off1.y)) * 2.0;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(0, off1.y)) * 2.0;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(off2.x, off2.y)) * 1.0;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(off2.x, off2.y)) * 1.0;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(off2.x, -off2.y)) * 1.0;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(off2.x, -off2.y)) * 1.0;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(off3.x, 0)) * 0.5;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(off3.x, 0)) * 0.5;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, off3.y)) * 0.5;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(0, off3.y)) * 0.5;
                    return color / 18.0;
                }
                else
                {
                    // 质量模式：完整高斯核（与主材质一致）
                    int sampleCount = (int)_SampleCount;
                    half4 accumulatedColor = 0;
                    float totalWeight = 0.0;

                    for (int y = -sampleCount; y <= sampleCount; y++)
                    {
                        for (int x = -sampleCount; x <= sampleCount; x++)
                        {
                            float2 sampleUV = clamp(uv + float2(x, y) * offset, 0.0, 1.0);
                            half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUV);
                            float r2 = float(x * x + y * y);
                            float weight = GaussianWeight(r2, _Sigma);
                            accumulatedColor += color * weight;
                            totalWeight += weight;
                        }
                    }
                    return accumulatedColor / totalWeight;
                }
            }
            ENDHLSL
        }
    }
}
