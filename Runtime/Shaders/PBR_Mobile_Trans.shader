// PBR_Mobile1.0    PBR基本属性，自发光开关（贴图数量：4）
// PBR_Mobile1.2    添加AO贴图通道及控制参数；添加Compute Buffer多点光源支持
// PBR_Mobile2.0    添加Compute Buffer多点光源支持
// PBR_Mobile2.1.4   将光滑度控制改为粗糙度控制，更符合PBR标准工作流
// PBR_Mobile2.1.5   修复自发光被AO影响问题
// PBR_Mobile2.1.7   修复pow潜在除零风险；阴影影响高光，添加自身阴影控制参数
// 继承PBR_Mobile3.1    添加Brightness参数
// 继承PBR_Mobile3.2    支持烘焙
// 继承PBR_Mobile3.3    校正基础光照；优化烘焙亮度
// 继承PBR_Mobile3.4    匹配PBR_Mobile高光算法
// 继承PBR_Mobile4.0    匹配PBR_Mobile优化金属度算法（包括LIGHTMAP_ON、CalculateCustomPointLights）
// 继承PBR_Mobile5.3    优化自身阴影平滑度，减少阶梯状硬边
// 继承PBR_Mobile5.4    性能优化 - 预计算PBR属性，消除重复计算；添加球形反射贴图支持，包含菲涅尔效果
// 继承PBR_Mobile5.5 完善自身阴影与半兰伯特阴影
// 继承PBR_Mobile5.7 优化自身阴影明暗交界线
// 继承PBR_Mobile5.8 高光亮度还原 - 移除specularColor削减，保持完整高光亮度；烘焙高光受实时阴影影响
// 继承PBR_Mobile5.9 高光算法优化 - 使用反射向量法替代Blinn-Phong，边缘高光产生自然拉伸效果；移除硬编码倍增
//      添加_DisableEnvironment参数 - 支持禁用环境光，只使用实时光照；修复finalColor缺少ambient的bug
// 继承PBR_Mobile6.0 完善所有效果，继承原始表现效果（支持ShadowMap透明投影）
// 继承PBR_Mobile6.3 添加“禁用主光颜色”选项，取消勾选时使用默认白色
// 继承PBR_Mobile6.5 P0性能优化：MRA贴图条件采样、新增_DSIABLEBAKEDSPECULAR/_DISABLEINDIRECTSPECULAR开关；_ZWrite改用shader_feature_local关键字化
// 继承PBR_Mobile7.1 软阴影重构：等边三角形120°采样(中心+3点,减少到4次采样,固定权重2:1:1:1÷8)；顶点阴影/像素阴影互斥重构(_USEVERSHADOW激活时跳过shadow map采样)；修正权重归一化；添加ShadowMap边界检测(sc.z≤0排除范围外错误阴影)
// 7.1.1 透明处理移至"DepthOnly"Pass中，适配Pre-Z
// 8.0 同步PBR_Mobile_NEW：替换高光算法为GGX（ARM Siggraph 2015 移动优化版，D*V*F合并近似）
//      MaterialProperties 扩展为 GGX 参数（alphaRoughness/alpha2/alpha2MinusOne），标准F0 = lerp(0.04, albedo, metallic)
// 8.1 同步PBR_Mobile_NEW：间接高光使用反射方向SH增强法线依赖（无光照贴图时光滑度调制反射方向采样）
// 8.2 同步PBR_Mobile_NEW：SimpleDiffuse 按 _DISABLEENVIRONMENT 切换算法（禁用环境光时暗部整体受 _HalfLambert 比例控制）
// 8.3 同步PBR_Mobile_NEW：阴影高光衰减曲线应用到直接高光/间接高光/烘焙高光，避免暗部高光过亮
// 8.4 同步PBR_Mobile_NEW 8.4：PBR 粗糙度钳制对齐 URP 标准（perceptualRoughness 允许到 0。
Shader "Custom/PBR_Mobile_Trans"
{
    Properties
    {
        [Toggle(_DISABLEENVIRONMENT)] _DisableEnvironment ("Disable Environment", Float) = 0
        [Toggle(_DISABLELIGHTCOLOR)] _DisableLightColor ("Disable LightColor", Float) = 0
        [Toggle(_USESOFTSHADOW)] _UseSoftShadow ("Use Optimized Soft Shadow", Float) = 1
        [Header(1  (Base Properties))]
        [Space(5)]
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        [MainTexture] _BaseMap ("Albedo (RGB)", 2D) = "white" {}
        
        [Header(2  (Metallic Roughness AO))]
        [Space(5)]
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _Roughness ("Roughness", Range(0, 2)) = 0.5
        _SpecularScale ("Specular Scale", Range(0.01, 1)) = 1
        _HalfLambert ("Half Lambert", Range(0, 1)) = 0.3
        _ShadowScale ("Self Shadow Scale", Range(0, 1)) = 0.5
        _Softness ("Shadow Softness（纹素数）", Range(0, 4)) = 1.5
        _Brightness ("Brightness", Range(0.5, 2)) = 1.2
        [HideInInspector] _BakedSpecularDirection ("Baked Specular Direction", Vector) = (0, 0, 1)
        [Toggle(_USEMSAMAP)] _UseMsaMap ("Use Metallic Roughness Map", Float) = 0
        _MetallicGlossMap ("Metallic(R) Roughness(G) AO(B)", 2D) = "white" {}
        [Toggle(_USEAOMAP)] _UseAOMap ("Use AO(B) Channel", Float) = 0
        _OcclusionContrast  ("AO Contrast", Range(0, 2)) = 0.8
        _OcclusionStrength  ("AO Strength", Range(0, 1)) = 0.5
        [Toggle(_PREVIEWAO)] _PreviewAOMap ("Preview AO(B) Channel", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0.001, 1.0)) = 0.5

        // [Header(........................................................)]
        // [Space(5)]
        [Toggle(_NORMALMAP)] _UseNormalMap("Use Normal Map", Float) = 0
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0.001, 3)) = 1.0
        [Toggle(_FILPG)] _FilpG("Filp Green Channel", Float) = 0
        [HideInInspector] _DebugNormal("Debug Normal Map", Float) = 0
        
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
        _ReflectionStrength ("Reflection Strength", Range(0, 6)) = 1.0
        _ReflectionBlur ("Reflection Blur", Range(0, 6)) = 0.0
        [Space(5)]
        _ReflectionFresnelPower ("Fresnel Power", Range(0.1, 10)) = 1.6
        _ReflectionFresnelBias ("Fresnel Bias", Range(-0.4, 1)) = 0.3
        
        [Header(6  (Custom Point Lights))]
        [HideInInspector] [Toggle(_USEPOINTLIGHT)] _UsePointlight("Use Point Lighting", Float) = 0
        _PointLightIntensity ("Point Light Intensity", Range(0, 8)) = 1.0
        _PointLightRangeMultiplier ("Range Multiplier", Range(0.1, 3)) = 1.0
        _PointLightFalloff ("Falloff Power", Range(0.5, 8)) = 2.0
        _PointLightAmount ("Light Amount", Range(1, 8)) = 8
        
        [Header(7  (Custom Spot Lights))]
        [HideInInspector] _UseSpotlight("Use Spot Lighting", Float) = 0
        [HideInInspector] _SpotLightIntensity ("Spot Light Intensity", Range(0, 8)) = 1.0
        [HideInInspector] _SpotLightRangeMultiplier ("Range Multiplier", Range(0.1, 3)) = 1.0
        [HideInInspector] _SpotLightFalloff ("Falloff Power", Range(0.1, 2)) = 2.0
        [HideInInspector] _SpotLightAmount ("Light Amount", Range(1, 2)) = 2
        [HideInInspector] _UseSpotTexture("Use Spot Texture", Float) = 0
        [HideInInspector] _SpotTexture ("Spot Texture", 2D) = "white" {}
        [HideInInspector] _SpotTextureContrast ("Spot Texture Contrast", Range(0.1, 5)) = 1.0
        [HideInInspector] _SpotTextureSize ("Spot Texture Size", Range(0.1, 1)) = 0.5
        [HideInInspector] _SpotTextureIntensity ("Spot Texture Intensity", Range(0, 2)) = 1.0
        
        // 透明裁剪模式（推荐）：_SrcBlend=1(One), _DstBlend=0(Zero), _ZWrite=1
        // 支持透明裁剪阴影，不会被黑色覆盖
        // 半透明模式：_SrcBlend=5(SrcAlpha), _DstBlend=10(OneMinusSrcAlpha), _ZWrite=0
        // 真正的半透明效果，但阴影投射会有问题
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 0
        
        [Toggle(_ZWWRITE)] _ZWrite("Z Write", Float) = 1
        
        [Enum(Off,0,Front,1,Back,2)] _Cull ("Cull Mode", Float) = 2
        [Toggle(_DISABLEBAKEDSPECULAR)] _DisableBakedSpecular ("Disable Baked Specular (烘焙高光)", Float) = 1
        [Toggle(_DISABLEINDIRECTSPECULAR)] _DisableIndirectSpecular ("Disable Indirect Specular (间接高光近似)", Float) = 1
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "Queue" = "AlphaTest"
            // "Queue" = "Transparent"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            // 注意：ShaderLab 的 Pass 状态块不支持 #ifdef 包裹 ZWrite
            // 改为用 shader_feature_local 关键字，Unity 通过 Properties 中的 [Toggle(_ZWWRITE)] 自动绑定
            // 这里用属性形式驱动 ZWrite（SRP Batcher 在单 ZWrite 状态下完全兼容）
            ZWrite [_ZWrite]
            Cull[_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma shader_feature_local _USEEMISSIONMAP
            #pragma shader_feature_local _INVERTEMISMAP
            #pragma shader_feature_local _FILPG
            #pragma shader_feature_local _USEMSAMAP
            #pragma shader_feature_local _USEAOMAP
            #pragma shader_feature_local _PREVIEWAO
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _USEPOINTLIGHT
            #pragma shader_feature_local _USEVERSHADOW
            #pragma shader_feature_local _USESOFTSHADOW
            #pragma shader_feature_local _USEREFLECTION
            #pragma shader_feature_local _DISABLEENVIRONMENT
            #pragma shader_feature_local _DISABLELIGHTCOLOR
            #pragma shader_feature_local _DISABLEBAKEDSPECULAR
            #pragma shader_feature_local _DISABLEINDIRECTSPECULAR
            #pragma shader_feature_local _ZWWRITE
            
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
                
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            struct CustomPointLight
            {
                float3 position;
                float range;
                float4 color;          
                float4 parameters;     
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
                half _Cutoff;
                
                float3 _BakedSpecularDirection;
                
                float _ReflectionStrength;
                float _ReflectionBlur;
                float _ReflectionFresnelPower;
                float _ReflectionFresnelBias;
                
            CBUFFER_END

            StructuredBuffer<CustomPointLight> _CustomPointLights;
            int _CustomPointLightCount;

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
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 8);
                #ifdef _USEVERSHADOW
                half shadowAttenuation : TEXCOORD5; 
                half NdotL : TEXCOORD7; 
                #endif
                
                #if defined(_NORMALMAP)
                float4 tangentWS : TEXCOORD6;
                #endif
            };

            // PBR 粗糙度钳制下限（对齐 URP InitializeBRDFData 的双重 HALF 钳制，与 PBR_Mobile_NEW 8.4 同步）
            //   MIN_ROUGHNESS  = HALF_MIN_SQRT ≈ 7.81e-3 — 防止 alphaRoughness 太小让 GGX D 项退化为 delta
            //                                                  （解决"粗糙度为零时高光不可见"）
            //   MIN_ROUGHNESS2 = HALF_MIN    ≈ 6.10e-5 — 防止 alpha2 在 fp16 下溢出到 0
            // MIN_ROUGHNESS2 = (MIN_ROUGHNESS)² 在数学上等价于 HALF_MIN
            // 注意：必须定义在 InitMaterialProperties 之前（HLSL 编译器逐语句解析，不前向追溯宏定义）
            #define MIN_ROUGHNESS  7.8125e-3h
            #define MIN_ROUGHNESS2 6.1035e-5h

            struct MaterialProperties
            {
                half3 albedo;
                half metallic;
                half roughness;
                half3 normalWS;
                float2 uv;
                
                // 预计算的PBR属性（性能优化）
                half3 diffuseColor;       // 预计算的漫反射颜色
                half3 specularColor;      // 预计算的高光颜色 (F0)
                half perceptualRoughness; // 感知粗糙度 (0-1)
                half alphaRoughness;      // alpha = perceptualRoughness^2 (GGX使用)
                half alpha2;              // alpha^2
                half alpha2MinusOne;      // alpha^2 - 1
                half oneMinusMetallic;    // 预计算的 1-metallic
            };

            MaterialProperties InitMaterialProperties(half3 albedo, half metallic, half roughness, half3 normalWS, float2 uv)
            {
                MaterialProperties mat;
                mat.albedo = albedo;
                mat.metallic = metallic;
                mat.roughness = roughness;
                mat.normalWS = normalWS;
                
                // 预计算PBR属性（性能优化 - 避免在每个光源中重复计算）
                mat.oneMinusMetallic = 1.0 - mat.metallic;
                
                // 标准PBR粗糙度参数（用于GGX BRDF，与PBR_Mobile_NEW 8.4 同步对齐 URP InitializeBRDFData）
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

            half3 SimpleDiffuse(half3 normalWS, half3 lightDir, half3 lightColor)
            {
                // 根据 _DisableEnvironment（禁用环境光）开关选择算法（与PBR_Mobile_NEW 8.2同步）：
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
            
            half fastPow(half x, half n) {
                return exp2(n * log2(x));
            }

            float2 fastSphericalUV(float3 reflectionVector) {
                reflectionVector = normalize(reflectionVector);
                
                return float2(
                    reflectionVector.x / 4.01 + 0.5,  
                    reflectionVector.y / 4.01 + 0.5   
                );
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
                
                half mainLightLuminance = dot(mainLightColor, half3(0.299, 0.587, 0.114));
                
                half lightInfluence = saturate(mainLightLuminance * 0.5 + 0.5); // 降低影响，确保最小值为0.5
                reflectionIntensity *= lightInfluence;
                
                return reflectionColor * _ReflectionStrength * reflectionIntensity;
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
                // 使用烘焙高光方向（如果设置）
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

                // 烘焙高光也应该受实时阴影影响（specularShadowMask 在调用方外部应用）
                return adjustedBakedGI * specularTerm * NoL * _SpecularScale * shadowAttenuation;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = normalInput.normalWS;
                output.viewDirWS = GetCameraPositionWS() - vertexInput.positionWS;
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                #ifdef _USEVERSHADOW
                    half NdotL = 0;
                    half shadowAttenuation = 1.0;
                    
                    // 当 _ShadowScale >= 0.88 时，跳过阴影计算以优化性能
                    if (_ShadowScale < 0.88)
                    {
                        float4 shadowCoord = TransformWorldToShadowCoord(output.positionWS);
                        shadowCoord.w = max(shadowCoord.w, 0.001);
                        Light mainLight = GetMainLight(shadowCoord);
                        
                        NdotL = dot(output.normalWS, mainLight.direction);
                        half baseShadow = mainLight.shadowAttenuation;
                        
                        // 使用Lambert光照作为遮罩来平滑阴影锯齿（预计算）
                        half lambertMask = saturate(NdotL * 0.5 + 0.5);
                        
                        // 检测阴影边界并应用Lambert遮罩平滑
                        half shadowEdge = saturate((baseShadow - 0.3) / 0.4);
                        half smoothedShadow = lerp(baseShadow, lambertMask, shadowEdge * (1.0 - shadowEdge) * shadowEdge);
                        
                        // 应用阴影强度控制
                        shadowAttenuation = lerp(smoothedShadow, 1.0, _ShadowScale);
                        
                        // 背面剔除优化 - 使用与Half Lambert协调的范围
                        half backfaceRange = lerp(0.0, 1.0, lambertMask);
                        half backfaceFactor = smoothstep(-backfaceRange, backfaceRange, NdotL);
                        shadowAttenuation = lerp(1.0, shadowAttenuation, backfaceFactor);
                    }
                    else
                    {
                        // 获取主光源方向用于后续计算，但不计算阴影
                        Light mainLight = GetMainLight();
                        NdotL = dot(output.normalWS, mainLight.direction);
                    }
                    
                    output.NdotL = NdotL;
                    output.shadowAttenuation = shadowAttenuation;
                #endif
                
                #if defined(_NORMALMAP)
                output.tangentWS = float4(normalInput.tangentWS, input.tangentOS.w);
                #endif
                
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS.xyz, output.vertexSH);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                
                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 albedo = baseColor.rgb;

                // 针对DepthPrimingMode=Forced的特殊处理关掉
                // clip(baseColor.a - _Cutoff);
                
                half3 normalWS = normalize(input.normalWS);
                
                #if defined(_NORMALMAP)
                    half4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv);
                    #ifdef _FILPG
                        normalSample.g = 1 - normalSample.g;
                    #endif
                    half3 normalTS = UnpackNormal(normalSample);
                    normalTS.xy *= _BumpScale;
                    normalTS = normalize(normalTS);
                    
                    float3 tangentWS = normalize(input.tangentWS.xyz);
                    float3 bitangentWS = cross(normalWS, tangentWS) * input.tangentWS.w;
                    float3x3 TBN = float3x3(tangentWS, bitangentWS, normalWS);
                    normalWS = normalize(mul(normalTS, TBN));
                #endif

                half metallic = _Metallic;
                half roughness = _Roughness;
                half4 metallicGloss = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, input.uv);
                
                #ifdef _USEMSAMAP
                    metallic *= metallicGloss.r;
                    roughness *= metallicGloss.g; 
                #endif

                // 初始化材质属性（包含预计算的PBR属性）
                MaterialProperties mat = InitMaterialProperties(albedo, metallic, roughness, normalWS, input.uv);

                half3 viewDirWS = normalize(input.viewDirWS);
                
                half shadowAttenuation = 1;
#ifdef _USESOFTSHADOW
                // 优化软阴影：4 层采样（1 中心 + 3 点 120° 均布），仅 1 次 TransformWorldToShadowCoord
                // 固定权重 2:1:1:1 ÷ 5 → 中心 40%，周边各 20%，羽化更强、归一化正确
                Light mainLight = GetMainLight();
                half3 lightDir = mainLight.direction;
                {
                    float4 sc = TransformWorldToShadowCoord(input.positionWS);
                    float2 texelSize = _MainLightShadowmapTexture_TexelSize.xy;
                    float radius = _Softness * texelSize.x;

                    // 中心点（权重 2）
                    float shadow = SAMPLE_TEXTURE2D_SHADOW(
                        _MainLightShadowmapTexture, sampler_LinearClampCompare, sc) * 2.0;
                    // 0°（权重 1）
                    shadow += SAMPLE_TEXTURE2D_SHADOW(
                        _MainLightShadowmapTexture, sampler_LinearClampCompare,
                        float4(sc.xy + float2(radius, 0), sc.zw));
                    // 120°（权重 1）
                    shadow += SAMPLE_TEXTURE2D_SHADOW(
                        _MainLightShadowmapTexture, sampler_LinearClampCompare,
                        float4(sc.xy + float2(-0.5 * radius, 0.866 * radius), sc.zw));
                    // 240°（权重 1）
                    shadow += SAMPLE_TEXTURE2D_SHADOW(
                        _MainLightShadowmapTexture, sampler_LinearClampCompare,
                        float4(sc.xy + float2(-0.5 * radius, -0.866 * radius), sc.zw));

                    // 超出阴影贴图范围（sc.z <= 0）时维持无阴影状态
                    half inRange = (sc.z > 0.0) ? 1.0 : 0.0;
                    mainLight.shadowAttenuation = lerp(1.0, shadow / 5.0, inRange);
                }
#else
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half3 lightDir = mainLight.direction;
#endif
                
                // ShadowMask：取软阴影和烘焙遮罩的较暗值
                #if defined(LIGHTMAP_ON) && defined(SHADOWS_SHADOWMASK)
                    half4 shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
                    mainLight.shadowAttenuation = min(mainLight.shadowAttenuation, shadowMask.r);
                #endif
                
                #ifdef _USEVERSHADOW
                    // 顶点阴影模式 - 直接使用顶点着色器预计算的阴影值（已包含完整算法）
                    if (_ShadowScale < 0.88)
                    {
                        shadowAttenuation = input.shadowAttenuation;
                    }
                    // 当 _ShadowScale >= 0.88 时，shadowAttenuation 保持为 1.0（已在上面初始化）
                #else
                    // 像素阴影模式
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
                // 仅作用于各类高光（直接/间接/烘焙），不影响漫反射
                half x = saturate(shadowAttenuation);
                half specularShadowMask = x * x * (0.2 + 0.8 * x);

                #ifndef _DISABLEENVIRONMENT
                // _DISABLELIGHTCOLOR：漫反射去色只保留强度，高光保留原始颜色影响金属度
                #ifdef _DISABLELIGHTCOLOR
                half lightIntensity1 = max(mainLight.color.r, max(mainLight.color.g, mainLight.color.b));
                half3 lightColor = half3(lightIntensity1, lightIntensity1, lightIntensity1) * mainLight.distanceAttenuation * shadowAttenuation;
                half3 specularLightColor1 = mainLight.color * mainLight.distanceAttenuation * shadowAttenuation;
                #else
                half3 lightColor = mainLight.color * mainLight.distanceAttenuation * shadowAttenuation;
                half3 specularLightColor1 = lightColor;
                #endif
                half3 diffuse = SimpleDiffuse(mat.normalWS, lightDir, lightColor);
                // specular 已包含 mat.specularColor (GGX BRDF 内部已乘)
                half3 specular = SimpleSpecular(mat.normalWS, lightDir, viewDirWS, mat, specularLightColor1);
                // 应用阴影高光衰减曲线：暗部高光按指数衰减（曲线>1 时比漫反射衰减更剧烈）
                specular *= specularShadowMask;
                
                // 计算环境光（烘焙光照或球谐光照）
                half3 bakedGI = 0;
                #ifdef LIGHTMAP_ON
                    // 采样烘焙光照（不乘以4.0，保持与PBR_Mobile一致）
                    bakedGI = SampleLightmap(input.lightmapUV, mat.normalWS);
                #else
                    // 没有lightmap，使用球谐光照
                    bakedGI = SampleSH(mat.normalWS);
                #endif
                
                // 间接漫反射：bakedGI只作用于漫反射通道（与URP Lit一致的能量分配）
                half3 ambient = bakedGI * mat.diffuseColor;
                #ifndef _DISABLEINDIRECTSPECULAR
                // 间接高光：使用反射方向SH增强法线依赖（Lit反射探针的轻量替代，与PBR_Mobile_NEW 8.1同步）
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
                
                // Subtractive模式特殊处理：让静态物体接收动态物体的实时阴影
                #if defined(LIGHTMAP_ON) && defined(LIGHTMAP_SHADOW_MIXING)
                    // Unity标准的Subtractive模式实现：
                    // 在阴影区域，使用unity_ShadowColor调暗烘焙光照
                    // shadowAttenuation: 1.0 = 无阴影, 0.0 = 完全阴影
                    half shadowStrength = 1.0 - shadowAttenuation;
                    half3 shadowTint = lerp(half3(1, 1, 1), unity_ShadowColor.rgb, shadowStrength);
                    ambient = lerp(ambient, ambient * 0.21, mat.metallic);
                    // 将实时阴影应用到烘焙光照上（保持烘焙的所有细节）
                    ambient *= shadowTint;
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
                half3 lightColor = half3(lightIntensity2, lightIntensity2, lightIntensity2) * mainLight.distanceAttenuation * shadowAttenuation;
                #else
                half3 lightColor = mainLight.color * mainLight.distanceAttenuation * shadowAttenuation;
                #endif
                half3 diffuse = SimpleDiffuse(mat.normalWS, lightDir, lightColor);
                // specular 已包含 mat.specularColor (GGX BRDF 内部已乘)
                half3 specular = SimpleSpecular(mat.normalWS, lightDir, viewDirWS, mat, lightColor);
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
                    finalColor += reflectionContrib * (1.0 - mat.metallic) * 0.5;
                #endif
                // finalColor += reflectionContrib * mat.albedo;
                
                #ifdef _USEAOMAP
                    half3 contrastedAO = saturate((metallicGloss.b - 0.5) * _OcclusionContrast + 0.5);
                    half3 finalAO = lerp(1.0, contrastedAO, _OcclusionStrength);
                    finalColor *= finalAO;
                #endif
                
                #ifdef _USEEMISSIONMAP
                    half3 emimap = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb;
                    half3 emicolor = emimap;
                    #ifdef _INVERTEMISMAP
                        emicolor = 1 - emimap;
                    #endif
                    half3 emission = emicolor * _EmissionColor.rgb * _EmissionScale;
                    finalColor += emission;
                #endif
                
                finalColor = MixFog(finalColor, input.fogFactor);
                
                #if defined(_PREVIEWAO) && defined(_USEAOMAP)
                return half4(finalAO, 1.0h);
                #elif defined(_PREVIEWAO)
                return half4(metallicGloss.r, metallicGloss.g, metallicGloss.b, 1.0h); 
                #else
                return half4(finalColor, 1.0h);
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
            
            // 声明纹理和采样器（这些不影响SRP Batcher）
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            
            // 使用主pass中已声明的CBUFFER（不重复声明，保持SRP Batcher兼容）
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
                half _Cutoff;
                
                float3 _BakedSpecularDirection;
                
                float _ReflectionStrength;
                float _ReflectionBlur;
                float _ReflectionFresnelPower;
                float _ReflectionFresnelBias;
            CBUFFER_END
            
            float3 _LightDirection;
            float4 _ShadowBias; 
            
            struct Attributes 
            { 
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct Varyings 
            { 
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
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
                
                // 传递UV坐标用于透明度采样
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                
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
                
                // 采样基础纹理的alpha通道
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
                
                // 根据alpha cutoff裁剪透明部分
                // 这会使透明区域不投射阴影到ShadowMap
                clip(alpha - _Cutoff);
                
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

            // 与 Forward pass 中 clip(baseColor.a - _Cutoff) 保持完全一致，保证像素集同步。
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // CBUFFER 必须与 Forward pass 字段顺序严格一致，维持 SRP Batcher 兼容
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
                half _Cutoff;

                float3 _BakedSpecularDirection;

                float _ReflectionStrength;
                float _ReflectionBlur;
                float _ReflectionFresnelPower;
                float _ReflectionFresnelBias;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;     // 采样 _BaseMap.alpha 用于 clip
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;     // 传递到 fragment 做 alpha 测试
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
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half DepthOnlyFragment(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 与 Forward pass 一致的 alpha clip：baseColor.a = _BaseMap.a * _BaseColor.a
                // 保证 depth pre-pass 写出的深度形状与 forward 像素集完全对齐
                half baseAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                clip(baseAlpha - _Cutoff);

                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
    
    CustomEditor "PBR_MobileGUI"
}
