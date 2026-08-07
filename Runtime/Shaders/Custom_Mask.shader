// TransCutout 1.0 初版：全透明 stencil mask 材质
// TransCutout 1.1 参数对齐 PBR_Mobile：_BaseColor（替换 _MaskColor）、_Cutoff 范围改 Range(0.001, 1.0)
// TransCutout 2.0 重做为「透明溶解材质」：
//                      - _BaseMap.a 作为溶解蒙版（"透明颗粒渐变"来自蒙版 alpha 的自然过渡）
//                      - _Cutoff 控制阈值：mask.a < _Cutoff 的像素 clip（溶解消失），反之按 _BaseColor 输出
//                      - 移除 stencil 块、_UseMaskMap toggle、_DebugShowMask、_SrcBlend/_DstBlend 这些冗余参数
//                        只剩 4 个属性，全部与 PBR_Mobile 对齐
// TransCutout 2.1 输出 alpha 改为由 _BaseMap.a 派生（_BaseColor.a 不再控制半透明）：
//                      - _BaseMap.a 仍是溶解蒙版：低于 _Cutoff 的像素 clip（消失）
//                      - 高于 _Cutoff 的像素：输出 alpha = smoothstep(_Cutoff, _Cutoff + 0.05, mask)
//                        在阈值附近提供 0.05 宽度的软过渡（避免硬边锯齿）
//                      - _BaseColor 现在仅作为颜色使用（RGB），A 通道已被忽略
//                      - _Cutoff 既是溶解阈值（clip 边界），也是输出 alpha 的"透明阈值"起点
// TransCutout 2.2 改为对齐 PBR_Mobile_Trans 的透明实现：
//                      - 移除 smoothstep 软过渡，回到硬裁剪（clip + 输出 alpha = 1.0）
//                      - _Cutoff 改为可见的「透明阈值」参数（取消 [HideInInspector]）
//                      - 新增 _SrcBlend / _DstBlend / _ZWrite 三个开关：
//                          · 透明裁剪模式（推荐）：_SrcBlend=One(1), _DstBlend=Zero(0), _ZWrite=1
//                            支持透明裁剪阴影，不会被黑色覆盖
//                          · 真半透明模式：       _SrcBlend=SrcAlpha(5), _DstBlend=OneMinusSrcAlpha(10), _ZWrite=0
//                            真正半透明效果，但阴影投射会有问题
//                      - RenderType 改为 "TransparentCutout"，与 PBR_Mobile_Trans 一致
//                      - 新增 ShadowCaster pass，clip 同步 _BaseMap.a 与 _Cutoff，保证阴影裁剪正确
// TransCutout 2.3 集成 DitherTemporalAA 函数（颗粒状渐变，模拟 UE 同名函数效果）：
//                      - 新增 _UseDither 开关 + _DitherTexture 属性（推荐 AA4.PNG，4x4 Bayer）
//                      - 开启时用 4x4 Bayer 抖动代替硬 clip，在 mask 接近 _Cutoff 的过渡带
//                        产生颗粒状渐变（类似 UE DitherTemporalAA 的视觉效果）
//                      - 函数与抖动纹理声明抽离到 Custom_TransCutoutDither.hlsl，
//                        Forward / ShadowCaster / DepthOnly 三个 pass 都 include 同一份代码
// TransCutout 2.4 粒子系统 ColorOverLifetime 驱动透明阈值：
//                      - 新增 _UseParticleAlpha toggle（shader_feature_local _USEPARTICLEALPHA）
//                      - 开启后 _Cutoff 由粒子 vertex color A 通道（input.color.a, 0~1）
//                        在 [_CutoffMin, _CutoffMax] 范围内线性映射得到：
//                          · particleAlpha=0 → cutoff = _CutoffMin
//                          · particleAlpha=1 → cutoff = _CutoffMax
//                          · dynamicCutoff = lerp(_CutoffMin, _CutoffMax, particleAlpha)
//                      - 不勾选时保持 _Cutoff 固定值行为（兼容现有材质）
//                      - 三个 pass 都从 Attributes.color (COLOR semantic) 读取，保证
//                        Forward 颜色 / ShadowCaster 阴影 / DepthOnly 深度像素集同步
// TransCutout 2.5 URP 雾效支持（仅 Forward pass）：
//                      - 加 #pragma multi_compile_fog,生成 FOG_LINEAR / FOG_EXP / FOG_EXP2 变体
//                      - include URP ShaderLibrary/Fog.hlsl（提供 ComputeFogFactor / MixFog）
//                      - Varyings 加 fogFactor (TEXCOORD2),vert 用 ComputeFogFactor(positionCS.z) 计算
//                      - frag 输出前 MixFog(finalColor.rgb, input.fogFactor) 把雾色混进最终色
//                      - ShadowCaster / DepthOnly 不加雾效（这两个 pass 只写深度,不写颜色）
Shader "Custom/TransCutout"
{
    Properties
    {
        [Header(Transparent Dissolve)]
        [MainTexture] _BaseMap    ("Base Map (A = transparency mask)", 2D) = "white" {}
        [MainColor]   _BaseColor  ("Base Color (RGB only, A unused)", Color) = (1,1,1,1)
        _Cutoff("透明阈值 (Alpha Cutoff)", Range(0.001, 2.0)) = 0.5

        // 粒子系统 ColorOverLifetime 驱动：勾选后 _Cutoff 由粒子颜色 A 通道控制
        // particleAlpha=0 时 cutoff=_CutoffMin（透明）
        // particleAlpha=1 时 cutoff=_CutoffMax（不透明）
        // 关闭时保持 _Cutoff 固定值
        // [Header(Particle Alpha (ColorOverLifetime))]
        [Toggle(_USEPARTICLEALPHA)] _UseParticleAlpha("Use Particle Alpha (驱动透明阈值)", Float) = 0
        _CutoffMin ("Cutoff Max (粒子Alpha=1)", Range(0, 2)) = 1.45
        _CutoffMax ("Cutoff Min (粒子Alpha=0)", Range(0, 1)) = 0.27

        // 颗粒状渐变（模拟 UE DitherTemporalAA）：开启后用 4x4 Bayer 抖动代替硬 clip
        // 默认值 "black" (RGBA=0) 是关键：未设置贴图时 ditherValue=0，退化为硬 clip 行为
        // （避免默认值 "white" (RGBA=1) 让所有像素都被 clip 导致整个材质消失）
        // 把 AA4.PNG（4x4 Bayer）拖到 _DitherTexture 槽后才能产生颗粒效果
        // [Header(Dither)]
        [Toggle(_USEDITHER)] _UseDither("Use Dither (颗粒状渐变)", Float) = 0
        [NoScaleOffset] _DitherTexture("Dither Texture", 2D) = "black" {}
        // 颗粒精细度：屏幕像素数 = _DitherSize × _DitherSize 个重复一次 Bayer 图案
        // 推荐：4=4x4(细颗粒), 8=8x8(中等), 16=16x16(粗颗粒/平滑过渡)
        // 必须与 _DitherTexture 的 Bayer 矩阵尺寸匹配（AA4.PNG=4, AA8.PNG=8）
        _DitherSize ("Dither Size (颗粒精细度)", Range(2, 16)) = 4


        // 透明裁剪模式（推荐）：_SrcBlend=1(One), _DstBlend=0(Zero), _ZWrite=1
        // 支持透明裁剪阴影，不会被黑色覆盖
        // 半透明模式：_SrcBlend=5(SrcAlpha), _DstBlend=10(OneMinusSrcAlpha), _ZWrite=0
        // 真正的半透明效果，但阴影投射会有问题
        // [Header(Transparent Blending)]
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 0
        [Toggle(_ZWWRITE)] _ZWrite("Z Write", Float) = 1

        // [Header(Render Settings)]
        [Enum(Off,0,Front,1,Back,2)] _Cull ("Cull Mode", Float) = 2  // Back
    }

    SubShader
    {
        Tags
        {
            "RenderType"        = "TransparentCutout"
            "RenderPipeline"    = "UniversalPipeline"
            "Queue"             = "Transparent"
            "IgnoreProjector"   = "True"
        }
        LOD 100

        // ── 主 Forward Pass：透明裁剪（默认 One/Zero + ZWrite=1）──
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #pragma shader_feature_local _ZWWRITE
            #pragma shader_feature_local _USEDITHER
            #pragma shader_feature_local _USEPARTICLEALPHA
            // 雾效:multi_compile_fog 生成 FOG_LINEAR/FOG_EXP/FOG_EXP2 变体,MixFog 根据变体分支
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 注:ComputeFogFactor / MixFog / MixFogColor 实际定义在
            //     Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl
            //     (URP Core.hlsl line 165 已间接 include),无需再 include Fog.hlsl
            //     (URP 14.0.12 里这个文件根本不存在,旧版本 URP 才有)
            #include "Packages/com.youdoo.victools/Runtime/Shaders/Custom_TransCutoutDither.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;        // 粒子 ColorOverLifetime 顶点色（包含 A 通道）
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : TEXCOORD1;     // 传递粒子顶点色到 frag
                float  fogFactor  : TEXCOORD2;     // 雾效插值因子（0=无雾,1=完全雾色）
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
                half   _CutoffMin;
                half   _CutoffMax;
                half   _DitherSize;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                // GetVertexPositionInputs: URP 内置工具函数,一次调用把 object-space 顶点位置
                // 变换到多个坐标系,返回的 vIn 结构体里包含:
                //   - positionWS  : 世界空间坐标(用于光照、世界空间特效)
                //   - positionVS  : 视图空间坐标(视图空间特效,如视差/雾效)
                //   - positionCS  : 裁剪空间坐标(SV_POSITION,vertex shader 必须输出的最终位置)
                // 比起手动写 TransformObjectToWorld / TransformWorldToHClip 更简洁,且保证与 URP 内部一致
                VertexPositionInputs vIn = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vIn.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;        // 传递粒子顶点色
                // 雾效因子:基于裁剪空间 z 计算,frag 里 MixFog 会按 fog 类型(线性/exp/exp2)分支处理
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 1. 采样 _BaseMap（RGB 作为颜色调制，.a 作为透明度源；_BaseColor.a 不再参与）
                half4 mask = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

                // 2. 计算动态 cutoff：
                //    - 默认（_USEPARTICLEALPHA 未定义）：用 _Cutoff 固定值（兼容现有材质）
                //    - 启用后：用粒子 vertex color A 通道（0~1）在 [_CutoffMin, _CutoffMax] 范围映射
                //      dynamicCutoff = lerp(_CutoffMin, _CutoffMax, particleAlpha)
                //      particleAlpha=0 → cutoff = _CutoffMin（更接近透明）
                //      particleAlpha=1 → cutoff = _CutoffMax（更接近不透明）
                #ifdef _USEPARTICLEALPHA
                    half dynamicCutoff = lerp(_CutoffMin, _CutoffMax, input.color.a);
                #else
                    half dynamicCutoff = _Cutoff;
                #endif

                // 3. 透明度决定：硬裁剪 或 颗粒状渐变（_USEDITHER）
                //    默认走硬 clip（兼容旧材质）；勾选 _UseDither 后用 DitherTemporalAA
                //    在 mask.a 接近 dynamicCutoff 的过渡带产生 4x4 颗粒图案
                //    mask.a < dynamicCutoff 仍然 100% clip（与硬 clip 一致）
                #ifdef _USEDITHER
                    half dithered = DitherTemporalAA(input.positionCS.xy, mask.a, _DitherSize);
                    clip(dithered - dynamicCutoff);
                #else
                    clip(mask.a - dynamicCutoff);
                #endif

                // 4. 颜色源选择：
                half3 finalColor = _BaseColor.rgb * input.color.rgb * mask.rgb;

                // 5. 雾效混合：按 URP 雾色(Fog Color)和 fogFactor 把 finalColor 推向雾色
                //    - 当 FOG_OFF 变体或 #pragma multi_compile_fog 未生效时,MixFog 退化为纯返回原色
                //    - 默认雾色在 Lighting → Environment → Fog 设置
                finalColor.rgb = MixFog(finalColor.rgb, input.fogFactor);

                // 6. 输出 alpha = 1（默认 Blend One/Zero 即"透明裁剪"模式）；
                //    若用户在材质面板把 _SrcBlend/_DstBlend 切到 SrcAlpha/OneMinusSrcAlpha，
                //    可结合材质颜色与背景做真半透明（但此时阴影投射会有问题，需自行权衡）
                return half4(finalColor, 1.0h);
            }
            ENDHLSL
        }

        // ── ShadowCaster Pass：clip 同步 _BaseMap.a 与 _Cutoff，保证阴影正确投影 ──
        // 实现与 PBR_Mobile_Trans 完全一致：使用 _LightDirection * _ShadowBias.x 简单偏移，
        // 避免 URP 14 下 ApplyShadowBias / LerpWhiteTo 跨文件依赖问题。
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex   shadowVert
            #pragma fragment shadowFrag

            #pragma shader_feature_local _USEDITHER
            #pragma shader_feature_local _USEPARTICLEALPHA

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.youdoo.victools/Runtime/Shaders/Custom_TransCutoutDither.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
                half   _CutoffMin;
                half   _CutoffMax;
                half   _DitherSize;
            CBUFFER_END

            float3 _LightDirection;
            float4 _ShadowBias;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;          // 粒子 ColorOverLifetime 顶点色
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : TEXCOORD1;       // 传递粒子顶点色
            };

            Varyings shadowVert(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(input.normalOS);

                // 与 PBR_Mobile_Trans 一致：简单法线方向偏移，避免阴影 acne
                positionWS = positionWS + _LightDirection * _ShadowBias.x;

                output.positionCS = TransformWorldToHClip(positionWS);

                // 传递 UV 和粒子顶点色给 frag，用于 clip 同步
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;

                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                return output;
            }

            half4 shadowFrag(Varyings input) : SV_Target
            {
                // ShadowCaster 统一走硬 clip,忽略 _USEDITHER 的颗粒抖动:
                //   - 颗粒抖动会在 shadow map 上"打孔",经过 PCF 多采样平均后阴影密度大幅下降
                //     (薄/小物体的阴影甚至会完全看不见),这是 dither 透明材质的通病。
                //   - URP 标准透明裁剪 shader 的 ShadowCaster pass 也是走硬 clip。
                //   - 视觉的颗粒半透明仍由 Forward pass 的 _USEDITHER 实现,不受影响。
                // _USEPARTICLEALPHA 公式分支保留(端点 bug 待修复,见 Forward pass 同步注释)。
                half mask = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
                #ifdef _USEPARTICLEALPHA
                    half dynamicCutoff = lerp(_Cutoff, _CutoffMin, input.color.a);
                #else
                    half dynamicCutoff = _Cutoff;
                #endif
                clip(mask - dynamicCutoff);
                return 0;
            }
            ENDHLSL
        }

        // ── DepthOnly Pass：与 Forward 一致的 clip，保证深度预写形状正确 ──
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag

            #pragma shader_feature_local _USEDITHER
            #pragma shader_feature_local _USEPARTICLEALPHA

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.youdoo.victools/Runtime/Shaders/Custom_TransCutoutDither.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
                half   _CutoffMin;
                half   _CutoffMax;
                half   _DitherSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;          // 粒子 ColorOverLifetime 顶点色
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : TEXCOORD1;       // 传递粒子顶点色
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;         // 传递粒子顶点色
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                // 与 Forward 完全一致的 clip 逻辑（含 _USEDITHER / _USEPARTICLEALPHA 分支），
                // 保证 depth 与 color 像素集完全同步
                half mask = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
                #ifdef _USEDITHER
                    #ifdef _USEPARTICLEALPHA
                        // _Cutoff 与粒子 alpha 共同决定（与 Forward 公式一致）
                        half dynamicCutoff = lerp(_Cutoff, _CutoffMin, input.color.a);
                    #else
                        half dynamicCutoff = _Cutoff;
                    #endif
                    half dithered = DitherTemporalAA(input.positionCS.xy, mask, _DitherSize);
                    clip(dithered - dynamicCutoff);
                #else
                    // 硬 clip 模式：直接用 _Cutoff（与 Forward 一致）
                    clip(mask - _Cutoff);
                #endif
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }

    FallBack Off
    CustomEditor "TransCutoutGUI"
}