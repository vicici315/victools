// SpotLightVolume v5.0 - 参考VLB HD架构
// v3.0 单Pass(Cull Front) + Ray-Cone Intersection + 简化Raymarching
// v5.0 增加射线遮挡截断
// 从任意方向（包括正面面对光源）都可见
Shader "Hidden/VicTools/SpotLightVolume"
{
    Properties
    {
        [HDR] _VolumeColor ("颜色", Color) = (1, 1, 1, 1)
        _Intensity ("强度", Range(0, 2)) = 1
        _FallOffStart ("衰减起始", Float) = 0
        _FallOffEnd ("衰减结束(最远距离)", Float) = 10
        _EdgeFade ("边缘羽化", Range(0.01, 2)) = 0.3
        _EndFade ("末端羽化", Range(0, 1)) = 0.9
        _GlareFrontal ("正面眩光", Range(0, 1)) = 0.5
        _GlareBehind ("背面眩光", Range(0, 1)) = 0.3
        _ConeRadiusStart ("锥体起始半径", Float) = 0.001
        _ConeRadiusEnd ("锥体末端半径", Float) = 3
        _ConeSlopeCosSin ("锥面斜率CosSin", Vector) = (1, 0, 0, 0)
        _ClipDistance ("射线截断距离", Float) = -1
        _StartBoostIntensity ("起始亮度", Range(0, 8)) = 1.5
        _StartBoostRange ("起始亮度范围", Range(0.01, 5)) = 1.0
        _CenterFade ("中心渐变距离", Range(0.01, 1)) = 0.5
        _MaskTex ("蒙版纹理", 2D) = "white" {}
        _MaskIntensity ("蒙版强度", Range(0, 1)) = 1

        [HideInInspector] _SrcBlend ("SrcBlend", Int) = 1
        [HideInInspector] _DstBlend ("DstBlend", Int) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SpotLightVolume"
            Blend [_SrcBlend] [_DstBlend]
            ZWrite On
            Cull Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // 体积光柱本身不参与场景雾，避免深度雾在圆锥网格边缘形成硬边界
            // 移除 #pragma multi_compile_fog，使 URP 不为该 Pass 生成雾效变体
            #pragma shader_feature_local _BLEND_ADDITIVE _BLEND_SOFTADD _BLEND_ALPHA
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "SpotLightVolumeCore.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
