using System.Collections.Generic;
using UnityEngine;

/// 草地交互控制器 v1.0 (2026.05.28)
/// 管理多个交互点，将位置和半径数据传递给 Grass shader 的全局参数
/// 支持最多 16 个交互点同时作用
[ExecuteAlways]
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

    // Shader 全局属性 ID
    private static readonly int _GrassInteractionCount_ID = Shader.PropertyToID("_GrassInteractionCount");
    private static readonly int _GrassInteractionStrength_ID = Shader.PropertyToID("_GrassInteractionStrength");
    private static readonly int _GrassInteractionData_ID = Shader.PropertyToID("_GrassInteractionData");

    // 每个交互点传递 float4: (worldX, worldY, worldZ, radius)
    private Vector4[] _interactionData = new Vector4[MaxInteractors];

    void OnEnable()
    {
        UpdateShaderData();
    }

    void OnDisable()
    {
        // 清除交互数据
        Shader.SetGlobalFloat(_GrassInteractionCount_ID, 0);
        Shader.SetGlobalFloat(_GrassInteractionStrength_ID, 0);
    }

    void Update()
    {
        UpdateShaderData();
    }

    private void UpdateShaderData()
    {
        // 清理空引用
        interactors.RemoveAll(i => i == null);

        int count = Mathf.Min(interactors.Count, MaxInteractors);

        for (int i = 0; i < MaxInteractors; i++)
        {
            if (i < count && interactors[i] != null && interactors[i].gameObject.activeInHierarchy)
            {
                Vector3 pos = interactors[i].transform.position;
                float radius = interactors[i].interactionRadius * radiusMultiplier;
                _interactionData[i] = new Vector4(pos.x, pos.y, pos.z, radius);
            }
            else
            {
                _interactionData[i] = Vector4.zero;
            }
        }

        Shader.SetGlobalFloat(_GrassInteractionCount_ID, count);
        Shader.SetGlobalFloat(_GrassInteractionStrength_ID, interactionStrength);
        Shader.SetGlobalVectorArray(_GrassInteractionData_ID, _interactionData);
    }

    /// 运行时动态添加交互对象
    public void AddInteractor(GrassInteractor interactor)
    {
        if (interactor != null && !interactors.Contains(interactor))
        {
            if (interactors.Count < MaxInteractors)
                interactors.Add(interactor);
            else
                Debug.LogWarning($"[GrassInteractionController] 交互点已达上限 ({MaxInteractors})，无法添加更多。");
        }
    }

    /// 运行时动态移除交互对象
    public void RemoveInteractor(GrassInteractor interactor)
    {
        interactors.Remove(interactor);
    }

    void OnValidate()
    {
        UpdateShaderData();
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showGizmos) return;
        foreach (var interactor in interactors)
        {
            if (interactor == null || !interactor.gameObject.activeInHierarchy) continue;
            Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.3f);
            Gizmos.DrawWireSphere(interactor.transform.position, interactor.interactionRadius * radiusMultiplier);
        }
    }
#endif
}
