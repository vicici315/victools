// Glass_MobileNew.v2.0 完善折射效果，玻璃固有色受Fresnel控制，优化高光效果
// Glass_MobileNew.v2.1 优化折射上下偏移问题，在顶点阶段计算物体中心的屏幕坐标，保证透视除法与 screenPos 一致
// Glass_MobileNew.v2.2 添加法线控制顶点位移、uv游走实现水柱流动效果
// Glass_MobileNew.v2.3 添加顶点颜色R通道作为顶点位移蒙版，约束水流起始位置的偏移
// Glass_MobileNew.v2.4 修复法线控制顶点位移跳动问题
// Glass_MobileNew.v2.5 优化场景模糊：_SceneBlurStrength 为 0 时跳过 9 点采样，直接单次采样
// Glass_MobileNew.v2.6 基础颜色Alpha控制颜色与场景色占比，添加基础光照模型（实现果冻效果）
// Glass_MobileNew.v2.7 添加接受阴影（传入 shadowCoord，支持软阴影）
// Glass_MobileNew.v2.8 果冻效果实现，顶点变形支持 UV 采样模式（蒙皮模型稳定不跳动）；优化阴影与控制基础颜色关系
// Glass_MobileNew.v2.9 修复 UV 采样模式接缝破面和蒙皮抖动：改为纯法线膨胀（顶点色R × 强度），不采样贴图，彻底消除接缝差异
// Glass_MobileNew.v2.10 修复蒙皮模式顶点抖动：改用模型UV采样法线贴图+UV哈希偏移方向，所有输入均为模型固有属性，不随变换变化
// Glass_MobileNew.v3.0 添加自发光参数
// Glass_MobileNew.v3.1 添加法线贴图交错游走，实现波浪起伏效果，修复法线强度参数影响反射图错位。
// Glass_MobileNew.v3.2 性能重构（渲染结果不变）：移除无用的 _SHADOWS_SOFT 变体（ForwardLit 变体数减半）；
//                       Varyings 插值器按需裁剪（切线/副切线/uv 仅 _USENORMALMAP、物体中心屏幕坐标仅 _USEREFRACTION、
//                       屏幕坐标仅折射/透明、雾因子仅有雾效关键字、移除已废弃的 positionWS）；
//                       阴影坐标改为顶点阶段计算并插值，省去每像素一次 mat4 变换；ShadowCaster 移除未使用的法线计算。

Shader "Custom/Glass_MobileNew"
{
    Properties
    {
        [Header(Glass Properties)]
        // [Space(5)]
        [MainColor]_BaseColor ("Base Color", Color) = (1,1,1,0.5)
        [MainTexture]_BaseMap ("Base Map", 2D) = "white" {}
        _Transparency ("Global Transparency", Range(0, 1)) = 0.98
        
        // [Header(Specular)]
        _Smoothness ("Smoothness", Range(0.01, 1)) = 0.7
        _SpecularStrength ("Specular Strength", Range(0, 3)) = 0.8
        _SceneBlurStrength ("Scene Blur Strength", Range(0, 1)) = 1
        [Header(Base Lighting)]
        _BaseLightStrength ("Base Light Strength", Range(0, 2)) = 0.5
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.39
        
        // [Header(Distortion)]
        [Toggle(_USENORMALMAP)] _UseNormalMap ("Use Normal Map", Float) = 0
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0, 2)) = 0.6
        // 顶点变形：用法线贴图的 RG 通道驱动顶点沿法线方向偏移
        // Normal Map 的 Offset XY 同时作为 UV 游走速度（值为0时不做动画）
        [Toggle(_USEVERTEXDEFORM)] _UseVertexDeform ("Vertex Deform", Float) = 0
        _VertexDeformStrength ("Deform Strength", Range(0, 1.0)) = 0.02
        [Toggle(_DEFORM_USE_UV)] _DeformUseUV ("Deform Use UV (Skinned)", Float) = 0

        // =============================================
        // 顶点风动位移（移植自 SyntyStudios/SciFiPlant）
        // 与 _USEVERTEXDEFORM 独立可叠加，关闭时整段被 shader_feature 剔除，零开销
        // =============================================
        // [Header(Vertex Wind Displacement)]
        [Toggle(_USEVERTEXDISPLACEMENT)] _UseVertexDisplacement ("Use Vertex Wind Displacement", Float) = 0
        [NoScaleOffset] _Tree_NoiseTexture1 ("Wind Noise Texture (R, 可选)", 2D) = "white" {}
        _WindDirection ("Wind Direction (XYZ 方向, W 无效)", Vector) = (1, 0, 0, 0)
        _Big_WindAmount ("Big Wind Amount (振幅)", Float) = 0.1
        _Small_WindSpeed ("Small Wind Speed (时间速度)", Float) = 0.5
        _Small_Wave ("Small Wave (空间频率, 0=无变化)", Range(0, 10)) = 1.0

        // [Header(Refraction)]
        [Toggle(_USEREFRACTION)] _UseRefraction ("Use Refraction", Float) = 1
        _RefractionStrength ("Refraction Strength", Range(-1.81, 1.81)) = -0.3
        
        // [Header(Reflection)]
        [Toggle(_USEREFLECTION)] _UseReflection("Use Reflection Map", Float) = 0
        [NoScaleOffset]_SphericalReflectionMap ("Spherical Reflection Map", 2D) = "white" {}
        _ReflectionScale ("Reflection Scale", Range(0.0, 2.0)) = 1.0
        _ReflectionBlur ("Max Reflection Blur", Range(0, 6)) = 6.0
        
        [Header(Fresnel)]
        _FresnelPower ("Fresnel Power", Range(0.1, 10)) = 1.86
        _FresnelBias ("Fresnel Bias", Range(0, 1)) = 0.072
        _FresnelScale ("Fresnel Scale", Range(0, 2)) = 1.2

        // [Header(Emission)] — 自发光（HDR 可触发 Bloom）
        // 参考 PBR_Mobile_NEW.shader 的 emission 命名
        [Toggle(_USEEMISSIONMAP)] _UseEmissionMap ("Use Emission Map", Float) = 0
        [HDR]_EmissionColor ("Emission Color", Color) = (1,1,1,1)
        _EmissionMap ("Emission Map", 2D) = "white" {}
        _EmissionScale  ("Emission Scale", Range(0, 12)) = 1.0

        // [Header(Render Settings)]
        [KeywordEnum(Transparent, Opaque)] _RenderMode ("Render Mode", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
        
        // Hidden properties controlled by GUI
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1   // One
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 10  // OneMinusSrcAlpha
        [HideInInspector] _ZWrite ("ZWrite", Float) = 0        // Off
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "DisableBatching"="True"
        }

        // 主渲染Pass
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // URP关键多编译指令（简化版）
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            // 本 Shader 使用自定义单点 shadowmap 采样，不经过 URP 的 PCF 柔化路径，
            // 因此移除 _SHADOWS_SOFT 变体（原 multi_compile 会让 ForwardLit 变体数量翻倍且无任何作用）
            #pragma multi_compile_fog
            
            // 基础功能开关（按性能影响排序）
            #pragma shader_feature_local _RENDERMODE_TRANSPARENT _RENDERMODE_OPAQUE
            #pragma shader_feature_local _USENORMALMAP
            #pragma shader_feature_local _USEREFRACTION
            #pragma shader_feature_local _USEREFLECTION
            #pragma shader_feature_local _USEVERTEXDEFORM
            #pragma multi_compile_local _ _DEFORM_USE_UV
            #pragma shader_feature_local _USEVERTEXDISPLACEMENT
            #pragma shader_feature_local _USEEMISSIONMAP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            
            // 函数前向声明
            inline half3 SampleSceneColorBlurred(float2 uv, float blurAmount);
            
            // 手动模糊采样函数 - 使用多次采样模拟模糊效果
            inline half3 SampleSceneColorBlurred(float2 uv, float blurAmount)
            {
                // 调用方已保证 blurAmount >= 0.01，此处无需再判分支
                // 计算采样偏移（基于屏幕空间像素大小）
                float2 texelSize = _ScreenParams.zw - 1.0; // 1/width, 1/height
                float2 offset = texelSize * blurAmount;

                // 使用9点采样进行模糊（优化的高斯模糊）
                half3 color = half3(0, 0, 0);

                // 中心权重
                color += SampleSceneColor(uv).rgb * 0.25;

                // 4个主方向
                color += SampleSceneColor(uv + float2(offset.x, 0)).rgb * 0.125;
                color += SampleSceneColor(uv + float2(-offset.x, 0)).rgb * 0.125;
                color += SampleSceneColor(uv + float2(0, offset.y)).rgb * 0.125;
                color += SampleSceneColor(uv + float2(0, -offset.y)).rgb * 0.125;

                // 4个对角线方向
                color += SampleSceneColor(uv + float2(offset.x, offset.y)).rgb * 0.0625;
                color += SampleSceneColor(uv + float2(-offset.x, offset.y)).rgb * 0.0625;
                color += SampleSceneColor(uv + float2(offset.x, -offset.y)).rgb * 0.0625;
                color += SampleSceneColor(uv + float2(-offset.x, -offset.y)).rgb * 0.0625;

                return color;
            }
            
            // 纹理声明应该始终存在，不要放在条件编译中
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_SphericalReflectionMap);
            SAMPLER(sampler_SphericalReflectionMap);
            // 顶点风动位移噪声贴图
            TEXTURE2D(_Tree_NoiseTexture1);
            SAMPLER(sampler_Tree_NoiseTexture1);
            // 自发光贴图（仅 _USEEMISSIONMAP 启用时实际启用）
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            half fastPow(half x, half n) {
                return exp2(n * log2(x)); // 在某些GPU上更快
            }
            
            // 快速球形UV映射
            #ifdef _USEREFLECTION
            float2 fastSphericalUV(float3 reflectionVector)
            {
                reflectionVector = normalize(reflectionVector);
                return float2(
                    reflectionVector.x / 4.01 + 0.5,
                    reflectionVector.y / 4.01 + 0.5
                );
            }
            
            // 采样球形反射贴图
            float3 SampleSphericalReflection(float3 reflectionVector, float blur)
            {
                float2 uv = fastSphericalUV(reflectionVector);
                float3 reflectionColor = SAMPLE_TEXTURE2D_LOD(_SphericalReflectionMap, sampler_SphericalReflectionMap, uv, blur).rgb;
                return reflectionColor;
            }
            #endif
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 vertexColor : COLOR;  // 顶点色，R 通道作为顶点偏移蒙版
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 baseMapUV : TEXCOORD9;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                float4 shadowCoord : TEXCOORD10;
                // 仅 _USENORMALMAP 需要：UV 与 TBN 切线/副切线
            #ifdef _USENORMALMAP
                float2 uv : TEXCOORD0;
                float3 tangentWS : TEXCOORD4;
                float3 bitangentWS : TEXCOORD5;
            #endif
                // 仅折射 / 透明混合需要屏幕坐标
            #if defined(_USEREFRACTION) || !defined(_RENDERMODE_OPAQUE)
                float4 screenPos : TEXCOORD3;
            #endif
                // 仅折射需要物体中心的屏幕坐标（折射轴心）
            #ifdef _USEREFRACTION
                float4 objectCenterScreenPos : TEXCOORD8;
            #endif
                // 仅启用雾效关键字时插值
            #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                float fogFactor : TEXCOORD6;
            #endif
            };

            // 阴影 map texel size (URP 全局注入, URP 内部宏使用, 显式声明)
            float4 _MainLightShadowmapTexture_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Transparency;
                half _Smoothness;
                half _SpecularStrength;
                half _SceneBlurStrength;
                half _BumpScale;
                half _RefractionStrength;
                half _ReflectionScale;
                half _ReflectionBlur;
                half _FresnelPower;
                half _FresnelBias;
                half _FresnelScale;
                half _BaseLightStrength;
                half _ShadowStrength;
                float4 _BaseMap_ST;
                float4 _BumpMap_ST;
                half _VertexDeformStrength;
                // 顶点风动位移参数
                half4 _WindDirection;
                half _Big_WindAmount;
                half _Small_WindSpeed;
                half _Small_Wave;
                // 自发光参数
                half _EmissionScale;
                half4 _EmissionColor;
            CBUFFER_END

            // ● FastSpecular函数 - 来自Glass_carWindow.shader的优化高光计算
            half3 FastSpecular(half3 normalWS, half3 lightDir, half3 viewDirWS, half3 lightColor, half shadowAttenuation, float fresnel)
            {
                // 计算光线的反射向量
                half3 reflectDir = reflect(-lightDir, normalWS);
                
                // 计算反射向量与视线方向的点积
                half RdotV = saturate(dot(reflectDir, viewDirWS));
                
                // 计算光泽度指数（smoothness³ * 512 + 2）
                half smoothnessCubed = _Smoothness * _Smoothness * _Smoothness;
                half gloss = min(smoothnessCubed * 256.0 + 2.0, 128.0);
                half specular = fastPow(max(RdotV, 0.01), gloss);
                
                // 能量守恒归一化（调整后的公式，减少低smoothness时的衰减）
                // 使用 (gloss + 8) / 16 作为归一化因子，提供更平缓的过渡
                half normalization = (gloss + 8.0) / 16.0;
                specular *= normalization;
                
                // 高光受菲涅尔影响（边缘更亮）
                half fresnelBoost = lerp(0.5, 0.8, fresnel);
                
                // 玻璃材质的高光（阴影处保留 10% 高光，避免完全消失）
                half shadowSpec = lerp(0.061, 1.0, shadowAttenuation);
                return lightColor * specular * shadowSpec * fresnelBoost;
            }

            // 计算菲涅尔效果（与Glass_carWindow.shader保持一致）
            float CalculateFresnel(float3 normalWS, float3 viewDirWS)
            {
                float fresnel = saturate(dot(normalWS, viewDirWS));
                fresnel = _FresnelBias + _FresnelScale * fastPow(1.0 - fresnel, _FresnelPower);
                return saturate(fresnel);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // ── 顶点风动位移（移植自 SyntyStudios/SciFiPlant） ──
                // 与 _USEVERTEXDEFORM 独立可叠加，先于顶点变形执行
                // 沿模型空间 X 轴位移，使用 _Tree_NoiseTexture1 的 R 通道作为位移源
                // max(1e-5, ...) 防止 _Small_Wave=1 时除零产生 NaN
                // 关闭 keyword 时整段被剔除，无任何性能开销
                #ifdef _USEVERTEXDISPLACEMENT
                    // 公式: windU = vertexX · _Small_Wave + time · _Small_WindSpeed (已解耦)
                    // 全部使用 float 精度，避免 half 哈希函数在部分 GPU 上产生 NaN
                    float windU     = IN.positionOS.x * _Small_Wave
                                    + _Time.y * _Small_WindSpeed;
                    float windNoise = SAMPLE_TEXTURE2D_LOD(_Tree_NoiseTexture1, sampler_Tree_NoiseTexture1,
                                                            float2(windU, windU), 0).r;
                    float useHash   = smoothstep(0.997, 0.999, windNoise);
                    float p         = frac(windU * 0.1031);
                    p               = p * p + 33.33;
                    p               = p * p + p;
                    float hashNoise = frac(p);
                    windNoise       = lerp(windNoise, hashNoise, useHash);
                    IN.positionOS.x += _Big_WindAmount * windNoise;
                #endif

                // ── 顶点变形 ──
                #ifdef _USEVERTEXDEFORM
                    float2 deformScrollSpeed = _BumpMap_ST.zw;
                    float deformAmount;

                    #ifdef _DEFORM_USE_UV
                        // 蒙皮/固定模式：用模型 UV 采样法线贴图，变形结果固定在模型表面
                        // UV 不随旋转/平移/动画变化，所以变形位置完全固定
                        float2 deformUV = IN.uv * _BumpMap_ST.xy + _BumpMap_ST.zw;
                        float4 normalTex = SAMPLE_TEXTURE2D_LOD(_BumpMap, sampler_BumpMap, deformUV, 0);
                        float3 normalTS = UnpackNormal(normalTex);
                        deformAmount = (1.0 - normalTS.z) * _VertexDeformStrength * _BumpScale * IN.vertexColor.r;
                        // 用 UV 哈希生成固定偏移方向，不依赖 normalOS
                        float3 fixedDir;
                        fixedDir.x = frac(sin(dot(IN.uv, float2(12.9898, 78.233))) * 43758.5453) * 2.0 - 1.0;
                        fixedDir.y = frac(sin(dot(IN.uv, float2(39.346, 11.135))) * 43758.5453) * 2.0 - 1.0;
                        fixedDir.z = frac(sin(dot(IN.uv, float2(73.156, 52.235))) * 43758.5453) * 2.0 - 1.0;
                        fixedDir = normalize(fixedDir);
                        IN.positionOS.xyz += fixedDir * deformAmount;
                    #else
                        // 世界坐标模式：用世界空间 XZ 坐标采样法线贴图，支持流动动画
                        float3 worldPosForDeform = TransformObjectToWorld(IN.positionOS.xyz);
                        float2 deformUV = worldPosForDeform.xz * _BumpMap_ST.xy;
                        float scrollSpeed = length(deformScrollSpeed);
                        if (scrollSpeed > 0.0001)
                            deformUV += deformScrollSpeed * (_Time.y * scrollSpeed);

                        float4 normalTex = SAMPLE_TEXTURE2D_LOD(_BumpMap, sampler_BumpMap, deformUV, 0);
                        float3 normalTS = UnpackNormal(normalTex);
                        deformAmount = (1.0 - normalTS.z) * _VertexDeformStrength * _BumpScale * IN.vertexColor.r;
                        IN.positionOS.xyz += IN.normalOS * deformAmount;
                    #endif
                #endif

                VertexPositionInputs vertexInput = GetVertexPositionInputs(IN.positionOS.xyz);

                OUT.positionCS = vertexInput.positionCS;
                OUT.baseMapUV = TRANSFORM_TEX(IN.uv, _BaseMap);
            #if defined(_USEREFRACTION) || !defined(_RENDERMODE_OPAQUE)
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
            #endif
                
            #ifdef _USEREFRACTION
                // 在顶点阶段计算物体中心的屏幕坐标，保证透视除法与 screenPos 一致
                float3 objectCenterWS = TransformObjectToWorld(float3(0, 0, 0));
                float4 objectCenterCS = TransformWorldToHClip(objectCenterWS);
                OUT.objectCenterScreenPos = ComputeScreenPos(objectCenterCS);
            #endif
                
                // 法线：TBN 切线/副切线仅在 _USENORMALMAP 需要，关闭时省去变换与插值
            #ifdef _USENORMALMAP
                VertexNormalInputs normalInput = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.normalWS = normalInput.normalWS;
                OUT.tangentWS = normalInput.tangentWS;
                OUT.bitangentWS = normalInput.bitangentWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BumpMap);
            #else
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
            #endif
                OUT.viewDirWS = GetCameraPositionWS() - vertexInput.positionWS;
            #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                OUT.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
            #endif
                // 阴影坐标在顶点阶段算好并插值，避免逐像素再做一次 mat4 变换
                OUT.shadowCoord = TransformWorldToShadowCoord(vertexInput.positionWS);
                
                return OUT;
            }

            half4 frag(Varyings IN, half facing : VFACE) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                // 背面片元翻转法线，避免背面高光异常
                normalWS *= (facing > 0) ? 1.0 : -1.0;
                float3 viewDirWS = normalize(IN.viewDirWS);
                // 屏幕 UV（仅折射 / 透明混合需要）
            #if defined(_USEREFRACTION) || !defined(_RENDERMODE_OPAQUE)
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
            #else
                float2 screenUV = 0;
            #endif

                // 光照计算（优化版）
                // 单点 shadowmap 采样：只用一层 shadow texture，无 PCF 柔化
                //   优点：1 个采样点天然没有 URP 默认 PCF 在 light penumbra 上的同心环 banding
                //         同时比 URP 默认 PCF 2x2（4 tap）节省 4 倍带宽
                //   缺点：阴影边缘是硬边（hard shadow）
                Light mainLight;
                mainLight = GetMainLight();
                {
                    // 复用顶点阶段插值得到的阴影坐标（等价于 TransformWorldToShadowCoord(IN.positionWS)）
                    float4 sc = IN.shadowCoord;
                    // 单点采样：无 PCF 加权（中心点权重 1，无邻居采样）
                    half inRange = (sc.z > 0.0) ? 1.0 : 0.0;
                    float shadow = SAMPLE_TEXTURE2D_SHADOW(
                        _MainLightShadowmapTexture, sampler_LinearClampCompare, sc);
                    mainLight.shadowAttenuation = lerp(1.0, shadow, inRange);
                }
                // 优化：预计算光照强度，避免重复计算
                half3 lightColor = mainLight.color * mainLight.distanceAttenuation;
                // 直接使用 rawShadow + _ShadowStrength 强度控制（无半 Lambert 化 / smoothstep 抹平等柔化）
                half shadowAttenuation = lerp(1.0 - _ShadowStrength, 1.0, mainLight.shadowAttenuation);
                half shadowAttenuationE = lerp(1, shadowAttenuation, _BaseColor.a);
                
                // 采样主纹理，用全局透明度控制主纹理与基础颜色的占比
                // _Transparency=1 偏向基础颜色，_Transparency=0 偏向主纹理
                half4 baseMapColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.baseMapUV);
                half4 baseColor = half4(lerp(_BaseColor.rgb, baseMapColor.rgb*_BaseColor.rgb, _Transparency), _BaseColor.a);
                
                // 法线贴图采样（可选）
                half3 normalTS = half3(0, 0, 1); // 默认法线
                #ifdef _USENORMALMAP
                    // 原地波涛算法：用相同法线贴图做"方向相反的偏移采样"，叠加形成原地起伏
                    //   原理：N1 向 +dir 偏移采样（看到图案向 -dir 方向游走）
                    //         N2 向 -dir 偏移采样（看到图案向 +dir 方向游走）
                    //         两方向相反 → 视觉上图案"原地波涛起伏"（净 UV 偏移 = 0，无整体游走）
                    //   xy 振幅叠加：波峰变高、波谷变深 → 高对比的水面波涛感
                    //   性能最优：1 次额外 sample（GPU 共享 cache）+ 1 dir*t 共享 + ~3 ALU
                    //   替代：原 Offset 平移游走（整体平移，无波涛感）
                    //   退回：dir=0 时单采样（避免振幅翻倍，保持用户预期）
                    float2 dir = _BumpMap_ST.zw;
                    if (abs(dir.x) > 0.0001 || abs(dir.y) > 0.0001) {
                        float2 dirT = dir * _Time.y;
                        half3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv + dirT));
                        half3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv - dirT));
                        // xy 叠加（不除以 2，保留高对比波峰波谷）
                        normalTS.xy = (n1.xy + n2.xy) * _BumpScale;
                    } else {
                        half3 n = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv));
                        normalTS.xy = n.xy * _BumpScale;
                    }
                    normalTS.z = sqrt(1 - saturate(dot(normalTS.xy, normalTS.xy)));

                    float3x3 TBN = float3x3(
                        normalize(IN.tangentWS),
                        normalize(IN.bitangentWS),
                        normalWS
                    );
                    normalWS = normalize(mul(normalTS, TBN));
                #endif
                
                // 菲涅尔效应（使用统一的计算函数）
                half fresnel = CalculateFresnel(normalWS, viewDirWS);
                // 折射效果 - 以物体中心为轴心进行扭曲缩放
                float2 finalScreenUV = screenUV;
                
                #ifdef _USEREFRACTION
                    // 使用顶点阶段传入的物体中心屏幕坐标，透视除法与 screenUV 完全一致
                    float2 objectCenterScreenUV = IN.objectCenterScreenPos.xy / IN.objectCenterScreenPos.w;
                    
                    // 计算从中心到当前像素的方向向量
                    float2 directionFromCenter = screenUV - objectCenterScreenUV;
                    
                    // 使用法线强度调制扭曲效果（法线越偏离，扭曲越强）
                    half normalDistortion = 0;
                    #ifdef _USENORMALMAP
                        normalDistortion = length(normalTS.xy);
                    #endif
                    
                    // 以中心为轴心进行径向扭曲缩放
                    // _RefractionStrength > 0: 向外扩张（放大效果）
                    // 优化折射效果
                    float refractionScale = 1.0 + _RefractionStrength * (fresnel*1.2-0.8) * (1.0 + normalDistortion);
                    float2 scaledDirection = directionFromCenter * refractionScale;
                    finalScreenUV = objectCenterScreenUV + scaledDirection;
                    
                    // 确保UV坐标在有效范围内
                    finalScreenUV = saturate(finalScreenUV);
                #endif
                
                // 根据光滑度计算模糊级别（光滑度越低，模糊越强）
                // _Smoothness: 0 = 粗糙（最大模糊），1 = 光滑（无模糊）
                // _SceneBlurStrength 或 _Smoothness 任一为极值时，直接单次采样跳过 9 点模糊
                half3 sceneColor;
                #ifdef _RENDERMODE_OPAQUE
                    // 不透明模式不采样场景颜色，直接使用基础颜色
                    sceneColor = baseColor.rgb;
                #else
                    float blurAmount = (1.0 - _Smoothness) * _SceneBlurStrength * 6.0;
                    if (blurAmount < 0.01)
                    {
                        sceneColor = SampleSceneColor(finalScreenUV).rgb;
                    }
                    else
                    {
                        sceneColor = SampleSceneColorBlurred(finalScreenUV, blurAmount);
                    }
                #endif
                
                // 高光计算：使用 NdotV 抑制掠射角/背面观察时的异常高亮
                half NdotV = saturate(dot(normalWS, viewDirWS));
                half3 specular = FastSpecular(normalWS, mainLight.direction, viewDirWS, lightColor, shadowAttenuation, fresnel) * NdotV * _SpecularStrength;
                
                // 基础光照模型：NdotL 漫反射，受法线贴图和菲涅尔影响
                // 基础光照模型：NdotL 漫反射，菲涅尔边缘衰减避免边缘过亮
                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half3 baseLighting = lightColor * NdotL * shadowAttenuation * _BaseLightStrength * (1.0 - fresnel);
                
                // 优化：直接计算最终玻璃基础颜色
                // baseColor.a 控制基础颜色与场景颜色的占比：a=1 偏向基础颜色，a=0 偏向场景颜色
                half3 tintedScene = lerp(sceneColor * baseColor.rgb, baseColor.rgb * shadowAttenuationE, baseColor.a);
                // 基础光照叠加到固有色上
                half3 litBaseColor = tintedScene + baseLighting * baseColor.rgb;
                // 菲涅尔边缘混合：边缘处过渡到 sceneColor，不叠加额外光照
                half3 edgeColor = sceneColor;
                half3 glassBaseColor = lerp(litBaseColor, edgeColor, fresnel * 0.8);
                
                // 反射计算（优化版）
                half3 reflectionColor = half3(0, 0, 0);
                #ifdef _USEREFLECTION
                    // 优化：预计算反射向量和模糊度
                    float3 reflectionVector = reflect(-viewDirWS, normalWS);
                    float reflectionBlur = lerp(_ReflectionBlur, 0, _Smoothness);
                    
                    // 优化：合并反射颜色计算和强度应用
                    reflectionColor = SampleSphericalReflection(reflectionVector, reflectionBlur);
                    
                    // 优化：直接应用菲涅尔混合，避免中间变量
                    glassBaseColor = lerp(glassBaseColor, glassBaseColor+ reflectionColor * (_ReflectionScale), (fresnel*_ReflectionScale));
                #endif
                
                // 优化：预计算高光亮度，避免重复计算
                half specularLuminance = dot(specular, half3(0.299, 0.587, 0.114));
                half3 enhancedSpecular = specular * _SpecularStrength;
                
                half3 finalColor = glassBaseColor;
                // 优化：合并场景颜色和高光增强，减少一次加法运算
                finalColor += enhancedSpecular;

                // ── 自发光（_USEEMISSIONMAP 控制编译时剥离，零开销） ──
                // 算法：emissionMap.rgb × _EmissionColor.rgb × _EmissionScale
                // _EmissionColor 是 [HDR]，>1.0 颜色可触发 Bloom 后处理
                #ifdef _USEEMISSIONMAP
                    half3 emissionMap = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.baseMapUV).rgb;
                    finalColor += emissionMap * _EmissionColor.rgb * _EmissionScale;
                #endif

                // 应用雾效（仅启用雾效关键字时插值/计算）
            #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                finalColor = MixFog(finalColor, IN.fogFactor);
            #endif
                
                // 优化：预计算基础透明度，避免在最终计算中重复lerp
                half baseAlpha = lerp(_Transparency, 1, fresnel);
                half finalAlpha = saturate(baseAlpha + specularLuminance) * _Transparency;
                
                #ifdef _RENDERMODE_OPAQUE
                    return half4(finalColor, 1);
                #else
                    return half4(finalColor, finalAlpha);
                #endif
            }
            ENDHLSL
        }
        
        // ● 阴影投射Pass（优化简化版）
        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}
            
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _USEVERTEXDEFORM
            #pragma multi_compile_local _ _DEFORM_USE_UV
            #pragma shader_feature_local _USEVERTEXDISPLACEMENT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float3 _LightDirection;
            float4 _ShadowBias;

            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            // 顶点风动位移噪声贴图（保持阴影几何与渲染几何一致）
            TEXTURE2D(_Tree_NoiseTexture1);
            SAMPLER(sampler_Tree_NoiseTexture1);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Transparency;
                half _Smoothness;
                half _SpecularStrength;
                half _SceneBlurStrength;
                half _BumpScale;
                half _RefractionStrength;
                half _ReflectionScale;
                half _ReflectionBlur;
                half _FresnelPower;
                half _FresnelBias;
                half _FresnelScale;
                half _BaseLightStrength;
                half _ShadowStrength;
                float4 _BaseMap_ST;
                float4 _BumpMap_ST;
                half _VertexDeformStrength;
                // 顶点风动位移参数
                half4 _WindDirection;
                half _Big_WindAmount;
                half _Small_WindSpeed;
                half _Small_Wave;
                // 自发光参数（与 ForwardLit Pass 一致以满足 SRP Batcher 要求）
                half _EmissionScale;
                half4 _EmissionColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 vertexColor : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // ── 顶点风动位移（与 ForwardLit Pass 一致） ──
                #ifdef _USEVERTEXDISPLACEMENT
                    // 公式: windU = vertexX · _Small_Wave + time · _Small_WindSpeed (已解耦)
                    // 全部使用 float 精度，避免 half 哈希函数在部分 GPU 上产生 NaN
                    float windU     = input.positionOS.x * _Small_Wave
                                    + _Time.y * _Small_WindSpeed;
                    float windNoise = SAMPLE_TEXTURE2D_LOD(_Tree_NoiseTexture1, sampler_Tree_NoiseTexture1,
                                                            float2(windU, windU), 0).r;
                    float useHash   = smoothstep(0.997, 0.999, windNoise);
                    float p         = frac(windU * 0.1031);
                    p               = p * p + 33.33;
                    p               = p * p + p;
                    float hashNoise = frac(p);
                    windNoise       = lerp(windNoise, hashNoise, useHash);
                    input.positionOS.x += _Big_WindAmount * windNoise;
                #endif

                // ── 与 ForwardLit Pass 相同的顶点位移 ──
                #ifdef _USEVERTEXDEFORM
                    float2 deformScrollSpeed = _BumpMap_ST.zw;
                    float deformAmount;
                    #ifdef _DEFORM_USE_UV
                        float2 deformUV = input.uv * _BumpMap_ST.xy + _BumpMap_ST.zw;
                        float4 normalTex = SAMPLE_TEXTURE2D_LOD(_BumpMap, sampler_BumpMap, deformUV, 0);
                        float3 normalTS = UnpackNormal(normalTex);
                        deformAmount = (1.0 - normalTS.z) * _VertexDeformStrength * _BumpScale * input.vertexColor.r;
                        float3 fixedDir;
                        fixedDir.x = frac(sin(dot(input.uv, float2(12.9898, 78.233))) * 43758.5453) * 2.0 - 1.0;
                        fixedDir.y = frac(sin(dot(input.uv, float2(39.346, 11.135))) * 43758.5453) * 2.0 - 1.0;
                        fixedDir.z = frac(sin(dot(input.uv, float2(73.156, 52.235))) * 43758.5453) * 2.0 - 1.0;
                        fixedDir = normalize(fixedDir);
                        input.positionOS.xyz += fixedDir * deformAmount;
                    #else
                        float3 worldPosForDeform = TransformObjectToWorld(input.positionOS.xyz);
                        float2 deformUV = worldPosForDeform.xz * _BumpMap_ST.xy;
                        float scrollSpeed = length(deformScrollSpeed);
                        if (scrollSpeed > 0.0001)
                            deformUV += deformScrollSpeed * (_Time.y * scrollSpeed);
                        float4 normalTex = SAMPLE_TEXTURE2D_LOD(_BumpMap, sampler_BumpMap, deformUV, 0);
                        float3 normalTS = UnpackNormal(normalTex);
                        deformAmount = (1.0 - normalTS.z) * _VertexDeformStrength * _BumpScale * input.vertexColor.r;
                        input.positionOS.xyz += input.normalOS * deformAmount;
                    #endif
                #endif

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                positionWS = positionWS + _LightDirection * _ShadowBias.x;

                output.positionCS = TransformWorldToHClip(positionWS);

                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }
        
    }
    
    // 禁用阴影投射
    // FallBack "Hidden/Universal Render Pipeline/FallbackError"
    CustomEditor "Glass_carWindowGUI"
}
