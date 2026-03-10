using UnityEngine;

namespace Game.Utils
{
    /// <summary>
    /// 旋转控制器组件
    /// 用于让挂载的对象沿着指定轴以指定速度旋转
    /// </summary>
    public class RotationController : MonoBehaviour
    {
        [Header("旋转设置")]
        [Tooltip("旋转速度（度/秒）")]
        [SerializeField] private float rotationSpeed = 90f;
        
        [Tooltip("旋转轴")]
        [SerializeField] private Vector3 rotationAxis = Vector3.up;
        
        [Tooltip("是否在世界坐标系中旋转")]
        [SerializeField] private bool rotateInWorldSpace = true;
        
        [Header("控制选项")]
        [Tooltip("是否启用旋转")]
        [SerializeField] private bool isRotationEnabled = true;
        
        [Tooltip("是否在开始时自动开始旋转")]
        [SerializeField] private bool autoStartOnAwake = true;
        
        [Header("高级设置")]
        [Tooltip("是否使用平滑旋转（使用Slerp）")]
        [SerializeField] private bool useSmoothRotation = false;
        
        [Tooltip("平滑旋转的插值速度（仅当useSmoothRotation为true时生效）")]
        [SerializeField] private float smoothRotationSpeed = 2f;
        
        [Tooltip("是否使用正弦波动的旋转速度")]
        [SerializeField] private bool useOscillatingSpeed = false;
        
        [Tooltip("正弦波动的幅度（度/秒）")]
        [SerializeField] private float oscillationAmplitude = 30f;
        
        [Tooltip("正弦波动的频率")]
        [SerializeField] private float oscillationFrequency = 1f;
        
        private bool isRotating = false;
        private float currentRotationSpeed;
        private Quaternion targetRotation;
        
        /// <summary>
        /// 获取或设置旋转速度
        /// </summary>
        public float RotationSpeed
        {
            get => rotationSpeed;
            set => rotationSpeed = value;
        }
        
        /// <summary>
        /// 获取或设置旋转轴
        /// </summary>
        public Vector3 RotationAxis
        {
            get => rotationAxis;
            set => rotationAxis = value.normalized;
        }
        
        /// <summary>
        /// 获取或设置是否启用旋转
        /// </summary>
        public bool IsRotationEnabled
        {
            get => isRotationEnabled;
            set => isRotationEnabled = value;
        }
        
        /// <summary>
        /// 获取当前是否正在旋转
        /// </summary>
        public bool IsRotating => isRotating;

        private void Awake()
        {
            if (autoStartOnAwake)
            {
                StartRotation();
            }
        }

        private void Update()
        {
            if (!isRotationEnabled || !isRotating)
                return;

            // 计算当前旋转速度（考虑正弦波动）
            currentRotationSpeed = rotationSpeed;
            if (useOscillatingSpeed)
            {
                currentRotationSpeed += Mathf.Sin(Time.time * oscillationFrequency) * oscillationAmplitude;
            }

            // 执行旋转
            if (useSmoothRotation)
            {
                PerformSmoothRotation();
            }
            else
            {
                PerformDirectRotation();
            }
        }

        /// <summary>
        /// 执行直接旋转
        /// </summary>
        private void PerformDirectRotation()
        {
            float rotationAmount = currentRotationSpeed * Time.deltaTime;
            
            if (rotateInWorldSpace)
            {
                transform.Rotate(rotationAxis, rotationAmount, Space.World);
            }
            else
            {
                transform.Rotate(rotationAxis, rotationAmount, Space.Self);
            }
        }

        /// <summary>
        /// 执行平滑旋转
        /// </summary>
        private void PerformSmoothRotation()
        {
            float rotationAmount = currentRotationSpeed * Time.deltaTime;
            Quaternion rotation = Quaternion.AngleAxis(rotationAmount, rotationAxis);
            
            if (rotateInWorldSpace)
            {
                targetRotation = rotation * transform.rotation;
            }
            else
            {
                targetRotation = transform.rotation * rotation;
            }
            
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, smoothRotationSpeed * Time.deltaTime);
        }

        /// <summary>
        /// 开始旋转
        /// </summary>
        public void StartRotation()
        {
            isRotating = true;
            isRotationEnabled = true;
        }

        /// <summary>
        /// 停止旋转
        /// </summary>
        public void StopRotation()
        {
            isRotating = false;
        }

        /// <summary>
        /// 暂停旋转（保持启用状态但停止旋转）
        /// </summary>
        public void PauseRotation()
        {
            isRotating = false;
        }

        /// <summary>
        /// 恢复旋转
        /// </summary>
        public void ResumeRotation()
        {
            if (isRotationEnabled)
            {
                isRotating = true;
            }
        }

        /// <summary>
        /// 切换旋转状态
        /// </summary>
        public void ToggleRotation()
        {
            if (isRotating)
            {
                StopRotation();
            }
            else
            {
                StartRotation();
            }
        }

        /// <summary>
        /// 设置旋转轴
        /// </summary>
        /// <param name="axis">旋转轴向量</param>
        public void SetRotationAxis(Vector3 axis)
        {
            rotationAxis = axis.normalized;
        }

        /// <summary>
        /// 设置旋转轴（通过枚举）
        /// </summary>
        /// <param name="axis">预设的轴方向</param>
        public void SetRotationAxis(Axis axis)
        {
            switch (axis)
            {
                case Axis.X:
                    rotationAxis = Vector3.right;
                    break;
                case Axis.Y:
                    rotationAxis = Vector3.up;
                    break;
                case Axis.Z:
                    rotationAxis = Vector3.forward;
                    break;
                case Axis.NegativeX:
                    rotationAxis = Vector3.left;
                    break;
                case Axis.NegativeY:
                    rotationAxis = Vector3.down;
                    break;
                case Axis.NegativeZ:
                    rotationAxis = Vector3.back;
                    break;
            }
        }

        /// <summary>
        /// 重置旋转到初始状态
        /// </summary>
        public void ResetRotation()
        {
            transform.rotation = Quaternion.identity;
        }

        /// <summary>
        /// 设置旋转到指定角度
        /// </summary>
        /// <param name="eulerAngles">欧拉角度</param>
        public void SetRotation(Vector3 eulerAngles)
        {
            transform.rotation = Quaternion.Euler(eulerAngles);
        }

        /// <summary>
        /// 设置旋转到指定四元数
        /// </summary>
        /// <param name="rotation">四元数旋转</param>
        public void SetRotation(Quaternion rotation)
        {
            transform.rotation = rotation;
        }
    }

    /// <summary>
    /// 轴方向枚举
    /// </summary>
    public enum Axis
    {
        X,
        Y,
        Z,
        NegativeX,
        NegativeY,
        NegativeZ
    }
}
