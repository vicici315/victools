// GrassInteractionController v1.0 (2026.05.28) - 草地交互控制器，管理多个交互点传递给Shader
// GrassInteractionController v1.1 - 重构：静态单例模式避免FindObjectOfType；消除每帧GC分配；修正注释与代码不一致
using System.Collections.Generic;
using UnityEngine;

namespace Vic.Runtime
{
    /// 草地交互控制器
    /// 管理多个交互点，将位置和半径数据传递给 Grass shader 的全局参数
    /// 支持最多 4 个交互点同时作用
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class GrassInteractionController : MonoBehaviour
    {
        public const int MaxInteractors = 4;

        [Header("交互设置")]
        [Tooltip("交互强度（草叶被压倒的程度）")]
        [Range(0f, 5f)]
        public float interactionStrength = 1.5f;

        [Tooltip("全局交互半径倍率")]
        [Range(0.1f, 5f)]
        public float radiusMultiplier = 1f;

        [Header("交互对象列表")]
        [Tooltip("拖入需要与草地交互的对象（挂载 GrassInteractor 组件）")]
        public List<GrassInteractor> interactors = new List<GrassInteractor>();

        [Header("调试")]
        [Tooltip("在 Scene 视图中显示交互范围")]
        public bool showGizmos = true;

        public static GrassInteractionController Instance { get; private set; }

        private static readonly int ShaderID_Count = Shader.PropertyToID("_GrassInteractionCount");
        private static readonly int ShaderID_Strength = Shader.PropertyToID("_GrassInteractionStrength");
        private static readonly int ShaderID_Data = Shader.PropertyToID("_GrassInteractionData");

        private readonly Vector4[] _interactionData = new Vector4[MaxInteractors];

        void OnEnable()
        {
            Instance = this;
            UpdateShaderData();
        }

        void OnDisable()
        {
            ClearShaderData();
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            UpdateShaderData();
        }

        void OnValidate()
        {
            if (enabled)
                UpdateShaderData();
        }

        private void UpdateShaderData()
        {
            int validCount = 0;

            for (int i = 0; i < MaxInteractors; i++)
            {
                if (i < interactors.Count && interactors[i] != null && interactors[i].gameObject.activeInHierarchy)
                {
                    Vector3 pos = interactors[i].transform.position;
                    float radius = interactors[i].interactionRadius * radiusMultiplier;
                    _interactionData[i] = new Vector4(pos.x, pos.y, pos.z, radius);
                    validCount++;
                }
                else
                {
                    _interactionData[i] = Vector4.zero;
                }
            }

            Shader.SetGlobalFloat(ShaderID_Count, validCount);
            Shader.SetGlobalFloat(ShaderID_Strength, interactionStrength);
            Shader.SetGlobalVectorArray(ShaderID_Data, _interactionData);
        }

        private static void ClearShaderData()
        {
            Shader.SetGlobalFloat(ShaderID_Count, 0);
            Shader.SetGlobalFloat(ShaderID_Strength, 0);
        }

        /// 运行时动态添加交互对象
        public void AddInteractor(GrassInteractor interactor)
        {
            if (interactor == null || interactors.Contains(interactor))
                return;

            if (interactors.Count < MaxInteractors)
            {
                interactors.Add(interactor);
            }
            else
            {
                Debug.LogWarning($"[GrassInteractionController] 交互点已达上限 ({MaxInteractors})，无法添加更多。");
            }
        }

        /// 运行时动态移除交互对象
        public void RemoveInteractor(GrassInteractor interactor)
        {
            interactors.Remove(interactor);
        }

        /// 清理列表中的空引用（编辑器手动调用或需要时调用）
        public void CleanNullEntries()
        {
            interactors.RemoveAll(i => i == null);
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!showGizmos) return;

            Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.3f);
            foreach (var interactor in interactors)
            {
                if (interactor == null || !interactor.gameObject.activeInHierarchy)
                    continue;
                Gizmos.DrawWireSphere(interactor.transform.position, interactor.interactionRadius * radiusMultiplier);
            }
        }
#endif
    }
}
