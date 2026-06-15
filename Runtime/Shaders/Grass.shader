// Grass.shader
// =============================================================================
// Grass 1.1 添加基础贴图纹理，控制草地根部颜色，A通道控制草生长区域
// Grass 1.2 DisableBatching防止打包后batching改变坐标空间；A通道控制边缘草宽度与动态约束；添加最小宽度属性
// Grass 1.3 前倾量改为角度旋转；风/弯曲按t2渐进混入根部锚定；从法线构建正交坐标系不依赖模型切线
// Grass 1.4 替换rand为frac-dot哈希避免half精度溢出；安全windDir处理；强制float精度；Domain Shader归一化法线切线
// Grass 1.5 优化（弯曲度、风力强度）参数的控制；添加（颜色倾向）参数控制使用纯色或贴图颜色
// Grass 1.6 添加草体段数设置，可以削去尖角；添加草根部下沉参数用于隐藏根本拉扯，贴图使用half精度
// Grass 1.7 添加草体透贴纹理参数，UV使用两段草体高度平展，适用于1~2段草体，添加剔除方式选项
    // 当"使用草体贴图"开启且段数 ≤ 2 时：
    // 草体宽度保持恒定（不随高度收窄），几何体变成矩形而非梯形
    // UV 完整平铺 0~1 矩形，不会有任何扭曲
    // 3 段草体的尖角顶点被排除（因为单个三角面无法合理平铺矩形 UV）
// Grass 1.8 添加超距离剔除：在Hull Shader阶段按摄像机距离线性衰减细分因子，超出距离后细分为0不生成草叶
// Grass 2.0 添加草地交互系统，添加草地控制器脚本
// Grass 2.1 添加DepthOnly pass写入深度纹理，修复SpotLightVolume无法被草遮挡的问题；ForwardLit显式ZWrite On；ShadowCaster支持overlay alpha clip

Shader "Custom/Grass"
{
    Properties
    {
        [Header(Tessellation)]
        _TessellationUniform("Tessellation Uniform", Range(1, 64)) = 1
        [Header(Shading)]
        _TopColor("Top Color", Color) = (0.45, 0.86, 0.17, 1)
        _BottomColor("Bottom Color", Color) = (0.02, 0.25, 0.08, 1)
        _GradientOffset("Gradient Offset", Range(-1, 1)) = 0
        _ColorBias("Color Bias", Range(0, 1)) = 0.4
        _BaseMap("Grass Color Map (RGB=Color, A=Aspect)", 2D) = "white" {}
        _AlphaCutoff("Alpha Cutoff (A < this = no grass)", Range(0, 1)) = 0.1
        _BladeMinHeight("Blade Min Height (below = cull)", Range(0, 1)) = 0.05
        _ShadowScale("Shadow Scale", Range(0, 1)) = 0.5

        [Header(Blade)]
        _BladeWidth("Blade Width", Range(0.0001, 1)) = 0.1
        _BladeBottomWidth("Blade Bottom Width", Range(0.0, 1.0)) = 0.1
        _BladeWidthRandom("Blade Width Random", Range(0, 1)) = 0.05
        _BladeMinWidth("Blade Min Width", Range(0.0, 1.0)) = 0.04
        _BladeHeight("Blade Height", Float) = 0.4
        _BladeHeightRandom("Blade Height Random", Float) = 0.3
        _BladeForward("Blade Forward Amount", Float) = 0.8
        _BladeCurve("Blade Curvature Amount", Range(0, 1)) = 0.5
        _BladeSegments("Blade Segments", Range(1, 3)) = 3
        _BendRotationRandom("Bend Rotation Random", Range(0, 1)) = 0.5
        _BladeRootSink("Blade Root Sink", Range(0, 1)) = 0.02

        [Header(Blade Overlay)]
        [Toggle(_BLADE_OVERLAY_ON)] _UseBladeOverlay("Use Blade Overlay (使用草体贴图)", Float) = 0
        _BladeOverlayTex("Blade Overlay Texture (草体透贴)", 2D) = "white" {}
        _BladeOverlayIntensity("Blade Overlay Color Intensity (纹理强度)", Range(0, 1)) = 1.0
        _BladeOverlayAlphaClip("Blade Overlay Alpha Clip", Range(0, 1)) = 0.5
        [Toggle] _UseBillboard("Use Billboard (使用公告板)", Float) = 0

        [Header(Wind)]
        _WindDistortionMap("Wind Distortion Map", 2D) = "blue" {}
        _WindFrequency("Wind Frequency", Vector) = (0.15, 0.25, 0, 0)
        _WindStrength("Wind Strength", Float) = 0.5

        [Header(Distance Culling)]
        _GrassFadeStart("Grass Fade Start Distance", Float) = 0
        _GrassFadeEnd("Grass Fade End Distance", Float) = 20

        [HideInInspector] _Cull("Cull Mode", Float) = 0
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

        Cull [_Cull]

        HLSLINCLUDE
        #pragma warning (disable : 3205)
        #define PREFER_HALF 0
        #pragma shader_feature_local _BLADE_OVERLAY_ON

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
            float _ShadowScale;
            float _BladeHeight;

            // 第7个 float4 槽位
            float _BladeHeightRandom;
            float _BladeMinHeight;
            float _BladeWidth;
            float _BladeWidthRandom;

            // 第8个 float4 槽位
            float _BladeBottomWidth;
            float _BladeMinWidth;
            float _BladeForward;
            float _BladeCurve;

            // 第9个 float4 槽位
            float _BladeSegments;
            float _BendRotationRandom;
            float _WindStrength;
            float _ColorBias;

            // 第10个 float4 槽位
            float _BladeRootSink;
            float _GrassFadeStart;
            float _GrassFadeEnd;
            float _GradientOffset;
            float _UseBillboard;

            // 第11个 float4 槽位
            float4 _BladeOverlayTex_ST;
            float _BladeOverlayIntensity;
            float _BladeOverlayAlphaClip;
            float _UseBladeOverlay;
        CBUFFER_END

        TEXTURE2D(_WindDistortionMap);
        SAMPLER(sampler_WindDistortionMap);
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BladeOverlayTex);
        SAMPLER(sampler_BladeOverlayTex);

        // ─── 草地交互全局参数 ───
        #define GRASS_MAX_INTERACTORS 4
        float _GrassInteractionCount;
        float _GrassInteractionStrength;
        float4 _GrassInteractionData[GRASS_MAX_INTERACTORS]; // xyz=worldPos, w=radius

        // 计算交互偏移：将草叶从交互点推开（返回世界空间偏移）
        // 当交互点高度远离草地表面时，影响自动衰减为0
        float3 ComputeInteractionOffset(float3 worldPos, float t)
        {
            float3 totalOffset = float3(0, 0, 0);
            int count = clamp((int)_GrassInteractionCount, 0, GRASS_MAX_INTERACTORS);
            if (count <= 0 || _GrassInteractionStrength <= 0) return totalOffset;

            for (int i = 0; i < GRASS_MAX_INTERACTORS; i++)
            {
                if (i >= count) break;
                float3 interactorPos = _GrassInteractionData[i].xyz;
                float radius = _GrassInteractionData[i].w;
                if (radius <= 0) continue;

                float3 diff = worldPos - interactorPos;
                float dist = length(diff.xz); // XZ平面距离

                // 高度衰减：交互点与草地的垂直距离超过半径时不产生影响
                float heightDiff = abs(diff.y);
                float heightFade = saturate(1.0 - heightDiff / radius);

                float influence = saturate(1.0 - dist / radius);
                influence = influence * influence * heightFade; // 平滑衰减 + 高度衰减

                // 推开方向（XZ平面）+ 向下压
                float3 pushDir = float3(0, 0, 0);
                if (dist > 0.001)
                    pushDir = normalize(float3(diff.x, 0, diff.z));

                totalOffset += (pushDir * influence - float3(0, influence * 0.5, 0)) * _GrassInteractionStrength * t * t;
            }
            return totalOffset;
        }

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
            #ifdef _BLADE_OVERLAY_ON
            float2 overlayUV : TEXCOORD4;
            #endif
        };

        geometryOutput VertexOutput(float3 pos, float2 uv, float3 normal, float4 grassColor, float2 overlayUV)
        {
            geometryOutput o;
            o.pos = TransformObjectToHClip(pos);
            o.worldPos = TransformObjectToWorld(pos);
            o.uv = uv;
            o.normal = TransformObjectToWorldNormal(normal);
            o.grassColor = grassColor;
            #ifdef _BLADE_OVERLAY_ON
            o.overlayUV = overlayUV;
            #endif
            return o;
        }

        // 生成草叶顶点 — forward 已融入弯曲角度，不再作为位移分量
        geometryOutput GenerateGrassVertex(float3 vertexPosition, float width, float height, float2 uv, float3x3 transformMatrix, float3 windOffset, float4 grassColor, float2 overlayUV)
        {
            float3 tangentPoint = float3(width, 0, height);
            float3 tangentNormal = float3(0, -1, 0);
            float3 localNormal = mul(transformMatrix, tangentNormal);
            float3 localPosition = vertexPosition + mul(transformMatrix, tangentPoint) + windOffset;
            return VertexOutput(localPosition, uv, localNormal, grassColor, overlayUV);
        }

        // overlay变体：最多2段=6顶点；普通变体：3段+尖角=9顶点
        #ifdef _BLADE_OVERLAY_ON
        [maxvertexcount(6)]
        #else
        [maxvertexcount(BLADE_SEGMENTS * 2 + 3)]
        #endif
        void geo(triangle vertexOutput IN[3], inout TriangleStream<geometryOutput> triStream)
        {
            // 使用三角形重心位置，避免只取IN[0]导致相邻三角形草叶位置跳变
            float3 pos = (IN[0].vertex.xyz + IN[1].vertex.xyz + IN[2].vertex.xyz) / 3.0;

            // 距离剔除：超出 FadeEnd 直接跳过，不生成任何草叶
            float3 worldPosCheck = TransformObjectToWorld(pos);
            float camDist = distance(worldPosCheck, GetCameraPositionWS());
            if (camDist > _GrassFadeEnd)
            {
                triStream.RestartStrip();
                return;
            }
            // 距离衰减因子用于降低草叶高度，实现渐隐效果
            float distFade = saturate(1.0 - (camDist - _GrassFadeStart) / max(_GrassFadeEnd - _GrassFadeStart, 0.001));

            float3 vNormal = normalize(IN[0].normal + IN[1].normal + IN[2].normal);

            // 根部下沉：沿法线反方向偏移，让拉扯隐于地面下
            pos -= vNormal * _BladeRootSink;

            float2 meshUV = (IN[0].uv + IN[1].uv + IN[2].uv) / 3.0;

            // 采样颜色纹理
            float2 colorUV = meshUV * _BaseMap_ST.xy + _BaseMap_ST.zw;
            half4 grassColorSample = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, colorUV, 0);

            // 用 bool 控制是否生成草叶，避免提前 return 跳过 RestartStrip
            bool shouldGenerate = (grassColorSample.a >= _AlphaCutoff);

            float growFactor = saturate((grassColorSample.a - _AlphaCutoff) / max(1.0 - _AlphaCutoff, 0.001));
            float dynamicAtten = lerp(0.2, 1.0, growFactor);

            float height = max(((rand(pos.zyx) * 2 - 1) * _BladeHeightRandom + _BladeHeight) * growFactor * distFade, 0);
            shouldGenerate = shouldGenerate && (height >= _BladeMinHeight);

            float widthRand = (rand(pos.xzy) * 2 - 1) * _BladeWidthRandom;
            float width = max((_BladeWidth + widthRand) * growFactor, _BladeMinWidth);
            float bottomWidth = max((_BladeBottomWidth + widthRand) * growFactor, _BladeMinWidth);

            // 宽高比安全剔除
            shouldGenerate = shouldGenerate && (width <= height * 2.0);

            if (shouldGenerate)
            {
                half4 grassColor = half4(grassColorSample.rgb, 1);

                #ifdef _BLADE_OVERLAY_ON
                // overlay变体：矩形等宽草叶或公告板，最多2段，无尖角
                float2 windUV = pos.xz * _WindDistortionMap_ST.xy + _WindDistortionMap_ST.zw + _WindFrequency.xy * _Time.y;
                float2 windSample = (SAMPLE_TEXTURE2D_LOD(_WindDistortionMap, sampler_WindDistortionMap, windUV, 0).xy * 2 - 1) * _WindStrength * dynamicAtten;
                float heightScale = saturate(height / max(_BladeHeight, 0.01));
                float3 windVec = float3(windSample.x, 0, windSample.y) * heightScale;

                // 计算朝向：公告板面向摄像机，否则随机朝向
                float3 rightDir;
                float3 faceNormal;
                if (_UseBillboard > 0.5)
                {
                    float3 worldPos = TransformObjectToWorld(pos);
                    float3 toCamera = GetCameraPositionWS() - worldPos;
                    toCamera.y = 0;
                    toCamera = normalize(toCamera);
                    // 反转rightDir方向，使三角形绕序在背面剔除时正确朝向摄像机
                    rightDir = -normalize(cross(float3(0, 1, 0), toCamera)) * width;
                    faceNormal = toCamera;
                }
                else
                {
                    float facingAngle = rand(pos) * UNITY_TWO_PI * _BendRotationRandom;
                    rightDir = float3(cos(facingAngle), 0, sin(facingAngle)) * width;
                    faceNormal = float3(-sin(facingAngle), 0, cos(facingAngle));
                }

                int segments = min(clamp((int)round(_BladeSegments), 1, BLADE_SEGMENTS), 2);
                int layerCount = segments + 1;
                float maxT = (float)segments / (float)BLADE_SEGMENTS;

                // 交互偏移：公告板模式下禁用（避免shader编译问题）
                float3 maxInteractOffset = float3(0, 0, 0);
                if (_UseBillboard < 0.5)
                {
                    float3 grassWorldPos2 = TransformObjectToWorld(pos);
                    maxInteractOffset = ComputeInteractionOffset(grassWorldPos2, 1.0);
                }

                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    if (i >= layerCount) break;
                    float t = i / (float)BLADE_SEGMENTS;
                    float segmentHeight = height * t;
                    float3 segWindOffset = windVec * t * t;
                    float colorV = saturate(t / maxT);

                    float3 upOffset = float3(0, segmentHeight, 0);
                    float3 localL = pos + rightDir + upOffset + segWindOffset + maxInteractOffset * t * t;
                    float3 localR = pos - rightDir + upOffset + segWindOffset + maxInteractOffset * t * t;

                    geometryOutput oL;
                    oL.pos = TransformObjectToHClip(localL);
                    oL.worldPos = TransformObjectToWorld(localL);
                    oL.uv = float2(0, colorV);
                    oL.normal = TransformObjectToWorldNormal(faceNormal);
                    oL.grassColor = grassColor;
                    oL.overlayUV = float2(0, colorV);
                    triStream.Append(oL);

                    geometryOutput oR;
                    oR.pos = TransformObjectToHClip(localR);
                    oR.worldPos = TransformObjectToWorld(localR);
                    oR.uv = float2(1, colorV);
                    oR.normal = TransformObjectToWorldNormal(faceNormal);
                    oR.grassColor = grassColor;
                    oR.overlayUV = float2(1, colorV);
                    triStream.Append(oR);
                }

                #else
                // 普通变体：多段弯曲草叶
                float3 refDir = abs(vNormal.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 vTangentDir = normalize(cross(refDir, vNormal));
                float3 vBinormal = cross(vNormal, vTangentDir);

                float3x3 tangentToLocal = float3x3(
                    vTangentDir.x, vBinormal.x, vNormal.x,
                    vTangentDir.y, vBinormal.y, vNormal.y,
                    vTangentDir.z, vBinormal.z, vNormal.z
                );

                float facingAngle = rand(pos) * UNITY_TWO_PI * _BendRotationRandom;
                float3x3 facingRotationMatrix = AngleAxis3x3(facingAngle, float3(0, 0, 1));

                float forwardAngle = rand(pos.yyz) * _BladeForward * UNITY_PI * 0.25 * dynamicAtten;
                float bendAngle = rand(pos.zzx) * _BladeCurve * UNITY_PI * 0.15 * dynamicAtten;
                float totalBendAngle = forwardAngle + bendAngle;

                float2 windUV = pos.xz * _WindDistortionMap_ST.xy + _WindDistortionMap_ST.zw + _WindFrequency.xy * _Time.y;
                float2 windSample = (SAMPLE_TEXTURE2D_LOD(_WindDistortionMap, sampler_WindDistortionMap, windUV, 0).xy * 2 - 1) * _WindStrength * dynamicAtten;

                float3x3 baseTransform = mul(tangentToLocal, facingRotationMatrix);

                float heightScale = saturate(height / max(_BladeHeight, 0.01));
                float3 windVec = float3(windSample.x, 0, windSample.y) * heightScale;
                totalBendAngle *= heightScale;

                // 计算交互偏移（一次计算，按高度比例应用）
                float3 grassWorldPos = TransformObjectToWorld(pos);
                float3 maxInteractOffset = ComputeInteractionOffset(grassWorldPos, 1.0);

                int segments = clamp((int)round(_BladeSegments), 1, BLADE_SEGMENTS);
                int layerCount = segments < BLADE_SEGMENTS ? (segments + 1) : BLADE_SEGMENTS;
                float maxT = (segments >= 3) ? 1.0 : (float)segments / (float)BLADE_SEGMENTS;

                [unroll]
                for (int i = 0; i < BLADE_SEGMENTS; i++)
                {
                    if (i >= layerCount) break;

                    float t = i / (float)BLADE_SEGMENTS;
                    float segmentHeight = height * t;
                    // 底部宽度独立控制：t=0 为 bottomWidth，t>0 从 width 线性收窄到 0
                    float segmentWidth = (i == 0) ? bottomWidth : width * (1 - t);
                    float tSq = i > 0 ? pow(t, 0.4) : 0.0;

                    float3x3 segBendRot = AngleAxis3x3(totalBendAngle * tSq, float3(1, 0, 0));
                    float3x3 transformMatrix = mul(baseTransform, segBendRot);
                    float3 segWindOffset = windVec * t * t;

                    // 交互偏移按 t*t 比例应用
                    float3 interactOffset = maxInteractOffset * t * t;

                    float colorV = saturate(t / maxT);

                    triStream.Append(GenerateGrassVertex(pos + interactOffset, segmentWidth, segmentHeight, float2(0, colorV), transformMatrix, segWindOffset, grassColor, float2(0, 0)));
                    triStream.Append(GenerateGrassVertex(pos + interactOffset, -segmentWidth, segmentHeight, float2(1, colorV), transformMatrix, segWindOffset, grassColor, float2(0, 0)));
                }

                if (segments >= 3)
                {
                    float3x3 tipBendRot = AngleAxis3x3(totalBendAngle, float3(1, 0, 0));
                    float3x3 tipTransform = mul(baseTransform, tipBendRot);
                    float3 tipWindOffset = windVec;
                    triStream.Append(GenerateGrassVertex(pos + maxInteractOffset, 0, height, float2(0.5, 1), tipTransform, tipWindOffset, grassColor, float2(0, 0)));
                }
                #endif
            }

            // 无论是否生成草叶，始终 RestartStrip 确保每棵草的 strip 完全隔离
            triStream.RestartStrip();
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On

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
                // 草叶为薄片，正反面使用相同法线计算光照，保持亮度一致
                float3 normal = i.normal;
                float4 shadowCoord = TransformWorldToShadowCoord(i.worldPos);
                Light mainLight = GetMainLight(shadowCoord);
                float shadow = mainLight.shadowAttenuation;
                float NdotL = saturate(dot(normal, mainLight.direction));
                float3 ambient = SampleSH(normal);
                float4 lightIntensity = float4(mainLight.color, 1) + float4(ambient, 1);
                // _ColorBias: 0=纯TopColor, 1=贴图颜色影响
                float4 topCol = lerp(_TopColor, i.grassColor * _TopColor, _ColorBias);
                float4 botCol = lerp(_BottomColor, i.grassColor * _BottomColor, _ColorBias);
                // 渐变偏移：正值=顶色占比多，负值=底色占比多
                // 被压缩方在剩余区域保持满强度原色
                float gradT;
                if (_GradientOffset >= 0.0)
                {
                    // 渐变在 [0, 1-offset] 内完成，之上全为顶色
                    gradT = saturate(i.uv.y / max(1.0 - _GradientOffset, 0.001));
                }
                else
                {
                    // 渐变在 [-offset, 1] 内完成，之下全为底色
                    float absOff = -_GradientOffset;
                    gradT = saturate((i.uv.y - absOff) / max(1.0 - absOff, 0.001));
                }
                float4 col = lerp(botCol, topCol, gradT) * lightIntensity * lerp((1-_ShadowScale), 1, shadow);

                // 草体透贴：clip裁剪
                #ifdef _BLADE_OVERLAY_ON
                half4 overlay = SAMPLE_TEXTURE2D(_BladeOverlayTex, sampler_BladeOverlayTex, i.overlayUV);
                clip(overlay.a - _BladeOverlayAlphaClip);
                col.rgb = lerp(col.rgb, overlay.rgb * col.rgb, _BladeOverlayIntensity);
                #endif

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
                #ifdef _BLADE_OVERLAY_ON
                half4 overlay = SAMPLE_TEXTURE2D(_BladeOverlayTex, sampler_BladeOverlayTex, i.overlayUV);
                clip(overlay.a - _BladeOverlayAlphaClip);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ZTest LEqual
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma hull hull
            #pragma domain domain
            #pragma geometry geo
            #pragma fragment fragDepth
            #pragma target 4.6

            half4 fragDepth(geometryOutput i) : SV_Target
            {
                #ifdef _BLADE_OVERLAY_ON
                half4 overlay = SAMPLE_TEXTURE2D(_BladeOverlayTex, sampler_BladeOverlayTex, i.overlayUV);
                clip(overlay.a - _BladeOverlayAlphaClip);
                #endif
                return 0;
            }
            ENDHLSL
        }
    }
    CustomEditor "GrassGUI"
}
