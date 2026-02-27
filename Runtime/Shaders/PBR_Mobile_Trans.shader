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
        _SpecularScale ("Specular Scale", Range(1, 12)) = 2
        _HalfLambert ("Half Lambert", Range(0, 1)) = 0.3
        _ShadowScale ("Self Shadow Scale", Range(0, 1)) = 0.3
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
                mat.oneMinusMetallic = 1.0 - metallic;
                mat.smoothness = 1.0 - roughness;
                
                // 预计算能量守恒的漫反射和高光颜色
                half oneMinusDielectricSpec = 0.96;
                mat.diffuseColor = albedo * oneMinusDielectricSpec * mat.oneMinusMetallic;
                
                half3 baseSpecularColor = lerp(0.04, albedo, metallic);
                half minSpecular = mad(0.03, mat.oneMinusMetallic, 0.01 * mat.oneMinusMetallic + 0.04 * metallic);
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
                
                half lightInfluence = saturate(mainLightLuminance * 2.0 + 0.1);
                reflectionIntensity *= lightInfluence;
                
                return reflectionColor * reflectionIntensity;
            }


            half3 SimpleSpecular(half3 normalWS, half3 lightDir, half3 viewDir, half shininess, half smoothness, half3 lightColor, half shadowAttenuation)
            {
                half3 halfDir = normalize(lightDir + viewDir);
                half NdotH = saturate(dot(normalWS, halfDir));
                
                half specular = fastPow(max(NdotH, 0.001), shininess) * smoothness;
                return lightColor * specular * shadowAttenuation; 
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
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                
                #ifdef _USEVERSHADOW
                    // 顶点阴影模式
                    half rawShadow = input.shadowAttenuation;
                    
                    // 简单的阴影计算，使用_ShadowScale控制强度
                    shadowAttenuation = lerp(rawShadow, 1.0, _ShadowScale);
                    
                    // 背面剔除优化 - 使用固定范围0.3
                    half backfaceFactor = smoothstep(-0.3, 0.3, input.NdotL);
                    shadowAttenuation = lerp(1.0, shadowAttenuation, backfaceFactor);
                #else
                    // 像素阴影模式
                    half baseShadow = mainLight.shadowAttenuation;
                    half pixelNdotL = dot(mat.normalWS, mainLight.direction);
                    
                    // 简单的阴影计算，使用_ShadowScale控制强度
                    shadowAttenuation = lerp(baseShadow, 1.0, _ShadowScale);
                    
                    // 背面剔除优化 - 使用与Half Lambert协调的范围
                    half backfaceRange = lerp(0.0, 1.0, pixelNdotL);
                    half backfaceFactor = smoothstep(-backfaceRange, backfaceRange, pixelNdotL);
                    shadowAttenuation = lerp(1.0, shadowAttenuation, backfaceFactor);
                #endif
		
                half3 lightColor = mainLight.color * mainLight.distanceAttenuation * shadowAttenuation;
                half3 lightDir = mainLight.direction;
                
                half3 diffuse = SimpleDiffuse(mat.normalWS, lightDir, lightColor);
                half3 specular = SimpleSpecular(mat.normalWS, lightDir, viewDirWS, mat.shininess, mat.smoothness, lightColor, shadowAttenuation);
                
                // 使用预计算的diffuseColor和specularColor（性能优化）
                half3 finalColor = mat.diffuseColor * diffuse + mat.specularColor * specular * _SpecularScale;
                
                half3 bakedGI = 0;
                #ifdef LIGHTMAP_ON
                    
                    bakedGI = SampleLightmap(input.lightmapUV, mat.normalWS)*4.0;
                #else
                    
                    bakedGI = SampleSH(mat.normalWS);
                #endif
                
                // 使用预计算的diffuseColor和specularColor（性能优化）
                half3 ambient = bakedGI * (mat.diffuseColor + mat.specularColor * mat.metallic * 0.5);
                finalColor += ambient;
                
                finalColor *= _Brightness;
                
                half3 reflectionContrib = 0;
                #ifdef _USEREFLECTION
                    reflectionContrib = CalculateSphericalReflection(mat.normalWS, viewDirWS, mat.metallic, mat.roughness, mat.uv, lightColor);
                #endif
                finalColor += reflectionContrib * mat.albedo;
                
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
