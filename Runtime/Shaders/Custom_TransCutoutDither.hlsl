#ifndef TRANSCUTOUT_DITHER_INCLUDED
#define TRANSCUTOUT_DITHER_INCLUDED

// =====================================================================
// DitherTemporalAA - 与 Unreal Engine Masked blend mode + DitherTemporalAA 节点一致
// ---------------------------------------------------------------------
// 用 4x4 Bayer-like 抖动纹理（推荐 AA4.PNG）给 alpha 蒙版加入颗粒状渐变，
// 在 mask 接近 (_Cutoff - 0.5) ~ _Cutoff 过渡带内产生 4x4 颗粒图案，
// 肉眼看像"半透明渐变"。
//
// UE 节点真实公式（来自 UE MaskedTemporalAA Material Expression 截图）：
//   clip((Mask + ditherValue * 0.16666) - 0.5)
//   = clip(Mask + ditherValue * 0.16666 - 0.5)
//   - 0.16666 = 1/6，对应"5 translucency steps"颗粒带宽度
//   - 颗粒带位于 Mask ∈ [0.333, 0.5]
//
// 我们这里扩展 ditherValue 范围到完整 [0, 1]（不再缩放到 1/6），
// 让 _Cutoff 控制颗粒带位置：
//   clip(DitherTemporalAA(SvPositionXY, mask) - _Cutoff)
//   = clip(mask + ditherValue - 0.5 - _Cutoff)
//   = clip((mask - _Cutoff) + ditherValue - 0.5)
//
// mask 与 _Cutoff 的关系（颗粒带中心 = mask == _Cutoff）：
//   mask < _Cutoff - 0.5       ：100% clip（颗粒带外）
//   mask == _Cutoff            ：约 50% 保留（颗粒带中心）
//   mask == _Cutoff + 0.5      ：约 100% 保留（颗粒带边缘）
//   mask > _Cutoff + 0.5       ：100% 保留
//
// _Cutoff 与 UE 标准"5 steps"对应关系（颗粒带宽度 = 1，5 个过渡级）：
//   _Cutoff = 0  ：mask < 0 全 clip，mask > 0.5 全保留（mask ∈ (0, 0.5] 颗粒带）
//   _Cutoff = 0.5：mask < 0 全 clip，mask > 1 全保留（mask ∈ (0.5, 1] 颗粒带）
//   _Cutoff = 1  ：mask < 0.5 全 clip，mask > 1.5 不可能（基本全 clip）
//
// 与硬 clip 的对比：
//   clip(mask - _Cutoff)                       // 硬边，单像素精确
//   clip(DitherTemporalAA(...) - _Cutoff)      // 颗粒状过渡，肉眼像半透明
// =====================================================================

TEXTURE2D(_DitherTexture);
SAMPLER(sampler_DitherTexture);

half DitherTemporalAA(float2 SvPositionXY, half Random2, half DitherSize)
{
    // Bayer-like 抖动：UV = (pixelPos mod DitherSize + 0.5) / DitherSize
    // DitherSize 控制颗粒精细度（默认 4 = 4x4 Bayer；可以改 8/16 等）：
    //   - 4  = 4x4：每 4 像素重复一次图案，颗粒最细
    //   - 8  = 8x8：每 8 像素重复一次图案，颗粒中等
    //   - 16 = 16x16：每 16 像素重复一次图案，颗粒最粗（更平滑的过渡）
    // 中心采样（+0.5）避免双线性插值干扰抖动矩阵
    // AA4.PNG 是 64x64 = 4x4 Bayer 模式重复 16 次（与 DitherSize=4 匹配）
    //
    // 注意：DitherSize 用函数参数传入（而不是全局 _DitherSize 引用）：
    //   Unity 编译 vertex shader 时也会解析整个 HLSLPROGRAM 块中的所有函数定义，
    //   函数体若直接引用 _DitherSize 而 CBUFFER 还在 include 之后，会报
    //   "undeclared identifier '_DitherSize'"。改用参数后函数体无外部依赖，
    //   vertex/fragment shader 都可独立编译，调用处只需传 _DitherSize（CBUFFER 之后可见）。
    float invDitherSize = 1.0 / DitherSize;
    float2 matrixUV = (frac(SvPositionXY * invDitherSize) + 0.5) * invDitherSize;

    // 采样 .r 通道：AA4.PNG 的抖动值在 RGB 通道（值如 0x00/0x45/0x82/0xC0/0xFF），
    // alpha 通道全是 0xFF（1.0），所以必须用 .r 不能用 .a
    // UE 标准 Bayer 抖动纹理通常在 .a 通道，但我们的 AA4.PNG 例外
    half ditherValue = SAMPLE_TEXTURE2D(_DitherTexture, sampler_DitherTexture, matrixUV).r;

    // UE 等价公式：clip(Mask + ditherValue * 0.16666 - 0.5)（UE 5 步颗粒带宽度 0.16666）
    // 这里改为：clip(Mask + ditherValue * 0.5 - _Cutoff)
    //   - ditherValue=0（默认 black 纹理）：退化为 clip(Mask - _Cutoff)（关键！避免消失 bug）
    //   - ditherValue=1：clip(Mask + 0.5 - _Cutoff)
    //   - 颗粒带宽度从 0.16666 扩展到 0.5（更明显的颗粒渐变）
    //   - 把固定阈值 0.5 替换为 _Cutoff（用户控制颗粒带位置）
    // 用法：clip(DitherTemporalAA(SvPositionXY, mask, _DitherSize) - _Cutoff)
    return Random2 + ditherValue * 0.5;
}

#endif // TRANSCUTOUT_DITHER_INCLUDED