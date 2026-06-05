using UnityEngine;

/// 草地交互点 v1.0 (2026.05.28)
/// 挂载到需要与草地交互的对象上（角色脚部、动物、球体等）
/// 配合 GrassInteractionController 使用
public class GrassInteractor : MonoBehaviour
{
    [Header("交互参数")]
    [Tooltip("交互半径（世界空间，米）")]
    [Range(0.1f, 2)]
    public float interactionRadius = 0.5f;

    [Header("可视化")]
    [Tooltip("在 Scene 视图中显示交互范围")]
    public bool showGizmo = true;
    public Color gizmoColor = new Color(0.3f, 1f, 0.4f, 0.6f);

    /// 自动注册到场景中的 Controller
    void OnEnable()
    {
        var controller = FindObjectOfType<GrassInteractionController>();
        if (controller != null)
            controller.AddInteractor(this);
    }

    /// 自动从 Controller 中移除
    void OnDisable()
    {
        var controller = FindObjectOfType<GrassInteractionController>();
        if (controller != null)
            controller.RemoveInteractor(this);
    }

    void OnDrawGizmos()
    {
        if (!showGizmo) return;
        DrawGizmo(0.4f);
    }

    void OnDrawGizmosSelected()
    {
        DrawGizmo(1f);
    }

    private void DrawGizmo(float alpha)
    {
        Color col = gizmoColor;
        col.a *= alpha;
        Gizmos.color = col;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);

        // 画一个实心小球表示中心
        col.a *= 0.3f;
        Gizmos.color = col;
        Gizmos.DrawSphere(transform.position, interactionRadius * 0.1f);
    }
}
