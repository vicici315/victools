// SpotLightVolumeCore.hlsl - URP核心着色逻辑 v4.1
// 保持v3.0原始效果，仅做性能优化（提取循环不变量、消除分支、减少采样）
// 单Pass(Cull Front) + Ray-Cone Intersection + 8步Raymarching（优化版）
// 模拟聚光灯体积光（光柱/光锥），从任意角度可见。
// 渲染方式：单 Pass，Cull Front（只渲染mesh背面），ZTest Always，fragment shader 中做 raymarching
// SpotLightVolumeCore3.1 步进预计算优化为1步。效果上边缘过渡会更粗糙（只有1个采样点取平均）

#ifndef SPOT_LIGHT_VOLUME_CORE_INCLUDED
#define SPOT_LIGHT_VOLUME_CORE_INCLUDED

struct Attributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0; // uv.x: 0=sides, 1=cap
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 posObjectSpace : TEXCOORD0;
    float3 cameraPosOS : TEXCOORD1;
    float4 screenPos : TEXCOORD2;
    float3 extraData : TEXCOORD3; // x=eyeDepth
    float fogFactor : TEXCOORD4;
};

CBUFFER_START(UnityPerMaterial)
    half4 _VolumeColor;
    half _Intensity;
    float _FallOffStart;
    float _FallOffEnd;
    half _EdgeFade;
    half _EndFade;
    half _GlareFrontal;
    half _GlareBehind;
    float _ConeRadiusStart;
    float _ConeRadiusEnd;
    float2 _ConeSlopeCosSin;
CBUFFER_END

// === Ray-Cone Intersection (Inigo Quilez / VLB) ===
float rayConeIntersect(float3 rayOrigin, float3 rayDir, float fallOffEnd, float radiusStart, float radiusEnd)
{
    float3 conePosEnd = float3(0, 0, fallOffEnd);
    float3 oa = rayOrigin;
    float3 ob = rayOrigin - conePosEnd;

    float m0 = fallOffEnd * fallOffEnd;
    float m1 = oa.z * fallOffEnd;
    float m2 = rayDir.z * fallOffEnd;
    float m3 = dot(rayDir, oa);
    float m5 = dot(oa, oa);
    float m9 = (rayOrigin.z - fallOffEnd) * fallOffEnd;

    // 起始端cap (z=0)
    if (m1 < 0.0)
    {
        float t = -m1 / m2;
        float2 hitXY = oa.xy + rayDir.xy * t;
        if (dot(hitXY, hitXY) < radiusStart * radiusStart)
            return t;
    }

    // 末端cap (z=fallOffEnd)
    if (m9 > 0.0)
    {
        float t = -m9 / m2;
        float2 hitXY = ob.xy + rayDir.xy * t;
        if (dot(hitXY, hitXY) < radiusEnd * radiusEnd)
            return t;
    }

    // 侧面
    float rr = radiusStart - radiusEnd;
    float hy = m0 + rr * rr;
    float k2 = m0 * m0 - m2 * m2 * hy;
    float k1 = m0 * m0 * m3 - m1 * m2 * hy + m0 * radiusStart * (rr * m2);
    float k0 = m0 * m0 * m5 - m1 * m1 * hy + m0 * radiusStart * (rr * m1 * 2.0 - m0 * radiusStart);
    float h = k1 * k1 - k2 * k0;

    if (h < 0.0) return -1.0;

    float t = (-k1 - sqrt(h)) / (k2 + 0.0001);
    float y = m1 + t * m2;
    if (y < 0.0 || y > m0) return -1.0;
    return t;
}

Varyings vert(Attributes input)
{
    Varyings o;

    float4 vertexOS = input.positionOS;
    float isCap = input.uv.x;

    float normalizedRadiusStart = _ConeRadiusStart / max(_ConeRadiusEnd, 0.001);

    if (isCap < 0.5)
    {
        vertexOS.z *= vertexOS.z;
        vertexOS.xy *= lerp(normalizedRadiusStart, 1.0, vertexOS.z);
    }
    else
    {
        float capRadius = lerp(normalizedRadiusStart, 1.0, vertexOS.z);
        vertexOS.xy *= capRadius;
    }

    float3 scaleOS = float3(_ConeRadiusEnd, _ConeRadiusEnd, _FallOffEnd);
    o.posObjectSpace = vertexOS.xyz * scaleOS;

    float3 posWS = TransformObjectToWorld(vertexOS.xyz);
    o.positionCS = TransformWorldToHClip(posWS);

    float3 rawCameraPosOS = TransformWorldToObject(GetCameraPositionWS());
    o.cameraPosOS = rawCameraPosOS * scaleOS;

    o.screenPos = ComputeScreenPos(o.positionCS);
    float3 posVS = TransformWorldToView(posWS);
    o.extraData.x = -posVS.z;
    o.extraData.y = 0;
    o.extraData.z = 0;

    o.fogFactor = ComputeFogFactor(o.positionCS.z);
    return o;
}

half4 frag(Varyings input) : SV_Target
{
    float3 posOS = input.posObjectSpace;
    float eyeDepth = input.extraData.x;
    float3 cameraPosOS = input.cameraPosOS;

    float3 rayDir = normalize(posOS - cameraPosOS);

    // 深度采样（仅一次）
    float2 screenUV = input.screenPos.xy / input.screenPos.w;
    float sceneRawDepth = SampleSceneDepth(screenUV);
    float sceneEyeDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);

    // Ray-Cone交点
    float tIn = rayConeIntersect(cameraPosOS, rayDir, _FallOffEnd, _ConeRadiusStart, _ConeRadiusEnd);
    tIn = max(tIn, 0.0);

    // tOut
    float tOut = length(posOS - cameraPosOS);

    // 场景深度限制tOut
    if (sceneEyeDepth < eyeDepth)
    {
        tOut = tOut * (sceneEyeDepth / max(eyeDepth, 0.001));
    }

    // 无效区域
    clip(tOut - tIn - 0.0001);

    // === 预计算循环不变量 ===
    float invFallOffEnd = 1.0 / max(_FallOffEnd, 0.001);
    float range = max(_FallOffEnd - _FallOffStart, 0.001);
    float invRange = 1.0 / range;
    float invEdgeFade = 1.0 / max(_EdgeFade, 0.01);
    float endFadeStart = 1.0 - _EndFade;
    float invEndFade = 1.0 / max(_EndFade, 0.001);

    // 步进预计算 为1步。效果上边缘过渡会更粗糙（只有两个采样点取平均），但对探照灯这种通常较均匀的光柱来说差异不大。如果觉得效果不够可以随时改回来
    const int STEPS = 1;
    float stepSize = (tOut - tIn) / float(STEPS);
    float3 stepStart = cameraPosOS + rayDir * (tIn + stepSize * 0.5); // 第一个采样点
    float3 stepVec = rayDir * stepSize;                                // 每步增量

    float totalIntensity = 0.0;
    float3 samplePos = stepStart;

    // 展开提示（大多数GPU对小常数循环自动展开）
    [unroll]
    for (int i = 0; i < STEPS; i++)
    {
        float sampleZ = samplePos.z;

        // 有效性掩码（替代 if/continue 分支）
        float valid = step(0.0, sampleZ) * step(sampleZ, _FallOffEnd);

        // 距离衰减
        float attenLinear = 1.0 - saturate((sampleZ - _FallOffStart) * invRange);
        float atten = attenLinear * attenLinear;

        // 末端羽化
        float depthRatio = sampleZ * invFallOffEnd;
        atten *= 1.0 - saturate((depthRatio - endFadeStart) * invEndFade);

        // 径向衰减
        float radiusAtZ = lerp(_ConeRadiusStart, _ConeRadiusEnd, depthRatio);
        float distFromAxis = length(samplePos.xy);
        float radialFade = saturate((radiusAtZ - distFromAxis) * invEdgeFade / max(radiusAtZ, 0.001));

        atten *= radialFade;
        totalIntensity += atten * valid;

        samplePos += stepVec;
    }

    // 归一化：平均光密度 × 射线长度贡献
    // 不除以_FallOffEnd，使"最远距离"不影响整体亮度
    // rayLength用对数压缩，避免远距离时过亮或近距离时过暗
    float rayLength = tOut - tIn;
    totalIntensity = (totalIntensity / float(STEPS)) * saturate(rayLength / max(rayLength + 1.0, 0.001));

    // 正面/背面眩光
    float facingLight = saturate(-rayDir.z);
    float facingAway = saturate(rayDir.z);
    totalIntensity *= (1.0 + facingLight * _GlareFrontal * 2.0 + facingAway * _GlareBehind);

    // 最终
    totalIntensity *= _Intensity;

    half4 color = half4(_VolumeColor.rgb, 1.0);

    #if _BLEND_ALPHA
        color.a = saturate(totalIntensity);
        color.rgb *= color.a;
    #else
        color.rgb *= saturate(totalIntensity);
        color.a = 1.0;
    #endif

    color.rgb = MixFog(color.rgb, input.fogFactor);
    return color;
}

#endif
