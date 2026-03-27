// v1.0 PBR_Mobile自发光闪烁脚本（脚本挂载到模型上）
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
        
        private Material targetMaterial;
        private float baseEmissionScale;
        private float time;
        private bool hasEmissionScale;
        
        private void OnEnable()
        {
            InitializeMaterial();
        }
        
        private void InitializeMaterial()
        {
            // 获取目标渲染器
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }
            
            if (targetRenderer == null)
            {
                Debug.LogWarning($"EmissionFlicker: 未找到 Renderer 组件在 {gameObject.name}");
                return;
            }
            
            // 获取材质
            if (Application.isPlaying)
            {
                // 运行时使用实例材质
                if (targetRenderer.materials.Length > materialIndex)
                {
                    targetMaterial = targetRenderer.materials[materialIndex];
                }
            }
            else
            {
                // 编辑器模式使用共享材质
                if (targetRenderer.sharedMaterials.Length > materialIndex)
                {
                    targetMaterial = targetRenderer.sharedMaterials[materialIndex];
                }
            }
            
            if (targetMaterial == null)
            {
                Debug.LogWarning($"EmissionFlicker: 未找到材质索引 {materialIndex} 在 {gameObject.name}");
                return;
            }
            
            // 检查材质是否有 _EmissionScale 属性
            hasEmissionScale = targetMaterial.HasProperty("_EmissionScale");
            
            if (!hasEmissionScale)
            {
                Debug.LogWarning($"EmissionFlicker: 材质 {targetMaterial.name} 没有 _EmissionScale 属性");
                return;
            }
            
            // 保存基础自发光强度
            baseEmissionScale = targetMaterial.GetFloat("_EmissionScale");
            time = 0f;
        }
        
        private void Update()
        {
            if (!enableFlicker || targetMaterial == null || !hasEmissionScale)
                return;
            
            // 累积时间
            time += Time.deltaTime;
            
            // 计算闪烁值（使用正弦波）
            float flickerValue = Mathf.Sin(time * flickerSpeed) * 0.5f + 0.5f; // 0到1之间
            flickerValue = Mathf.Pow(flickerValue, curvePower); // 使用幂函数调整曲线
            
            // 计算当前强度
            float minScale = baseEmissionScale * minIntensity;
            float maxScale = baseEmissionScale * maxIntensity;
            float currentScale = Mathf.Lerp(minScale, maxScale, flickerValue);
            
            // 应用到材质
            targetMaterial.SetFloat("_EmissionScale", currentScale);
        }
        
        private void OnDisable()
        {
            // 恢复原始强度
            if (targetMaterial != null && hasEmissionScale)
            {
                targetMaterial.SetFloat("_EmissionScale", baseEmissionScale);
            }
        }
        
        private void OnValidate()
        {
            // 参数改变时重新初始化
            if (Application.isPlaying || !Application.isEditor)
            {
                InitializeMaterial();
            }
        }
        
        /// 重置基础强度为当前材质的值
        public void ResetBaseIntensity()
        {
            if (targetMaterial != null && hasEmissionScale)
            {
                baseEmissionScale = targetMaterial.GetFloat("_EmissionScale");
            }
        }
    }
}
