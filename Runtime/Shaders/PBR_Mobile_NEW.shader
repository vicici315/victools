// PBR_Mobile1.0    PBR基本属性，自发光开关（贴图数量：4）
// PBR_Mobile1.2    添加AO贴图通道及控制参数；添加Compute Buffer多点光源支持
// PBR_Mobile2.0    添加Compute Buffer多点光源支持
// PBR_Mobile2.0.1    添加基础反射功能，支持反射贴图和菲涅尔效果
// PBR_Mobile2.0.2    性能优化版本 - 减少变体、优化采样、简化计算
// PBR_Mobile2.0.3   编辑器模式实时更新优化 - 增强Compute Buffer系统与编辑器集成，支持非运行模式下点光效果预览
// PBR_Mobile2.1.4   将光滑度控制改为粗糙度控制，更符合PBR标准工作流
// PBR_Mobile2.1.5   修复自发光被AO影响问题
// PBR_Mobile2.1.6   添加法线贴图Debug
// PBR_Mobile2.1.7   高光受阴影影响修正；修复pow除零警告
// PBR_Mobile2.1.8   优化高光不受BaseColor影响
// PBR_Mobile3.0    添加顶点阴影选项（进一步优化性能），剔除自身阴影的背面（优化效果）；修复自阴影算法；自发光颜色饱和度控制
// PBR_Mobile3.1    统一管理所有纹理采样结果；优化高光计算性能；优化自身阴影
// PBR_Mobile3.2    支持烘焙，添加Brightness参数
// PBR_Mobile3.3    校正基础光照；优化烘焙亮度
// PBR_Mobile3.4    添加烘焙虚拟高光（基于主光源方向，配合脚本快速匹配灯光方向）；简化阴影与深度Pass（减少变体）
// PBR_Mobile4.0    优化金属度算法（包括LIGHTMAP_ON、CalculateCustomPointLights）；优化反射融入固有色；修复自定义点光[loop]
// PBR_Mobile4.1    添加禁用环境光选项
// PBR_Mobile4.2 取消反射颜色
// PBR_Mobile5.0 添加SpotLight支持
// PBR_Mobile5.1 添加聚光灯纹理彩色光环
// PBR_Mobile5.2 添加UI脚本控制
// PBR_Mobile5.3 优化自身阴影平滑度，减少阶梯状硬边
// PBR_Mobile5.4 性能优化 - 预计算PBR属性，消除重复计算，优化光源循环
// PBR_Mobile5.5 完善自身阴影（使用Lambert光照作为遮罩来平滑阴影锯齿）
// PBR_Mobile5.6 修复反射被烘焙光照覆盖问题
// PBR_Mobile5.7 烘焙投影支持，使用Unity标准的Subtractive模式方法；优化偏移明暗交界线减少ShadowMap投影噪点
// PBR_Mobile5.8 优化高光亮度，移除specularColor削减；烘焙高光受实时阴影影响(暂时还原高光衰减)
// PBR_Mobile5.9 优化高光算法，高光随模型边缘形状挤压还原真实高光效果；增加金属反射对比
// PBR_Mobile6.0 完善所有效果，继承原始表现效果
// PBR_Mobile6.1 添加变色通道控制，MRA贴图的a通道作为基础颜色蒙版
// PBR_Mobile6.2 支持烘焙模式Shadowmask模式，修复该模式时使用顶点阴影时报错
// PBR_Mobile6.3 添加"禁用主光颜色"选项，取消勾选时使用默认白色
// PBR_Mobile6.4 添加Meta Pass支持烘焙器正确读取材质albedo和emission；修正GI合成公式分离间接漫反射与间接高光（与URP Lit能量分配一致）；Subtractive混合光照改用URP标准算法
// PBR_Mobile6.5 P0性能优化：MRA贴图条件采样（仅_USEMSAMAP开启时采样）、新增_DSIABLEBAKEDSPECULAR/_DISABLEINDIRECTSPECULAR开关（按需裁剪烘焙高光与间接高光计算）
// PBR_Mobile7.1 软阴影重构：等边三角形120°采样(中心+3点,减少到4次采样,固定权重2:1:1:1÷8)；顶点阴影/像素阴影互斥重构(_USEVERSHADOW激活时跳过shadow map采样)；修正权重归一化；添加ShadowMap边界检测(sc.z≤0排除范围外错误阴影)
// PBR_Mobile_NEW8.0 新增 GGXSpecularTerm（ARM Siggraph 2015 移动优化版），高光形状更宽、尾部更长，粗糙表面高光更自然
// PBR_Mobile_NEW8.1 间接高光：使用反射方向SH增强法线依赖（Lit反射探针的轻量替代）无光照贴图时额外采样反射方向SH，光滑度越高反射方向权重越大（物理正确）添加PBR参数重置按钮。
// PBR_Mobile_NEW8.2 优化"禁用环境光"半兰伯特参数控制效果。
// PBR_Mobile_NEW8.3 阴影高光衰减优化：高光在阴影处按拟合曲线 x²·(0.2+0.8x)（等价 x^2.8）衰减。
//                       用多项式拟合避开 fastPow 调用(2 mul + 1 mad vs log2+mul+exp2)；端点及一阶导数完美匹配。
//                       _ShadowScale降低时高光衰减更剧烈，解决暗部高光过亮问题；同时给原本不受阴影影响的间接高光(SH/反射方向采样)也加上衰减。
// PBR_Mobile_NEW8.4 PBR 粗糙度钳制对齐 URP 标准：perceptualRoughness 允许到 0（不再硬钳 0.01）。
// PBR_Mobile_NEW8.5 跨场景保护：新增全局安全位 _CustomLightSystemActive（由 ComputeBufferLightManager 独占写入），
//                       缓冲区释放时同步关闭，避免材质残留 _USEPOINTLIGHT/_USESPOTLIGHT 关键字时采样已销毁的
//                       StructuredBuffer 产生 NaN，导致材质变黑或整块 tile 被丢弃。
// PBR_Mobile_NEW8.6 添加反射贴图水平旋转偏移、水平镜像选项参数。
Shader "Custom/PBR_Mobile_NEW"
{
    Properties
    {
        [Toggle(_DISABLEENVIRONMENT)] _DisableEnvironment ("Disable Environment", Float) = 0
        [Toggle(_DISABLELIGHTCOLOR)] _DisableLightColor ("Disable LightColor", Float) = 0
        [Toggle(_USEVERSHADOW)] _UseVerShadow ("Use Vertex Shadow", Float) = 0
        [Toggle(_USESOFTSHADOW)] _UseSoftShadow ("Use Optimized Soft Shadow", Float) = 1
        [Header(1  (Base Properties))]
        [Space(5)]
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        [MainTexture] _BaseMap ("Albedo (RGB)", 2D) = "white" {}
        
        [Header(2  (Metallic Roughness AO))]
        [Space(5)]
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _Roughness ("Roughness", Range(0, 2)) = 1.0
        _SpecularScale ("Specular Scale", Range(0.01, 1)) = 1
        _HalfLambert ("Half Lambert", Range(0, 1)) = 0.4
        _ShadowScale ("Self Shadow Scale", Range(0, 1)) = 0.3
        _Softness ("Shadow Softness（纹素数）", Range(0, 4)) = 1.5
        _Brightness ("Brightness", Range(0.5, 20)) = 1.0
        _BakedSpecularDirection ("Baked Specular Direction", Vector) = (0, 0, 1)
        [Toggle(_USEMSAMAP)] _UseMsaMap ("Use Metallic Roughness Map", Float) = 0
        _MetallicGlossMap ("Metallic(R) Roughness(G) AO(B)", 2D) = "white" {}
        [Toggle(_USEAOMAP)] _UseAOMap ("Use AO(B) Channel", Float) = 0
        _OcclusionContrast  ("AO Contrast", Range(0, 2)) = 0.8
        _OcclusionStrength  ("AO Strength", Range(0, 1)) = 0.5
        [Toggle(_PREVIEWAO)] _PreviewAOMap ("Preview AO(B) Channel", Float) = 0

        // [Header(........................................................)]
        // [Space(5)]
        [Toggle(_NORMALMAP)] _UseNormalMap("Use Normal Map", Float) = 0
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0.001, 3)) = 1.0
        [Toggle(_FILPG)] _FilpG("Filp Green Channel", Float) = 0
        [Toggle(_DEBUGNORMAL)] _DebugNormal("Debug Normal Map", Float) = 0
        
        // [Header(........................................................)]
        // [Space(5)]
        [Toggle(_USEEMISSIONMAP)] _UseEmissionMap("Use Emission Map", Float) = 0
        [HDR]_EmissionColor ("Emission Color", Color) = (1,1,1,1)
        _EmissionMap ("Emission Map", 2D) = "white" {}
        _EmissionScale  ("Emission Scale", Range(0, 3)) = 1.0
        [Toggle(_INVERTEMISMAP)] _InvertEmisMap("Invert Emission Map", Float) = 0   
        
        // [Header(........................................................)]
        // [Space(5)]
        [Toggle(_USEREFLECTION)] _UseReflection("Use Reflection", Float) = 0
        [NoScaleOffset]_SphericalReflectionMap ("Spherical Reflection Map", 2D) = "white" {}
        _ReflectionStrength ("Reflection Strength", Range(0, 10)) = 2.0
        _ReflectionBlur ("Reflection Blur", Range(0, 6)) = 0.0
        _ReflectionRotation ("Reflection Rotation (反射球水平转动)", Range(-180, 180)) = 0.0
        [Toggle(_REFLECTIONMIRRORX)] _ReflectionMirrorX("Mirror Reflection X (水平镜像反射图)", Float) = 0
        [Space(5)]
        _ReflectionFresnelPower ("Fresnel Power", Range(0.1, 10)) = 1.6
        _ReflectionFresnelBias ("Fresnel Bias", Range(-0.4, 1)) = 0.3
        
        // [Header(........................................................)]
        // [Space(5)]
        [Toggle(_USEPOINTLIGHT)] _UsePointlight("Use Point Lighting", Float) = 0
        _PointLightIntensity ("Point Light Intensity", Range(0, 8)) = 1.0
        _PointLightRangeMultiplier ("Range Multiplier", Range(0.1, 3)) = 1.0
        _PointLightFalloff ("Falloff Power", Range(0.5, 8)) = 2.0
        _PointLightAmount ("Light Amount", Range(1, 8)) = 8

        // [Header(7  (Custom Spot Lights))]
        [Toggle(_USESPOTLIGHT)] _UseSpotlight("Use Spot Lighting", Float) = 0
        _SpotLightIntensity ("Spot Light Intensity", Range(0, 8)) = 1.0
        _SpotLightRangeMultiplier ("Range Multiplier", Range(0.1, 3)) = 1.0
        _SpotLightFalloff ("Falloff Power", Range(0.1, 2)) = 2.0
        _SpotLightAmount ("Light Amount", Range(1, 2)) = 2
        [Toggle(_USESPOTTEXTURE)] _UseSpotTexture("Use Spot Texture", Float) = 0
        _SpotTexture ("Spot Texture", 2D) = "white" {}
        _SpotTextureContrast ("Spot Texture Contrast", Range(0.1, 5)) = 1.0
        _SpotTextureSize ("Spot Texture Size", Range(0.1, 1)) = 0.5
        _SpotTextureIntensity ("Spot Texture Intensity", Range(0, 2)) = 1.0

        [Header(........................................................)]
        [Space(5)]
        [HideInInspector]_Cutoff("Alpha Cutoff", Range(0.001, 1.0)) = 0.5
        [Enum(Off,0,Front,1,Back,2)] _Cull ("Cull Mode", Float) = 2

        [Header(8  (Performance Toggles))]
        [Space(5)]
        [Toggle(_DISABLEBAKEDSPECULAR)] _DisableBakedSpecular ("Disable Baked Specular (烘焙高光)", Float) = 1
        [Toggle(_DISABLEINDIRECTSPECULAR)] _DisableIndirectSpecular ("Disable Indirect Specular (间接高光近似)", Float) = 1
        
        [HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "DisableBatching" = "False"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
            "ShaderGraphTargetId" = "UniversalUnlitSubTarget"
        }
        
        Pass
        {
            Name "Universal Forward"
            Tags
            {
                "Queue" = "Geometry"
                "RenderType" = "Opaque"
                "DisableBatching" = "False"
                "RenderPipeline" = "UniversalPipeline"
                "UniversalMaterialType" = "Unlit"
                "ShaderGraphTargetId" = "UniversalUnlitSubTarget"
            }
            
            // 深度写入和测试（修复阴影遮挡问题的关键）
            // ZWrite On
            // ZTest LEqual
            
            Cull[_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma shader_feature_local _USEEMISSIONMAP
            #pragma shader_feature_local _INVERTEMISMAP
            #pragma shader_feature_local _FILPG
            #pragma shader_feature_local _USEVERSHADOW
            #pragma shader_feature_local _USESOFTSHADOW
            #pragma shader_feature_local _USEMSAMAP
            #pragma shader_feature_local _USEAOMAP
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _USEPOINTLIGHT
            #pragma shader_feature_local _USESPOTLIGHT
            #pragma shader_feature_local _USEREFLECTION
            #pragma shader_feature_local _REFLECTIONMIRRORX
            #pragma shader_feature_local _PREVIEWAO
            #pragma shader_feature_local _DISABLEENVIRONMENT
            #pragma shader_feature_local _DISABLELIGHTCOLOR
            
            #pragma shader_feature_local _DEBUGNORMAL
            #pragma shader_feature_local _DISABLEBAKEDSPECULAR
            #pragma shader_feature_local _DISABLEINDIRECTSPECULAR
            
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            
            #pragma shader_feature_local _USESPOTTEXTURE
            
            #ifdef SHADER_API_MOBILE
                #define MAX_POINT_LIGHTS _PointLightAmount      
                #define USE_FAST_MATH 1
            #else
                #define MAX_POINT_LIGHTS 8      
                #define USE_FAST_MATH 0
            #endif

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            struct CustomPointLight
            {
                float3 position;
                float range;
                float4 color;          
                float4 parameters;     
            };

            struct CustomSpotLight
            {
                float3 position;      
                float range;          
                float4 color;         
                float3 direction;     
                float spotAngle;      
                float innerSpotAngle; 
                float falloff;        
                float padding;        
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap);
            SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);
            TEXTURE2D(_SphericalReflectionMap);
            SAMPLER(sampler_SphericalReflectionMap);
            TEXTURE2D(_SpotTexture);
            SAMPLER(sampler_SpotTexture);

            float4 _MainLightShadowmapTexture_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Metallic;
                half _Roughness;
                half _SpecularScale;
                half _ShadowScale;
                float _Softness;
                half _HalfLambert;
                half _Brightness;
                half _BumpScale;
                half _OcclusionContrast;
                half _OcclusionStrength;
                half _EmissionScale;
                half4 _EmissionColor;

                half _PointLightIntensity;
                half _PointLightRangeMultiplier;
                half _PointLightFalloff;
                half _PointLightAmount;

                half _SpotLightIntensity;
                half _SpotLightRangeMultiplier;
                half _SpotLightFalloff;
                half _SpotLightAmount;

                half _SpotTextureContrast;
                half _SpotTextureSize;
                half _SpotTextureIntensity;

                float3 _BakedSpecularDirection;

                float _ReflectionStrength;
                float _ReflectionBlur;
                float _ReflectionRotation;
                float _ReflectionFresnelPower;
                float _ReflectionFresnelBias;
            CBUFFER_END

            StructuredBuffer<CustomPointLight> _CustomPointLights;
            int _CustomPointLightCount;
            
            StructuredBuffer<CustomSpotLight> _CustomSpotLights;
            int _CustomSpotLightCount;

            // ● 全局安全位：由 ComputeBufferLightManager 独占写入，1 = 灯光系统已接管，0 = 未接管
            //   场景切换/管理器销毁后，材质关键字与 _CustomPointLightCount 可能残留上一场景的值，
            //   但缓冲区已被释放。此时若仍进入光照循环会读到无效数据产生 NaN，导致材质变黑甚至整块 tile 被丢弃。
            //   此守卫确保：只要没有活跃的管理器，自定义光照一律跳过，退化为普通材质。
            float _CustomLightSystemActive;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 positionWS_shadow : TEXCOORD1; // xyz = positionWS, w = shadowAttenuation (or 1.0)
                float4 normalWS_ndotl : TEXCOORD2;    // xyz = normalWS, w = NdotL (or 0.0)
                float4 viewDirWS_fog : TEXCOORD3;     // xyz = viewDirWS, w = fogFactor
                
                #if defined(_NORMALMAP)
                float4 tangentWS : TEXCOORD4; 
                #endif
                
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 5);
            };

            #if USE_FAST_MATH
            half fastPow(half x, half n) {
                return exp2(n * log2(x));
            }
            #else
            #define fastPow pow
            #endif

            float2 fastSphericalUV(float3 reflectionVector) {
                reflectionVector = normalize(reflectionVector);

                // _ReflectionRotation：绕世界竖直轴（Y）旋转反射向量，模拟反射球水平转动。
                // 贴图为球面反射截图，直接平移 uv 只会线性滑动窗口；
                // 旋转反射向量后 x/z 分量按球面关系联动，转动效果更接近真实转球。
                // Rotation = 0 时与原算法输出完全一致，不影响存量材质。
                float rotAngle = radians(_ReflectionRotation);
                float s, c;
                sincos(rotAngle, s, c);
                reflectionVector = float3(
                    reflectionVector.x * c - reflectionVector.z * s,
                    reflectionVector.y,
                    reflectionVector.x * s + reflectionVector.z * c
                );

                float2 uv = float2(
                    reflectionVector.x / 4.01 + 0.5,
                    reflectionVector.y / 4.01 + 0.5
                );

                // _REFLECTIONMIRRORX：水平镜像反射贴图（uv.x 取反）
                #ifdef _REFLECTIONMIRRORX
                    uv.x = 1.0 - uv.x;
                #endif

                return uv;
            }

            half fastAttenuation(float distance, float range) {
                half d = saturate(distance / range);
                return 1.0 - d * d; 
            }

            half3 AdjustSaturation(half3 color, half saturation) {
                
                half luminance = dot(color, half3(0.299, 0.587, 0.114));
                
                return lerp(half3(luminance, luminance, luminance), color, saturation);
            }

            half3 AdjustContrast(half3 color, half contrast) {
                
                return saturate((color - 0.5) * contrast + 0.5);
            }

            float3 SampleSphericalReflection(float3 reflectionVector, float blur)
            {
                float2 uv = fastSphericalUV(reflectionVector);
                
                float3 reflectionColor = SAMPLE_TEXTURE2D_LOD(_SphericalReflectionMap, sampler_SphericalReflectionMap, uv, blur).rgb;
                return reflectionColor;
            }

            float CalculateFresnel(float3 normalWS, float3 viewDirWS, float power, float bias)
            {
                float fresnel = saturate(dot(normalWS, viewDirWS));
                fresnel = saturate(bias + (1.0 - bias) * fastPow(1.0 - fresnel, power));
                return fresnel;
            }

            float3 CalculateSphericalReflection(float3 normalWS, float3 viewDirWS, float metallic, float roughness, float2 uv, half3 mainLightColor)
            {
                
                float3 reflectionVector = reflect(-viewDirWS, normalWS);
                
                float3 reflectionColor = SampleSphericalReflection(reflectionVector, _ReflectionBlur);
                
                // 增加反射贴图的对比强度
                // reflectionColor *= reflectionColor;
                
                float fresnel = CalculateFresnel(normalWS, viewDirWS, _ReflectionFresnelPower, _ReflectionFresnelBias);
                
                float smoothness = 1.0 - roughness; 
                float reflectionIntensity = metallic * smoothness * fresnel;
                
                // 减少主光源亮度对反射的影响，在烘焙场景中也能正常显示反射
                half mainLightLuminance = dot(mainLightColor, half3(0.299, 0.587, 0.114));
                half lightInfluence = saturate(mainLightLuminance * 0.5 + 0.5); // 降低影响，确保最小值为0.5
                reflectionIntensity *= lightInfluence;
                
                return reflectionColor*_ReflectionStrength * reflectionIntensity;
            }

            half3 SimpleDiffuse(half3 normalWS, half3 lightDir, half3 lightColor)
            {
                // 根据 _DisableEnvironment（禁用环境光）开关选择算法：
                //   _DISABLEENVIRONMENT 启用（禁用环境光）→ 整个暗部受 _HalfLambert 比例控制（max 公式）
                //   否则（启用环境光）                → 传统 PBR_Mobile 算法（NdotL * (1-x) + x）
                #ifdef _DISABLEENVIRONMENT
                    // === 新版（禁用环境光时）：暗部整体受控 ===
                    // 保留有符号 NdotL，让暗部不同法线方向保留细微差异
                    half NdotL = dot(normalWS, lightDir);
                    // max 取『亮部标准 Lambert』与『暗部整体填充』两者中较大值：
                    //   亮部 (NdotL > 0)：saturate(NdotL) 主导 → 完全不受 _HalfLambert 影响，标准 Lambert
                    //   暗部 (NdotL <= 0)：darkPart 主导，按 (1 - NdotL) * 0.5 * x 整体比例填充
                    //     NdotL = 0 (明暗交界) → 0.5 * x（受 _HalfLambert 控制）
                    //     NdotL = -1 (背光极)  → x（受 _HalfLambert 控制）
                    half darkPart = (1.0 - NdotL) * 0.5 * _HalfLambert + (0.5 * _HalfLambert);
                    return lightColor * max(saturate(NdotL), darkPart);
                #else
                    // === 传统算法（启用环境光时）：与 PBR_Mobile.shader 保持完全一致 ===
                    half NdotL = dot(normalWS, lightDir);
                    half halfLambertEffect = NdotL * (1.0 - _HalfLambert) + _HalfLambert;
                    return lightColor * saturate(halfLambertEffect);
                #endif
            }

            struct MaterialProperties
            {
                half3 albedo;           
                half4 baseColor;        
                half4 mraSample;        
                half metallic;          
                half roughness;         
                half aoValue;
                half3 normalTS;         
                half3 normalWS;         
                half3 emissionMap;      
                float2 uv;
                
                // 预计算的PBR属性（性能优化）
                half3 diffuseColor;     // 预计算的漫反射颜色
                half3 specularColor;    // 预计算的高光颜色 (F0)
                half perceptualRoughness; // 感知粗糙度 (0-1)
                half alphaRoughness;    // alpha = perceptualRoughness^2 (GGX使用)
                half alpha2;            // alpha^2
                half alpha2MinusOne;    // alpha^2 - 1
                half oneMinusMetallic;  // 预计算的 1-metallic
            };

            // PBR 粗糙度钳制下限（对齐 URP InitializeBRDFData 的双重 HALF 钳制）
            //   MIN_ROUGHNESS  = HALF_MIN_SQRT ≈ 7.81e-3 — 防止 alphaRoughness 太小让 GGX D 项退化为 delta
            //                                                  （解决"粗糙度为零时高光不可见"）
            //   MIN_ROUGHNESS2 = HALF_MIN    ≈ 6.10e-5 — 防止 alpha2 在 fp16 下溢出到 0
            // MIN_ROUGHNESS2 = (MIN_ROUGHNESS)² 在数学上等价于 HALF_MIN
            // 注意：必须定义在 InitMaterialProperties 之前（HLSL 编译器逐语句解析，不前向追溯宏定义）
            #define MIN_ROUGHNESS  7.8125e-3h
            #define MIN_ROUGHNESS2 6.1035e-5h

            MaterialProperties InitMaterialProperties(float2 uv, half3 worldNormal)
            {
                MaterialProperties mat;
                mat.uv = uv;
                
                mat.baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                #ifdef _USEMSAMAP
                    // 仅在使用 MRA 贴图时才采样，避免不必要的带宽/缓存开销
                    mat.mraSample = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
                #else
                    // 未开启 MRA 贴图时，蒙版通道 a 视为 1（即使用 _BaseColor 调色）
                    mat.mraSample = half4(1, 1, 1, 1);
                #endif
                mat.albedo = lerp(mat.baseColor.rgb, mat.baseColor.rgb * _BaseColor.rgb, mat.mraSample.a);
                
                mat.metallic = _Metallic;
                mat.roughness = _Roughness;
                mat.aoValue = 1.0;
                
                #ifdef _USEMSAMAP
                    mat.metallic *= mat.mraSample.r;
                    mat.roughness *= mat.mraSample.g;
                #endif

                #if defined(_USEMSAMAP) && defined(_USEAOMAP)
                    mat.aoValue = mat.mraSample.b;
                #endif

                mat.normalWS = worldNormal;
                mat.normalTS = half3(0, 0, 1);
                
                #ifdef _USEEMISSIONMAP
                    mat.emissionMap = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb;
                #else
                    mat.emissionMap = half3(0, 0, 0);
                #endif
                
                // 预计算PBR属性（性能优化 - 避免在每个光源中重复计算）
                mat.oneMinusMetallic = 1.0 - mat.metallic;
                
                // 标准PBR粗糙度参数（用于GGX BRDF，对齐 URP InitializeBRDFData）
                //   perceptualRoughness 允许到 0（让 smoothness=1 不被破坏）
                //   alphaRoughness = perceptualRoughness²，钳到 MIN_ROUGHNESS (HALF_MIN_SQRT)
                //     —— 防止 GGX D 项(α²/d²)在 roughness=0 时退化成 delta 函数，
                //        导致偏离完美反射方向的高光瞬衰到 0，即"高光不可见"
                //   alpha2 = alphaRoughness²，钳到 MIN_ROUGHNESS2 (HALF_MIN)
                //     —— 防止 fp16 下溢到 0，保持最坏情形数值稳定
                mat.perceptualRoughness = saturate(mat.roughness);
                mat.alphaRoughness = max(mat.perceptualRoughness * mat.perceptualRoughness, MIN_ROUGHNESS);
                mat.alpha2 = max(mat.alphaRoughness * mat.alphaRoughness, MIN_ROUGHNESS2);
                mat.alpha2MinusOne = mat.alpha2 - 1.0;
                
                // 标准PBR能量守恒漫反射
                half oneMinusReflectivity = 0.96 * mat.oneMinusMetallic;
                mat.diffuseColor = mat.albedo * oneMinusReflectivity;
                
                // 标准PBR F0 (非金属0.04，金属用albedo)
                mat.specularColor = lerp(half3(0.04, 0.04, 0.04), mat.albedo, mat.metallic);
                
                return mat;
            }

            void ApplyNormalMap(inout MaterialProperties mat, Varyings input)
            {
                #if defined(_NORMALMAP)
                    half4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, mat.uv);
                    #ifdef _FILPG
                        normalSample.g = 1 - normalSample.g;
                    #endif
                    mat.normalTS = UnpackNormal(normalSample);
                    mat.normalTS.xy *= _BumpScale;
                    mat.normalTS = normalize(mat.normalTS);
                    
                    float3 tangentWS = normalize(input.tangentWS.xyz);
                    float3 bitangentWS = cross(mat.normalWS, tangentWS) * input.tangentWS.w;
                    float3x3 TBN = float3x3(tangentWS, bitangentWS, mat.normalWS);
                    mat.normalWS = normalize(mul(mat.normalTS, TBN));
                #endif
            }

            // GGX高光项 (ARM Siggraph 2015移动优化: D*V*F合并近似)
            // 参考URP DirectBRDFSpecular移动端实现
            half GGXSpecularTerm(half NoH, half LoH, half alphaRoughness, half alpha2, half alpha2MinusOne)
            {
                half d = NoH * NoH * alpha2MinusOne + 1.00001h;
                half specularTerm = alpha2 / ((d * d) * max(0.1h, LoH * LoH) * (alphaRoughness * 4.0h + 2.0h));
                
                #ifdef SHADER_API_MOBILE
                specularTerm = specularTerm - HALF_MIN;
                specularTerm = clamp(specularTerm, 0.0h, 100.0h);
                #endif
                
                return specularTerm;
            }

            half3 SimpleSpecular(half3 normalWS, half3 lightDir, half3 viewDir, MaterialProperties mat, half3 lightColor)
            {
                half3 halfDir = normalize(lightDir + viewDir);
                half NoH = saturate(dot(normalWS, halfDir));
                half LoH = saturate(dot(lightDir, halfDir));
                half NoL = saturate(dot(normalWS, lightDir));
                
                half specularTerm = GGXSpecularTerm(NoH, LoH, mat.alphaRoughness, mat.alpha2, mat.alpha2MinusOne);
                // lightColor 已包含 distanceAttenuation 和 shadowAttenuation，高光被阴影正确遮挡一次（与URP Lit一致）
                half3 specular = mat.specularColor * specularTerm * NoL * lightColor;
                
                return specular;
            }
            
            half3 BakedSpecular(half3 normalWS, half3 lightDir, half3 viewDir, MaterialProperties mat, half3 bakedGI, half shadowAttenuation)
            {
                half3 finalLightDir = lightDir;
                if (length(_BakedSpecularDirection) > 0.001)
                {
                    finalLightDir = normalize(_BakedSpecularDirection);
                }
                
                half3 halfDir = normalize(finalLightDir + viewDir);
                half NoH = saturate(dot(normalWS, halfDir));
                half LoH = saturate(dot(finalLightDir, halfDir));
                half NoL = saturate(dot(normalWS, finalLightDir));
                
                half specularTerm = GGXSpecularTerm(NoH, LoH, mat.alphaRoughness, mat.alpha2, mat.alpha2MinusOne);
                
                half metallicFactor = mat.metallic * mat.metallic; 
                half3 adjustedBakedGI = lerp(bakedGI, half3(1, 1, 1), metallicFactor);
                
                // 烘焙高光也应该受实时阴影影响
                return adjustedBakedGI * specularTerm * NoL * _SpecularScale * shadowAttenuation;
            }

            half3 CalculateCustomPointLights(float3 worldPos, MaterialProperties mat, float3 viewDir, half specularShadowMask)
            {
                half3 totalLight = half3(0, 0, 0);

                if (_CustomPointLightCount == 0) return totalLight;

                int lightCount = min(_CustomPointLightCount, MAX_POINT_LIGHTS);

                // 预计算是否需要高光（避免在循环中判断）
                bool needSpecular = (mat.metallic > 0.1 || mat.roughness < 0.9);

                [loop]for (int i = 0; i < lightCount; i++)
                {
                    CustomPointLight light = _CustomPointLights[i];

                    float3 lightVector = light.position - worldPos;
                    float distance = length(lightVector);
                    float3 lightDir = lightVector / max(distance, 0.001);

                    float effectiveRange = light.range * _PointLightRangeMultiplier;
                    if (distance > effectiveRange) continue;

                    float distanceRatio = distance / effectiveRange;
                    bool useSimpleCalculation = distanceRatio > 0.7;

                    half attenuation = saturate(1.0 - fastPow(max((distance / effectiveRange),0.01), _PointLightFalloff));

                    half3 lightColor = light.color.rgb * light.color.a * _PointLightIntensity * attenuation;

                    // 漫反射计算
                    half NdotL = saturate(dot(mat.normalWS, lightDir));
                    half3 diffuse = lightColor * NdotL;

                    // 高光计算（使用预计算的材质属性）
                    half3 specular = 0;
                    if (!useSimpleCalculation && needSpecular)
                    {
                        half3 halfDir = normalize(lightDir + viewDir);
                        half NoH = saturate(dot(mat.normalWS, halfDir));
                        half LoH = saturate(dot(lightDir, halfDir));

                        // 复用漫反射已计算的NdotL，避免重复点积
                        half specularTerm = GGXSpecularTerm(NoH, LoH, mat.alphaRoughness, mat.alpha2, mat.alpha2MinusOne) * attenuation;

                        // 高光直接使用lightColor，但受主光阴影遮挡（场景阴影区域高光同步衰减）
                        // specularShadowMask 已应用阴影高光衰减曲线（指数 2.8），_ShadowScale 越低时衰减越剧烈
                        specular = lightColor * specularTerm * NdotL * specularShadowMask;
                    }

                    // 漫反射使用diffuseColor，高光直接使用不削减
                    totalLight += mat.diffuseColor * diffuse + mat.specularColor * specular * _SpecularScale;
                }

                return totalLight;
            }

            half3 CalculateCustomSpotLights(float3 worldPos, MaterialProperties mat, float3 viewDir, half specularShadowMask)
            {
                half3 totalLight = half3(0, 0, 0);
                
                if (_CustomSpotLightCount == 0) return totalLight;
                
                int lightCount = min(_CustomSpotLightCount, 2);
                
                // 预计算是否需要高光（避免在循环中判断）
                bool needSpecular = (mat.metallic > 0.1 || mat.roughness < 0.9);
                
                [unroll(2)]for (int i = 0; i < lightCount; i++)
                {
                    CustomSpotLight light = _CustomSpotLights[i];
                    
                    float3 lightVector = light.position - worldPos;
                    float distance = length(lightVector);
                    float3 lightDir = lightVector / max(distance, 0.001); 
                    
                    float effectiveRange = light.range * _SpotLightRangeMultiplier;
                    if (distance > effectiveRange) continue;
                    
                    float cosOuterAngle = cos(light.spotAngle * 0.5 * 0.0174533); 
                    float cosInnerAngle = cos(light.innerSpotAngle * 0.5 * 0.0174533);
                    
                    float cosAngle = dot(-lightDir, light.direction);
                    if (cosAngle < cosOuterAngle) continue; 
                    
                    float spotAttenuation = 1.0;
                    if (cosAngle < cosInnerAngle)
                    {
                        
                        spotAttenuation = (cosAngle - cosOuterAngle) / (cosInnerAngle - cosOuterAngle);
                    }
                    
                    half distanceAttenuation = saturate(1.0 - fastPow(max((distance / effectiveRange),0.01), _SpotLightFalloff));
                    
                    half3 lightColor = light.color.rgb * light.color.a * _SpotLightIntensity * distanceAttenuation * spotAttenuation;
                    
                    half3 textureModulation = half3(1.0, 1.0, 1.0);
                    #ifdef _USESPOTTEXTURE
                        
                        float3 lightToSurface = normalize(worldPos - light.position);
                        float3 lightRight = normalize(cross(light.direction, float3(0, 1, 0)));
                        if (length(lightRight) < 0.001)
                        {
                            lightRight = normalize(cross(light.direction, float3(1, 0, 0)));
                        }
                        float3 lightUp = normalize(cross(lightRight, light.direction));
                        
                        float2 spotUV;
                        spotUV.x = dot(lightToSurface, lightRight) * 0.5 + 0.5;
                        spotUV.y = dot(lightToSurface, lightUp) * 0.5 + 0.5;
                        
                        float textureScale = 1.0 / _SpotTextureSize;
                        spotUV = (spotUV - 0.5) * textureScale + 0.5;
                        
                        half3 spotTexture = SAMPLE_TEXTURE2D_LOD(_SpotTexture, sampler_SpotTexture, spotUV, 0).rgb;
                        
                        half3 contrastAdjusted = saturate((spotTexture - 0.5) * _SpotTextureContrast + 0.5);
                        
                        textureModulation = contrastAdjusted * _SpotTextureIntensity;
                    #endif
                    
                    // 漫反射计算
                    half NdotL = saturate(dot(mat.normalWS, lightDir));
                    half3 diffuse = lightColor * NdotL * textureModulation;
                    
                    // 高光计算（使用预计算的材质属性）
                    half3 specular = 0;
                    if (needSpecular) 
                    {
                        half3 halfDir = normalize(lightDir + viewDir);
                        half NoH = saturate(dot(mat.normalWS, halfDir));
                        half LoH = saturate(dot(lightDir, halfDir));
                        
                        // 复用漫反射已计算的NdotL，避免重复点积
                        half specularTerm = GGXSpecularTerm(NoH, LoH, mat.alphaRoughness, mat.alpha2, mat.alpha2MinusOne) * distanceAttenuation * spotAttenuation;

                        // 高光直接使用lightColor和纹理调制，但受主光阴影遮挡（场景阴影区域高光同步衰减）
                        // specularShadowMask 已应用阴影高光衰减曲线（指数 2.8），_ShadowScale 越低时衰减越剧烈
                        specular = lightColor * specularTerm * NdotL * textureModulation * specularShadowMask;
                    }
                    
                    // 漫反射使用diffuseColor，高光衰减
                    totalLight += mat.diffuseColor * diffuse + mat.specularColor * specular * _SpecularScale;
                }
                
                return totalLight;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                
                output.positionCS = vertexInput.positionCS;
                output.positionWS_shadow = float4(vertexInput.positionWS, 1.0);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS_ndotl = float4(normalInput.normalWS, 0.0);
                output.viewDirWS_fog = float4(GetCameraPositionWS() - vertexInput.positionWS, ComputeFogFactor(vertexInput.positionCS.z));
                #ifdef _USEVERSHADOW
                    half NdotL = 0;
                    half shadowAttenuation = 1.0;
                    
                    if (_ShadowScale < 0.88)
                    {
                        float4 shadowCoord = TransformWorldToShadowCoord(output.positionWS_shadow.xyz);
                        shadowCoord.w = max(shadowCoord.w, 0.001);
                        Light mainLight = GetMainLight(shadowCoord);
                        
                        // Shadowmask 不在 vert 中采样（vs_4_0 顶点纹理采样指令受限）
                        // 改为在 frag 中融合 Shadowmask 结果
                        
                        NdotL = dot(output.normalWS_ndotl.xyz, mainLight.direction);
                        half baseShadow = mainLight.shadowAttenuation;
                        
                        half lambertMask = saturate(NdotL * 0.5 + 0.5);
                        half shadowEdge = saturate((baseShadow - 0.3) / 0.4);
                        half smoothedShadow = lerp(baseShadow, lambertMask, shadowEdge * (1.0 - shadowEdge) * shadowEdge);
                        
                        shadowAttenuation = lerp(smoothedShadow, 1.0, _ShadowScale);
                        
                        half backfaceRange = lerp(0.0, 1.0, lambertMask);
                        half backfaceFactor = smoothstep(-backfaceRange, backfaceRange, NdotL);
                        shadowAttenuation = lerp(1.0, shadowAttenuation, backfaceFactor);
                    }
                    else
                    {
                        Light mainLight = GetMainLight();
                        NdotL = dot(output.normalWS_ndotl.xyz, mainLight.direction);
                    }
                    
                    output.positionWS_shadow.w = shadowAttenuation;
                    output.normalWS_ndotl.w = NdotL;
                #endif
                
                #if defined(_NORMALMAP)
                output.tangentWS = float4(normalInput.tangentWS, input.tangentOS.w);
                #endif
                
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS_ndotl.xyz, output.vertexSH);
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                
                MaterialProperties mat = InitMaterialProperties(input.uv, normalize(input.normalWS_ndotl.xyz));
                
                ApplyNormalMap(mat, input);
                
                half3 viewDirWS = normalize(input.viewDirWS_fog.xyz);

                half3 diffuse = 0;
                half3 specular = 0;
                half shadowAttenuation = 1;
                half3 lightColor = 0;
                half3 lightDir = 0;
                
#ifdef _USEVERSHADOW
                // 顶点阴影模式：跳过 shadow map 采样，直接使用顶点预计算阴影
                Light mainLight = GetMainLight();
                lightDir = mainLight.direction;
                
                if (_ShadowScale < 0.88)
                {
                    half vertShadow = input.positionWS_shadow.w;
                    #if defined(SHADOWS_SHADOWMASK) && defined(LIGHTMAP_ON)
                        // Shadowmask 模式：取顶点阴影和烘焙遮罩的较暗值
                        half4 shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
                        shadowAttenuation = min(vertShadow, shadowMask.r);
                    #else
                        shadowAttenuation = vertShadow;
                    #endif
                }
#else
                // 像素阴影模式
#ifdef _USESOFTSHADOW
                // ===== 软阴影 =====
                // 算法：UV 空间等边三角形采样（1 中心 + 3 点 120° 均布），仅 1 次 TransformWorldToShadowCoord
                // 固定权重 2:1:1:1 ÷ 5 → 中心 40%，周边各 20%，羽化更强、归一化正确
                Light mainLight = GetMainLight();
                lightDir = mainLight.direction;
                {
                    // 世界坐标 → ShadowCoord（仅 1 次，4 层采样共享）
                    float4 sc = TransformWorldToShadowCoord(input.positionWS_shadow.xyz);
                    float2 texelSize = _MainLightShadowmapTexture_TexelSize.xy;
                    float radius = _Softness * texelSize.x;    // 采样半径（纹素单位）

                    // 采样 1：中心点（权重 2）
                    float shadow = SAMPLE_TEXTURE2D_SHADOW(
                        _MainLightShadowmapTexture, sampler_LinearClampCompare, sc) * 2.0;
                    // 采样 2：0°（右侧，权重 1）
                    shadow += SAMPLE_TEXTURE2D_SHADOW(
                        _MainLightShadowmapTexture, sampler_LinearClampCompare,
                        float4(sc.xy + float2(radius, 0), sc.zw));
                    // 采样 3：120°（左上，cos120°=-0.5, sin120°≈0.866，权重 1）
                    shadow += SAMPLE_TEXTURE2D_SHADOW(
                        _MainLightShadowmapTexture, sampler_LinearClampCompare,
                        float4(sc.xy + float2(-0.5 * radius, 0.866 * radius), sc.zw));
                    // 采样 4：240°（左下，cos240°=-0.5, sin240°≈-0.866，权重 1）
                    shadow += SAMPLE_TEXTURE2D_SHADOW(
                        _MainLightShadowmapTexture, sampler_LinearClampCompare,
                        float4(sc.xy + float2(-0.5 * radius, -0.866 * radius), sc.zw));

                    // 超出阴影贴图范围（sc.z <= 0）时维持无阴影状态
                    half inRange = (sc.z > 0.0) ? 1.0 : 0.0;
                    mainLight.shadowAttenuation = lerp(1.0, shadow / 5.0, inRange);
                }
#else
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS_shadow.xyz);
                Light mainLight = GetMainLight(shadowCoord);
                lightDir = mainLight.direction;
#endif
                
                // ShadowMask：取软阴影和烘焙遮罩的较暗值
                #if defined(LIGHTMAP_ON) && defined(SHADOWS_SHADOWMASK)
                    half4 shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
                    mainLight.shadowAttenuation = min(mainLight.shadowAttenuation, shadowMask.r);
                #endif
                
                    if (_ShadowScale < 0.88)
                    {
                        half baseShadow = mainLight.shadowAttenuation;
                        half pixelNdotL = dot(mat.normalWS, lightDir);
                        
                        // 方法：通过偏移pixelNdotL来调整明暗交界线
                        // 正值偏移 → 交界线向暗部移动（亮部扩大）
                        // 负值偏移 → 交界线向亮部移动（暗部扩大）
                        pixelNdotL = pixelNdotL - 0.1;  // 向白色部分偏移（亮部缩小）
                        
                        // 使用Lambert光照作为遮罩来平滑阴影锯齿
                        half lambertMask = saturate(pixelNdotL * 0.5 + 0.5);
                        
                        // 使用pow让明暗交界线向白色部分偏移（暗部收缩，亮部扩大）
                        // 指数越大，交界线越向暗部移动
                        lambertMask = fastPow(lambertMask, 2);
                        
                        // 检测阴影边界并应用Lambert遮罩平滑
                        half shadowEdge = saturate((baseShadow - 0.3) / 0.4);
                        half smoothedShadow = lerp(baseShadow, lambertMask, shadowEdge * (1.0 - shadowEdge) * shadowEdge);
                        
                        // 应用阴影强度控制
                        shadowAttenuation = lerp(smoothedShadow, 1.0, _ShadowScale);
                        
                        // 背面剔除优化 - 使用与Half Lambert协调的范围
                        half backfaceRange = lerp(0.0, 1.0, lambertMask);
                        half backfaceFactor = smoothstep(-backfaceRange, backfaceRange, pixelNdotL);
                        shadowAttenuation = lerp(1.0, shadowAttenuation, backfaceFactor);
                    }
                    // 当 _ShadowScale >= 0.88 时，shadowAttenuation 保持为 1.0（已在上面初始化）
#endif

                // 阴影高光衰减曲线：让高光在阴影处的衰减比漫反射更剧烈，避免暗部高光过亮
                // 等价于 x^2.8 的多项式拟合：f(x) = x² · (0.2 + 0.8·x) = 0.2·x² + 0.8·x³
                //   端点匹配：f(0)=0, f(1)=1, f'(1)=2.8（与 x^2.8 一阶导数完全相同）
                //   中段误差：x∈[0.4, 1.0] 误差均 < 4%，视觉无感
                // 性能优势：2 mul + 1 mad，避开 fastPow(≈ log2+mul+exp2)，省 1-2 ALU
                // 仅作用于各类高光（直接/间接/烘焙/自定义光源），不影响漫反射
                half x = saturate(shadowAttenuation);
                half specularShadowMask = x * x * (0.2 + 0.8 * x);

                #ifndef _DISABLEENVIRONMENT
                // _DISABLELIGHTCOLOR：漫反射去色只保留强度，高光保留原始18颜色影响金属度
                #ifdef _DISABLELIGHTCOLOR
                half lightIntensity1 = max(mainLight.color.r, max(mainLight.color.g, mainLight.color.b));
                lightColor = half3(lightIntensity1, lightIntensity1, lightIntensity1) * mainLight.distanceAttenuation * shadowAttenuation;
                half3 specularLightColor1 = mainLight.color * mainLight.distanceAttenuation * shadowAttenuation;
                #else
                lightColor = mainLight.color * mainLight.distanceAttenuation * shadowAttenuation;
                half3 specularLightColor1 = lightColor;
                #endif
                diffuse = SimpleDiffuse(mat.normalWS, lightDir, lightColor);
                specular = SimpleSpecular(mat.normalWS, lightDir, viewDirWS, mat, specularLightColor1);
                // 应用阴影高光衰减曲线：暗部高光按指数衰减（曲线>1 时比漫反射衰减更剧烈）
                specular *= specularShadowMask;
                
                // 计算环境光（烘焙光照或球谐光照）
                half3 bakedGI = 0;
                
                #ifdef LIGHTMAP_ON
                    // 采样烘焙光照
                    bakedGI = SampleLightmap(input.lightmapUV, mat.normalWS);
                #else
                    // 没有lightmap，使用球谐光照
                    bakedGI = SampleSH(mat.normalWS);
                #endif
                
                // Subtractive模式：使用URP标准方法从lightmap中减去主光贡献
                #if defined(LIGHTMAP_ON) && defined(LIGHTMAP_SHADOW_MIXING) && !defined(SHADOWS_SHADOWMASK)
                    half contributionTerm = saturate(dot(lightDir, mat.normalWS));
                    half3 lambert = mainLight.color * contributionTerm;
                    half3 estimatedLightContribution = lambert * (1.0 - mainLight.shadowAttenuation);
                    half3 subtractedLightmap = bakedGI - estimatedLightContribution;
                    half3 realtimeShadow = max(subtractedLightmap, unity_ShadowColor.xyz);
                    realtimeShadow = lerp(bakedGI, realtimeShadow, GetMainLightShadowStrength());
                    bakedGI = min(bakedGI, realtimeShadow);
                #endif
                
                // 间接漫反射：bakedGI只作用于漫反射通道（与URP Lit一致的能量分配）
                half3 ambient = bakedGI * mat.diffuseColor;
                #ifndef _DISABLEINDIRECTSPECULAR
                // 间接高光：使用反射方向SH增强法线依赖（Lit反射探针的轻量替代）
                // 无光照贴图时额外采样反射方向SH，光滑度越高反射方向权重越大（物理正确）
                half NoV = saturate(dot(mat.normalWS, viewDirWS));
                half fresnelTerm = fastPow(1.0 - NoV, 4.0);
                half smoothnessVal = 1.0 - mat.perceptualRoughness;
                
                half3 indirectSpecularBase = bakedGI;
                #ifndef LIGHTMAP_ON
                    half3 reflectDir = reflect(-viewDirWS, mat.normalWS);
                    half3 envSpecularSH = SampleSH(reflectDir);
                    indirectSpecularBase = lerp(bakedGI, envSpecularSH, smoothnessVal);
                #endif
                
                half3 indirectSpecular = indirectSpecularBase * mat.specularColor * (fresnelTerm * smoothnessVal + mat.metallic * 0.15);
                // 应用阴影高光衰减曲线：间接高光（SH/反射方向采样）原本不受阴影影响，是暗部高光偏亮的主要原因
                indirectSpecular *= specularShadowMask;
                ambient += indirectSpecular;
                #endif

                // 合成最终颜色：环境光 + 实时光照
                // 注意：specular已包含mat.specularColor (GGX BRDF内部已乘)
                half3 finalColor = ambient + mat.diffuseColor * diffuse + specular * _SpecularScale;

                // 烘焙高光（保留原来的效果，但不削减亮度，且受阴影影响）
                half3 bakedSpecular = 0;
                #if defined(LIGHTMAP_ON) && !defined(_DISABLEBAKEDSPECULAR)
                    bakedSpecular = BakedSpecular(mat.normalWS, lightDir, viewDirWS, mat, bakedGI, shadowAttenuation);
                    // 应用阴影高光衰减曲线：烘焙高光在阴影深处按指数衰减，避免暗部烘焙高光过亮
                    bakedSpecular *= specularShadowMask;
                    finalColor += mat.specularColor * bakedSpecular;
                #endif
                #else
                // 禁用环境光时，只使用实时光照
                #ifdef _DISABLELIGHTCOLOR
                half lightIntensity2 = max(mainLight.color.r, max(mainLight.color.g, mainLight.color.b));
                lightColor = half3(lightIntensity2, lightIntensity2, lightIntensity2) * mainLight.distanceAttenuation * shadowAttenuation;
                #else
                lightColor = mainLight.color * mainLight.distanceAttenuation * shadowAttenuation;
                #endif
                diffuse = SimpleDiffuse(mat.normalWS, lightDir, lightColor);
                specular = SimpleSpecular(mat.normalWS, lightDir, viewDirWS, mat, lightColor);
                // 应用阴影高光衰减曲线：暗部高光按指数衰减
                specular *= specularShadowMask;

                // 注意：specular已包含mat.specularColor (GGX BRDF内部已乘)
                half3 finalColor = mat.diffuseColor * diffuse + specular * _SpecularScale;
                #endif 
                
                finalColor *= _Brightness;
                
                half3 reflectionContrib = 0;
                #ifdef _USEREFLECTION
                    reflectionContrib = CalculateSphericalReflection(mat.normalWS, viewDirWS, mat.metallic, mat.roughness, mat.uv, lightColor);
                    // 反射应该混合而不是累加，金属度高的材质反射替换漫反射
                    finalColor = lerp(finalColor, finalColor + reflectionContrib *mat.albedo, mat.metallic);
                    // 非金属材质的反射叠加
                    finalColor += (reflectionContrib * (1.0 - mat.metallic) * 0.5);
                #endif
                
                #if defined(_USEMSAMAP) && defined(_USEAOMAP)
                    mat.aoValue = saturate(fastPow(max(mat.aoValue,0.01), _OcclusionContrast));
                    mat.aoValue = lerp(1.0, mat.aoValue, _OcclusionStrength);
                    finalColor *= mat.aoValue;
                #endif
                
                half3 pointLightContrib = 0;
                #ifdef _USEPOINTLIGHT
                    // ● 安全位守卫：缓冲区可能已被释放，仅在系统接管时才采样，避免 NaN 污染 finalColor
                    if (_CustomLightSystemActive > 0.5)
                    {
                        pointLightContrib = CalculateCustomPointLights(input.positionWS_shadow.xyz, mat, viewDirWS, specularShadowMask);
                    }
                #endif
                finalColor += pointLightContrib;

                half3 spotLightContrib = 0;
                #ifdef _USESPOTLIGHT
                    // ● 安全位守卫：同上，防止读取已释放的聚光灯缓冲区
                    if (_CustomLightSystemActive > 0.5)
                    {
                        spotLightContrib = CalculateCustomSpotLights(input.positionWS_shadow.xyz, mat, viewDirWS, specularShadowMask);
                    }
                #endif
                finalColor += spotLightContrib;
                
                half3 emissionContrib = 0;
                
                #ifdef _USEEMISSIONMAP
                    half3 emissionMap = mat.emissionMap;
                    emissionMap = AdjustSaturation(emissionMap, min(_EmissionScale, 1.1));
                    #ifdef _INVERTEMISMAP
                        emissionMap = 1.0 - emissionMap;
                    #endif
                    
                    emissionContrib = emissionMap * _EmissionColor.rgb * _EmissionScale;
                #endif
                
                finalColor += emissionContrib;
                
                finalColor = MixFog(finalColor, input.viewDirWS_fog.w);
                
                #ifdef _DEBUGNORMAL
                    #if defined(_NORMALMAP)
                        
                        return half4(mat.normalWS * (1.0-_HalfLambert) + _HalfLambert, 1.0);
                    #else
                        
                        return half4(1.0, 0.0, 0.0, 1.0);
                    #endif
                #endif
                
                #if defined(_USEMSAMAP) && defined(_PREVIEWAO)
                return mat.aoValue;
                #else
                return half4(finalColor, 1.0);
                #endif
            }
            ENDHLSL
        }
        
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
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            float3 _LightDirection;
            float4 _ShadowBias; 
            
            struct Attributes 
            { 
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
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
                
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                
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
        
        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LIGHTMODE" = "DepthOnly"
                "QUEUE" = "Geometry"
                "RenderType" = "Opaque"
                "DisableBatching" = "False"
                "RenderPipeline" = "UniversalPipeline"
                "UniversalMaterialType" = "Unlit"
                "ShaderGraphTargetId" = "UniversalUnlitSubTarget"
            }
            
            ZWrite On
            ColorMask 0
            Cull[_Cull]
            
            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS   : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            
            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            
            half DepthOnlyFragment(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                
                return input.positionCS.z;
            }
            ENDHLSL
        }
        
        // Meta Pass - 烘焙器读取材质albedo和emission的入口
        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex UniversalVertexMeta
            #pragma fragment frag_meta

            #pragma shader_feature_local _USEMSAMAP
            #pragma shader_feature_local _USEEMISSIONMAP
            #pragma shader_feature_local _INVERTEMISMAP
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MetallicGlossMap);
            SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Metallic;
                half _Roughness;
                half _SpecularScale;
                half _ShadowScale;
                float _Softness;
                half _HalfLambert;
                half _Brightness;
                half _BumpScale;
                half _OcclusionContrast;
                half _OcclusionStrength;
                half _EmissionScale;
                half4 _EmissionColor;

                half _PointLightIntensity;
                half _PointLightRangeMultiplier;
                half _PointLightFalloff;
                half _PointLightAmount;

                half _SpotLightIntensity;
                half _SpotLightRangeMultiplier;
                half _SpotLightFalloff;
                half _SpotLightAmount;

                half _SpotTextureContrast;
                half _SpotTextureSize;
                half _SpotTextureIntensity;

                float3 _BakedSpecularDirection;

                float _ReflectionStrength;
                float _ReflectionBlur;
                float _ReflectionFresnelPower;
                float _ReflectionFresnelBias;
            CBUFFER_END

            // UniversalMetaPass必须在CBUFFER之后include，因为其中的UniversalVertexMeta使用了_BaseMap_ST
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UniversalMetaPass.hlsl"

            half4 frag_meta(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                half3 albedo = baseMap.rgb * _BaseColor.rgb;

                half metallic = _Metallic;
                half roughness = _Roughness;
                #ifdef _USEMSAMAP
                    half4 mra = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
                    metallic *= mra.r;
                    roughness *= mra.g;
                #endif

                // 与URP Lit一致的能量守恒分配
                half oneMinusReflectivity = (1.0 - metallic) * 0.96;
                half3 diffuse = albedo * oneMinusReflectivity;
                half3 specular = lerp(half3(0.04, 0.04, 0.04), albedo, metallic);

                MetaInput metaInput;
                metaInput.Albedo = diffuse + specular * roughness * 0.5;

                #ifdef _USEEMISSIONMAP
                    half3 emissionMap = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb;
                    #ifdef _INVERTEMISMAP
                        emissionMap = 1.0 - emissionMap;
                    #endif
                    metaInput.Emission = emissionMap * _EmissionColor.rgb * _EmissionScale;
                #else
                    metaInput.Emission = half3(0, 0, 0);
                #endif

                return UniversalFragmentMeta(input, metaInput);
            }
            ENDHLSL
        }
    }
    
    CustomEditor "PBR_MobileGUI"
}
