// RoadScroll v2.1 - 道路无限滚动（段循环移动）+ 速度联动后处理 + 距离 HUD
// 重构：
//   - 纳入 Vic.Runtime 命名空间，融入 VicTools 工具集
//   - 提取 MapSpeedToRange 通用映射，消除 MapSpeedToVignette / MapSpeedToRadialBlurScale 重复
//   - 消除 minSpeed 魔数重复（原 60f 在两处独立定义，存在静默不一致风险）
//   - 统一 MetersPerSecondToKmh 转换，3.6f 仅在一处定义
//   - 缓存 Shader.PropertyToID、Renderer、Material，避免每帧 GetComponent / 字符串查找 / .material 实例化
//   - 将 Start / Update 拆分为职责清晰的私有方法
//   - 提取 DebugLog 辅助，消除 if(showDebug) 样板
//   - 替换已弃用的 FindObjectOfType 为 FindFirstObjectByType
//   - 提取命名常量（kVignette*、kRadialBlur*、kJoystickAccel、kMsToKmh）
//   - #region 划分代码块，可读性对齐 SpotLightVolume / RotationController 风格
// RoadScroll v2.1 变更：
//   - 移除 UVScroll 模式（含 RoadScrollMode 枚举、scrollMode / scrollMaterial / scrollTextureProperty 字段、
//     _uvScrollOffset 状态、UpdateUVScroll / ResolveScrollMaterial / DetectMeshExtent /
//     ComputeUVScrollOffset / ResolveTexturePropertyId 方法）：默认 MeshTranslate 已能覆盖模型滚动需求，
//     不再对模型材质做任何修改。RadialBlur 仍保留（独立的视觉特效模块）。
// 行为变更：保留 MeshTranslate（段沿 scrollDirection 移动 + 远处段瞬移回来）。
// public 字段名/类型/默认值/顺序与 v2.0 完全一致以保证序列化数据兼容（已删除字段在旧资产里
// 会被 Unity 自动忽略，不会破坏场景）。
// RoadScroll v2.2 移除scrollMaterial相关功能；取消swapThreshold参数，改为使用segmentLength作为阈值，避免大物件滚动时出现闪烁问题。
// RoadScroll v2.3 无限循环偏移改用局部坐标轴：
//               - SortRoadSegments / CheckAndRearrangeSegments: scrollDirection 通过
//                 transform.TransformDirection 变换到世界空间再用于世界位置的点积/瞬移
//               ScrollSegments 也改回世界空间移动：scrollDirection 通过 transform.TransformDirection
//               变换到世界空间再用 Space.World 移动；原因：原 Space.Self 使用段自身局部轴，
//               当段不是 RoadScroll 子物体时（用户手动拖入场景的段），段自身 rotation 不会跟父对象
//               旋转，导致 X/Y/Z 三个轴中只有 X（无旋转时方向一致）才能"看起来"滚动，其他轴方向错乱。
//               现在 ScrollSegments / SortRoadSegments / CheckAndRearrangeSegments 三处统一使用
//               worldDir = transform.TransformDirection(scrollDirection) 做世界空间运算，X/Y/Z 都正常
// RoadScroll v2.4 Start 时新增 CalibrateOwnerTransform：不破坏 roadSegments 中任何
//               滚动对象的位置，仅对挂载脚本的 RoadScroll 自身 transform.position
//               按其局部坐标系下的 scrollDirection 方向，偏移 2 * segmentLength 做初始矫正。
//               （注：原文笔误写成 localPosition，实际写的是 transform.position 即世界位移。）
// RoadScroll v2.5 优化超大段对象（segmentLength >> 段 mesh 真实尺寸，如长桥/绵延山脉桥段）
//               下"初始偏移矫正产生大偏差"的问题：
//               - CalibrateOwnerTransform：从 v2.4 硬编码 `2 × segmentLength` 偏移改为
//                 **自适应偏移**——实测所有段的 worldDir 投影中心，把 origin 平移到队列几何中心。
//                 这样无论 L 多大、N 多少、段是否事先等距分布，origin 始终落在队列车头 +halfSpan 处，
//                 不会再因 origin 推得过远而在第一帧触发 CheckAndRearrangeSegments 的 3L 量级跳变。
//               - CheckAndRearrangeSegments：阈值从非对称 `[-N×L, +L]`（区间宽度 (N+1)×L，
//                 多走 1L 出现漂移）改为对称 `[-halfSpan, +halfSpan]`（区间宽度 = N×L = totalLength
//                 = 每段循环一圈步长，恰好对齐）。消除 v2.2 删除 swapThreshold 后遗留的"每循环 1L 漂移"，
//                 也消除 v2.4 在大 L 下与 origin 偏移叠加的越界跳变 bug。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Vic.Runtime
{
    /// 道路无限滚动脚本
    /// 使用多段模型实现无限循环游走效果
    /// 同步驱动：URP Vignette 暗角、径向模糊后处理、距离/速度 HUD
    [DisallowMultipleComponent]
    public class RoadScroll : MonoBehaviour
    {
        // ====================================================================
        // Inspector - 字段名/类型/默认值/顺序与原版严格一致，保证序列化兼容
        // ====================================================================

        #region Inspector

        [Header("滚动模型设置")]
        [Tooltip("滚动模型列表（按滚动方向从前往后排列）")]
        public List<Transform> roadSegments = new List<Transform>();

        [Header("特效配置")]
        [Tooltip("拖入场景中的 Volume 对象")]
        public Volume volume;

        [Tooltip("径向模糊效果GameObject，速度≥radialBlurShowSpeed时显示，<时隐藏，自动设置材质_Scale参数")]
        public GameObject radialBlur;

        [Header("滚动设置")]
        [Tooltip("单段滚动的长度（单位：米）。可通过 Inspector 下方 X/Y/Z 按钮自动按主对象对应轴向长度设置。")]
        public float segmentLength = 10f;

        [Tooltip("滚动速度（米/秒）")]
        public float scrollSpeed = 5f;

        [Tooltip("滚动方向")]
        public Vector3 scrollDirection = Vector3.forward;

        // [Tooltip("交替时机距离阈值（单位：米）")]
        // public float swapThreshold = 5.0f; //加大值到接近滚动速度，避免大物件滚动时出现闪烁

        [Tooltip("是否自动开始滚动")]
        public bool autoStart = true;

        [Header("UI显示")]
        [Tooltip("显示总滚动距离的UI文本组件")]
        public Text distanceText;

        [Tooltip("UI文本显示位置偏移")]
        public Vector2 textOffset = new Vector2(0f, -50f);

        [Header("调试信息")]
        [Tooltip("显示调试信息")]
        public bool showDebug = true;

        #endregion

        // ====================================================================
        // 常量
        // ====================================================================

        #region Constants

        // Vignette 速度-强度映射区间
        private const float kVignetteMinSpeed = 30f;
        private const float kVignetteMaxSpeed = 160f;
        private const float kVignetteMaxValue = 0.45f;

        // RadialBlur 速度-模糊映射区间（minSpeed 即为显示/隐藏阈值）
        private const float kRadialBlurMinSpeed = 60f;       // 同时作为显示阈值
        private const float kRadialBlurMaxSpeed = 280f;      // 速度区间（x3.6 为公里时速）
        private const float kRadialBlurMaxValue = 17.1f;     // 径向最高模糊值
        private const float kRadialBlurMinClamp = 1f;        // 与原 Mathf.Max(scale, 1f) 钳制一致

        // 操纵杆（垂直轴）速度调节系数
        private const float kJoystickAccel = 0.2f;

        // 米/秒 -> 公里/小时
        private const float kMsToKmh = 3.6f;

        #endregion

        // ====================================================================
        // 运行时状态
        // ====================================================================

        #region Runtime State

        private bool _isScrolling;
        private bool _isInitialized;
        private float _totalDistance;

        // 速度联动 - Vignette
        private Vignette _vignette;

        // 速度联动 - RadialBlur（缓存避免每帧 GetComponent / 字符串查找 / .material 实例化）
        private Renderer _radialBlurRenderer;
        private Material _radialBlurMaterial;

        // Shader 属性 ID 缓存
        private static readonly int kRadialBlurSampleCountId = Shader.PropertyToID("_SampleCount");

        #endregion

        // ====================================================================
        // Unity 生命周期
        // ====================================================================

        #region Unity Lifecycle

        private void Start()
        {
            UpdateDistanceDisplay();

            if (roadSegments == null || roadSegments.Count < 1)
            {
                Debug.LogError("RoadScroll: 需要至少一段滚动模型！");
                return;
            }

            CalibrateOwnerTransform();
            SortRoadSegments();
            SetupUITextPosition();
            InitializeVignette();
            CacheRadialBlur();

            if (autoStart)
                StartScrolling();

            DebugLog($"RoadScroll 初始化完成，共有 {roadSegments.Count} 段模型，每段长度 {segmentLength} 米");

            _isInitialized = true;
        }

        private void Update()
        {
            if (!_isInitialized || !_isScrolling) return;

            ApplyJoystickInput();

            // Mesh 移动：段沿 scrollDirection 移动 + 远处段瞬移
            ScrollSegments();
            CheckAndRearrangeSegments();

            UpdateVignette();
            UpdateRadialBlur();
            UpdateDistanceDisplay();
        }

        private void OnDestroy()
        {
            DebugLog($"RoadScroll: 对象 {gameObject.name} 被销毁");
        }

        private void OnDrawGizmos()
        {
            if (!showDebug) return;
            DrawSegmentGizmos();
            DrawHudLabelGizmo();
        }

        #endregion

        // ====================================================================
        // 公开 API
        // ====================================================================

        #region Public API

        /// 开始滚动
        public void StartScrolling()
        {
            _isScrolling = true;
            // swapThreshold = segmentLength;  //设置swapThreshold接近滚动速度，避免大物件滚动时出现闪烁
            DebugLog("RoadScroll: 开始滚动");
        }

        #endregion

        // ====================================================================
        // 滚动核心
        // ====================================================================

        #region Scroll

        private void ApplyJoystickInput()
        {
            scrollSpeed += Input.GetAxis("Vertical") * kJoystickAccel;
        }

        private void ScrollSegments()
        {
            float moveDistance = scrollSpeed * Time.deltaTime;
            _totalDistance += moveDistance;

            // scrollDirection 是父对象的局部轴向量 → 必须变换到世界空间才能让段在世界空间移动，
            // 这样无论段是否作为子物体挂在 RoadScroll 下，无论段自身的 rotation 怎样，
            // 都会沿"父对象的局部 axis 在世界空间的方向"推进，避免 Space.Self 用段自身轴的 bug。
            Vector3 worldDir = transform.TransformDirection(scrollDirection.normalized);
            Vector3 delta = worldDir * moveDistance;
            foreach (var segment in roadSegments)
            {
                if (segment != null)
                    segment.Translate(delta, Space.World);
            }
        }

        #endregion

        // ====================================================================
        // 段无限循环
        // ====================================================================

        #region Segment Cycling

        private void CalibrateOwnerTransform()
        {
            // v2.5 重构：原 v2.4 用硬编码 `2 × segmentLength` 把 origin 推到"最末段下游 +2L"位置。
            // 当 segmentLength 远超段 mesh 真实尺寸（超大段对象，如长桥、绵延山脉桥段、海面平台）
            // 时，origin 偏移后会落入 CheckAndRearrangeSegments 非对称阈值区间 [-N×L, +L] 的
            // 下游越界区，立即触发第一帧段的 `position -= worldDir × totalLength` 跳变
            // （量级 ≈ 3 × segmentLength），表现为"场景整排对象被瞬间平移几千/几万米"。
            //
            // 修复思路：与编辑器 AlignSegmentsAlongAxis 的"worldCenter 落在队列几何中心"
            // 语义对齐——实测所有段在 worldDir 方向上的投影中心，把 origin 沿 worldDir 平移到
            // 这个几何中心。这样无论 L 多大、N 多少、段是否事先等距分布，origin 始终落在
            // "队列车头 + halfSpan" 处，配合 v2.5 CheckAndRearrangeSegments 的对称阈值
            // 即可让所有段投影稳定落在 [-halfSpan, +halfSpan] 内。

            if (segmentLength <= 0f)
            {
                Debug.LogWarning($"[RoadScroll Calibrate] 跳过：segmentLength={segmentLength}");
                return;
            }

            if (scrollDirection.sqrMagnitude < 1e-6f)
            {
                Debug.LogWarning("[RoadScroll Calibrate] scrollDirection 长度为 0，无法确定矫正方向，已跳过");
                return;
            }

            Vector3 worldDir = transform.TransformDirection(scrollDirection.normalized);

            // 实测当前所有段在 worldDir 方向的投影中心（投影原点 = 当前 transform.position）
            float sumProjection = 0f;
            int validCount = 0;
            foreach (var segment in roadSegments)
            {
                if (segment == null) continue;
                sumProjection += Vector3.Dot(segment.position - transform.position, worldDir);
                validCount++;
            }
            if (validCount == 0) return;

            // 把 origin 沿 worldDir 平移 -meanProjection，使 origin 落在队列车头与几何中心的中点。
            // 配合 v2.5 对称阈值，段在 [−halfSpan, +halfSpan] 内稳定分布，不再第一帧跳变。
            float offset = -sumProjection / validCount;
            if (Mathf.Abs(offset) < 1e-6f) return;  // 偏差 < 1μm，跳过避免无意义的 transform.position 写入

            Vector3 worldDelta = worldDir * offset;
            transform.position += worldDelta;

            DebugLog($"[RoadScroll Calibrate] 自适应偏移：origin 沿 worldDir 移动 {offset:F3}m 到队列几何中心" +
                     $"（N={validCount}, segmentLength={segmentLength}；已避免 v2.4 固定 2L 偏移在大 L 下的 3L 跳变 bug）");
        }

        private void SortRoadSegments()
        {
            // 按"父坐标系下"段在 scrollDirection 方向上的投影值**从大到小**排序：
            //   - scrollDirection 在不同父对象旋转下不再是世界向量；将其变换到世界空间后再做投影。
            //   - 段在该方向上投影越大 → 越"靠后"（段将向 scrollDirection 方向移动，所以它在更远的下游）
            //   - 段在该方向上投影越小 → 越"靠前"（离"循环入口"更近，会先被移动到队列尾）
            // 这样后续 CheckAndRearrangeSegments 移动段时，索引顺序与滚动循环方向一致。
            Vector3 worldDir = transform.TransformDirection(scrollDirection.normalized);
            Vector3 worldOrigin = transform.position;
            roadSegments.Sort((a, b) =>
                Vector3.Dot(b.position - worldOrigin, worldDir)
                    .CompareTo(Vector3.Dot(a.position - worldOrigin, worldDir)));
        }

        private void CheckAndRearrangeSegments()
        {
            if (!_isInitialized) return;

            // scrollDirection 是相对父对象的局部轴，需变换到世界空间后才能用世界位置点积/位移。
            Vector3 worldDir = transform.TransformDirection(scrollDirection.normalized);
            Vector3 worldOrigin = transform.position;
            float totalLength = segmentLength * roadSegments.Count;

            // v2.5 重构：阈值从非对称 `[-N×L, +L]`（区间宽度 (N+1)×L，每循环一圈净走 (N+1)×L，
            // 多了 1L 累积漂移）改为对称 `[-halfSpan, +halfSpan]`。对称区间宽度 = totalLength = N×L
            // = 每段循环一圈步长，每段恰好走过 N×L 就回到原位，消除 v2.2 删除 swapThreshold 后
            // 遗留的"每循环 1L 漂移"；同时与 v2.5 CalibrateOwnerTransform 的自适应偏移对接，
            // 第一帧不再因 origin 偏移导致段被反向瞬移 3L 量级。
            float halfSpan = totalLength * 0.5f;

            foreach (var segment in roadSegments)
            {
                if (segment == null) continue;

                float projection = Vector3.Dot(segment.position - worldOrigin, worldDir);
                if (projection > halfSpan)
                    segment.position -= worldDir * totalLength;
                else if (projection < -halfSpan)
                    segment.position += worldDir * totalLength;
            }
        }

        #endregion

        // ====================================================================
        // 速度联动 - Vignette
        // ====================================================================

        #region Vignette

        private void InitializeVignette()
        {
            if (volume == null)
            {
                // URP 场景里 Volume 应当存在，但允许用户不挂：自动找一次，找不到就静默降级
                volume = FindFirstObjectByType<Volume>();
                if (volume == null)
                {
                    Debug.LogWarning("RoadScroll: 未找到Volume组件，暗角效果将不可用");
                    return;
                }
            }

            if (volume.profile != null && volume.profile.TryGet(out _vignette))
            {
                DebugLog("RoadScroll: Vignette效果初始化成功");
            }
            else
            {
                Debug.LogWarning("RoadScroll: 无法从Volume中获取Vignette效果");
                _vignette = null;
            }
        }

        private void UpdateVignette()
        {
            if (_vignette == null) return;
            _vignette.intensity.value = MapSpeedToRange(
                scrollSpeed, kVignetteMinSpeed, kVignetteMaxSpeed, kVignetteMaxValue);
        }

        #endregion

        // ====================================================================
        // 速度联动 - RadialBlur
        // ====================================================================

        #region RadialBlur

        private void CacheRadialBlur()
        {
            if (radialBlur == null) return;

            _radialBlurRenderer = radialBlur.GetComponent<Renderer>();
            if (_radialBlurRenderer == null)
            {
                Debug.LogWarning("RoadScroll: radialBlur GameObject没有Renderer组件");
                return;
            }

            // 首次访问 .material 会创建实例；缓存避免每帧重复实例化
            _radialBlurMaterial = _radialBlurRenderer.material;
            if (_radialBlurMaterial == null)
                Debug.LogWarning("RoadScroll: 无法从Renderer获取材质");
        }

        private void UpdateRadialBlur()
        {
            if (radialBlur == null) return;

            UpdateRadialBlurVisibility();
            UpdateRadialBlurMaterial();
        }

        private void UpdateRadialBlurVisibility()
        {
            bool shouldShow = scrollSpeed >= kRadialBlurMinSpeed;
            if (radialBlur.activeSelf == shouldShow) return;

            radialBlur.SetActive(shouldShow);
            DebugLog($"RoadScroll: radialBlur {(shouldShow ? "显示" : "隐藏")} (速度: {scrollSpeed:F1}, 阈值: {kRadialBlurMinSpeed})");
        }

        private void UpdateRadialBlurMaterial()
        {
            if (_radialBlurMaterial == null) return;

            float scale = MapSpeedToRange(
                scrollSpeed, kRadialBlurMinSpeed, kRadialBlurMaxSpeed, kRadialBlurMaxValue);

            // 原代码 (s-60)/(280-60)*17.1 在 s<=60 时为 0，再被 Mathf.Max 钳制到 1。
            // MapSpeedToRange 已忠实于这个"从 0 出发"的语义，保留 Mathf.Max 作为钳制表达。
            float sampleCount = Mathf.Max(scale, kRadialBlurMinClamp);
            _radialBlurMaterial.SetFloat(kRadialBlurSampleCountId, sampleCount);

            DebugLog($"RoadScroll: 设置RadialBlur Scale为 {sampleCount:F3}");
        }

        #endregion

        // ====================================================================
        // UI / HUD
        // ====================================================================

        #region UI

        private void SetupUITextPosition()
        {
            if (distanceText == null) return;

            RectTransform rect = distanceText.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = textOffset;

            distanceText.alignment = TextAnchor.UpperCenter;

            DebugLog("RoadScroll: UI文本位置已设置为屏幕中间顶部");
        }

        private void UpdateDistanceDisplay()
        {
            if (!_isInitialized || distanceText == null) return;
            distanceText.text = BuildHudText();
        }

        private string BuildHudText()
        {
            return $"距离: {_totalDistance:F1}米\n速度: {MetersPerSecondToKmh(scrollSpeed):F1}公里/小时";
        }

        #endregion

        // ====================================================================
        // Gizmos
        // ====================================================================

        #region Gizmos

        private void DrawSegmentGizmos()
        {
            Vector3 size = new Vector3(segmentLength, 1f, segmentLength);
            Vector3 arrowDir = scrollDirection.normalized * 2f;

            Gizmos.color = Color.green;
            foreach (var segment in roadSegments)
            {
                if (segment == null) continue;

                Gizmos.DrawWireCube(segment.position, size);

                Gizmos.color = Color.red;
                Gizmos.DrawRay(segment.position, arrowDir);
                Gizmos.color = Color.green;
            }
        }

        private void DrawHudLabelGizmo()
        {
#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying) return;
            if (transform == null) return;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, BuildHudText());
#endif
        }

        #endregion

        // ====================================================================
        // 工具方法
        // ====================================================================

        #region Helpers

        /// 将速度从 [minSpeed, maxSpeed] 区间线性映射到 [0, maxOutput]，
        /// 区间外钳制。区间内从 0 出发，与原 MapSpeedToVignette / MapSpeedToRadialBlurScale
        /// 的"0 基准 + 调用方按需钳制"语义完全一致。
        /// 用途：速度 -> Vignette 强度、速度 -> 径向模糊采样数。
        private static float MapSpeedToRange(float speed, float minSpeed, float maxSpeed, float maxOutput)
        {
            if (speed <= minSpeed) return 0f;
            if (speed >= maxSpeed) return maxOutput;
            return (speed - minSpeed) / (maxSpeed - minSpeed) * maxOutput;
        }

        /// 米/秒 -> 公里/小时
        private static float MetersPerSecondToKmh(float mps) => mps * kMsToKmh;

        /// 统一受 showDebug 控制的 Debug.Log
        private void DebugLog(string message)
        {
            if (showDebug) Debug.Log(message);
        }

        #endregion
    }
}
