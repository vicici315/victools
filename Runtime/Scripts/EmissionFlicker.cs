// EmissionFlicker v1.0 - PBR_Mobile自发光闪烁脚本（脚本挂载到模型上）
// EmissionFlicker v1.1 - 缓存ShaderPropertyID，优化材质获取逻辑，消除硬编码字符串
using UnityEngine;

namespace Vic.Runtime
{
    /// 自发光闪烁组件 - 运行时自动控制材质的自发光强度闪烁
    [ExecuteAlways]
    public class EmissionFlicker : MonoBehaviour
    {
        [Header("闪烁设置")]
        [Tooltip("是否启用闪烁")]
        public bool enableFlicker = true;

        [Tooltip("闪烁速度")]
        [Range(0.1f, 20f)]
        public float flickerSpeed = 5.0f;

        [Tooltip("最小亮度倍数")]
        [Range(0f, 1f)]
        public float minIntensity = 0.2f;

        [Tooltip("最大亮度倍数")]
        [Range(1f, 3f)]
        public float maxIntensity = 1.5f;

        [Tooltip("闪烁曲线强度（越大闪烁越明显）")]
        [Range(1f, 5f)]
        public float curvePower = 2.0f;

        [Header("目标设置")]
        [Tooltip("目标渲染器（留空则自动获取）")]
        public Renderer targetRenderer;

        [Tooltip("材质索引（如果有多个材质）")]
        public int materialIndex = 0;

        private static readonly int EmissionScaleID = Shader.PropertyToID("_EmissionScale");

        private Material _material;
        private float _baseEmissionScale;
        private float _time;
        private bool _initialized;

        private void OnEnable()
        {
            Initialize();
        }

        private void Update()
        {
            if (!enableFlicker || !_initialized)
                return;

            _time += Time.deltaTime;

            float t = Mathf.Sin(_time * flickerSpeed) * 0.5f + 0.5f;
            t = Mathf.Pow(t, curvePower);

            float currentScale = Mathf.Lerp(
                _baseEmissionScale * minIntensity,
                _baseEmissionScale * maxIntensity,
                t);

            _material.SetFloat(EmissionScaleID, currentScale);
        }

        private void OnDisable()
        {
            if (_initialized)
                _material.SetFloat(EmissionScaleID, _baseEmissionScale);
        }

        private void OnValidate()
        {
            Initialize();
        }

        public void ResetBaseIntensity()
        {
            if (_initialized)
                _baseEmissionScale = _material.GetFloat(EmissionScaleID);
        }

        private void Initialize()
        {
            _initialized = false;

            if (targetRenderer == null)
                targetRenderer = GetComponent<Renderer>();

            if (targetRenderer == null)
            {
                Debug.LogWarning($"EmissionFlicker: 未找到 Renderer 组件在 {gameObject.name}");
                return;
            }

            _material = GetTargetMaterial();
            if (_material == null)
            {
                Debug.LogWarning($"EmissionFlicker: 未找到材质索引 {materialIndex} 在 {gameObject.name}");
                return;
            }

            if (!_material.HasProperty(EmissionScaleID))
            {
                Debug.LogWarning($"EmissionFlicker: 材质 {_material.name} 没有 _EmissionScale 属性");
                return;
            }

            _baseEmissionScale = _material.GetFloat(EmissionScaleID);
            _time = 0f;
            _initialized = true;
        }

        private Material GetTargetMaterial()
        {
            var materials = Application.isPlaying
                ? targetRenderer.materials
                : targetRenderer.sharedMaterials;

            return materialIndex < materials.Length ? materials[materialIndex] : null;
        }
    }
}
