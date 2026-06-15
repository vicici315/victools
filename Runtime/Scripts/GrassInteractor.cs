// GrassInteractor v1.0 (2026.05.28) - 草地交互点，挂载到需要与草地交互的对象上
// GrassInteractor v1.1 - 重构：使用静态Controller引用替代FindObjectOfType，消除每次Enable/Disable的O(n)搜索
using UnityEngine;

namespace Vic.Runtime
{
    /// 草地交互点 - 挂载到需要与草地交互的对象上（角色脚部、动物、球体等）
    /// 配合 GrassInteractionController 使用
    public class GrassInteractor : MonoBehaviour
    {
        [Header("交互参数")]
        [Tooltip("交互半径（世界空间，米）")]
        [Range(0.1f, 2f)]
        public float interactionRadius = 0.5f;

        [Header("可视化")]
        [Tooltip("在 Scene 视图中显示交互范围")]
        public bool showGizmo = true;
        public Color gizmoColor = new Color(0.3f, 1f, 0.4f, 0.6f);

        void OnEnable()
        {
            var controller = GrassInteractionController.Instance;
            if (controller != null)
                controller.AddInteractor(this);
        }

        void OnDisable()
        {
            var controller = GrassInteractionController.Instance;
            if (controller != null)
                controller.RemoveInteractor(this);
        }

#if UNITY_EDITOR
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

            col.a *= 0.3f;
            Gizmos.color = col;
            Gizmos.DrawSphere(transform.position, interactionRadius * 0.1f);
        }
#endif
    }
}
