// ============================================================================
// FPS5.0 与 Unity Stats 面板对齐的 FPS 统计 + CPU/GPU 帧耗时
// FPS5.1 文本显示使用TextMeshProUGUI
// FPS5.2 修复从后台/休眠恢复时帧率刷新率设置失效问题
// ----------------------------------------------------------------------------
// FPS 算法：指数移动平均（EMA）对 1/unscaledDeltaTime 做平滑
//   Unity Stats 面板内部使用 1/Time.smoothDeltaTime，
//   smoothDeltaTime 本质是引擎对 deltaTime 做的 EMA。
//   本脚本直接对 1/unscaledDeltaTime 做相同的 EMA，
//   结果与 Stats 面板高度一致，且不受 timeScale 影响。
//
// CPU/GPU 耗时：通过 Unity.Profiling.ProfilerRecorder 采集引擎内部计数器。
//   GPU 时间三级回退：ProfilerRecorder → FrameTimingManager → 估算值(~)
// ============================================================================
using UnityEngine;
using UnityEngine.UI;
using Unity.Profiling;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class FPS : MonoBehaviour
{
    [Header("显示设置")]
    [Tooltip("FPS显示刷新间隔（秒）")]
    [Range(0.1f, 2.0f)]
    public float refreshRate = 0.4f;

    [Tooltip("EMA 平滑系数：越大越灵敏，越小越平滑（建议 0.05~0.15）")]
    [Range(0.01f, 0.5f)]
    public float emaAlpha = 0.1f;

    [Tooltip("是否显示CPU/GPU帧耗时（需Development Build）")]
    public bool showFrameTiming = true;

    public Color goodColor    = Color.green;
    public Color warningColor = Color.yellow;
    public Color badColor     = Color.red;

    public float warningThreshold = 29f;
    public float badThreshold     = 15f;

    [Header("帧率限制设置")]
    public bool unlockFrameRate = false;
    public int  targetFrameRate = 60;

    // ---- 私有变量 ----
    private TextMeshProUGUI fpsText;
    private float fpsEma;       // EMA 平滑后的 FPS 值
    private float displayFps;   // 上次刷新时锁定的显示值
    private float elapsed;      // 距上次刷新经过的时间

    // 单帧 dt 上限：超过此值视为异常帧（休眠唤醒/卡顿），直接跳过 EMA 更新
    private const float kMaxDeltaTime = 0.5f;
    private bool _needReset;    // 唤醒后标记需要重置状态

    private ProfilerRecorder cpuMainThreadRecorder;
    private ProfilerRecorder cpuRenderThreadRecorder;
    private ProfilerRecorder gpuFrameTimeRecorder;
    private readonly FrameTiming[] frameTimings = new FrameTiming[1];

    void Start()
    {
        fpsText    = GetComponent<TextMeshProUGUI>();
        fpsText.fontSize = 26;
        fpsEma     = 0f;
        displayFps = 0f;
        elapsed    = 0f;
        ApplyFrameRateSettings();
    }

    void OnEnable()
    {
        if (showFrameTiming)
        {
            cpuMainThreadRecorder   = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Main Thread Frame Time",   1);
            cpuRenderThreadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Render Thread Frame Time", 1);
            gpuFrameTimeRecorder    = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GPU Frame Time",               1);
        }
    }

    void OnDisable()
    {
        if (cpuMainThreadRecorder.Valid)   cpuMainThreadRecorder.Dispose();
        if (cpuRenderThreadRecorder.Valid) cpuRenderThreadRecorder.Dispose();
        if (gpuFrameTimeRecorder.Valid)    gpuFrameTimeRecorder.Dispose();
    }

    void ApplyFrameRateSettings()
    {
        if (unlockFrameRate)
        {
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount  = 0;
        }
        else if (targetFrameRate > 0)
        {
            Application.targetFrameRate = targetFrameRate;
            QualitySettings.vSyncCount  = 0;
        }
        else
        {
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount  = 1;
        }
    }

    static double GetRecorderMs(ProfilerRecorder recorder)
    {
        return recorder.Valid && recorder.Count > 0
            ? recorder.LastValue / 1_000_000.0
            : 0;
    }

    void OnApplicationPause(bool paused)
    {
        // 从后台/休眠恢复时，标记下一帧重置，避免巨大 dt 污染 EMA 和 elapsed
        if (!paused) _needReset = true;
    }

    void OnApplicationFocus(bool hasFocus)
    {
        // 部分平台（PC/Editor）通过 Focus 事件触发，同样处理
        if (hasFocus) _needReset = true;
    }

    void Update()
    {
        // 唤醒后第一帧：重置 EMA 和计时器，跳过本帧计算
        if (_needReset)
        {
            _needReset = false;
            fpsEma  = 0f;
            elapsed = 0f;
            return;
        }

        float dt = Time.unscaledDeltaTime;

        // 钳制异常大的 dt（休眠唤醒漏网帧、极端卡顿），避免 EMA 被拉崩
        if (dt > kMaxDeltaTime) return;

        // ---- EMA 更新 ----
        // 每帧计算瞬时 FPS = 1/dt，然后做指数移动平均
        // 与 Unity Stats 的 smoothDeltaTime 原理相同，读数高度一致
        float instantFps = dt > 0f ? 1f / dt : 0f;
        if (fpsEma <= 0f)
            fpsEma = instantFps;                              // 首帧直接赋值，避免从 0 爬升
        else
            fpsEma += (instantFps - fpsEma) * emaAlpha;      // EMA: new = old + alpha*(sample - old)

        // ---- 按 refreshRate 节流刷新显示 ----
        elapsed += dt;
        if (elapsed < refreshRate) return;
        elapsed -= refreshRate;

        displayFps = fpsEma;

        string frameRateInfo = unlockFrameRate ? " (Unlocked)" :
                               (targetFrameRate > 0 ? $" (Target:{targetFrameRate})" : " (VSync)");

        if (showFrameTiming)
        {
            double cpuMainMs   = GetRecorderMs(cpuMainThreadRecorder);
            double cpuRenderMs = GetRecorderMs(cpuRenderThreadRecorder);
            double gpuMs       = GetRecorderMs(gpuFrameTimeRecorder);
            double cpuMs       = System.Math.Max(cpuMainMs, cpuRenderMs);

            bool gpuEstimated = false;
            if (gpuMs <= 0)
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, frameTimings) > 0)
                    gpuMs = frameTimings[0].gpuFrameTime;
            }
            if (gpuMs <= 0 && cpuMs > 0)
            {
                // 用平均帧时间估算 GPU，比单帧 deltaTime 更稳定
                gpuMs = System.Math.Max(0, dt * 1000.0 - cpuMs);
                gpuEstimated = true;
            }

            string cpuStr = cpuMs > 0 ? $"{cpuMs:0.0}" : "--";
            string gpuStr = gpuMs > 0
                ? (gpuEstimated ? $"~{gpuMs:0.0}" : $"{gpuMs:0.0}")
                : "--";

            fpsText.text = $"FPS:{displayFps:0.0}{frameRateInfo}\nCPU:{cpuStr}ms\nGPU:{gpuStr}ms";
        }
        else
        {
            fpsText.text = $"FPS:{displayFps:0.0}{frameRateInfo}";
        }

        if (displayFps >= warningThreshold)
            fpsText.color = goodColor;
        else if (displayFps >= badThreshold)
            fpsText.color = warningColor;
        else
            fpsText.color = badColor;
    }

    void OnValidate()
    {
        if (Application.isPlaying)
            ApplyFrameRateSettings();
    }
}
