// Grass.shader
// =============================================================================
// Grass 1.1 添加基础贴图纹理，控制草地根部颜色，A通道控制草生长区域
// Grass 1.2 DisableBatching防止打包后batching改变坐标空间；A通道控制边缘草宽度与动态约束；添加最小宽度属性
// Grass 1.3 前倾量改为角度旋转；风/弯曲按t2渐进混入根部锚定；从法线构建正交坐标系不依赖模型切线
// Grass 1.4 替换rand为frac-dot哈希避免half精度溢出；安全windDir处理；强制float精度；Domain Shader归一化法线切线
// Grass 1.5 优化（弯曲度、风力强度）参数的控制；添加（颜色倾向）参数控制使用纯色或贴图颜色
// Grass 1.6 添加草体段数设置，可以削去尖角；添加草根部下沉参数用于隐藏根本拉扯，贴图使用half精度
Shader "Custom/Grass"
{
    Properties
    {
        [Header(Tessellation)]
        _TessellationUniform("Tessellation Uniform", Range(1, 64)) = 1
        [Header(Shading)]
        _TopColor("Top Color", Color) = (0.45, 0.86, 0.17, 1)
        _BottomColor("Bottom Color", Color) = (0.02, 0.25, 0.08, 1)
        _ColorBias("Color Bias", Range(0, 1)) = 0.4
        _BaseMap("Grass Color Map (RGB=Color, A=Aspect)", 2D) = "white" {}
        _AlphaCutoff("Alpha Cutoff (A < this = no grass)", Range(0, 1)) = 0.1
        _BladeMinHeight("Blade Min Height (below = cull)", Range(0, 1)) = 0.05
        _TranslucentGain("Translucent Gain", Range(0, 1)) = 0.6

        [Header(Blade)]
        _BladeWidth("Blade Width", Range(0.0001, 1)) = 0.1
        _BladeWidthRandom("Blade Width Random", Range(0, 1)) = 0.05
        _BladeMinWidth("Blade Min Width", Range(0.0, 1.0)) = 0.04
        _BladeHeight("Blade Height", Float) = 0.4
        _BladeHeightRandom("Blade Height Random", Float) = 0.3
        _BladeForward("Blade Forward Amount", Float) = 0.8
        _BladeCurve("Blade Curvature Amount", Range(0, 1)) = 0.5
        _BladeSegments("Blade Segments", Range(1, 3)) = 3
        _BendRotationRandom("Bend Rotation Random", Range(0, 1)) = 0.5
        _BladeRootSink("Blade Root Sink", Range(0, 1)) = 0.02

        [Header(Wind)]
        _WindDistortionMap("Wind Distortion Map", 2D) = "blue" {}
        _WindFrequency("Wind Frequency", Vector) = (0.15, 0.25, 0, 0)
        _WindStrength("Wind Strength", Float) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "DisableBatching" = "True"  //添加 "DisableBatching" = "True" 后，Unity 不会对使用该 shader 的物体进行 batching，顶点始终保持在对象空间，几何着色器的计算就能正确工作了
        }

        Cull Off

        HLSLINCLUDE
        #pragma warning (disable : 3205)
        #define PREFER_HALF 0

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

        #define BLADE_SEGMENTS 3
        #define UNITY_PI 3.14159265359
        #define UNITY_TWO_PI 6.28318530718

        CBUFFER_START(UnityPerMaterial)
            float4 _TopColor;
            float4 _BottomColor;
            float4 _BaseMap_ST;
            float4 _WindDistortionMap_ST;
            float4 _WindFrequency; // xy used, zw padding

            // 第6个 float4 槽位 (对齐到 16 字节边界)
            float _TessellationUniform;
            float _AlphaCutoff;
            float _TranslucentGain;
            float _BladeHeight;

            // 第7个 float4 槽位
            float _BladeHeightRandom;
            float _BladeMinHeight;
            float _BladeWidth;
            float _BladeWidthRandom;

            // 第8个 float4 槽位
            float _BladeMinWidth;
            float _BladeForward;
            float _BladeCurve;
            float _BladeSegments;

            // 第9个 float4 槽位
            float _BendRotationRandom;
            float _WindStrength;
            float _ColorBias;
            float _BladeRootSink;
        CBUFFER_END

        TEXTURE2D(_WindDistortionMap);
        SAMPLER(sampler_WindDistortionMap);
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        #include "CustomTessellation.hlsl"

        float rand(float3 co)
        {
            float3 p = frac(co * float3(443.8975, 397.2973, 491.1871));
            p += dot(p, p.yzx + 19.19);
            return frac((p.x + p.y) * p.z);
        }

        float3x3 AngleAxis3x3(float angle, float3 axis)
        {
            float c, s;
            sincos(angle, s, c);
            float t = 1 - c;
            float x = axis.x;
            float y = axis.y;
            float z = axis.z;
            return float3x3(
                t * x * x + c,      t * x * y - s * z,  t * x * z + s * y,
                t * x * y + s * z,  t * y * y + c,      t * y * z - s * x,
                t * x * z - s * y,  t * y * z + s * x,  t * z * z + c
            );
        }

        struct geometryOutput
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 worldPos : TEXCOORD1;
            float3 normal : TEXCOORD2;
            float4 grassColor : TEXCOORD3;
        };

        geometryOutput VertexOutput(float3 pos, float2 uv, float3 normal, float4 grassColor)
        {
            geometryOutput o;
            o.pos = TransformObjectToHClip(pos);
            o.worldPos = TransformObjectToWorld(pos);
            o.uv = uv;
            o.normal = TransformObjectToWorldNormal(normal);
            o.grassColor = grassColor;
            return o;
        }

        // 生成草叶顶点 — forward 已融入弯曲角度，不再作为位移分量
        geometryOutput GenerateGrassVertex(float3 vertexPosition, float width, float height, float2 uv, float3x3 transformMatrix, float3 windOffset, float4 grassColor)
        {
            float3 tangentPoint = float3(width, 0, height);
            float3 tangentNormal = float3(0, -1, 0);
            float3 localNormal = mul(transformMatrix, tangentNormal);
            float3 localPosition = vertexPosition + mul(transformMatrix, tangentPoint) + windOffset;
            return VertexOutput(localPosition, uv, localNormal, grassColor);
        }

        [maxvertexcount(BLADE_SEGMENTS * 2 + 3)]
        void geo(triangle vertexOutput IN[3], inout TriangleStream<geometryOutput> triStream)
        {
            // 使用三角形重心位置，避免只取IN[0]导致相邻三角形草叶位置跳变
            float3 pos = (IN[0].vertex.xyz + IN[1].vertex.xyz + IN[2].vertex.xyz) / 3.0;
            float3 vNormal = normalize(IN[0].normal + IN[1].normal + IN[2].normal);

            // 根部下沉：沿法线反方向偏移，让拉扯隐于地面下
            pos -= vNormal * _BladeRootSink;

            float2 meshUV = (IN[0].uv + IN[1].uv + IN[2].uv) / 3.0;

            float3 refDir = abs(vNormal.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
            float3 vTangentDir = normalize(cross(refDir, vNormal));
            float3 vBinormal = cross(vNormal, vTangentDir);

            // 采样颜色纹理
            float2 colorUV = meshUV * _BaseMap_ST.xy + _BaseMap_ST.zw;
            half4 grassColorSample = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, colorUV, 0);

            // 用 bool 控制是否生成草叶，避免提前 return 跳过 RestartStrip
            bool shouldGenerate = (grassColorSample.a >= _AlphaCutoff);

            float3x3 tangentToLocal = float3x3(
                vTangentDir.x, vBinormal.x, vNormal.x,
                vTangentDir.y, vBinormal.y, vNormal.y,
                vTangentDir.z, vBinormal.z, vNormal.z
            );

            float facingAngle = rand(pos) * UNITY_TWO_PI * _BendRotationRandom;
            float3x3 facingRotationMatrix = AngleAxis3x3(facingAngle, float3(0, 0, 1));

            float growFactor = saturate((grassColorSample.a - _AlphaCutoff) / max(1.0 - _AlphaCutoff, 0.001));
            float dynamicAtten = lerp(0.2, 1.0, growFactor);

            float forwardAngle = rand(pos.yyz) * _BladeForward * UNITY_PI * 0.25 * dynamicAtten;
            float bendAngle = rand(pos.zzx) * _BladeCurve * UNITY_PI * 0.15 * dynamicAtten;
            float totalBendAngle = forwardAngle + bendAngle;

            // Wind
            float2 windUV = pos.xz * _WindDistortionMap_ST.xy + _WindDistortionMap_ST.zw + _WindFrequency.xy * _Time.y;
            float2 windSample = (SAMPLE_TEXTURE2D_LOD(_WindDistortionMap, sampler_WindDistortionMap, windUV, 0).xy * 2 - 1) * _WindStrength * dynamicAtten;

            float3x3 baseTransform = mul(tangentToLocal, facingRotationMatrix);

            float height = max(((rand(pos.zyx) * 2 - 1) * _BladeHeightRandom + _BladeHeight) * growFactor, 0);
            shouldGenerate = shouldGenerate && (height >= _BladeMinHeight);

            float width = max(((rand(pos.xzy) * 2 - 1) * _BladeWidthRandom + _BladeWidth) * growFactor, _BladeMinWidth);

            // 宽高比安全剔除
            shouldGenerate = shouldGenerate && (width <= height * 2.0);

            if (shouldGenerate)
            {
                float heightScale = saturate(height / max(_BladeHeight, 0.01));
                float3 windVec = float3(windSample.x, 0, windSample.y) * heightScale;
                totalBendAngle *= heightScale;

                half4 grassColor = half4(grassColorSample.rgb, 1);

                int segments = clamp((int)round(_BladeSegments), 1, BLADE_SEGMENTS);
                int layerCount = segments < BLADE_SEGMENTS ? (segments + 1) : BLADE_SEGMENTS;

                [unroll]
                for (int i = 0; i < BLADE_SEGMENTS; i++)
                {
                    if (i >= layerCount) break;

                    float t = i / (float)BLADE_SEGMENTS;
                    float segmentHeight = height * t;
                    float segmentWidth = width * (1 - t);

                    float tSq = i > 0 ? pow(t, 0.4) : 0.0;

                    float3x3 segBendRot = AngleAxis3x3(totalBendAngle * tSq, float3(1, 0, 0));
                    float3x3 transformMatrix = mul(baseTransform, segBendRot);
                    float3 segWindOffset = windVec * t * t;

                    triStream.Append(GenerateGrassVertex(pos, segmentWidth, segmentHeight, float2(0, t), transformMatrix, segWindOffset, grassColor));
                    triStream.Append(GenerateGrassVertex(pos, -segmentWidth, segmentHeight, float2(1, t), transformMatrix, segWindOffset, grassColor));
                }

                if (segments >= 3)
                {
                    float3x3 tipBendRot = AngleAxis3x3(totalBendAngle, float3(1, 0, 0));
                    float3x3 tipTransform = mul(baseTransform, tipBendRot);
                    float3 tipWindOffset = windVec;
                    triStream.Append(GenerateGrassVertex(pos, 0, height, float2(0.5, 1), tipTransform, tipWindOffset, grassColor));
                }
            }

            // 无论是否生成草叶，始终 RestartStrip 确保每棵草的 strip 完全隔离
            triStream.RestartStrip();
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma hull hull
            #pragma domain domain
            #pragma geometry geo
            #pragma fragment frag
            #pragma target 4.6

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            half4 frag(geometryOutput i, half facing : VFACE) : SV_Target
            {
                float3 normal = facing > 0 ? i.normal : -i.normal;
                float4 shadowCoord = TransformWorldToShadowCoord(i.worldPos);
                Light mainLight = GetMainLight(shadowCoord);
                float shadow = mainLight.shadowAttenuation;
                float NdotL = saturate(saturate(dot(normal, mainLight.direction)) + _TranslucentGain) * shadow;
                float3 ambient = SampleSH(normal);
                float4 lightIntensity = NdotL * float4(mainLight.color, 1) + float4(ambient, 1);
                // _ColorBias: 0=纯TopColor, 1=贴图颜色影响
                float4 topCol = lerp(_TopColor, i.grassColor * _TopColor, _ColorBias);
                float4 botCol = lerp(_BottomColor, i.grassColor * _BottomColor, _ColorBias);
                float4 col = lerp(botCol, topCol * lightIntensity, i.uv.y);
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma hull hull
            #pragma domain domain
            #pragma geometry geo
            #pragma fragment fragShadow
            #pragma target 4.6

            half4 fragShadow(geometryOutput i) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    CustomEditor "GrassGUI"
}
