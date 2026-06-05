// Custom_Snow.shader v2.0 (2026.05.27)
// =============================================================================
// URP风格化雪地材质 - 柔和卡通积雪质感
// - 凹陷位移加max(0)限制，不超过原始地面
// - 颜色过渡直接使用RT值+DeformEdgeSoftness曲线，保留自然柔和边缘
// Custom_Snow 1.1 优化雪地闪烁表现，陡峭斜面剔除：法线Y分量越小说明越陡峭，剔除闪光避免拉线
// Custom_Snow 1.2 使用_SparkleTex G通道噪波增强镜头转动时随机闪烁效果，添加【蒙版纹理密度】值，完善GUI参数说明
// Custom_Snow 1.3 优化压痕加深颜色效果

Shader "Custom/Snow"
{
    Properties
    {
        // [Header(Base Snow)]
        [MainColor] _BaseColor("Snow Color (亮面)", Color) = (0.92, 0.96, 1.0, 1)
        _ShadowColor("Shadow Color (暗面)", Color) = (0.55, 0.65, 0.85, 1)
        [MainTexture] _BaseMap("Base Map (RGB)", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        _NormalScale("Normal Scale", Range(0, 5)) = 1.0

        // [Header(Lighting)]
        _ShadowSoftness("Shadow Softness (阴影柔和度)", Range(0.001, 0.5)) = 0.15
        _ShadowOffset("Shadow Offset (阴影偏移)", Range(-0.5, 0.5)) = 0.0
        _Smoothness("Specular Smoothness", Range(0, 1)) = 0.5
        _SpecularStrength("Specular Strength", Range(0, 2)) = 0.4

        // [Header(Sparkle)]
        _SparkleTex("Sparkle Map (细密点纹理)", 2D) = "black" {}
        _SparkleScale("Sparkle Tiling", Range(1, 200)) = 11
        _SparkleIntensity("Sparkle Intensity", Range(0, 50)) = 12
        _SparkleThreshold("Sparkle Threshold", Range(0, 1)) = 0
        _SparkleViewDep("Sparkle View Shift", Range(0, 1)) = 0.6
        _SparkleFlickerScale("Sparkle Flicker Tiling (G蒙版)", Range(0.1, 10)) = 0.1
        _SparkleFadeDistance("Sparkle Fade Distance (可见距离)", Range(0,30)) = 18

        // [Header(Fresnel)]
        _FresnelColor("Fresnel Color", Color) = (0.7, 0.85, 1.0, 1)
        _FresnelPower("Fresnel Power", Range(1, 10)) = 3.0
        _FresnelStrength("Fresnel Strength", Range(0, 2)) = 0.4

        [Header(Render Settings)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode", Float) = 2

        // [Header(Snow Deform)]
        _DeformColor("Deform Color (压痕颜色)", Color) = (0.0, 0.75, 0.95, 1)
        _DeformColorStrength("Deform Color Strength (压痕染色强度)", Range(0, 5)) = 2.8
        _DeformEdgeSoftness("Deform Edge Softness (压痕过渡柔和度)", Range(0.1, 5)) = 0.7
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _ShadowColor;
            float4 _BaseMap_ST;
            float4 _NormalMap_ST;
            float  _NormalScale;
            float  _ShadowSoftness;
            float  _ShadowOffset;
            float  _Smoothness;
            float  _SpecularStrength;
            float4 _SparkleTex_ST;
            float  _SparkleScale;
            float  _SparkleIntensity;
            float  _SparkleThreshold;
            float  _SparkleViewDep;
            float  _SparkleFlickerScale;
            float  _SparkleFadeDistance;
            float4 _FresnelColor;
            float  _FresnelPower;
            float  _FresnelStrength;
            float4 _DeformColor;
            float  _DeformColorStrength;
            float  _DeformEdgeSoftness;
        CBUFFER_END

        // 全局 shader 参数（由 SnowDeformManager 设置）
        TEXTURE2D(_SnowDeformRT);   SAMPLER(sampler_SnowDeformRT);
        float  _SnowDeformDepth;    // 顶点位移深度
        float  _SnowDeformDarken;   // 凹陷区域变暗程度
        float4 _SnowAreaCenter;     // xy: 投影区域中心 (世界XZ)
        float  _SnowAreaSize;       // 投影区域边长 (米)
        float  _SnowSinkDepth;     // 雪面整体上抬高度（模拟雪厚度）

        TEXTURE2D(_BaseMap);        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap);       SAMPLER(sampler_NormalMap);
        TEXTURE2D(_SparkleTex);     SAMPLER(sampler_SparkleTex);
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float3 tangentWS   : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
                float2 fogAndDepth : TEXCOORD5; // x=fog, y=linearDepth
                float  deformMask  : TEXCOORD6; // 凹陷程度 0-1
            };

            half fastPow(half x, half n) {
                return exp2(n * log2(max(x, 1e-6h))); // 避免 log2(0) 导致除0/NaN
            }

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                
                // 先计算世界空间位置，用于 XZ 投影采样 RT
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                
                // 世界空间 XZ → RT UV（投影映射，消除 UV 接缝劈裂）
                float2 deformUV = (worldPos.xz - _SnowAreaCenter.xy) / _SnowAreaSize + 0.5;
                
                // 5-tap十字模糊采样，减少RT边缘锯齿
                float texelSize = 1.0 / 1024.0; // RT分辨率的倒数
                half deformCenter = SAMPLE_TEXTURE2D_LOD(_SnowDeformRT, sampler_SnowDeformRT, deformUV, 0).r;
                half deformL = SAMPLE_TEXTURE2D_LOD(_SnowDeformRT, sampler_SnowDeformRT, deformUV + float2(-texelSize, 0), 0).r;
                half deformR = SAMPLE_TEXTURE2D_LOD(_SnowDeformRT, sampler_SnowDeformRT, deformUV + float2( texelSize, 0), 0).r;
                half deformU = SAMPLE_TEXTURE2D_LOD(_SnowDeformRT, sampler_SnowDeformRT, deformUV + float2(0,  texelSize), 0).r;
                half deformD = SAMPLE_TEXTURE2D_LOD(_SnowDeformRT, sampler_SnowDeformRT, deformUV + float2(0, -texelSize), 0).r;
                half deform = (deformCenter * 4.0 + deformL + deformR + deformU + deformD) * 0.125;
                
                // 投影边缘衰减：超出区域的部分不位移
                float2 edgeDist = min(deformUV, 1.0 - deformUV);
                float edgeFade = saturate(min(edgeDist.x, edgeDist.y) * 20.0);
                deform *= edgeFade;
                
                // 统一沿世界空间 Y 轴向下位移（避免硬边法线分裂导致破面）
                float3 displaceDir = TransformWorldToObjectNormal(float3(0, -1, 0));
                // 先整体上抬 sinkDepth（模拟雪厚度），再减去凹陷位移，clamp确保不低于原始地面
                float netDisplace = max(0, _SnowSinkDepth - deform * _SnowDeformDepth);
                input.positionOS.xyz -= displaceDir * netDisplace;
                
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   norInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                o.positionCS  = posInputs.positionCS;
                o.positionWS  = posInputs.positionWS;
                o.normalWS    = norInputs.normalWS;
                o.tangentWS   = norInputs.tangentWS;
                o.bitangentWS = norInputs.bitangentWS;
                o.uv          = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogAndDepth = float2(ComputeFogFactor(posInputs.positionCS.z), posInputs.positionCS.w);
                o.deformMask  = deform;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // --- 凹陷区域材质变化 ---
                half deform = input.deformMask;
                // 凹陷区域染色：保留画笔边缘柔和过渡
                half3 iceColor = _DeformColor.rgb;
                half deformBlend = saturate(fastPow(deform, _DeformEdgeSoftness) * _SnowDeformDarken * _DeformColorStrength);
                
                // --- Base Color ---
                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                // 凹陷处从原色直接过渡到深蓝冰色
                half3 texColor = lerp(baseMap.rgb, iceColor, deformBlend);

                // --- Normal ---
                half4 normalSample = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv);
                half3 normalWS;
                // 判断是否有有效法线贴图（默认bump纹理的ag通道约为0.5）
                if (_NormalScale > 0.001)
                {
                    half3 normalTS = UnpackNormalScale(normalSample, _NormalScale);
                    float3x3 TBN = float3x3(
                        normalize(input.tangentWS),
                        normalize(input.bitangentWS),
                        normalize(input.normalWS));
                    normalWS = normalize(mul(normalTS, TBN));
                }
                else
                {
                    normalWS = normalize(input.normalWS);
                }

                // --- View & Light ---
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half3 lightDir = normalize(mainLight.direction);
                half  shadow   = mainLight.shadowAttenuation;

                // --- Half-Lambert 柔和漫反射 ---
                half NdotL = dot(normalWS, lightDir);
                half halfLambert = NdotL * 0.5 + 0.5; // [0,1] 柔和过渡
                halfLambert += _ShadowOffset;
                // smoothstep 控制阴影边缘柔和度
                half lightMask = smoothstep(0.5 - _ShadowSoftness, 0.5 + _ShadowSoftness, halfLambert);
                lightMask *= shadow;

                // 亮面/暗面颜色混合
                half3 snowColor = lerp(_ShadowColor.rgb, _BaseColor.rgb, lightMask);
                half3 diffuse = snowColor * texColor * mainLight.color;

                // --- Specular (柔和高光) ---
                half NdotV = saturate(dot(normalWS, viewDirWS));
                half3 halfDir = normalize(lightDir + viewDirWS);
                half NdotH = saturate(dot(normalWS, halfDir));
                half specPow = exp2(10.0 * _Smoothness + 1.0);
                half spec = fastPow(NdotH, specPow) * _SpecularStrength;
                half3 specular = spec * mainLight.color * shadow * lightMask;

                // --- Sparkle (G通道噪波双层交叠蒙版 + R通道定点位) ---
                // 世界空间XZ采样基础UV
                float2 sparkleUV = input.positionWS.xz * _SparkleScale * 0.1;

                // R通道确定闪光点位置，Threshold 控制密度
                half sparkleR = SAMPLE_TEXTURE2D_LOD(_SparkleTex, sampler_SparkleTex, sparkleUV, 0).r;
                half sparklePoints = smoothstep(_SparkleThreshold, 1.0, sparkleR);

                // 视角投影到表面
                float2 viewOffset = float2(
                    dot(viewDirWS, normalize(input.tangentWS)),
                    dot(viewDirWS, normalize(input.bitangentWS))
                );

                // G通道双层交叠蒙版：两层不同频率的G通道噪波相乘
                // _SparkleFlickerScale 控制G蒙版整体tiling，各层保留差异倍率
                // 层1：中等偏移（旋转敏感）
                float2 gUV1 = sparkleUV * 1.5 * _SparkleFlickerScale + viewOffset * _SparkleViewDep * 2.0;
                half g1 = SAMPLE_TEXTURE2D_LOD(_SparkleTex, sampler_SparkleTex, gUV1, 0).g;
                // 层2：高频偏移（平移也敏感）
                float2 gUV2 = sparkleUV * 2.7 * _SparkleFlickerScale + viewOffset * _SparkleViewDep * 4.0;
                half g2 = SAMPLE_TEXTURE2D_LOD(_SparkleTex, sampler_SparkleTex, gUV2, 0).g;

                // 两层G通道相乘后做阈值切换
                half gProduct = g1 * g2;
                half flickerMask = smoothstep(0.15, 0.35, gProduct);

                // 大颗亮点惰性：减小偏移灵敏度
                half particleSize = saturate((sparkleR - _SparkleThreshold) / (1.0 - _SparkleThreshold));
                half inertia = lerp(1.0, 0.2, particleSize);
                // 大颗粒用更低灵敏度重新采样
                float2 gUV1_large = sparkleUV * 1.5 * _SparkleFlickerScale + viewOffset * _SparkleViewDep * 2.0 * inertia;
                float2 gUV2_large = sparkleUV * 2.7 * _SparkleFlickerScale + viewOffset * _SparkleViewDep * 4.0 * inertia;
                half g1L = SAMPLE_TEXTURE2D_LOD(_SparkleTex, sampler_SparkleTex, gUV1_large, 0).g;
                half g2L = SAMPLE_TEXTURE2D_LOD(_SparkleTex, sampler_SparkleTex, gUV2_large, 0).g;
                half flickerMask_sized = smoothstep(0.15, 0.35, g1L * g2L);
                // 根据粒子大小混合：小颗粒用高灵敏度，大颗粒用低灵敏度
                half finalFlicker = lerp(flickerMask, flickerMask_sized, particleSize);

                // 陡峭斜面剔除
                half slopeMask = smoothstep(0.3, 0.7, normalWS.y);
                // 法线凹陷处降低闪光
                half cavityMask = smoothstep(0.01, 0.3, NdotV);
                // R通道定哪些点能闪 × G通道蒙版定何时闪 × 遮罩 × 凹陷区域抑制闪烁
                half deformSparkleMask = 1.0 - saturate(deformBlend * 2.0); // 压痕区域压暗闪光
                half sparkleFinal = sparklePoints * finalFlicker * slopeMask * cavityMask * deformSparkleMask;

                // 结合法线光照和阴影：阴影处保留微弱闪光
                half sparkleLight = lerp(0.04, 1.0, lightMask * lightMask); // 阴影处保留闪光亮度
                // 距离衰减：使用线性深度，不受视角倾斜影响
                half linearDepth = input.fogAndDepth.y;
                half distFade = 1.0 - saturate(linearDepth / _SparkleFadeDistance);
                distFade = distFade * distFade; // 平方衰减，近处更亮远处柔和淡出
                // 结合颜色贴图明暗：亮区闪烁更强，暗区（如缝隙/纹理阴影）闪烁减弱
                half texLuminance = dot(texColor, half3(0.299, 0.587, 0.114));
                half3 sparkle = sparkleFinal * _SparkleIntensity * mainLight.color * sparkleLight * distFade * texLuminance;

                // --- Fresnel Rim (边缘泛蓝光) ---
                half fresnel = fastPow(1.0 - NdotV, _FresnelPower) * _FresnelStrength;
                half3 rim = fresnel * _FresnelColor.rgb * mainLight.color;

                // --- Additional Lights ---
                half3 addLighting = 0;
                #ifdef _ADDITIONAL_LIGHTS
                uint addLightCount = GetAdditionalLightsCount();
                for (uint i = 0; i < addLightCount; i++)
                {
                    Light addLight = GetAdditionalLight(i, input.positionWS);
                    half addNdotL = dot(normalWS, addLight.direction) * 0.5 + 0.5;
                    half addMask = smoothstep(0.4, 0.6, addNdotL);
                    addLighting += snowColor * texColor * addMask * addLight.color
                                 * addLight.distanceAttenuation * addLight.shadowAttenuation;
                }
                #endif

                // --- Ambient ---
                half3 ambient = SampleSH(normalWS) * snowColor * texColor;

                // --- Combine ---
                half3 color = ambient + diffuse + specular + sparkle + rim + addLighting;

                // Fog
                color = MixFog(color, input.fogAndDepth.x);

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // ShadowCaster Pass
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings o;
                
                // 世界空间 XZ 投影采样（与 ForwardLit 一致）
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                float2 deformUV = (worldPos.xz - _SnowAreaCenter.xy) / _SnowAreaSize + 0.5;
                
                // 5-tap十字模糊采样，减少RT边缘锯齿
                float texelSize = 1.0 / 1024.0;
                half deformCenter = SAMPLE_TEXTURE2D_LOD(_SnowDeformRT, sampler_SnowDeformRT, deformUV, 0).r;
                half deformL = SAMPLE_TEXTURE2D_LOD(_SnowDeformRT, sampler_SnowDeformRT, deformUV + float2(-texelSize, 0), 0).r;
                half deformR = SAMPLE_TEXTURE2D_LOD(_SnowDeformRT, sampler_SnowDeformRT, deformUV + float2( texelSize, 0), 0).r;
                half deformU = SAMPLE_TEXTURE2D_LOD(_SnowDeformRT, sampler_SnowDeformRT, deformUV + float2(0,  texelSize), 0).r;
                half deformD = SAMPLE_TEXTURE2D_LOD(_SnowDeformRT, sampler_SnowDeformRT, deformUV + float2(0, -texelSize), 0).r;
                half deform = (deformCenter * 4.0 + deformL + deformR + deformU + deformD) * 0.125;
                float2 edgeDist = min(deformUV, 1.0 - deformUV);
                float edgeFade = saturate(min(edgeDist.x, edgeDist.y) * 20.0);
                deform *= edgeFade;
                float3 displaceDir = TransformWorldToObjectNormal(float3(0, -1, 0));
                float netDisplace = max(0, _SnowSinkDepth - deform * _SnowDeformDepth);
                input.positionOS.xyz -= displaceDir * netDisplace;
                
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 norWS = TransformObjectToWorldNormal(input.normalOS);
                posWS = ApplyShadowBias(posWS, norWS, _LightDirection);
                o.positionCS = TransformWorldToHClip(posWS);
                #if UNITY_REVERSED_Z
                    o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // DepthOnly Pass
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
    CustomEditor "CustomSnowGUI"
}
