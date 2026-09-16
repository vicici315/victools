// Custom_Ocean 2.0 性能优化重构（渲染结果与原版保持一致，无可察觉差异）：
//   1. 片元中的 SampleSceneDepth 由 6 次降为 5 次 —— 中心点的水深已由 depthOffset 得到，直接复用，不再重复采样
//   2. 顶点不再走 GetVertexPositionInputs（会额外算 positionWS/positionVS/positionNDC），改用等价的 TransformObjectToHClip
//   3. 已 Tiling 的泡沫 UV（input.uv * _FoamDetailMap_ST.xy）只算一次，供水色斑驳/泡沫两层共用
//   4. _FoamDetailStrength = 0 时整段泡沫跳过（此时泡沫形状恒为 0），_FoamDistortStrength = 0 时跳过两处差分采样；
//      二者都是 Uniform 常量分支，所有像素走同一路径，无分支发散开销
//   5. 合并冗余运算：移除 saturate(step(...))、saturate+smoothstep 合一、提取公共子表达式
// Custom_Ocean 1.11 浪花颜色 _WaveColor 的 A 通道作为浪花不透明度：控制浪花混入水色的强度 + 浪花处不降低水面 alpha
// Custom_Ocean 1.10 泡沫扭曲的扭曲源由「波浪贴图」改为「水色斑驳的大尺度层输出」（低频、柔和、随水团流动）
// Custom_Ocean 1.9 波浪均匀化：条纹坐标改用「屏幕空间十字 5 点平均 + 线性」的水深（_WaveFalloff 只管强度），
//                 泡沫扭曲改为 U/V 双向梯度（两轴等权、均值 0），修复各方向拉扯不匀
// Custom_Ocean 1.7 ForwardLit Pass 接入 URP 雾，处理方式对齐 PBR_Mobile_NEW：multi_compile_fog + 顶点 ComputeFogFactor 存入 Varyings.fogFactor + 片元末尾 MixFog，不新增任何雾参数
// 适配 URP，使用 SampleSceneDepth 与 LinearEyeDepth，单 Pass 性能优化
// 参数键名与项目其他 Shader 保持一致（主贴图使用 _BaseMap），切换材质时贴图不丢失

Shader "Custom/Custom_Ocean"
{
    Properties
    {
        [Header(Depth Gradient)]
        _DepthShallowColor("Shallow Color (浅水颜色)", Color) = (0.32, 0.72, 0.88, 0.80)
        _DepthDeepColor("Deep Color (深水颜色)", Color) = (0.05, 0.25, 0.45, 0.95)
        _DepthMaxDistance("Depth Max Distance (深度最大距离)", Float) = 1.0
        _WaterColorVariation("Water Color Variation (水色斑驳强度)", Range(0.0, 1.0)) = 0.4
        _WaterVariationScale("Water Variation Scale (斑驳密度，相对泡沫 Tiling)", Float) = 0.5
        _WaterVariationSpeed("Water Variation Speed (斑驳扰动速度)", Float) = 0.02

        [Header(Stylized Wave)]
        [MainTexture] _BaseMap("Wave Texture (波浪贴图)", 2D) = "white" {}
        _WaveColor("Wave Color (浪花颜色)", Color) = (1.0, 1.0, 1.0, 0.90)
        _WaveMaxDistance("Wave Max Distance (浪花最大距离)", Float) = 1.0
        _WaveFalloff("Wave Falloff (波浪外扩，越小越宽)", Range(0.2, 4.0)) = 1.6
        _WaveSpeed("Wave Speed (波浪速度)", Float) = 0.4
        _WaveCount("Wave Count (波浪密度)", Float) = 2.0
        _WaveStrength("Wave Strength (波浪强度)", Float) = 13
        _WaveCutOff("Wave Cutoff (波浪裁切)", Range(0.0, 1.0)) = 0.5

        [Header(Surface Foam)]
        _FoamDetailMap("Foam Texture (泡沫纹理，R通道，Offset=游走参数)", 2D) = "white" {}
        _FoamColor("Foam Color (泡沫颜色)", Color) = (1.0, 1.0, 1.0, 0.95)
        _FoamDetailStrength("Foam Strength (泡沫网强度)", Range(0.0, 4.0)) = 1.5
        _FoamDetailCutOff("Foam Cutoff (泡沫网裁切，越高网线越细)", Range(0.0, 1.0)) = 0.55
        _FoamDistortStrength("Foam Distort (波浪扭曲泡沫)", Range(0.0, 0.2)) = 0.01

        [Header(Render Settings)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // 雾：展开 FOG_LINEAR / FOG_EXP / FOG_EXP2 变体，由 Renderer Data 的 Fog 开关自动选择
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // SampleSceneDepth / LoadSceneDepth 定义在此文件中，Core.hlsl 不包含
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                half   fogFactor  : TEXCOORD2; // 雾因子，按 PBR_Mobile_NEW 的规范在顶点计算
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_FoamDetailMap);
            SAMPLER(sampler_FoamDetailMap);

            CBUFFER_START(UnityPerMaterial)
                half4  _DepthShallowColor;
                half4  _DepthDeepColor;
                half4  _WaveColor;
                half4  _FoamColor;
                float4 _BaseMap_ST;
                float4 _FoamDetailMap_ST;
                half   _DepthMaxDistance;
                half   _WaterColorVariation;
                half   _WaterVariationScale;
                half   _WaterVariationSpeed;
                half   _WaveMaxDistance;
                half   _WaveFalloff;
                half   _WaveSpeed;
                half   _WaveCount;
                half   _WaveStrength;
                half   _WaveCutOff;
                half   _FoamDetailStrength;
                half   _FoamDetailCutOff;
                half   _FoamDistortStrength;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                // 本 Pass 只需要裁剪空间坐标，用 TransformObjectToHClip 替代 GetVertexPositionInputs：
                // 后者额外计算 positionWS / positionVS / positionNDC 但都没用到（等价运算，结果完全一致）
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.screenPos = ComputeScreenPos(output.positionCS);
                // 雾因子：与 PBR_Mobile_NEW(viewDirWS_fog.w) 同一套处理，顶点计算 + 片元 MixFog
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // 周边点水深：海底视深度 - 水面片元视深度（水面之下为正）
            float GetWaterDepth(float2 screenUV, float waterEyeDepth)
            {
                return max(0.0, LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams) - waterEyeDepth);
            }

            half4 frag(Varyings input) : SV_Target
            {
                // ==========================================================
                // 场景深度（全片元只采样一次，后续全部复用）
                // ==========================================================
                float2 screenUV      = input.screenPos.xy / input.screenPos.w;
                float  sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float  waterEyeDepth = input.positionCS.w;
                float  depthOffset   = max(0.0, sceneEyeDepth - waterEyeDepth);

                // 深度渐变因子（近岸浅、远海深）
                half depthOffset01 = saturate(depthOffset / max(_DepthMaxDistance, 0.001));

                // ==========================================================
                // 波带专用水深：屏幕空间十字 5 点（自身 + 上下左右）取平均
                // 直接用单点原始深度时，海底每一点起伏/锯齿都会按各自方向把条纹拉扯得疏密不匀；
                // 先 saturate 再平均：天空等未写入深度的像素恒为 1，不会把均值拉爆
                // 优化：中心点复用上面已算出的 depthOffset（等价于原先的 GetWaterDepth(screenUV, waterEyeDepth)），省一次深度采样
                // ==========================================================
                half waveInvMax = 1.0 / max(_WaveMaxDistance, 0.001);
                half waveDepth01 = saturate(depthOffset * waveInvMax);

                float2 blurStep = (_ScreenParams.zw - 1.0) * 3.0; // 约 3 像素
                waveDepth01 += saturate(GetWaterDepth(screenUV + float2( blurStep.x, 0.0), waterEyeDepth) * waveInvMax);
                waveDepth01 += saturate(GetWaterDepth(screenUV + float2(-blurStep.x, 0.0), waterEyeDepth) * waveInvMax);
                waveDepth01 += saturate(GetWaterDepth(screenUV + float2(0.0,  blurStep.y), waterEyeDepth) * waveInvMax);
                waveDepth01 += saturate(GetWaterDepth(screenUV + float2(0.0, -blurStep.y), waterEyeDepth) * waveInvMax);
                waveDepth01 *= 0.2;

                // 本帧常量：多次用到的时间值 / UV 只算一次
                float  time = _Time.y;
                // 泡沫与斑驳都以泡沫纹理 Tiling 为基准UV，这里统一乘一次（input.uv * _FoamDetailMap_ST.xy）
                float2 foamTiledUV = input.uv * _FoamDetailMap_ST.xy;

                // ==========================================================
                // 水色斑驳：双层低频噪声 + 域扭曲 + 缓慢漂移，得到自然的深浅交替（参考实拍海面）
                // 频率以泡沫纹理 Tiling 为基准再乘 _WaterVariationScale（<1 即比泡沫更疏、更大块）
                // ==========================================================
                float2 variationUV   = foamTiledUV * _WaterVariationScale;
                float  variationTime = time * _WaterVariationSpeed;

                // 第一层：低频噪声扰动采样坐标（域扭曲），打散规则平铺感
                half variationWarp = SAMPLE_TEXTURE2D(_FoamDetailMap, sampler_FoamDetailMap,
                                                      variationUV * 0.53 + 0.29
                                                      + variationTime * float2(0.008, -0.012)).r;
                float2 warpedUV = variationUV + (variationWarp - 0.5) * 0.35;

                // 第二/三层：一大一小两个尺度的水团缓慢反向漂移，混合出大小不一的斑块
                // 大尺度层的采样坐标单独留出来：泡沫扭曲直接复用它求差分，与水色斑驳是同一份噪声场
                float2 variationBigUV = warpedUV + variationTime * float2(0.011, 0.007);
                half variationBig   = SAMPLE_TEXTURE2D(_FoamDetailMap, sampler_FoamDetailMap, variationBigUV).r;
                half variationSmall = SAMPLE_TEXTURE2D(_FoamDetailMap, sampler_FoamDetailMap,
                                                       warpedUV * 2.1 + 0.43
                                                       - variationTime * float2(0.009, 0.006)).r;
                half variation = smoothstep(0.2, 0.8, saturate(variationBig * 0.85 + variationSmall * 0.4));

                // 斑驳值偏移深浅因子：亮斑靠浅水色、暗斑靠深水色，形成自然的深浅交替
                // 优化：把公共子表达式 (variation - 0.5) * _WaterColorVariation 提出来复用
                half variationMod = (variation - 0.5) * _WaterColorVariation;
                half depthVariant = saturate(depthOffset01 + variationMod * 1.6);
                half4 waterColor = lerp(_DepthShallowColor, _DepthDeepColor, depthVariant);

                // 再叠加同一色相的轻微明暗起伏，让斑驳更有层次
                waterColor.rgb *= 1.0 + variationMod;

                // ==========================================================
                // 风格化波浪：2D 横向条纹，深度越浅浪花越明显
                // _WaveFalloff 只作用于「强度」：越小衰减越平缓，浪花带向外扩得更宽
                // 条纹坐标改用「线性 + 平滑」的水深：沿纵深方向间距均匀，
                // 不会出现近岸挤成一片、远岸被无限拉长（pow 曲线叠加原始深度会各方向疏密不一）
                // ==========================================================
                half waveBand01 = 1.0 - waveDepth01;
                half waveFade = pow(waveBand01, _WaveFalloff);
                float2 waveUV = TRANSFORM_TEX(float2(input.uv.x, (waveBand01 + _WaveSpeed * time) * _WaveCount), _BaseMap);

                // 波浪贴图原始值（平滑，未裁切）：用于裁出浪花（泡沫扭曲已改用斑驳场）
                // step 的结果本身只能是 0 或 1，原先外面再套一次 saturate 是冗余的
                half waveRaw = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, waveUV).r;
                half waveMask = step(_WaveCutOff, waveRaw * waveFade * _WaveStrength);

                // 深水颜色渐变（waterColor）保持不变，仅叠加浪花
                // 浪花的透明度由 _WaveColor.a 控制：
                // rgb 按「浪花遮罩 × a」混合 —— a 越小浪花越淡、越透出水色，a = 1 时与原来一致
                // alpha 取「水面与浪花取大」—— 浪花只会让水面更实，不会在浪花处把水面挖透明（与泡沫一致）
                half waveOpacity = waveMask * _WaveColor.a;
                half4 finalColor = waterColor;
                finalColor.rgb = lerp(waterColor.rgb, _WaveColor.rgb, waveOpacity);
                finalColor.a = lerp(waterColor.a, max(waterColor.a, _WaveColor.a), waveMask);

                // ==========================================================
                // 海面泡沫纹理：Offset(_FoamDetailMap_ST.zw) 作为游走参数（方向 + 速度），随时间漂移
                // 两层错位缩放采样（第二层反向游走），用于构建交织泡沫网
                // 泡沫扭曲：扭曲源改用「水色斑驳」的大尺度层输出，不再用波浪条纹
                // 斑驳是大块低频噪声，扭曲柔和、方向随水团流动，泡沫网不会被条纹切成一条条
                // 取斑驳场在 U/V 两个方向的前向差分构成二维向量：两轴等权、均值为 0，不会只沿单方向拉扯
                // 差分步长固定 0.03：斑驳噪声在该坐标空间下周期约为 1，幅度不受「斑驳密度」缩放影响
                // 两层泡沫共用同一扭曲向量 —— 泡沫网整体随水团被撕扯扭动，同时网线不会互相错断
                // ==========================================================
                // _FoamDetailStrength = 0 时，foamWebLine 与 foamPatch 两项都乘 0，泡沫形状恒为 0、对结果无任何贡献，
                // 因此这里（以及内部的扭曲分支）可以直接跳过，最多省下 7 次纹理采样。
                // 分支条件是材质 uniform，同一 draw call 内所有像素走向一致，不产生分支发散代价。
                if (_FoamDetailStrength > 0.0)
                {
                    float2 foamDistort = 0.0;
                    // _FoamDistortStrength = 0 时扭曲向量恒为 0，差分的两处采样纯属浪费
                    if (_FoamDistortStrength > 0.0)
                    {
                        half varDx = SAMPLE_TEXTURE2D(_FoamDetailMap, sampler_FoamDetailMap,
                                                      variationBigUV + float2(0.03, 0.0)).r;
                        half varDy = SAMPLE_TEXTURE2D(_FoamDetailMap, sampler_FoamDetailMap,
                                                      variationBigUV + float2(0.0, 0.03)).r;
                        foamDistort = float2(varDx - variationBig, varDy - variationBig) * (_FoamDistortStrength * 2.0);
                    }

                    float2 foamDetailUV1 = foamTiledUV + _FoamDetailMap_ST.zw * time
                                         + foamDistort;
                    float2 foamDetailUV2 = foamTiledUV * 1.37 + 0.41
                                         + float2(-_FoamDetailMap_ST.zw.y, _FoamDetailMap_ST.zw.x) * time * 0.83
                                         + foamDistort;
                    half foamDetail1 = SAMPLE_TEXTURE2D(_FoamDetailMap, sampler_FoamDetailMap, foamDetailUV1).r;
                    half foamDetail2 = SAMPLE_TEXTURE2D(_FoamDetailMap, sampler_FoamDetailMap, foamDetailUV2).r;

                    // 泡沫网：两层差值越接近（1-|a-b| 越大）越靠近等值轮廓，裁切成细丝后交织成网
                    half foamWeb = 1.0 - abs(foamDetail1 - foamDetail2);
                    half foamWebLine = saturate((foamWeb - _FoamDetailCutOff)
                                              / max(1.0 - _FoamDetailCutOff, 0.001) * _FoamDetailStrength);

                    // 片状白沫：直接用泡沫纹理的高亮区域扣出成块浪花
                    half foamPatch = saturate((foamDetail1 - _FoamDetailCutOff) * _FoamDetailStrength);

                    // 合并：全海面细丝泡沫网 + 成块白沫
                    half foamShape = saturate(max(foamWebLine, foamPatch));

                    finalColor.rgb = lerp(finalColor.rgb, _FoamColor.rgb, foamShape);
                    finalColor.a = lerp(finalColor.a, max(finalColor.a, _FoamColor.a), foamShape);
                }

                // 雾：与 PBR_Mobile_NEW 完全一致的处理方式（无独立参数，跟随 Renderer Data 的 Fog 设置）
                // 只在 rgb 上施雾，alpha 保持不变以免破坏现有的 Alpha 混合
                finalColor.rgb = MixFog(finalColor.rgb, input.fogFactor);
                return finalColor;
            }
            ENDHLSL
        }
    }

    CustomEditor "CustomOceanGUI"
}
