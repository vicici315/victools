// 雪地痕迹绘制 Shader v2.1 (2026.05.28)
// - 新增矩形画笔SDF距离计算，支持旋转矩形
// v2.0 (2026.05.27)
// - 指数衰减边缘柔和算法
// 支持圆形（胶囊线段）和矩形画笔

Shader "Hidden/SnowPaint"
{
    Properties
    {
        _ExistingTex ("Existing", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ExistingTex);
            SAMPLER(sampler_ExistingTex);

            float4 _BrushPosA;      // xy: 起点 UV
            float4 _BrushPosB;      // xy: 终点 UV
            float _BrushSize;       // 画笔半径/宽度（UV 空间）
            float _BrushStrength;   // 画笔强度
            float _BrushSoftness;   // 边缘柔和度
            float _BrushFeather;    // 羽化度（0=硬边，1=完全羽化）
            float _BrushShape;      // 0 = circle, 1 = rectangle
            float _RectLength;      // 矩形长度（UV 空间）
            float _RectAngle;       // 矩形旋转角度（弧度）

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

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            // 计算点到线段的最短距离（圆形/胶囊画笔）
            float distToSegment(float2 p, float2 a, float2 b)
            {
                float2 ab = b - a;
                float2 ap = p - a;
                float t = saturate(dot(ap, ab) / max(dot(ab, ab), 0.0001));
                float2 closest = a + ab * t;
                return length(p - closest);
            }

            // 计算点到旋转矩形的距离（矩形画笔）
            float distToRect(float2 p, float2 center, float halfWidth, float halfLength, float angle)
            {
                // 将点变换到矩形局部坐标系
                float2 d = p - center;
                float cosA = cos(angle);
                float sinA = sin(angle);
                // 旋转到局部空间（沿角度方向为X轴）
                float2 local = float2(
                    d.x * cosA + d.y * sinA,
                    -d.x * sinA + d.y * cosA
                );
                // 计算到矩形边缘的距离
                float2 q = abs(local) - float2(halfLength, halfWidth);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
            }

            half4 frag(Varyings input) : SV_Target
            {
                half existing = SAMPLE_TEXTURE2D(_ExistingTex, sampler_ExistingTex, input.uv).r;

                float dist;
                float maxDist;

                if (_BrushShape > 0.5)
                {
                    // 矩形画笔
                    float halfWidth = _BrushSize * 0.5;
                    float halfLength = _RectLength * 0.5;
                    dist = distToRect(input.uv, _BrushPosA.xy, halfWidth, halfLength, _RectAngle);
                    maxDist = min(halfWidth, halfLength);
                }
                else
                {
                    // 圆形/胶囊画笔
                    dist = distToSegment(input.uv, _BrushPosA.xy, _BrushPosB.xy);
                    maxDist = _BrushSize;
                }

                // 边缘羽化：feather控制羽化区域比例，softness控制衰减曲线
                // feather=0时只有很窄的过渡，feather=1时整个画笔都是渐变
                float featherStart = 1.0 - max(_BrushFeather, 0.01);
                float normalizedDist = dist / max(maxDist, 0.0001);
                float brush = saturate(1.0 - smoothstep(featherStart, 1.0, normalizedDist));
                
                // softness进一步调整衰减曲线形状
                float softFactor = max(_BrushSoftness * 3.0, 0.01);
                brush = pow(brush, 1.0 / softFactor);

                // 取最大值叠加
                half painted = max(existing, brush * _BrushStrength);

                return half4(painted, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
