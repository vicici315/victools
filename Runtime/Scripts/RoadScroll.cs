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

            Vector3 delta = scrollDirection.normalized * moveDistance;
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

        private void SortRoadSegments()
        {
            // 按 scrollDirection 投影值**从大到小**排序：
            //   - 段在 scrollDirection 方向上投影越大 → 越"靠后"（段将向 scrollDirection 方向移动，所以它在更远的下游）
            //   - 段在 scrollDirection 方向上投影越小 → 越"靠前"（离"循环入口"更近，会先被移动到队列尾）
            // 这样后续 CheckAndRearrangeSegments 移动段时，索引顺序与滚动循环方向一致。
            Vector3 dir = scrollDirection.normalized;
            roadSegments.Sort((a, b) =>
                Vector3.Dot(b.position, dir).CompareTo(Vector3.Dot(a.position, dir)));
        }

        private void CheckAndRearrangeSegments()
        {
            if (!_isInitialized) return;

            Vector3 origin = transform.position;
            Vector3 dir = scrollDirection.normalized;
            float totalLength = segmentLength * roadSegments.Count;
            float negativeSwapThreshold = -segmentLength - segmentLength * (roadSegments.Count - 1);

            foreach (var segment in roadSegments)
            {
                if (segment == null) continue;

                float projection = Vector3.Dot(segment.position - origin, dir);
                if (projection > segmentLength)
                    segment.position -= dir * totalLength;
                else if (projection < negativeSwapThreshold)
                    segment.position += dir * totalLength;
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
