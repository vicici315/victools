// ============================================================================
// FPS4.0 精确帧率统计 + CPU/GPU 帧耗时（ProfilerRecorder 方案）
// ----------------------------------------------------------------------------
// 核心算法：区间帧计数法（Frame Counting Method）
//   在固定时间窗口（refreshRate）内累计实际渲染的帧数，
//   窗口结束时计算 FPS = frameCount / elapsedTime。
//   相比 EMA（指数移动平均）对单帧 deltaTime 取倒数的方式，
//   该方法不受显示设备刷新率影响，同一机器接不同显示器读数一致。
//
// CPU/GPU 耗时：通过 Unity.Profiling.ProfilerRecorder 采集引擎内部计数器。
//   数据来源与 Unity Profiler 窗口一致，比 FrameTimingManager 更可靠。
//
// GPU 时间获取策略（三级回退）：
//   1. 优先使用 ProfilerRecorder 的 "GPU Frame Time" 计数器（精确值）
//   2. 若不可用，使用 FrameTimingManager.gpuFrameTime（部分平台支持）
//   3. 若仍为 0（Vulkan 安卓常见），用估算：GPU ≈ 总帧时间 - CPU时间
//      原理：渲染流水线中 总帧时间 ≈ max(CPU, GPU)
//      估算值前加 "~" 标记，与精确值区分
//
// CPU 时间：取 max(主线程, 渲染线程)，代表 CPU 侧的实际瓶颈耗时
//
// 注意事项：
//   - ProfilerRecorder 仅在 Development Build 或 Editor 中可用
//   - Release Build 中计数器不可用，耗时显示为 "--"
// ============================================================================
using UnityEngine;
using UnityEngine.UI;
using Unity.Profiling;

/// 帧率显示组件，挂载到带有 Text 组件的 UI 对象上即可使用。
/// 支持精确 FPS 统计、CPU/GPU 帧耗时显示、帧率限制控制。
[RequireComponent(typeof(Text))]
public class FPS : MonoBehaviour
{
    // ========================================================================
    // Inspector 可配置参数
    // ========================================================================

    [Header("显示设置")]

    /// FPS 数值的刷新间隔（秒）。
    /// 同时也是帧计数的采样窗口大小：
    ///   - 值越小，刷新越频繁，但数值跳动越明显
    ///   - 值越大，数值越稳定，但响应延迟越高
    /// 推荐 0.5 秒，兼顾稳定性和实时性。
    [Tooltip("FPS显示刷新间隔（秒）- 值越小刷新越快，但数值跳动越明显")]
    [Range(0.1f, 2.0f)]
    public float refreshRate = 0.5f;

    /// 是否在 FPS 旁边额外显示 CPU/GPU 每帧耗时（毫秒）。
    /// 开启后可直观判断性能瓶颈：
    ///   - CPU 耗时高 → 逻辑/物理/动画等 CPU 侧工作过重
    ///   - GPU 耗时高 → 渲染负载过重（DrawCall、Shader、分辨率等）
    /// 需要 Development Build 才能获取数据。
    [Tooltip("是否显示CPU/GPU帧耗时（需Development Build）")]
    public bool showFrameTiming = true;

    // ---- 颜色设置：根据帧率高低自动变色，方便一眼判断性能状态 ----
    [Tooltip("FPS文本的颜色")]
    public Color goodColor = Color.green;       // FPS >= warningThreshold 时显示绿色
    public Color warningColor = Color.yellow;   // badThreshold <= FPS < warningThreshold 时显示黄色
    public Color badColor = Color.red;          // FPS < badThreshold 时显示红色

    [Tooltip("颜色变化的阈值")]
    public float warningThreshold = 29f;        // 低于此值变黄色警告
    public float badThreshold = 15f;            // 低于此值变红色严重警告

    [Header("帧率限制设置")]

    /// 解除帧率限制后，Application.targetFrameRate = -1 且关闭垂直同步，
    /// 引擎会以硬件能达到的最高速度渲染，用于测试设备的极限性能。
    [Tooltip("是否解除帧率限制")]
    public bool unlockFrameRate = false;

    /// 目标帧率，仅在 unlockFrameRate = false 时生效。
    ///   - 正数（如 30、60）：锁定到指定帧率，同时关闭 VSync
    ///   - -1：启用垂直同步（VSync），帧率跟随显示器刷新率
    [Tooltip("目标帧率（仅在不解除限制时有效，-1表示使用垂直同步）")]
    public int targetFrameRate = 60;

    // ========================================================================
    // 私有变量
    // ========================================================================

    private Text fpsText;              // 用于显示帧率信息的 UI Text 组件

    // ---- 区间帧计数法所需变量 ----
    private int frameCount;            // 当前采样窗口内已渲染的帧数
    private float elapsed;             // 当前采样窗口已经过的时间（秒）
    private float currentFps;          // 最近一次计算得到的 FPS 值

    // ---- ProfilerRecorder：订阅引擎内部 Profiler 计数器 ----
    // 数据单位为纳秒（ns），显示时需要除以 1,000,000 转换为毫秒（ms）。
    //
    // "CPU Main Thread Frame Time"    — CPU 主线程处理一帧的耗时
    //   包含：脚本 Update/LateUpdate、物理、动画、UI 布局等
    //
    // "CPU Render Thread Frame Time"  — 渲染线程处理一帧的耗时
    //   包含：将渲染命令提交给 GPU 驱动（CommandBuffer 提交）
    //   在多线程渲染模式下，此线程与主线程并行工作
    //   CPU 显示值 = max(主线程, 渲染线程)，代表 CPU 侧实际瓶颈
    //
    // "GPU Frame Time"                — GPU 执行渲染命令的耗时
    //   包含：顶点处理、片元着色、后处理等所有 GPU 工作
    //   Vulkan 安卓设备上此计数器通常不可用，会自动回退到估算模式
    private ProfilerRecorder cpuMainThreadRecorder;
    private ProfilerRecorder cpuRenderThreadRecorder;
    private ProfilerRecorder gpuFrameTimeRecorder;

    // ---- FrameTimingManager 回退（第二级 GPU 时间来源）----
    private readonly FrameTiming[] frameTimings = new FrameTiming[1];

    // ========================================================================
    // 生命周期方法
    // ========================================================================

    /// 初始化：获取 Text 组件引用，重置所有计数器，应用帧率设置。
    void Start()
    {
        fpsText = GetComponent<Text>();
        fpsText.fontSize = 26;

        // 重置帧计数器和时间累加器
        frameCount = 0;
        elapsed = 0f;
        currentFps = 0f;

        // 根据 Inspector 配置应用帧率限制
        ApplyFrameRateSettings();
    }

    /// 启用时启动 ProfilerRecorder。
    /// 使用 OnEnable/OnDisable 而非 Start/OnDestroy，
    /// 这样在组件被禁用/重新启用时能正确管理 Recorder 生命周期，避免资源泄漏。
    /// 
    /// ProfilerRecorder.StartNew() 会立即开始采集指定计数器的数据，
    /// capacity 参数指定内部环形缓冲区大小，1 表示只保留最新一帧的数据。
    void OnEnable()
    {
        if (showFrameTiming)
        {
            // 订阅三个引擎内置 Profiler 计数器，capacity=1 只保留最新值
            cpuMainThreadRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Internal, "CPU Main Thread Frame Time", 1);
            cpuRenderThreadRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Internal, "CPU Render Thread Frame Time", 1);
            gpuFrameTimeRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Internal, "GPU Frame Time", 1);
        }
    }

    /// 禁用时释放 ProfilerRecorder。
    /// ProfilerRecorder 是值类型但持有原生资源，必须调用 Dispose() 释放。
    /// Valid 检查确保只释放已成功创建的 Recorder。
    void OnDisable()
    {
        if (cpuMainThreadRecorder.Valid) cpuMainThreadRecorder.Dispose();
        if (cpuRenderThreadRecorder.Valid) cpuRenderThreadRecorder.Dispose();
        if (gpuFrameTimeRecorder.Valid) gpuFrameTimeRecorder.Dispose();
    }

    /// 根据 Inspector 中的配置应用帧率限制策略。
    /// 三种模式：
    ///   1. unlockFrameRate = true  → 不限帧，关闭 VSync，跑满硬件极限
    ///   2. targetFrameRate > 0     → 锁定到指定帧率，关闭 VSync
    ///   3. targetFrameRate = -1    → 启用 VSync，帧率跟随显示器
    ///
    /// 注意：VSync（vSyncCount）的优先级高于 targetFrameRate，
    /// 所以锁帧时必须先把 vSyncCount 设为 0，否则 targetFrameRate 不生效。
    void ApplyFrameRateSettings()
    {
        if (unlockFrameRate)
        {
            // 模式1：解除所有帧率限制
            Application.targetFrameRate = -1;   // -1 表示不限制
            QualitySettings.vSyncCount = 0;     // 0 表示关闭垂直同步
        }
        else
        {
            if (targetFrameRate > 0)
            {
                // 模式2：锁定到指定帧率
                Application.targetFrameRate = targetFrameRate;
                QualitySettings.vSyncCount = 0; // 必须关闭 VSync 才能让 targetFrameRate 生效
            }
            else
            {
                // 模式3：使用垂直同步
                Application.targetFrameRate = -1;
                QualitySettings.vSyncCount = 1; // 1 表示每个 VBlank 渲染一帧
            }
        }
    }

    /// 从 ProfilerRecorder 读取最新值并转换为毫秒。
    /// 
    /// LastValue 返回最近一帧的计数器值（单位：纳秒）。
    /// 除以 1,000,000 转换为毫秒，方便阅读。
    /// 如果 Recorder 无效（Release Build 或计数器不存在），返回 0。
    /// <param name="recorder">要读取的 ProfilerRecorder</param>
    /// <returns>耗时（毫秒），无效时返回 0</returns>
    static double GetRecorderMs(ProfilerRecorder recorder)
    {
        // Valid：Recorder 是否成功创建并正在采集
        // Count > 0：缓冲区中是否有数据（首帧可能还没有）
        return recorder.Valid && recorder.Count > 0
            ? recorder.LastValue / 1000000.0   // 纳秒 → 毫秒
            : 0;
    }

    /// 每帧调用，执行帧计数和定时刷新显示。
    ///
    /// 【区间帧计数法原理】
    /// 每次 Update 调用代表引擎完成了一帧渲染，所以：
    ///   1. frameCount++ 记录本窗口内渲染了多少帧
    ///   2. elapsed 累加真实经过时间（unscaledDeltaTime 不受 Time.timeScale 影响）
    ///   3. 当 elapsed >= refreshRate 时，计算 FPS = frameCount / elapsed
    ///   4. 重置计数器，开始下一个采样窗口
    ///
    /// 这种方式的优势：
    ///   - 直接统计"单位时间内的帧数"，物理含义清晰
    ///   - 不依赖单帧 deltaTime 的倒数，避免非线性偏差
    ///   - 采样窗口是固定的真实时间，不受显示器刷新率影响
    ///   - 同一机器接不同显示器，FPS 读数一致
    void Update()
    {
        // 每帧递增计数器并累加时间
        frameCount++;
        elapsed += Time.unscaledDeltaTime;  // 使用 unscaledDeltaTime 确保不受时间缩放影响

        // 达到刷新间隔时更新显示
        if (elapsed >= refreshRate)
        {
            // ----------------------------------------------------------------
            // 核心计算：FPS = 区间内帧数 / 区间实际耗时
            // 例如：0.5 秒内渲染了 30 帧 → FPS = 30 / 0.5 = 60
            // ----------------------------------------------------------------
            currentFps = frameCount / elapsed;

            // ----------------------------------------------------------------
            // 构建显示文本
            // ----------------------------------------------------------------
            string frameRateInfo = unlockFrameRate ? " (Unlocked)" :
                                   (targetFrameRate > 0 ? $" (Target: {targetFrameRate})" : " (VSync)");

            if (showFrameTiming)
            {
                // 从 ProfilerRecorder 读取最新的帧耗时数据
                double cpuMainMs = GetRecorderMs(cpuMainThreadRecorder);     // CPU 主线程耗时
                double cpuRenderMs = GetRecorderMs(cpuRenderThreadRecorder); // 渲染线程耗时
                double gpuMs = GetRecorderMs(gpuFrameTimeRecorder);          // GPU 耗时（可能为 0）

                // CPU 显示值 = max(主线程, 渲染线程)
                // 主线程和渲染线程并行工作，取较大值代表 CPU 侧的实际瓶颈
                double cpuMs = System.Math.Max(cpuMainMs, cpuRenderMs);

                // --------------------------------------------------------
                // GPU 时间三级回退策略：
                // 1. ProfilerRecorder "GPU Frame Time"（上面已获取）
                // 2. FrameTimingManager.gpuFrameTime
                // 3. 估算：总帧时间 - CPU 时间
                // --------------------------------------------------------
                bool gpuEstimated = false;

                if (gpuMs <= 0)
                {
                    // 第二级：尝试 FrameTimingManager
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, frameTimings) > 0)
                    {
                        gpuMs = frameTimings[0].gpuFrameTime;  // 单位已经是毫秒
                    }
                }

                if (gpuMs <= 0 && cpuMs > 0)
                {
                    // 第三级：估算模式
                    // 总帧时间 = unscaledDeltaTime * 1000
                    // 渲染流水线中：总帧时间 ≈ max(CPU, GPU)
                    // 当总帧时间 > CPU 时间 → 差值即为 GPU 额外消耗
                    double totalFrameMs = Time.unscaledDeltaTime * 1000.0;
                    gpuMs = System.Math.Max(0, totalFrameMs - cpuMs);
                    gpuEstimated = true;
                }

                // 格式化显示
                string cpuStr = cpuMs > 0 ? $"{cpuMs:0.0}" : "--";
                string gpuStr;
                if (gpuMs > 0)
                    gpuStr = gpuEstimated ? $"~{gpuMs:0.0}" : $"{gpuMs:0.0}";
                else
                    gpuStr = "--";

                // 显示格式示例：
                // F: 60.0 (Target: 60)  CPU: 8.2ms  GPU: 5.4ms
                // 或 Vulkan 估算模式：
                // F: 60.0 (Target: 60)  CPU: 8.2ms  GPU: ~5.4ms
                fpsText.text = $"FPS: {currentFps:0.0}{frameRateInfo}\nCPU: {cpuStr}ms\nGPU: {gpuStr}ms";
            }
            else
            {
                fpsText.text = $"F: {currentFps:0.0}{frameRateInfo}";
            }

            // 根据帧率高低设置文本颜色，方便一眼判断性能状态
            if (currentFps >= warningThreshold)
                fpsText.color = goodColor;      // 绿色：性能良好
            else if (currentFps >= badThreshold)
                fpsText.color = warningColor;   // 黄色：性能警告
            else
                fpsText.color = badColor;       // 红色：性能严重不足

            // 重置计数器，开始下一个采样窗口
            frameCount = 0;
            elapsed = 0f;
        }
    }

    /// 编辑器中修改 Inspector 参数时自动回调。
    /// 如果游戏正在运行，立即应用新的帧率设置，方便实时调试。
    void OnValidate()
    {
        if (Application.isPlaying)
        {
            ApplyFrameRateSettings();
        }
    }
}
