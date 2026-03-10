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
Shader "Custom/PBR_Mobile_Trans"
{
    Properties
    {
        [HideInInspector] _DisableEnvironment ("Disable Environment", Float) = 0
        [Toggle(_USEVERSHADOW)] _UseVerShadow ("Use Vertex Shadow", Float) = 1
        [Header(1  (Base Properties))]
        [Space(5)]
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        [MainTexture] _BaseMap ("Albedo (RGB)", 2D) = "white" {}
        
        [Header(2  (Metallic Roughness AO))]
        [Space(5)]
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _Roughness ("Roughness", Range(0, 2)) = 0.5
        _SpecularScale ("Specular Scale", Range(0.1, 5)) = 2
        _HalfLambert ("Half Lambert", Range(0, 1)) = 0.3
        _ShadowScale ("Self Shadow Scale", Range(0, 1)) = 0.5
        _Brightness ("Brightness", Range(0.5, 2)) = 1.2
        [HideInInspector] _BakedSpecularDirection ("Baked Specular Direction", Vector) = (0, 0, 1)
        [Toggle(_USEMSAMAP)] _UseMsaMap ("Use Metallic Roughness Map", Float) = 0
        _MetallicGlossMap ("Metallic(R) Roughness(G) AO(B)", 2D) = "white" {}
        [Toggle(_USEAOMAP)] _UseAOMap ("Use AO(B) Channel", Float) = 0
        _OcclusionContrast  ("AO Contrast", Range(0, 2)) = 0.8
        _OcclusionStrength  ("AO Strength", Range(0, 1)) = 0.5
        [Toggle(_PREVIEWAO)] _PreviewAOMap ("Preview AO(B) Channel", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0.001, 1.0)) = 0.5

        [Header(........................................................)]
        [Space(5)]
        [Toggle(_NORMALMAP)] _UseNormalMap("Use Normal Map", Float) = 0
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0.001, 3)) = 1.0
        [Toggle(_FILPG)] _FilpG("Filp Green Channel", Float) = 0
        [HideInInspector] _DebugNormal("Debug Normal Map", Float) = 0
        
        [Header(........................................................)]
        [Space(5)]
        [Toggle(_USEEMISSIONMAP)] _UseEmissionMap("Use Emission Map", Float) = 0
        [HDR]_EmissionColor ("Emission Color", Color) = (1,1,1,1)
        _EmissionMap ("Emission Map", 2D) = "white" {}
        _EmissionScale  ("Emission Scale", Range(0, 3)) = 1.0
        [Toggle(_INVERTEMISMAP)] _InvertEmisMap("Invert Emission Map", Float) = 0
        
        [Header(........................................................)]
        [Space(5)]
        [Toggle(_USEREFLECTION)] _UseReflection("Use Reflection", Float) = 0
        [NoScaleOffset]_SphericalReflectionMap ("Spherical Reflection Map", 2D) = "white" {}
        _ReflectionStrength ("Reflection Strength", Range(0, 2)) = 1.0
        _ReflectionBlur ("Reflection Blur", Range(0, 6)) = 0.0
        [Space(5)]
        _ReflectionFresnelPower ("Fresnel Power", Range(0.1, 10)) = 1.6
        _ReflectionFresnelBias ("Fresnel Bias", Range(-0.4, 1)) = 0.3
        
        [Header(6  (Custom Point Lights))]
        [Space(5)]
        [HideInInspector] [Toggle(_USEPOINTLIGHT)] _UsePointlight("Use Point Lighting", Float) = 0
        _PointLightIntensity ("Point Light Intensity", Range(0, 8)) = 1.0
        _PointLightRangeMultiplier ("Range Multiplier", Range(0.1, 3)) = 1.0
        _PointLightFalloff ("Falloff Power", Range(0.5, 8)) = 2.0
        _PointLightAmount ("Light Amount", Range(1, 16)) = 4
        
        [Header(7  (Custom Spot Lights))]
        [Space(5)]
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
        
        [Header(8  (Transparent))]
        [Space(5)]        
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 0
        
        [Toggle] _ZWrite("Z Write", Float) = 1
        
        [Header(#  (Performance))]
        [Space(5)]
        [Enum(Off,0,Front,1,Back,2)] _Cull ("Cull Mode", Float) = 2
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "Queue" = "AlphaTest"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
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
            #pragma shader_feature_local _USEREFLECTION
            
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

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Metallic;
                half _Roughness;
                half _SpecularScale;
                half _ShadowScale;
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

            struct MaterialProperties
            {
                half3 albedo;
                half metallic;
                half roughness;
                half3 normalWS;
                float2 uv;
                
                // 预计算的PBR属性（性能优化）
                half3 diffuseColor;     // 预计算的漫反射颜色
                half3 specularColor;    // 预计算的高光颜色
                half smoothness;        // 预计算的光滑度
                half shininess;         // 预计算的高光指数
                half oneMinusMetallic;  // 预计算的 1-metallic
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
                mat.smoothness = 1.0 - mat.roughness;
                
                // 预计算能量守恒的漫反射和高光颜色
                half oneMinusDielectricSpec = 0.96;
                mat.diffuseColor = albedo * oneMinusDielectricSpec * mat.oneMinusMetallic;
                
                // 优化高光基础能量：提高非金属材质的基础高光强度
                half dielectricSpecular = 0.12; // 从0.04提升到0.12，增强非金属高光
                half3 baseSpecularColor = lerp(dielectricSpecular, mat.albedo, mat.metallic);
                
                // 使用粗糙度调制高光强度，粗糙度低时高光更强
                // 降低粗糙度的影响，避免过度增强
                half roughnessFactor = 1.0 - mat.roughness * 0.3; // 从0.5降低到0.3
                baseSpecularColor *= roughnessFactor;
                
                // 金属度加成：金属材质获得额外的高光强度提升
                // 使用更温和的曲线，避免高金属度时过曝
                half metallicBoost = 1.0 + mat.metallic * mat.metallic * 0.8; // 使用平方曲线，最高1.8倍
                baseSpecularColor *= metallicBoost;
                
                // 确保最小高光值，避免完全消失
                half minSpecular = lerp(0.08, 0.15, mat.metallic); // 降低金属最大最小值
                mat.specularColor = max(baseSpecularColor, minSpecular);
                
                // 预计算高光指数（避免在每个光源中重复pow运算）
                half smoothnessCubed = mat.smoothness * mat.smoothness * mat.smoothness;
                mat.shininess = mad(smoothnessCubed, 512.0, 2.0);
                
                return mat;
            }

            half3 SimpleDiffuse(half3 normalWS, half3 lightDir, half3 lightColor)
            {
                half NdotL = saturate(dot(normalWS, lightDir));
                
                // 标准Half Lambert效果
                half halfLambertEffect = NdotL * (1.0 - _HalfLambert) + _HalfLambert;
                
                return lightColor * halfLambertEffect;
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
                
                reflectionColor *= _ReflectionStrength;
                
                float fresnel = CalculateFresnel(normalWS, viewDirWS, _ReflectionFresnelPower, _ReflectionFresnelBias);
                
                float smoothness = 1.0 - roughness; 
                float reflectionIntensity = metallic * smoothness * fresnel;
                
                half mainLightLuminance = dot(mainLightColor, half3(0.299, 0.587, 0.114));
                
                half lightInfluence = saturate(mainLightLuminance * 0.5 + 0.5); // 降低影响，确保最小值为0.5
                reflectionIntensity *= lightInfluence;
                
                return reflectionColor * reflectionIntensity;
            }


            half3 SimpleSpecular(half3 normalWS, half3 lightDir, half3 viewDir, half shininess, half smoothness, half3 lightColor, half shadowAttenuation)
            {
                half3 halfDir = normalize(lightDir + viewDir);
                half NdotH = saturate(dot(normalWS, halfDir));
                
                half specular = fastPow(max(NdotH, 0.001), shininess) * smoothness;
                
                return lightColor * specular * shadowAttenuation * 2.0; 
            }
            
            half3 BakedSpecular(half3 normalWS, half3 lightDir, half3 viewDir, half shininess, half smoothness, half3 bakedGI, half metallic, half shadowAttenuation)
            {
                
                half3 finalLightDir = lightDir;
                float3 bakedDir = float3(_BakedSpecularDirection.x, _BakedSpecularDirection.y, _BakedSpecularDirection.z);
                if (length(bakedDir) > 0.001)
                {
                    
                    finalLightDir = normalize(bakedDir);
                }
                
                half3 halfDir = normalize(finalLightDir + viewDir);
                half NdotH = saturate(dot(normalWS, halfDir));
                
                half specular = fastPow(max(NdotH, 0.001), shininess) * smoothness;
                
                half metallicFactor = metallic * metallic; 
                half3 adjustedBakedGI = lerp(bakedGI, half3(1, 1, 1), metallicFactor);
                
                // 烘焙高光也应该受实时阴影影响
                return adjustedBakedGI * specular * _SpecularScale * shadowAttenuation;
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

                clip(baseColor.a - _Cutoff);
                
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
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half3 lightDir = mainLight.direction;
                
                // 处理ShadowMask（用于Subtractive和Shadowmask模式下的Mix灯光）
                #if defined(LIGHTMAP_ON)
                    #if defined(SHADOWS_SHADOWMASK)
                        // Shadowmask模式：使用烘焙的阴影遮罩
                        half4 shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
                        mainLight.shadowAttenuation = min(mainLight.shadowAttenuation, shadowMask.r);
                    #elif defined(LIGHTMAP_SHADOW_MIXING)
                        // Subtractive模式：从lightmap中减去主光源的贡献，然后添加实时光照
                        // 这样可以让动态物体正确接收实时阴影
                        // 注意：在Subtractive模式下，静态物体的阴影已经烘焙到lightmap中
                        // 动态物体需要接收实时阴影
                    #endif
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
		
                // 烘焙光照处理
                half3 bakedGI = 0;
                #ifdef LIGHTMAP_ON
                    // 采样烘焙光照（不乘以4.0，保持与PBR_Mobile一致）
                    bakedGI = SampleLightmap(input.lightmapUV, mat.normalWS);
                #else
                    // 没有lightmap，使用球谐光照
                    bakedGI = SampleSH(mat.normalWS);
                #endif
                
                // 使用预计算的diffuseColor和specularColor（性能优化）
                half3 ambient = bakedGI * (mat.diffuseColor + mat.specularColor * mat.metallic * 0.5);
                
                // 计算实时光照
                half3 lightColor = mainLight.color * mainLight.distanceAttenuation * shadowAttenuation;
                half3 diffuse = SimpleDiffuse(mat.normalWS, lightDir, lightColor);
                half3 specular = SimpleSpecular(mat.normalWS, lightDir, viewDirWS, mat.shininess, mat.smoothness, lightColor, shadowAttenuation);
                
                // Subtractive模式特殊处理：让静态物体接收动态物体的实时阴影
                #if defined(LIGHTMAP_ON) && defined(LIGHTMAP_SHADOW_MIXING)
                    // Unity标准的Subtractive模式实现：
                    // 在阴影区域，使用unity_ShadowColor调暗烘焙光照
                    // shadowAttenuation: 1.0 = 无阴影, 0.0 = 完全阴影
                    half shadowStrength = 1.0 - shadowAttenuation;
                    half3 shadowTint = lerp(half3(1, 1, 1), unity_ShadowColor.rgb, shadowStrength);
                    ambient = lerp(ambient,ambient*0.21, mat.metallic);
                    // 将实时阴影应用到烘焙光照上（保持烘焙的所有细节）
                    ambient *= shadowTint;
                #endif
                
                // 组合烘焙光照和实时光照（高光不再被specularColor削减）
                half3 finalColor = ambient + mat.diffuseColor * diffuse + mat.specularColor * specular * _SpecularScale;
                
                // 烘焙高光（保留原来的效果）
                half3 bakedSpecular = 0;
                #ifdef LIGHTMAP_ON
                    bakedSpecular = BakedSpecular(mat.normalWS, lightDir, viewDirWS, mat.shininess, mat.smoothness, bakedGI, mat.metallic, shadowAttenuation);
                    finalColor += mat.specularColor * bakedSpecular;
                #endif
                
                finalColor *= _Brightness;
                
                half3 reflectionContrib = 0;
                #ifdef _USEREFLECTION
                    reflectionContrib = CalculateSphericalReflection(mat.normalWS, viewDirWS, mat.metallic, mat.roughness, mat.uv, lightColor);
                    // 反射应该混合而不是累加，金属度高的材质反射替换漫反射
                    finalColor = lerp(finalColor, finalColor + reflectionContrib, mat.metallic);
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
    }
    
    CustomEditor "PBR_MobileGUI"
}
