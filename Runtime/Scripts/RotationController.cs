// RotationController v2.0 - 新增PingPong摆动模式、AnimationCurve缓动曲线、RotationMode枚举，重构代码结构

using UnityEngine;

namespace Vic.Runtime
{
    /// 旋转模式
    public enum RotationMode
    {
        /// 持续旋转
        Continuous,
        /// 平滑持续旋转（Slerp插值）
        ContinuousSmooth,
        /// 在两个角度之间来回摆动
        PingPong
    }

    /// 轴方向枚举
    public enum Axis
    {
        X, Y, Z,
        NegativeX, NegativeY, NegativeZ
    }

    /// 旋转控制器组件
    /// 支持持续旋转、平滑旋转和来回摆动三种模式
    public class RotationController : MonoBehaviour
    {
        #region Serialized Fields

        [Header("基础设置")]
        [Tooltip("旋转模式")]
        [SerializeField] private RotationMode rotationMode = RotationMode.Continuous;

        [Tooltip("旋转速度（度/秒）")]
        [SerializeField] private float rotationSpeed = 90f;

        [Tooltip("旋转轴")]
        [SerializeField] private Vector3 rotationAxis = Vector3.up;

        [Tooltip("是否在世界坐标系中旋转（仅持续旋转模式）")]
        [SerializeField] private bool rotateInWorldSpace = true;

        [Header("控制选项")]
        [Tooltip("是否启用旋转")]
        [SerializeField] private bool isRotationEnabled = true;

        [Tooltip("是否在Awake时自动开始旋转")]
        [SerializeField] private bool autoStartOnAwake = true;

        [Header("来回旋转设置")]
        [Tooltip("旋转起始角度（基于Transform的Rotation）")]
        [SerializeField] private float pingPongAngleMin = -45f;

        [Tooltip("旋转结束角度（基于Transform的Rotation）")]
        [SerializeField] private float pingPongAngleMax = 45f;

        [Tooltip("来回旋转的缓动曲线（X轴=时间0~1，Y轴=角度插值0~1）")]
        [SerializeField] private AnimationCurve pingPongEaseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("平滑旋转设置")]
        [Tooltip("Slerp插值速度（仅ContinuousSmooth模式）")]
        [SerializeField] private float smoothLerpSpeed = 2f;

        [Header("速度波动设置")]
        [Tooltip("是否使用正弦波动的旋转速度")]
        [SerializeField] private bool useOscillatingSpeed = false;

        [Tooltip("正弦波动的幅度（度/秒）")]
        [SerializeField] private float oscillationAmplitude = 30f;

        [Tooltip("正弦波动的频率")]
        [SerializeField] private float oscillationFrequency = 1f;

        #endregion

        #region Runtime State

        private bool isRotating;
        private float pingPongT;
        private int pingPongDirection = 1;

        #endregion

        #region Public Properties

        public float RotationSpeed
        {
            get => rotationSpeed;
            set => rotationSpeed = value;
        }

        public Vector3 RotationAxis
        {
            get => rotationAxis;
            set => rotationAxis = value.normalized;
        }

        public bool IsRotationEnabled
        {
            get => isRotationEnabled;
            set => isRotationEnabled = value;
        }

        public bool IsRotating => isRotating;

        public RotationMode Mode
        {
            get => rotationMode;
            set => rotationMode = value;
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (autoStartOnAwake)
                StartRotation();
        }

        private void Update()
        {
            if (!isRotationEnabled || !isRotating)
                return;

            switch (rotationMode)
            {
                case RotationMode.Continuous:
                    PerformContinuousRotation(smooth: false);
                    break;
                case RotationMode.ContinuousSmooth:
                    PerformContinuousRotation(smooth: true);
                    break;
                case RotationMode.PingPong:
                    PerformPingPongRotation();
                    break;
            }
        }

        #endregion

        #region Rotation Implementations

        private void PerformContinuousRotation(bool smooth)
        {
            float speed = GetEffectiveSpeed();
            float rotationAmount = speed * Time.deltaTime;

            if (!smooth)
            {
                Space space = rotateInWorldSpace ? Space.World : Space.Self;
                transform.Rotate(rotationAxis, rotationAmount, space);
            }
            else
            {
                Quaternion delta = Quaternion.AngleAxis(rotationAmount, rotationAxis);
                Quaternion target = rotateInWorldSpace
                    ? delta * transform.rotation
                    : transform.rotation * delta;
                transform.rotation = Quaternion.Slerp(transform.rotation, target, smoothLerpSpeed * Time.deltaTime);
            }
        }

        private void PerformPingPongRotation()
        {
            float angleRange = pingPongAngleMax - pingPongAngleMin;
            if (Mathf.Approximately(angleRange, 0f))
                return;

            float speed = Mathf.Abs(rotationSpeed) / angleRange;
            pingPongT += speed * Time.deltaTime * pingPongDirection;

            if (pingPongT >= 1f)
            {
                pingPongT = 1f;
                pingPongDirection = -1;
            }
            else if (pingPongT <= 0f)
            {
                pingPongT = 0f;
                pingPongDirection = 1;
            }

            float currentAngle = Mathf.Lerp(pingPongAngleMin, pingPongAngleMax, pingPongEaseCurve.Evaluate(pingPongT));
            ApplyEulerAngleOnAxis(currentAngle);
        }

        #endregion

        #region Helpers

        /// 获取当前帧的有效速度（含波动）
        private float GetEffectiveSpeed()
        {
            float speed = rotationSpeed;
            if (useOscillatingSpeed)
                speed += Mathf.Sin(Time.time * oscillationFrequency) * oscillationAmplitude;
            return speed;
        }

        /// 将角度值应用到rotationAxis对应的欧拉角分量
        private void ApplyEulerAngleOnAxis(float angle)
        {
            Vector3 axis = rotationAxis.normalized;
            float absX = Mathf.Abs(axis.x);
            float absY = Mathf.Abs(axis.y);
            float absZ = Mathf.Abs(axis.z);

            // 判断主轴：取绝对值最大的分量作为旋转轴
            if (absX >= absY && absX >= absZ)
            {
                Vector3 euler = transform.localEulerAngles;
                euler.x = angle;
                transform.localEulerAngles = euler;
            }
            else if (absY >= absX && absY >= absZ)
            {
                Vector3 euler = transform.localEulerAngles;
                euler.y = angle;
                transform.localEulerAngles = euler;
            }
            else if (absZ >= absX && absZ >= absY)
            {
                Vector3 euler = transform.localEulerAngles;
                euler.z = angle;
                transform.localEulerAngles = euler;
            }
            else
            {
                // Fallback: 自定义轴使用AngleAxis
                transform.localRotation = Quaternion.AngleAxis(angle, rotationAxis);
            }
        }

        private static readonly Vector3[] AxisVectors =
        {
            Vector3.right,   // X
            Vector3.up,      // Y
            Vector3.forward, // Z
            Vector3.left,    // NegativeX
            Vector3.down,    // NegativeY
            Vector3.back     // NegativeZ
        };

        #endregion

        #region Public API

        public void StartRotation()
        {
            isRotating = true;
            isRotationEnabled = true;
        }

        public void StopRotation()
        {
            isRotating = false;
        }

        public void ResumeRotation()
        {
            if (isRotationEnabled)
                isRotating = true;
        }

        public void ToggleRotation()
        {
            if (isRotating) StopRotation();
            else StartRotation();
        }

        public void SetRotationAxis(Vector3 axis)
        {
            rotationAxis = axis.normalized;
        }

        public void SetRotationAxis(Axis axis)
        {
            rotationAxis = AxisVectors[(int)axis];
        }

        public void ResetRotation()
        {
            transform.rotation = Quaternion.identity;
        }

        public void SetRotation(Vector3 eulerAngles)
        {
            transform.rotation = Quaternion.Euler(eulerAngles);
        }

        public void SetRotation(Quaternion rotation)
        {
            transform.rotation = rotation;
        }

        #endregion
    }
}
