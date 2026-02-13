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
Shader "Custom/PBR_Mobile"
{
    Properties
    {
        [Toggle(_DISABLEENVIRONMENT)] _DisableEnvironment ("Disable Environment", Float) = 0
        [Toggle(_USEVERSHADOW)] _UseVerShadow ("Use Vertex Shadow", Float) = 1
        [Header(1  (Base Properties))]
        [Space(5)]
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        [MainTexture] _BaseMap ("Albedo (RGB)", 2D) = "white" {}
        
        [Header(2  (Metallic Roughness AO))]
        [Space(5)]
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _Roughness ("Roughness", Range(0, 2)) = 0.5
        _SpecularScale ("Specular Scale", Range(1, 12)) = 2
        _HalfLambert ("Half Lambert", Range(0, 1)) = 0.5
        _ShadowScale ("Self Shadow Scale", Range(0, 1)) = 0.3
        _Brightness ("Brightness", Range(0.5, 20)) = 1.0
        _BakedSpecularDirection ("Baked Specular Direction", Vector) = (0, 0, 1)
        [Toggle(_USEMSAMAP)] _UseMsaMap ("Use Metallic Roughness Map", Float) = 0
        _MetallicGlossMap ("Metallic(R) Roughness(G) AO(B)", 2D) = "white" {}
        [Toggle(_USEAOMAP)] _UseAOMap ("Use AO(B) Channel", Float) = 0
        _OcclusionContrast  ("AO Contrast", Range(0, 2)) = 0.8
        _OcclusionStrength  ("AO Strength", Range(0, 1)) = 0.5
        [Toggle(_PREVIEWAO)] _PreviewAOMap ("Preview AO(B) Channel", Float) = 0

        [Header(3  (Normal Map))]
        [Space(5)]
        [Toggle(_NORMALMAP)] _UseNormalMap("Use Normal Map", Float) = 0
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0.001, 3)) = 1.0
        [Toggle(_FILPG)] _FilpG("Filp Green Channel", Float) = 0
        [Toggle(_DEBUGNORMAL)] _DebugNormal("Debug Normal Map", Float) = 0
        
        [Header(4  (Emission))]
        [Space(5)]
        [Toggle(_USEEMISSIONMAP)] _UseEmissionMap("Use Emission Map", Float) = 0
        [HDR]_EmissionColor ("Emission Color", Color) = (1,1,1,1)
        _EmissionMap ("Emission Map", 2D) = "white" {}
        _EmissionScale  ("Emission Scale", Range(0, 3)) = 1.0
        [Toggle(_INVERTEMISMAP)] _InvertEmisMap("Invert Emission Map", Float) = 0
        
        [Header(5  (Reflection))]
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
        [Toggle(_USEPOINTLIGHT)] _UsePointlight("Use Point Lighting", Float) = 0
        _PointLightIntensity ("Point Light Intensity", Range(0, 8)) = 1.0
        _PointLightRangeMultiplier ("Range Multiplier", Range(0.1, 3)) = 1.0
        _PointLightFalloff ("Falloff Power", Range(0.5, 8)) = 2.0
        _PointLightAmount ("Light Amount", Range(1, 16)) = 4

        [Header(7  (Custom Spot Lights))]
        [Space(5)]
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

        [Header(#  (Performance))]
        [Space(5)]
        [Enum(Off,0,Front,1,Back,2)] _Cull ("Cull Mode", Float) = 2
        
        [HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
    }

    SubShader
    {
        Tags
        {
            "QUEUE" = "Geometry"
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
                "QUEUE" = "Geometry"
                "RenderType" = "Opaque"
                "DisableBatching" = "False"
                "RenderPipeline" = "UniversalPipeline"
                "UniversalMaterialType" = "Unlit"
                "ShaderGraphTargetId" = "UniversalUnlitSubTarget"
            }
            Cull[_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma shader_feature_local _USEEMISSIONMAP
            #pragma shader_feature_local _INVERTEMISMAP
            #pragma shader_feature_local _FILPG
            #pragma shader_feature_local _USEVERSHADOW
            #pragma shader_feature_local _USEMSAMAP
            #pragma shader_feature_local _USEAOMAP
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _USEPOINTLIGHT
            #pragma shader_feature_local _USESPOTLIGHT
            #pragma shader_feature_local _USEREFLECTION
            #pragma shader_feature_local _PREVIEWAO
            #pragma shader_feature_local _DISABLEENVIRONMENT
            
            #pragma shader_feature_local _DEBUGNORMAL
            
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
                float4 _SphericalReflectionMap_ST;
                float _ReflectionBlur;
                float _ReflectionFresnelPower;
                float _ReflectionFresnelBias;
            CBUFFER_END

            StructuredBuffer<CustomPointLight> _CustomPointLights;
            int _CustomPointLightCount;
            
            StructuredBuffer<CustomSpotLight> _CustomSpotLights;
            int _CustomSpotLightCount;

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
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 viewDirWS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                #ifdef _USEVERSHADOW
                half shadowAttenuation : TEXCOORD5; 
                half NdotL : TEXCOORD7; 
                #endif
                
                #if defined(_NORMALMAP)
                float4 tangentWS : TEXCOORD6; 
                #endif
                
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 8);
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
                
                return float2(
                    
                    reflectionVector.x / 4.01 + 0.5,  
                    reflectionVector.y / 4.01 + 0.5   
                );
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
                uv = TRANSFORM_TEX(uv, _SphericalReflectionMap);
                
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
                
                half lightInfluence = saturate(mainLightLuminance * 2.0 + 0.1);
                reflectionIntensity *= lightInfluence;
                
                return reflectionColor * reflectionIntensity;
            }

            half3 SimpleDiffuse(half3 normalWS, half3 lightDir, half3 lightColor)
            {
                half NdotL = saturate(dot(normalWS, lightDir));
                
                half halfLambertEffect = NdotL * (1.0 - _HalfLambert) + _HalfLambert;
                return lightColor * halfLambertEffect;
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
            };

            MaterialProperties InitMaterialProperties(float2 uv, half3 worldNormal)
            {
                MaterialProperties mat;
                mat.uv = uv;
                
                mat.baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                mat.albedo = mat.baseColor.rgb * _BaseColor.rgb;
                mat.mraSample = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
                
                mat.metallic = _Metallic;
                mat.roughness = _Roughness;
                mat.aoValue = 1.0;
                
                #ifdef _USEMSAMAP
                    mat.metallic *= mat.mraSample.r;
                    mat.roughness *= mat.mraSample.g;
                #endif

                #ifdef _USEAOMAP
                    mat.aoValue = mat.mraSample.b;
                #endif
                
                mat.normalWS = worldNormal;
                mat.normalTS = half3(0, 0, 1);
                
                #ifdef _USEEMISSIONMAP
                    mat.emissionMap = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb;
                #else
                    mat.emissionMap = half3(0, 0, 0);
                #endif
                
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

            half3 SimpleSpecular(half3 normalWS, half3 lightDir, half3 viewDir, half roughness, half3 lightColor, half shadowAttenuation)
            {
                half3 halfDir = normalize(lightDir + viewDir);
                half NdotH = saturate(dot(normalWS, halfDir));
                half smoothness = 1.0 - roughness; 
                
                half shininess = 2.0 + smoothness * smoothness * smoothness * 512.0;

                half specular = fastPow(max(NdotH, 0.001), shininess) * smoothness;
                
                return lightColor * specular * shadowAttenuation * 2.0; 
            }
            
            half3 BakedSpecular(half3 normalWS, half3 lightDir, half3 viewDir, half roughness, half3 bakedGI, half metallic)
            {
                
                half3 finalLightDir = lightDir;
                if (length(_BakedSpecularDirection) > 0.001)
                {
                    
                    finalLightDir = normalize(_BakedSpecularDirection);
                }
                
                half3 halfDir = normalize(finalLightDir + viewDir);
                half NdotH = saturate(dot(normalWS, halfDir));
                half smoothness = 1.0 - roughness;
                
                half shininess = 2.0 + smoothness * smoothness * smoothness * 512.0;
                
                half specular = fastPow(max(NdotH, 0.001), shininess) * smoothness;
                
                half metallicFactor = metallic * metallic; 
                half3 adjustedBakedGI = lerp(bakedGI, half3(1, 1, 1), metallicFactor);
                
                return adjustedBakedGI * specular * _SpecularScale;
            }

            half3 CalculateCustomPointLights(float3 worldPos, MaterialProperties mat, float3 viewDir)
            {
                half3 totalLight = half3(0, 0, 0);
                
                if (_CustomPointLightCount == 0) return totalLight;
                
                int lightCount = min(_CustomPointLightCount, MAX_POINT_LIGHTS);
                
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
                    
                    half3 diffuse;
                    half3 specular = 0;
                    
                    if (useSimpleCalculation) 
                    {
                        
                        half NdotL = saturate(dot(mat.normalWS, lightDir));
                        diffuse = lightColor * NdotL;
                    }
                    else 
                    {
                        
                        half NdotL = saturate(dot(mat.normalWS, lightDir));
                        diffuse = lightColor * NdotL;
                        
                        if (mat.metallic > 0.1 || mat.roughness < 0.9) 
                        {
                            half3 halfDir = normalize(lightDir + viewDir);
                            half NdotH = saturate(dot(mat.normalWS, halfDir));
                            half smoothness = 1.0 - mat.roughness; 
                            
                            half shininess = 2.0 + smoothness * smoothness * smoothness * 512.0;
                            #if USE_FAST_MATH
                                specular = fastPow(max(NdotH, 0.001), shininess) * smoothness * attenuation;
                            #else
                                specular = pow(max(NdotH, 0.001), shininess) * smoothness * attenuation;
                            #endif
                        }
                    }
                    
                    half oneMinusDielectricSpec = 0.96; 
                    half3 diffuseColor = mat.albedo * oneMinusDielectricSpec * (1.0 - mat.metallic);
                    
                    half3 baseSpecularColor = lerp(0.04, mat.albedo, mat.metallic);
                    half minSpecular = 0.01 * (1.0 - mat.metallic) + 0.04 * mat.metallic;
                    half3 specularColor = max(baseSpecularColor, minSpecular);
                    
                    totalLight += diffuseColor * diffuse + specularColor * specular * _SpecularScale;
                }
                
                return totalLight;
            }

            half3 CalculateCustomSpotLights(float3 worldPos, MaterialProperties mat, float3 viewDir)
            {
                half3 totalLight = half3(0, 0, 0);
                
                if (_CustomSpotLightCount == 0) return totalLight;
                
                int lightCount = min(_CustomSpotLightCount, 2);
                
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
                    
                    half NdotL = saturate(dot(mat.normalWS, lightDir));
                    half3 diffuse = lightColor * NdotL * textureModulation;
                    
                    half3 specular = 0;
                    if (mat.metallic > 0.1 || mat.roughness < 0.9) 
                    {
                        half3 halfDir = normalize(lightDir + viewDir);
                        half NdotH = saturate(dot(mat.normalWS, halfDir));
                        half smoothness = 1.0 - mat.roughness;
                        half shininess = 2.0 + smoothness * smoothness * smoothness * 512.0;
                        specular = fastPow(max(NdotH, 0.001), shininess) * smoothness * distanceAttenuation * spotAttenuation * textureModulation;
                    }
                    
                    half oneMinusDielectricSpec = 0.96; 
                    half3 diffuseColor = mat.albedo * oneMinusDielectricSpec * (1.0 - mat.metallic);
                    
                    half3 baseSpecularColor = lerp(0.04, mat.albedo, mat.metallic);
                    half minSpecular = 0.01 * (1.0 - mat.metallic) + 0.04 * mat.metallic;
                    half3 specularColor = max(baseSpecularColor, minSpecular);
                    
                    totalLight += diffuseColor * diffuse + specularColor * specular * _SpecularScale;
                }
                
                return totalLight;
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
                    
                    float4 shadowCoord = TransformWorldToShadowCoord(output.positionWS);
                    
                    shadowCoord.w = max(shadowCoord.w, 0.001);
                    Light mainLight = GetMainLight(shadowCoord);
                    
                    half NdotL = dot(output.normalWS, mainLight.direction);
                    output.NdotL = NdotL;
                    
                    half baseShadow = mainLight.shadowAttenuation;
                    half shadowAttenuation = lerp(baseShadow, 1.0, _ShadowScale * (1.0 - _HalfLambert) + _HalfLambert);
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
                
                MaterialProperties mat = InitMaterialProperties(input.uv, normalize(input.normalWS));
                
                ApplyNormalMap(mat, input);
                
                half3 viewDirWS = normalize(input.viewDirWS);

                half3 diffuse = 0;
                half3 specular = 0;
                half shadowAttenuation = 1;
                half3 lightColor = 0;
                half3 lightDir = 0;
                
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                lightDir = mainLight.direction; 
                
                #ifdef _USEVERSHADOW
                    
                    shadowAttenuation = input.shadowAttenuation;
                    
                    half smoothFactor = smoothstep(-0.3, 0.3, input.NdotL);
                    shadowAttenuation = lerp(1.0, shadowAttenuation, smoothFactor);
                #else
                    
                    half baseShadow = mainLight.shadowAttenuation;
                    shadowAttenuation = lerp(baseShadow, 1.0, _ShadowScale * (1.0 - _HalfLambert) + _HalfLambert);
                    
                    half pixelNdotL = dot(mat.normalWS, lightDir);
                    
                    half smoothFactor = smoothstep(-0.3, 0.3, pixelNdotL);
                    shadowAttenuation = lerp(1.0, shadowAttenuation, smoothFactor);
                #endif
                
                lightColor = mainLight.color * mainLight.distanceAttenuation * shadowAttenuation;
                
                diffuse = SimpleDiffuse(mat.normalWS, lightDir, lightColor);
                specular = SimpleSpecular(mat.normalWS, lightDir, viewDirWS, mat.roughness, lightColor, shadowAttenuation);
                
                half oneMinusDielectricSpec = 0.96; 
                half3 diffuseColor = mat.albedo * oneMinusDielectricSpec * (1.0 - mat.metallic);
                
                half3 baseSpecularColor = lerp(0.04, mat.albedo, mat.metallic);
                
                half minSpecular = 0.01 * (1.0 - mat.metallic) + 0.04 * mat.metallic;
                half3 specularColor = max(baseSpecularColor, minSpecular);
                
                half3 finalColor = diffuseColor * diffuse + specularColor * specular * _SpecularScale;
                
                #ifndef _DISABLEENVIRONMENT
                half3 bakedGI = 0;
                #ifdef LIGHTMAP_ON
                    
                    bakedGI = SampleLightmap(input.lightmapUV, mat.normalWS);
                #else
                    
                    bakedGI = SampleSH(mat.normalWS);
                #endif
                
                half3 ambient = bakedGI * (diffuseColor + specularColor * mat.metallic * 0.5);
                
                half3 bakedSpecular = 0;
                
                half3 baseBakedSpecularColor = lerp(0.04, mat.albedo, mat.metallic);
                half bakedMinSpecular = 0.01 * (1.0 - mat.metallic) + 0.04 * mat.metallic;
                half3 bakedSpecularColor = max(baseBakedSpecularColor, bakedMinSpecular);
                #ifdef LIGHTMAP_ON
                    bakedSpecular = BakedSpecular(mat.normalWS, lightDir, viewDirWS, mat.roughness, bakedGI, mat.metallic);
                #endif
                
                finalColor += ambient + bakedSpecularColor * bakedSpecular;
                #endif 
                
                finalColor *= _Brightness;
                
                half3 reflectionContrib = 0;
                #ifdef _USEREFLECTION
                    reflectionContrib = CalculateSphericalReflection(mat.normalWS, viewDirWS, mat.metallic, mat.roughness, mat.uv, lightColor);
                #endif
                finalColor += reflectionContrib * mat.albedo;
                
                #ifdef _USEAOMAP
                    mat.aoValue = saturate(fastPow(max(mat.aoValue,0.01), _OcclusionContrast));
                    mat.aoValue = lerp(1.0, mat.aoValue, _OcclusionStrength);
                    finalColor *= mat.aoValue;
                #endif
                
                half3 pointLightContrib = 0;
                #ifdef _USEPOINTLIGHT
                    pointLightContrib = CalculateCustomPointLights(input.positionWS, mat, viewDirWS);
                #endif
                finalColor += pointLightContrib;
                
                half3 spotLightContrib = 0;
                #ifdef _USESPOTLIGHT
                    spotLightContrib = CalculateCustomSpotLights(input.positionWS, mat, viewDirWS);
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
                
                finalColor = MixFog(finalColor, input.fogFactor);
                
                #ifdef _DEBUGNORMAL
                    #if defined(_NORMALMAP)
                        
                        return half4(mat.normalWS * (1.0-_HalfLambert) + _HalfLambert, 1.0);
                    #else
                        
                        return half4(1.0, 0.0, 0.0, 1.0);
                    #endif
                #endif
                
                #ifdef _PREVIEWAO
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
    }
    
}
