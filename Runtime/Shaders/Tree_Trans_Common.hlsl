#ifndef TREE_TRANS_COMMON_INCLUDED
#define TREE_TRANS_COMMON_INCLUDED

// ============================================================================
// Tree_Trans 共享片段
// ----------------------------------------------------------------------------
// 解决原 shader 中三个 Pass (ForwardLit / DepthOnly / ShadowCaster) 各自重复
// 维护 CBUFFER、纹理、ApplyWind、alpha clip、Ramp、法线扰动、虚拟阴影 tint 的问题。
//
// 设计原则：
//   - 一处声明，三处复用：CBUFFER 必须每个 Pass 都出现（SRP Batcher 硬性要求），
//     但通过 include 同一份声明避免漂移。
//   - 函数按"单一职责"切：每个函数只完成一项光照子任务。
//   - 数值常量集中命名：消除魔法数字。
// ============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// ----------------------------------------------------------------------------
// 风力算法调谐常量
// ----------------------------------------------------------------------------
// 主摆动：低频大幅度，决定"树干摇晃"
static const float  WIND_SWAY_MAIN_AMP        = 0.6;
// 谐波摆动：高频低幅度，决定"叶片轻颤"
static const float  WIND_SWAY_HARMONIC_AMP    = 0.25;
// 谐波时间频率倍率：>1 让谐波比主摆动跑得更快
static const float  WIND_HARMONIC_TIME_SCALE  = 2.3;
// 谐波相位倍率：让谐波与主摆动错开相位，避免对齐造成的节拍感
static const float  WIND_HARMONIC_PHASE_SCALE = 1.5;
// 噪声纹理 UV 的时间滚动速度：越大叶子局部抖动越快
static const float  WIND_NOISE_TIME_SCROLL    = 0.15;
// 位置→相位响应方向：决定风摆动的"波纹方向"，xz 平面内倾斜以打破对称
static const float2 WIND_PHASE_DIR           = float2(0.7, 0.3);

// ----------------------------------------------------------------------------
// 纹理采样器（每个 Pass 都需可见的共享声明）
// ----------------------------------------------------------------------------
TEXTURE2D(_BaseMap);         SAMPLER(sampler_BaseMap);
TEXTURE2D(_ShadowNormalMap); SAMPLER(sampler_ShadowNormalMap);
TEXTURE2D(_RampMap);         SAMPLER(sampler_RampMap);
TEXTURE2D(_WindNoiseTex);    SAMPLER(sampler_WindNoiseTex);

// ----------------------------------------------------------------------------
// 材质常量缓冲（SRP Batcher 要求每个 Pass 都有同名同序 CBUFFER；通过 include
// 保证三处完全一致，避免漂移）
// ----------------------------------------------------------------------------
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4  _BaseColor;
    half   _Cutoff;
    half   _HalfLambert;
    half4  _ShadowColor;
    half   _ShadowStrength;
    half   _ShadowSoftness;
    half   _VirtualShadowBias;
    half   _ShadowBrightness;
    float4 _ShadowNormalMap_ST;
    half   _ShadowNormalScale;
    half   _WindSpeed;
    half   _WindStrength;
    float4 _WindDirection;
    half   _WindRadius;
    float4 _WindNoiseTex_ST;
    half   _WindNoiseScale;
    half   _WindNoiseStrength;
    half   _RampStrength;
    half   _RampRow;
    half   _RampHeight;
    half   _RampOffset;
CBUFFER_END

// ============================================================================
// ApplyWind：object-space 风力位移
// ----------------------------------------------------------------------------
// 算法：
//   1. 距原点的径向权重（半径由 _WindRadius 控制，越靠外影响越大）
//   2. 主摆动 = 低频 sin(time + 位置相位)；谐波摆动 = 高频 sin(错相后)
//   3. 噪声纹理采样：[0,1] 重映射到 [-1,1] 后叠加到 sway
//   4. 沿 _WindDirection 方向施加总位移，乘以径向权重
//
// 仅当 shader_feature_local _WIND 开启时产生任何效果（编译时整段剥离）。
// ============================================================================
float3 ApplyWind(float3 positionOS)
{
    #ifdef _WIND
        half dist = length(positionOS.xyz);
        half radialWeight = saturate(dist / _WindRadius);
        radialWeight *= radialWeight;

        float time = _Time.y * _WindSpeed;
        float3 worldPos = TransformObjectToWorld(positionOS);

        // 主摆动 + 谐波叠加
        float phase = dot(worldPos.xz, WIND_PHASE_DIR);
        float sway  = sin(time + phase) * WIND_SWAY_MAIN_AMP
                    + sin(time * WIND_HARMONIC_TIME_SCALE + phase * WIND_HARMONIC_PHASE_SCALE) * WIND_SWAY_HARMONIC_AMP;

        // 噪声纹理采样：[0,1] → [-1,1] 重映射，再叠加到 sway
        float2 noiseUV = worldPos.xz * _WindNoiseScale + time * WIND_NOISE_TIME_SCROLL;
        half noiseSample = SAMPLE_TEXTURE2D_LOD(_WindNoiseTex, sampler_WindNoiseTex, noiseUV, 0).r;
        sway += (noiseSample * 2.0 - 1.0) * _WindNoiseStrength;

        float3 windDir = normalize(_WindDirection.xyz);
        positionOS.xyz += windDir * sway * _WindStrength * radialWeight;
    #endif
    return positionOS;
}

// ============================================================================
// GetBaseAlpha：提取 base map alpha 用于 alpha clip
// ----------------------------------------------------------------------------
// 原 ForwardLit 风格：albedo = baseMap * _BaseColor，再 clip(albedo.a - _Cutoff)。
// 等价于"clip(baseMap.a * _BaseColor.a - _Cutoff)"，但保持阅读一致。
//
// 用法：clip(GetBaseAlpha(uv) - _Cutoff);
// ============================================================================
half GetBaseAlpha(float2 uv)
{
    return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a;
}

// ============================================================================
// ComputeHeightGradient：根据 object-space Y 计算 ramp 渐变 V
// ----------------------------------------------------------------------------
// _RampOffset 偏移渐变底部起始高度（正值让渐变从更高处开始，负值从更低处开始）。
// 公式：saturate((posOS.y - _RampOffset) / _RampHeight)
// ============================================================================
half ComputeHeightGradient(float3 positionOS)
{
    return saturate((positionOS.y - _RampOffset) / _RampHeight);
}

// ============================================================================
// ApplyHeightRamp：按 heightGradient 在高度方向上叠加 ramp 渐变纹理
// ----------------------------------------------------------------------------
// _RampRow 控制使用 ramp 图集的哪一行；_RampStrength 控制 ramp 混合强度。
// 输出：lerp(原 albedo, albedo * rampColor, _RampStrength)
// ============================================================================
half3 ApplyHeightRamp(half3 albedo, half heightGradient)
{
    half3 rampColor = SAMPLE_TEXTURE2D(_RampMap, sampler_RampMap, half2(heightGradient, _RampRow)).rgb;
    return lerp(albedo, albedo * rampColor, _RampStrength);
}

// ============================================================================
// PerturbNormal：TS→WS 变换，应用 ShadowNormalMap 法线扰动
// ----------------------------------------------------------------------------
// 输入：
//   uv          - 纹理坐标
//   normalWS    - 已 normalize 过的世界空间法线
//   tangentWS   - 原始世界空间切线（函数内会 normalize）
//   tangentSign - 切线 w 分量（用于 handedness）
// 输出：扰动后的世界空间法线
// ============================================================================
half3 PerturbNormal(float2 uv, half3 normalWS, half3 tangentWS, half tangentSign)
{
    float2 shadowNormalUV = uv * _ShadowNormalMap_ST.xy + _ShadowNormalMap_ST.zw;
    half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_ShadowNormalMap, sampler_ShadowNormalMap, shadowNormalUV));
    normalTS.xy *= _ShadowNormalScale;
    normalTS = normalize(normalTS);

    half3 T = normalize(tangentWS);
    half3 B = cross(normalWS, T) * tangentSign;
    return normalize(
        normalTS.x * T +
        normalTS.y * B +
        normalTS.z * normalWS
    );
}

// ============================================================================
// ComputeVirtualShadowTint：根据扰动法线与光照方向计算虚拟阴影色
// ----------------------------------------------------------------------------
// 算法：
//   NdotL_biased = dot(perturbedN, lightDir) + _VirtualShadowBias
//   shadowMask   = smoothstep(-_ShadowSoftness, +_ShadowSoftness, NdotL_biased)
//   第 1 次 lerp：_ShadowColor → white，按 shadowMask 混合出"受光/背光"色
//   第 2 次 lerp：white → 上述色 * _ShadowBrightness，按 _ShadowStrength 混合
//
// 输出：可直接乘到 (diffuse + ambient) 的 shadow tint。
// ============================================================================
half3 ComputeVirtualShadowTint(half3 perturbedNormal, half3 lightDir)
{
    half  NdotL_biased  = dot(perturbedNormal, lightDir) + _VirtualShadowBias;
    half  shadowMask    = smoothstep(-_ShadowSoftness, _ShadowSoftness, NdotL_biased);
    half3 fullShadowTint= lerp(_ShadowColor.rgb, half3(1, 1, 1), shadowMask);
    return lerp(half3(1, 1, 1), fullShadowTint * _ShadowBrightness, _ShadowStrength);
}

#endif // TREE_TRANS_COMMON_INCLUDED
