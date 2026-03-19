//FurShell 1.1 完善基础fur特性
//FurShell 1.2 添加AlphaOffset优化毛发透明结构算法，优化毛发剔除效果BaseMapA颜色图A通道影响毛发长度
//FurShell 1.3 添加UseVerShadow选项，可以使用像素阴影，优化Fresnel暗部过暗问题
//FurShell 1.4 修改边缘光范围加大；修改变量_BaseColor；修改默认值
Shader "Custom/FurShell_Mobile_SingleC"
{
    Properties
    {
        [Toggle(_USEDISTANCEATTEN)] _UseDistanceAtten ("Use Distance Attenuation", Float) = 0.0
        [Toggle(_USEVERSHADOW)] _UseVerShadow ("Use Vertex Shadow", Float) = 1.0
        [Toggle(_USESELFSHADOW)] _UseSelfShadow ("Use Self Shadow", Float) = 1.0
        [Space]
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        [ToggleUI] _UseAlpha ("Use Alpha", Float) = 1.0
        _BaseMap("Base Map", 2D) = "white" {}
        _FurMap("Fur Map", 2D) = "white" {}
//        _NormalMap("Fur Map", 2D) = "white" {}
        [IntRange] _ShellAmount("Shell Amount", Range(1, 20)) = 8
        _FurLength("Fur Length", Range(0.0, 0.02)) = 0.004
        _AlphaOffset("Alpha Offset", Range(0.0, 1.01)) = 0.06
        _AlphaCutout("Alpha Cutout", Range(0.0, 1.0)) = 0.3
        _FurScale("Fur Density", Range(0.1, 50.0)) = 1.0
        _Occlusion("Occlusion", Range(0.0, 1.0)) = 0.23
        [Space]
        [ToggleUI] _UseTouch ("Use Touch", Float) = 0.0
        [Space]
        [ToggleUI] _UseWind ("Use Wind", Float) = 0.0
        // 基础移动参数：控制毛发的整体运动
        // x, y, z: 基础移动方向向量 (世界空间)
        // w: 移动因子指数，控制移动强度随毛发层数的变化 (建议值: 2.0-4.0)
        _BaseMove("Base Move", Vector) = (0.9, -1.0, 0.0, 2.6)
        
        // 风频率参数：控制风动画的速度和节奏
        // x, y, z: 风频率向量，分别对应三个轴向的动画频率
        // w: 预留参数，当前未使用
        // 值越大，风动画变化越快
        _WindFreq("Wind Freq", Vector) = (2.5, 0.7, 0.9, 1.0)

        // 风移动参数：控制风动画的幅度和空间变化
        // x, y, z: 风移动幅度向量，控制风在各个方向上的摆动强度
        // w: 空间频率，控制风动画随模型位置的变化 (值越大，空间变化越明显)
        _WindMove("Wind Move", Vector) = (1.2, 0.3, 0.2, 2.0)
        
        [Space(20)]
        // 背面剔除阈值
        _FaceViewProdThresh("Direction Threshold", Range(0.0001, 1.0)) = 0.001
        // 触摸挤压参数
        _TouchPosition("Touch Position", Vector) = (0, 0, 0, 1)  // xyz: 世界空间位置, w: 强度
        _TouchRadius("Touch Radius", Float) = 1.0
        _MaxDepression("Max Depression", Range(0.0, 2.0)) = 1.3  // 最大凹陷度
        
        [Space(20)]
        // 圆锥形风力影响参数（模拟吹风机效果）
        [ToggleUI] _UseWindCone ("Use Wind Cone", Float) = 0.0
        _WindConePosition("Wind Cone Position", Vector) = (0, 0, 0, 1)  // xyz: 圆锥中心位置, w: 强度倍增
        _WindConeDirection("Wind Cone Direction", Vector) = (0, 1, 0, 0)  // xyz: 圆锥方向, w: 未使用
        _WindConeAngle("Wind Cone Angle", Range(0.0, 90.0)) = 30.0  // 圆锥角度（度）
        _WindConeRange("Wind Cone Range", Float) = 5.0  // 圆锥范围
        _WindConeFrequencyBoost("Wind Cone Freq Boost", Range(0.0, 10.0)) = 2.0  // 圆锥内风频率加大值
        
        // Alpha通道控制参数
//        _AlphaControlPower("Alpha Control Power", Range(0.0, 4.0)) = 0.8  // Alpha通道控制强度
//        _AlphaThreshold("Alpha Threshold", Range(0.0, 1.0)) = 0.8  // Alpha通道阈值：大于此值才允许变形
    }

    SubShader
    {
        Tags 
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline" 
            "IgnoreProjector" = "True"
        }

        LOD 100

        ZWrite Off
        Cull Back

        Pass
        {
            Name "UnlitLambertGeo"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            // #pragma exclude_renderers gles gles3 glcore
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            // #pragma multi_compile _ _SHADOWS_SOFT
            #pragma vertex vert
            #pragma require geometry
            #pragma geometry geom 
            #pragma fragment frag
            #pragma shader_feature_local _USESELFSHADOW
            #pragma shader_feature_local _USEVERSHADOW
            // #pragma shader_feature_local _USETOUCH
            // #pragma shader_feature_local _USEWINDCONE
            #pragma shader_feature_local _USEDISTANCEATTEN
            
            // 首先包含必要的URP头文件以定义TEXTURE2D和SAMPLER宏
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 纹理和采样器（不在 CBUFFER 中）
            TEXTURE2D(_FurMap); 
            SAMPLER(sampler_FurMap);
            // ============================================
            // 包含 Param.hlsl（SRP Batcher 兼容）
            // ============================================
            int _ShellAmount;
            float _FurLength;
            float _AlphaOffset;
            float _AlphaCutout;
            float _Occlusion;
            float _UseWind;
            float _UseTouch;
            float _FurScale;
            float _UseAlpha;
            float4 _BaseMove;
            float4 _WindFreq;
            float4 _WindMove;
            // float4 _BaseColor;  // 注释掉，由UnlitInput.hlsl定义
            float3 _AmbientColor;
            float _FaceViewProdThresh;
            // 边缘光参数
            // float _RimLightPower;
            // float _RimLightIntensity;

            float4 _FurMap_ST;
            // float4 _NormalMap_ST;
            // float _NormalScale;
            
            // 触摸挤压参数
            float4 _TouchPosition;  // xyz: 世界空间位置, w: 强度
            float _TouchRadius;
            float _MaxDepression;   // 最大凹陷度
            
            // 圆锥形风力影响参数
            float _UseWindCone;
            float4 _WindConePosition;  // xyz: 圆锥中心位置, w: 强度倍增
            float4 _WindConeDirection; // xyz: 圆锥方向, w: 未使用
            float _WindConeAngle;      // 圆锥角度（度）
            float _WindConeRange;      // 圆锥范围
            float _WindConeFrequencyBoost; // 圆锥内风频率加大值
            
            float3 _LightDirection;
            float _ShadowExtraBias;
            // ============================================
            
            // ============================================
            // 开始嵌入 Common.hlsl 内容
            // ============================================
            // #ifndef FUR_COMMON_HLSL
            // #define FUR_COMMON_HLSL

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"
            // #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // 包含 CommonMaterial.hlsl 以提供 LerpWhiteTo 函数定义，使用Shadows.hlsl中的会报错
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"


            inline float3 GetViewDirectionOS(float3 posOS)
            {
                float3 cameraOS = TransformWorldToObject(GetCameraPositionWS());
                return normalize(posOS - cameraOS);
            }

            inline float3 CustomApplyShadowBias(float3 positionWS, float3 normalWS)
            {
                positionWS += _LightDirection * (_ShadowBias.x + _ShadowExtraBias);
                
                float invNdotL = 1.0 - saturate(dot(_LightDirection, normalWS));
                float scale = invNdotL * _ShadowBias.y;
                
                positionWS += normalWS * scale.xxx;

                return positionWS;
            }

            inline float4 GetShadowPositionHClip(float3 positionWS, float3 normalWS)
            {
                positionWS = CustomApplyShadowBias(positionWS, normalWS);
                
                float4 positionCS = TransformWorldToHClip(positionWS);
                
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }


            inline float rand(float2 seed)
            {
                return frac(sin(dot(seed.xy, float2(12.9898, 78.233))) * 43758.5453);
            }

            inline float3 rand3(float2 seed)
            {
                return 2.0 * (float3(rand(seed * 1), rand(seed * 2), rand(seed * 3)) - 0.5);
            }

            struct FurMoverData // 这个结构体存储了毛发动画点的数据
            {
                float3 posWS;      // 世界空间位置
                float3 dPosWS;     // 世界空间位置增量（用于动画）
                float3 velocityWS; // 世界空间速度
                float time;        // 时间参数
            };

            // RWStructuredBuffer<T>是HLSL中的一种缓冲区类型，表示一个可读写的结构化缓冲区。RW表示"Read-Write"
            // <FurMoverData>是缓冲区中存储的数据类型。从上面的定义可以看到存储毛发动画的数据类型
            // _Buffer：这是缓冲区的变量名
            // 这指定了缓冲区在哪个寄存器上绑定。u1表示无序访问视图(Unordered Access View)寄存器1。在HLSL中，u寄存器用于可读写资源（如RWStructuredBuffer），而t寄存器用于只读资源（如纹理）
            RWStructuredBuffer<FurMoverData> _Buffer : register(u1);
        // #endif
            // ============================================
            // 结束 Common.hlsl
            // ============================================
            
            // ============================================
            // 开始嵌入 UnlitLambert.hlsl 内容
            // ============================================
            // 包含UnlitInput.hlsl获取_BaseColor、_BaseMap等定义
            #include "Packages/com.unity.render-pipelines.universal/Shaders/UnlitInput.hlsl"

            // 触摸挤压计算函数（改进版）- 使用预采样的BaseMap alpha值
            inline float3 ApplyTouchDeformation(float3 positionWS, float3 normalWS, float2 uv)
            {
                // 计算顶点到触摸点的距离
                float3 touchPos = _TouchPosition.xyz;
                float touchStrength = _TouchPosition.w;
                float distanceToTouch = length(positionWS - touchPos);
                
                // 如果距离大于半径，没有影响
                if (distanceToTouch > _TouchRadius)
                    return positionWS;
                
                // 计算衰减因子：使用平滑的衰减曲线
                float falloff = 1.0 - saturate(distanceToTouch / _TouchRadius);
                // 使用三次衰减使效果更自然，中心强边缘弱
                falloff = falloff * falloff;
                
                // 使用预采样的BaseMap alpha通道来控制变形区域
                // 注意：baseAlpha参数已经是SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 0).a的结果
                
                // 使用alpha通道作为变形强度乘数：alpha值低（无毛发）的部位变形弱
                // 首先检查是否超过阈值：只有alpha大于阈值的区域才允许变形
                // 使用smoothstep实现平滑过渡，避免硬边界
                // 确保smoothstep参数在[0,1]范围内
                // float thresholdMin = max(0.0, _AlphaThreshold - 0.1);
                // float thresholdMax = min(1.0, _AlphaThreshold + 0.1);

                float baseAlpha = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 1).a;
                float alphaMask = smoothstep(0.7, 0.9, baseAlpha)*0.2;
                // 如果alphaMask接近0，完全不允许变形
                if (alphaMask < 0.01)
                    return positionWS;
                // 对于允许变形的区域，使用alpha值作为强度乘数
                // 同时考虑alphaMask（阈值控制）和alpha值（强度控制）
                
                // 计算凹陷方向：使用法线方向（永远向内凹陷）
                float3 depressionDir = -normalize(normalWS);
                
                // 计算基础位移（基于触摸强度和alpha通道）
                float3 baseDisplacement = depressionDir * touchStrength * falloff * 0.1 * alphaMask;
                
                // 计算最大允许位移（基于最大凹陷度和alpha通道）
                float maxDisplacementLength = _MaxDepression * 0.1 * alphaMask; // 缩放因子与baseDisplacement保持一致
                
                // 限制位移长度，确保不超过最大凹陷度
                float displacementLength = length(baseDisplacement);
                if (displacementLength > maxDisplacementLength)
                {
                    baseDisplacement = normalize(baseDisplacement) * maxDisplacementLength;
                }
                
                // 添加保护机制：确保位移方向是向内凹陷（与法线方向夹角小于90度）
                float dotProduct = dot(normalize(baseDisplacement), depressionDir);
                if (dotProduct < 0.5) // 如果方向偏差太大，强制使用法线方向
                {
                    baseDisplacement = depressionDir * min(displacementLength, maxDisplacementLength);
                }
                
                // 额外保护：当触摸点在模型内部时，减少凹陷效果
                // 计算从触摸点到顶点的方向
                float3 toVertexDir = normalize(positionWS - touchPos);
                float touchInsideFactor = saturate(dot(toVertexDir, depressionDir));
                
                // 如果触摸点在模型表面内部（dot接近1），减少效果
                baseDisplacement *= (1.0 - touchInsideFactor * 0.5);
                
                return positionWS + baseDisplacement;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float  layer : TEXCOORD1;
                float3 lighting : TEXCOORD2;    // 预计算的灯光贡献 (diffuse + ambient + fresnel)
                float alphaFactor : TEXCOORD3;  // 预计算的alpha相关因子
                float3 viewDirWS : TEXCOORD4;   // 世界空间视角方向（用于可能的边缘光计算）
                #ifndef _USEVERSHADOW
                    // 像素阴影模式：需要传递位置和法线到片段着色器
                    float3 positionWS : TEXCOORD5;
                    float3 normalWS : TEXCOORD6;
                #endif
            };

            Attributes vert(Attributes input)
            {
                return input;
            }
            
            half fastPow(half x, half n) {
                return exp2(n * log2(x)); // 在某些GPU上更快
            }
            
            // 计算圆锥形风力影响（优化版）
            // 增强毛发飘动的方向感，使毛发更明显地沿着风吹的方向飘动
            // 优化进入风区的过渡效果，避免剧烈抖动
            // 返回：影响值，并输出调整后的风方向和BaseMove.w调整因子
            inline float CalculateWindConeInfluence(float3 positionWS, out float3 adjustedWindDirection, out float baseMoveWAdjustment)
            {
                // 初始化输出方向为原始风方向
                adjustedWindDirection = normalize(_WindMove.xyz);
                baseMoveWAdjustment = 1.0; // 默认不调整
                
                // 如果未启用圆锥风力影响，返回基础影响值1.0
                if (_UseWindCone < 0.5)
                    return 1.0;
                
                // 获取圆锥参数
                float3 conePos = _WindConePosition.xyz;
                float3 coneDir = normalize(_WindConeDirection.xyz);
                float coneAngleRad = radians(_WindConeAngle);
                float coneRange = _WindConeRange;
                float coneIntensity = _WindConePosition.w;
                
                // 计算顶点到圆锥中心的向量
                float3 toVertex = positionWS - conePos;
                float distanceToCone = length(toVertex);
                
                // 计算顶点方向与圆锥方向的夹角
                float3 vertexDir = normalize(toVertex);
                float cosAngle = dot(vertexDir, coneDir);
                float angle = acos(clamp(cosAngle, -1.0, 1.0));
                
                // 优化：使用平滑过渡函数，避免在边界处突然变化
                // 1. 距离过渡：使用smoothstep实现平滑的距离衰减
                float distanceFactor = 1.0 - smoothstep(coneRange * 0.8, coneRange, distanceToCone);
                
                // 2. 角度过渡：使用smoothstep实现平滑的角度衰减
                float angleFactor = 1.0 - smoothstep(coneAngleRad * 0.8, coneAngleRad, angle);
                
                // 如果完全在圆锥范围外，无影响
                if (distanceFactor <= 0.0 || angleFactor <= 0.0)
                    return 1.0;
                
                // 计算距离衰减：越靠近圆锥中心影响越强
                float distanceFalloff = 1.0 - saturate(distanceToCone / coneRange);
                distanceFalloff = fastPow(distanceFalloff, 2.0); // 二次衰减，中心更强
                
                // 计算角度衰减：越靠近圆锥中心轴影响越强
                float angleFalloff = 1.0 - saturate(angle / coneAngleRad);
                angleFalloff = fastPow(angleFalloff, 2.0); // 二次衰减，中心轴更强
                
                // 综合衰减因子，应用过渡因子使边界平滑
                float falloff = distanceFalloff * angleFalloff * distanceFactor * angleFactor;
                
                // 优化：增强方向感
                // 1. 计算从圆锥中心到顶点的方向（风从圆锥中心吹向顶点）
                float3 windFromConeDir = normalize(toVertex);
                
                // 2. 计算风的方向应该是圆锥方向（吹风机方向）与从圆锥中心到顶点方向的混合
                // 这样可以创建更自然的发散效果，毛发被风吹离风源
                float3 coneToVertexDir = -windFromConeDir; // 从顶点指向圆锥中心的方向
                
                // 3. 计算方向混合因子：基于角度和距离
                // 在圆锥中心轴附近，主要使用圆锥方向
                // 在圆锥边缘，混合更多从圆锥中心到顶点的方向，创建发散效果
                float directionBlend = 1.0 - angleFalloff * 0.7; // 在边缘增加发散效果
                
                // 4. 计算最终风方向
                // 基础方向是圆锥方向（风吹的方向）
                float3 baseWindDir = coneDir;
                
                // 在圆锥边缘添加发散效果：风从圆锥中心向外发散
                float3 divergentDir = normalize(coneDir + coneToVertexDir * 0.5);
                
                // 混合基础方向和发散方向，应用过渡因子使方向变化平滑
                float smoothDirectionBlend = lerp(1.0, 1.0 - directionBlend, distanceFactor * angleFactor);
                adjustedWindDirection = normalize(lerp(baseWindDir, divergentDir, smoothDirectionBlend));
                
                // 5. 确保风方向与圆锥方向大致一致（防止反向）
                float alignment = dot(adjustedWindDirection, coneDir);
                if (alignment < 0.3) // 如果方向偏差太大，强制使用圆锥方向
                {
                    adjustedWindDirection = coneDir;
                }
                
                // 6. 增强方向感：在圆锥中心轴附近，风方向更集中
                // 在边缘，风方向更发散
                float directionFocus = 1.0 + angleFalloff * 2.0; // 边缘增加发散度
                adjustedWindDirection = normalize(adjustedWindDirection * directionFocus);
                
                // 7. 计算BaseMove.w调整因子：在圆锥中心处过渡为0.1
                // 在圆锥中心轴附近，减少基础移动的影响，使毛发更明显地沿着风吹的方向飘动
                // 目标值：圆锥中心处为0.1，边缘处为1.0（不调整）
                float targetBaseMoveW = 0.5; // 圆锥中心处的目标值
                // 使用平滑过渡：在圆锥边界处逐渐过渡
                baseMoveWAdjustment = lerp(targetBaseMoveW, 1.0, saturate(falloff * 2.0));
                
                // 计算最终影响值：基础值 + 圆锥内增强
                // 在圆锥中心轴附近，风频率和强度都增加
                float influence = 1.0 + falloff * (coneIntensity - 1.0);
                
                // 优化：增强方向感的影响
                // 在圆锥中心轴附近，影响更强
                influence *= 1.0 + angleFalloff * 0.5;
                
                // 应用过渡因子，使影响值在边界处平滑变化
                influence = lerp(1.0, influence, distanceFactor * angleFactor);
                
                return influence;
            }
            
            void AppendShellVertex(inout TriangleStream<Varyings> stream, Attributes input, int index, float3 deformedBasePosWS, float3 normalWS, float2 uv)
            {
                Varyings output = (Varyings)0;

                float3 posOS = input.positionOS.xyz;
                
                float3 posWS = float3(0.0f, 0.0f, 0.0f);
                float3 windMove = float3(0, 0, 0);
                
                // ============================================
                // 风动计算逻辑详解
                // ============================================
                
                // 1. 移动因子计算：控制不同毛发层的移动强度
                // 公式：moveFactor = pow(abs(index / _ShellAmount), _BaseMove.w)
                // - index: 当前毛发层索引 (0为基础层，越大越靠近毛发尖端)
                // - _ShellAmount: 总毛发层数
                // - _BaseMove.w: 指数参数，控制移动强度随层数的变化曲线
                //   值越大，外层毛发移动强度衰减越快
                float moveFactor = fastPow(abs((float)index / _ShellAmount), _BaseMove.w);
                float winmoveW = _WindMove.w;
                
                // 2. 计算圆锥形风力影响（无论是否启用风，都需要计算BaseMove.w调整）
                float baseMoveWAdjustment = 1.0;
                float3 adjustedWindDirection = normalize(_WindMove.xyz);
                float coneInfluence = 1.0;
                
                if (_UseWindCone > 0.5)
                {
                    // 计算圆锥形风力影响，获取BaseMove.w调整因子
                    coneInfluence = CalculateWindConeInfluence(deformedBasePosWS, adjustedWindDirection, baseMoveWAdjustment);
                    
                    // 应用BaseMove.w调整：在圆锥中心处过渡为0.1
                    // 调整移动因子计算，使用调整后的BaseMove.w值
                    float adjustedBaseMoveW = _BaseMove.w * baseMoveWAdjustment;
                    moveFactor = fastPow(abs((float)index / _ShellAmount), adjustedBaseMoveW);
                }
                
                // 3. 风动画计算（当_UseWind启用时）
                if (_UseWind > 0.5)
                {
                    // 3.1 计算风角度：基于时间和频率参数
                    // 公式：windAngle = _Time.w * _WindFreq.xyz
                    // - _Time.w: Unity提供的全局时间
                    // - _WindFreq.xyz: 风频率向量，控制动画速度
                    //   不同轴向使用不同频率，创造更自然的风效果
                    float3 windAngle = _Time.w * _WindFreq.xyz;
                    
                    // 3.2 应用圆锥频率增强
                    // 在圆锥范围内增加风频率，模拟吹风机效果
                    float frequencyBoost = 1.0 + (coneInfluence - 1.0) * _WindConeFrequencyBoost;
                    windAngle *= frequencyBoost;
                    
                    // 3.3 计算风移动向量
                    // 公式：windMove = moveFactor * _WindMove.xyz * sin(windAngle + posOS * _WindMove.w)
                    // - moveFactor: 移动因子，控制不同层的风强度
                    // - _WindMove.xyz: 使用调整后的风方向
                    // - sin(...): 正弦函数创建周期性摆动
                    // - posOS * _WindMove.w: 添加空间变化，使不同位置的毛发有不同的相位
                    float3 windMoveVector = _WindMove.xyz;
                    // 调整风方向以匹配圆锥方向
                    windMoveVector = adjustedWindDirection * length(_WindMove.xyz);
                    // 应用圆锥强度影响
                    windMoveVector *= coneInfluence;
                    
                    windMove = moveFactor * windMoveVector * sin(windAngle + posOS * _WindMove.w);
                }
                
                // 3. 基础移动计算
                // 公式：move = moveFactor * _BaseMove.xyz
                // - _BaseMove.xyz: 基础移动方向向量
                // 提供毛发的整体运动方向，如重力效果或基础摆动
                float3 move = moveFactor * _BaseMove.xyz;
                
                // 4. 触摸区域移动衰减：使用Lerp做渐变过渡，使效果更自然
                // 只有在启用触摸时才计算触摸衰减
                if (_UseTouch > 0.5)
                {
                    // 计算变形后的基础位置到触摸点的距离（世界空间）
                    // 注意：这里使用deformedBasePosWS而不是posWS，因为posWS在此时可能还未被赋值
                    float3 touchPos = _TouchPosition.xyz;
                    float distanceToTouch = length(deformedBasePosWS - touchPos);
                    
                    // 计算衰减因子：使用smoothstep实现平滑过渡
                    // 当distanceToTouch为0时（触摸中心），falloff为1（完全禁用移动）
                    // 当distanceToTouch为_TouchRadius时（触摸边缘），falloff为0（完全恢复移动）
                    // 使用smoothstep实现平滑过渡，避免硬边界
                    float falloff = 1.0 - smoothstep(_TouchRadius*0.5, _TouchRadius, distanceToTouch);
                    
                    // 根据衰减因子减弱所有移动
                    // falloff为1时（触摸中心），移动完全消失
                    // falloff为0时（触摸边缘），移动完全恢复
                    // 使用lerp实现平滑过渡：移动值从原始值lerp到0
                    windMove = lerp(float3(0, -0.9, 0), windMove, 1.0 - falloff);
                    _WindMove.w = lerp(0, winmoveW, 1.0 - falloff);
                    // move = lerp(float3(0, 0, 0), move, 1.0 - falloff);
                }
                
                // 5. 最终毛发方向计算
                // 公式：shellDir = normalize(normalWS + move + windMove)
                // - normalWS: 世界空间法线方向
                // - move: 基础移动向量
                // - windMove: 风移动向量
                // 归一化得到最终的毛发生长方向
                float3 shellDir = normalize(normalWS + move + windMove);
                float BaseMapA = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 1).a;
                if (index > 0)
                {
                    float3 viewDirOS = GetViewDirectionOS(posOS);
                    float eyeDotN = dot(viewDirOS, input.normalOS);
                    if (abs(eyeDotN) < _FaceViewProdThresh) return;
                    
                    // 基于变形后的基础位置计算上层毛发位置(BaseMapA颜色图A通道影响毛发长度)
                    posWS = deformedBasePosWS + shellDir * (_FurLength*BaseMapA * index);
                }
                else
                {
                    // index <= 0（包括基础层和第一层毛发）使用变形后的基础位置
                    posWS = deformedBasePosWS;
                }
                float4 posCS = TransformWorldToHClip(posWS);
                
                // 计算世界空间视角方向
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(posWS);
                
                // 计算Fresnel效果（基础版本，1 - N·V）
                float Fresnel = 1.0h - max(0.0h, dot(normalWS, viewDirWS));
                
                // ============================================
                // 阴影计算：支持顶点阴影和像素阴影两种模式
                // ============================================
                float shadowAttenuation = 1.0;
                Light mainLight = GetMainLight();
                
                #ifdef _USEVERSHADOW
                    // 顶点阴影模式：在几何着色器中计算阴影（性能更好）
                    #ifdef _USESELFSHADOW
                        float4 shadowCoord = TransformWorldToShadowCoord(posWS);
                        shadowCoord.w = max(shadowCoord.w, 0.001);
                        mainLight = GetMainLight(shadowCoord);
                        shadowAttenuation = mainLight.shadowAttenuation;
                    #endif
                #else
                    // 像素阴影模式：传递位置和法线到片段着色器（质量更好）
                    // 阴影将在片段着色器中计算
                    shadowAttenuation = 1.0; // 顶点阶段不计算阴影
                #endif
                
                // ============================================
                // 团结引擎兼容性修复：distanceAttenuation 可能返回异常值
                // ============================================
                float distanceAtten = 1.0; // 默认无衰减
                
                #ifdef _USEDISTANCEATTEN
                    distanceAtten = mainLight.distanceAttenuation;
                    
                    // 修复异常值：如果小于0.001或为NaN，设置为1.0（无衰减）
                    if (distanceAtten < 0.001 || isnan(distanceAtten) || isinf(distanceAtten))
                    {
                        distanceAtten = 1.0;
                    }
                #endif
                
                // 计算NdotL和diffuse
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                
                // 使用修复后的距离衰减值和阴影衰减
                float3 diffuse = NdotL * mainLight.color * distanceAtten * shadowAttenuation;
                
                // 添加Fresnel到diffuse（模拟边缘光效果）
                diffuse += lerp(0.3,1,fastPow(Fresnel,2));
                half baseAlpha = 1;
                // 计算环境光
                float3 ambient = SampleSH(normalWS);
                if (_UseAlpha > 0.5)    // 使用颜色贴图a通道排除无毛发处的MainColor影响
                {   // 即使物体很远，也强制使用固定LOD=0（原始分辨率）
                    baseAlpha = BaseMapA;
                }
                
                // 合并灯光贡献（包含Fresnel）
                float3 lighting = (diffuse + ambient) * lerp(float3(1,1,1), _BaseColor.rgb, baseAlpha);
                
                // 预计算alpha相关因子（考虑基础遮挡）
                float alphaFactor = _Occlusion; // 将光影计算移至顶点，这里就不能反转 1.0 - _Occlusion
                
                output.vertex = posCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.layer = (float)index / _ShellAmount;
                output.lighting = lighting;
                output.alphaFactor = alphaFactor;
                output.viewDirWS = viewDirWS;
                
                #ifndef _USEVERSHADOW
                    // 像素阴影模式：传递位置和法线到片段着色器
                    output.positionWS = posWS;
                    output.normalWS = normalWS;
                #endif

                stream.Append(output);
            }
            
            #define MAX_SHELL_LAYERS _ShellAmount
            
            // 根据是否启用像素阴影，动态调整最大顶点数
            // 像素阴影模式：每个顶点20个标量（增加了positionWS和normalWS）
            // 顶点阴影模式：每个顶点14个标量
            // 硬件限制：总标量数不能超过1024
            #ifndef _USEVERSHADOW
                // 像素阴影：1024 / 20 = 51.2，取51（17层毛发）
                [maxvertexcount(51)]
            #else
                // 顶点阴影：1024 / 14 = 73.1，取72（24层毛发）
                [maxvertexcount(72)]
            #endif
            void geom(triangle Attributes input[3], inout TriangleStream<Varyings> stream)
            {
                // int maxLayers = 72 / 3;
                // int layers = min(_ShellAmount, maxLayers);

                // 预先计算每个顶点的变形位置和法线
                float3 deformedBasePosWS[3];    //声明一个包含3个元素的数组，用于存储每个顶点变形后的世界空间位置
                float3 normalWS[3];     //用于存储每个顶点的世界空间法线
                
                if (_UseTouch>0.5){
                [unroll] for (int k = 0; k < 3; k++)
                {
                    VertexPositionInputs vertexInput = GetVertexPositionInputs(input[k].positionOS.xyz);
                    VertexNormalInputs normalInput = GetVertexNormalInputs(input[k].normalOS, input[k].tangentOS);
                    // 计算变形后的基础位置（触摸挤压效果）
                    float3 basePosWS = vertexInput.positionWS;
                    // 获取UV坐标用于纹理采样
                    float2 uv = TRANSFORM_TEX(input[k].uv, _BaseMap);
                    deformedBasePosWS[k] = ApplyTouchDeformation(basePosWS, normalInput.normalWS, uv);
                    normalWS[k] = normalize(normalInput.normalWS);
                }
                }else{
                // 当_USETOUCH未定义时，仍然需要初始化数组
                [unroll] for (int k = 0; k < 3; k++)
                {
                    VertexPositionInputs vertexInput = GetVertexPositionInputs(input[k].positionOS.xyz);
                    VertexNormalInputs normalInput = GetVertexNormalInputs(input[k].normalOS, input[k].tangentOS);
                    // 没有触摸变形，直接使用原始位置
                    deformedBasePosWS[k] = vertexInput.positionWS;
                    normalWS[k] = normalize(normalInput.normalWS);
                }
                }
// 推荐：移除 [loop]，让编译器自动展开（或显式 [unroll]）`[unroll]` 指令提示编译器展开循环，提高GPU执行效率
                for (int i = MAX_SHELL_LAYERS - 1; i >= -1; i--)
                {
                    [unroll] for (int j = 0; j < 3; j++)
                    {
                        float2 uv = TRANSFORM_TEX(input[j].uv, _BaseMap);
                        AppendShellVertex(stream, input[j], i, deformedBasePosWS[j], normalWS[j], uv);
                    }
                    stream.RestartStrip();
                }
            }

            float4 frag(Varyings input) : SV_Target
            {
                // 像素级BaseMap颜色采样（保持像素级质量）
                float4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

                // 毛发层逻辑（贴图采样）
                float4 furColor = SAMPLE_TEXTURE2D(_FurMap, sampler_FurMap, input.uv * _FurScale);
                
                // 每层毛发宽度控制因子 - 优化版本
                // 1. 计算基础毛发密度：增强纹理对比度
                float furDensity = furColor.r * 2.0;
                
                // 2. 计算偏移量：简化数学表达式，提高可读性
                // 原公式：_AlphaOffset*_AlphaOffset + (_AlphaOffset*_AlphaCutout*2)
                // 优化为：_AlphaOffset * (_AlphaOffset + _AlphaCutout * 6.0)
                float offsetValue = _AlphaOffset * (_AlphaOffset + _AlphaCutout * 6.0);
                
                // 3. 计算基础alpha值：毛发密度减去偏移量
                float baseAlpha = furDensity - offsetValue;
                
                // 4. 应用层衰减：越靠近毛发尖端透明度越高
                float alpha = baseAlpha * (1.0 - input.layer);
                
                // 5. 使用BaseMap的alpha通道控制毛发区域并加强剔除范围
                // 加强baseColor.a的作用：不仅作为蒙版，还影响剔除阈值
                half baseAlphaMask = baseColor.a;
                
                // 应用baseColor.a作为毛发区域蒙版
                alpha *= baseAlphaMask;
                
                // Alpha裁剪 - 加强baseColor.a的剔除范围
                if (input.layer < 0.0)
                {   // 最底层：增强alpha确保基础层可见
                    // 当layer为负时，(1.0 - input.layer) > 1，增强alpha值
                    alpha = alpha * (1.0 - input.layer);
                    // 确保alpha在有效范围内
                    alpha = clamp(alpha, 0.0, 1.0);
                }
                else
                {   // 其他层：根据综合阈值剔除稀疏片段，加强baseColor.a的影响
                    // 优化剔除逻辑：加强baseColor.a在剔除中的作用
                    // 1. 基础剔除阈值
                    float baseDiscardThreshold = max(_AlphaCutout, _AlphaOffset * 0.5);
                    
                    // 2. 根据baseColor.a调整剔除阈值：baseColor.a越低，剔除越严格
                    // 使用指数函数加强baseColor.a的影响：baseColor.a^2
                    float alphaMaskFactor = baseAlphaMask * baseAlphaMask; // 平方加强影响
                    
                    // 3. 计算最终剔除阈值：baseColor.a越低，阈值越高（更容易剔除）
                    // 当baseColor.a接近1时，使用基础阈值；当baseColor.a接近0时，阈值增加
                    float adjustedThreshold = lerp(baseDiscardThreshold * 2.0, baseDiscardThreshold, alphaMaskFactor);
                    
                    // 4. 应用剔除：alpha小于调整后的阈值时剔除
                    if (alpha < adjustedThreshold) discard;
                    
                    // 确保alpha在有效范围内
                    alpha = saturate(alpha);
                }
                
                // 使用预计算的遮挡因子（考虑层间混合，使用baseColor.a排除最底层无毛发处AO影响）
                float occlusion = lerp(1.0 - input.alphaFactor * baseColor.a, 1.0, input.layer);
                float3 color = baseColor.xyz * occlusion;
                
                // ============================================
                // 像素阴影计算（仅在未启用顶点阴影时）
                // ============================================
                float3 lighting = input.lighting;
                
                #ifndef _USEVERSHADOW
                    // 像素阴影模式：在片段着色器中重新计算阴影（质量更好）
                    #ifdef _USESELFSHADOW
                        // 获取主光源和阴影
                        float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                        shadowCoord.w = max(shadowCoord.w, 0.001);
                        Light mainLight = GetMainLight(shadowCoord);
                        float pixelShadowAtten = mainLight.shadowAttenuation;
                        
                        // 重新计算光照（使用像素级阴影）
                        float NdotL = saturate(dot(input.normalWS, mainLight.direction));
                        
                        // 计算Fresnel
                        float fresnel = 1.0 - max(0.0, dot(input.normalWS, normalize(input.viewDirWS)));
                        
                        // 重新计算diffuse（使用像素级阴影）
                        float3 pixelDiffuse = NdotL * mainLight.color * pixelShadowAtten;
                        pixelDiffuse += lerp(0.3,1.1,fastPow(fresnel,2));   //边缘光
                        
                        // 环境光
                        float3 ambient = SampleSH(input.normalWS);
                        
                        // 重新计算lighting（使用baseColor.a作为alpha控制）
                        half colorAlpha = 1;
                        if (_UseAlpha > 0.5)
                        {
                            colorAlpha = baseColor.a;
                        }
                        lighting = (pixelDiffuse + ambient) * lerp(float3(1,1,1), _BaseColor.rgb, colorAlpha);
                    #endif
                #endif
                
                // 应用光照
                color *= lighting;
                
                return float4(color, alpha);
            }
            // #endif
            // ============================================
            // 结束 UnlitLambert.hlsl
            // ============================================
            
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
        
            ZWrite On
            ColorMask 0
        
            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma vertex vert
            #pragma fragment frag
            
            // ============================================
            // 开始嵌入 DepthSimple.hlsl 内容
            // ============================================
            // #ifndef FUR_SHELL_DEPTH_HLSL
            // 包含UnlitInput.hlsl获取_BaseColor、_BaseMap等定义
            #include "Packages/com.unity.render-pipelines.universal/Shaders/UnlitInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.vertex = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(0, 0, 0, 0);
            }
            // #endif
            // ============================================
            // 结束 DepthSimple.hlsl
            // ============================================
            
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
            #pragma exclude_renderers gles gles3 glcore
            #pragma vertex vert
            #pragma fragment frag
            
            // ============================================
            // 开始嵌入 ShadowSimple.hlsl 内容
            // ============================================
            // #ifndef FUR_SHELL_SHADOW_HLSL
            // 包含UnlitInput.hlsl获取_BaseColor、_BaseMap等定义
            #include "Packages/com.unity.render-pipelines.universal/Shaders/UnlitInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.vertex = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            // #endif
            // ============================================
            // 结束 ShadowSimple.hlsl
            // ============================================
            
            ENDHLSL
        }
    }
    CustomEditor "FurShell_MobileGUI"
}

