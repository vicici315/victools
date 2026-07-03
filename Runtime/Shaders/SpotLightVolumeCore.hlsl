// SpotLightVolumeCore.hlsl - URP核心着色逻辑
// v1.0 基础锥形体积光
// v2.0 参考VLB架构重写，归一化Mesh + localScale缩放
// v3.0 单Pass(Cull Off) + Ray-Cone Intersection + 8步Raymarching
// v3.1 步进预计算优化为1步。效果上边缘过渡会更粗糙（只有1个采样点取平均）
// v4.1 保持v3.0原始效果，仅做性能优化（提取循环不变量、消除分支、减少采样）
// v5.0 射线遮挡截断：
//      - C#端Physics.Raycast沿光柱forward方向检测第一个碰撞物体
//      - 碰撞距离传入shader的_ClipDistance参数
//      - Raymarching循环中，采样点Z接近_ClipDistance时平滑羽化淡出（30%范围渐变）
//      - 衰减计算始终基于原始_FallOffEnd，截断不影响光柱亮度分布
//      - 完整保留v4.1的所有原始效果：双面显示、深度tOut截断、ZTest Always、Cull Off
// v5.1 射线遮挡截断：
//      - 添加_ClipDistance参数，用于C#端Physics.Raycast返回的截断距离
//      - 添加_ClipDistance参数，用于C#端Physics.Raycast返回的截断距离
//      - 添加_ClipDistance参数，用于C#端Physics.Raycast返回的截断距离
// v5.2 排除雾影响避免聚光灯体积光模拟边缘在深度雾中出现硬边。
// v6.2 蒙版投影：新增_MaskTex蒙版纹理和_MaskIntensity，在raymarching中逐步采样实现窗格光柱投影效果
// v6.3 起始增亮与中心渐变优化：
//      - 新增_StartBoostIntensity（起始亮度）和_StartBoostRange（起始亮度范围）参数
//      - 起始增亮基于光轴实际Z深度计算，使用smoothstep实现自然渐变无断层
//      - 增亮直接融入_Intensity倍率，效果明显可控
//      - 新增_CenterFade（中心渐变距离）参数，控制光轴中心高亮向外扩散的范围
//      - 所有新参数纳入存读档系统


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
    // 体积光柱不参与场景雾，不再输出 fogFactor 插值，
    // 否则在浓雾下 Mesh 硬边界会出现明显的"圆锥轮廓"伪像
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
    float _ClipDistance;  // C#端射线检测得到的截断距离, -1表示无遮挡
    half _StartBoostIntensity;  // 起始亮度增强幅度
    float _StartBoostRange;     // 起始亮度范围（绝对距离）
    half _CenterFade;           // 中心高亮渐变距离（0=无中心高亮，1=全锥体范围渐变）
    half _MaskIntensity;  // 蒙版纹理影响强度
CBUFFER_END

TEXTURE2D(_MaskTex);
SAMPLER(sampler_MaskTex);

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

    // 不再调用 ComputeFogFactor / MixFog，避免深度雾在圆锥网格硬边界处形成伪像
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

    // 场景深度限制tOut（原始v4.1逻辑，保持不变）
    if (sceneEyeDepth < eyeDepth)
    {
        tOut = tOut * (sceneEyeDepth / max(eyeDepth, 0.001));
    }

    // 无效区域
    clip(tOut - tIn - 0.0001);

    // === 预计算循环不变量（全部基于原始_FallOffEnd，不受截断影响）===
    float invFallOffEnd = 1.0 / max(_FallOffEnd, 0.001);
    float range = max(_FallOffEnd - _FallOffStart, 0.001);
    float invRange = 1.0 / range;
    float invEdgeFade = 1.0 / max(_EdgeFade, 0.01);
    float endFadeStart = 1.0 - _EndFade;
    float invEndFade = 1.0 / max(_EndFade, 0.001);

    // 步进预计算为1步
    const int STEPS = 1;
    float stepSize = (tOut - tIn) / float(STEPS);
    float3 stepStart = cameraPosOS + rayDir * (tIn + stepSize * 0.5);
    float3 stepVec = rayDir * stepSize;

    float totalIntensity = 0.0;
    float3 samplePos = stepStart;

    [unroll]
    for (int i = 0; i < STEPS; i++)
    {
        float sampleZ = samplePos.z;

        // 有效性掩码
        float valid = step(0.0, sampleZ) * step(sampleZ, _FallOffEnd);

        // === 射线遮挡截断（v5.0新增，仅此一处改动）===
        // 在_ClipDistance处平滑羽化，不影响衰减计算
        if (_ClipDistance > 0.0 && _ClipDistance < _FallOffEnd)
        {
            float clipFadeRange = min(_ClipDistance * 0.3, 0.5);
            float clipFade = 1.0 - saturate((sampleZ - (_ClipDistance - clipFadeRange)) / max(clipFadeRange, 0.001));
            valid *= clipFade;
        }

        // 距离衰减（基于原始_FallOffEnd）
        float attenLinear = 1.0 - saturate((sampleZ - _FallOffStart) * invRange);
        float atten = attenLinear * attenLinear;

        // 深度比例
        float depthRatio = sampleZ * invFallOffEnd;

        // 末端羽化
        atten *= 1.0 - saturate((depthRatio - endFadeStart) * invEndFade);

        // 径向衰减
        float radiusAtZ = lerp(_ConeRadiusStart, _ConeRadiusEnd, depthRatio);
        float distFromAxis = length(samplePos.xy);
        float radialFade = saturate((radiusAtZ - distFromAxis) * invEdgeFade / max(radiusAtZ, 0.001));

        // 中心高亮渐变：从轴线向外按_CenterFade控制渐变距离
        float centerRatio = distFromAxis / max(radiusAtZ, 0.001);
        float centerHighlight = 1.0 + (1.0 - saturate(centerRatio / max(_CenterFade, 0.01)));

        atten *= radialFade * centerHighlight * centerHighlight;

        // 蒙版纹理采样（沿光轴投影，将采样点XY归一化到该深度处的锥体横截面）
        if (_MaskIntensity > 0.001)
        {
            float2 maskUV = samplePos.xy / max(radiusAtZ, 0.001) * 0.5 + 0.5;
            maskUV = saturate(maskUV); // clamp防止超出范围
            half maskValue = SAMPLE_TEXTURE2D_LOD(_MaskTex, sampler_MaskTex, maskUV, 0).r;
            atten *= lerp(1.0, maskValue, _MaskIntensity);
        }

        totalIntensity += atten * valid;

        samplePos += stepVec;
    }

    // 归一化
    float rayLength = tOut - tIn;
    totalIntensity = (totalIntensity / float(STEPS)) * saturate(rayLength / max(rayLength + 1.0, 0.001));

    // 起始增亮：基于射线在光轴上的实际Z深度，使用smoothstep实现自然渐变避免断层
    float zIn = cameraPosOS.z + rayDir.z * tIn;
    float zOut = cameraPosOS.z + rayDir.z * tOut;
    float avgZ = (zIn + zOut) * 0.5;
    float boostFalloff = 1.0 - smoothstep(0.0, max(_StartBoostRange, 0.01), avgZ);
    float finalIntensity = _Intensity * (1.0 + boostFalloff * _StartBoostIntensity);

    // 正面/背面眩光
    float facingLight = saturate(-rayDir.z);
    float facingAway = saturate(rayDir.z);
    totalIntensity *= (1.0 + facingLight * _GlareFrontal * 2.0 + facingAway * _GlareBehind);

    // 最终
    totalIntensity *= finalIntensity;

    half4 color = half4(_VolumeColor.rgb, 1.0);

    #if _BLEND_ALPHA
        color.a = saturate(totalIntensity);
        color.rgb *= color.a;
    #else
        color.rgb *= saturate(totalIntensity);
        color.a = 1.0;
    #endif

    // 体积光柱不应用场景雾，避免在圆锥 Mesh 硬边界上出现深度雾伪像
    return color;
}

#endif
