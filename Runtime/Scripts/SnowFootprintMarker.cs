using UnityEngine;

/// 雪地脚印标记 v2.2 (2026.05.28)
/// - 支持圆形/矩形画笔形状，矩形适合雪橇等长条划痕
/// - 根据穿透深度动态计算绘制强度
/// v2.0 (2026.05.27)
/// - 移除deformDepth参数，深度由Manager的sinkDepth全局控制
/// - brushStrength固定1.0，边缘柔和由brushSoftness控制

public enum BrushShape
{
    Circle,
    Rectangle
}

public class SnowFootprintMarker : MonoBehaviour
{
    [Header("画笔参数")]
    [Tooltip("画笔形状")]
    public BrushShape brushShape = BrushShape.Circle;
    
    [Tooltip("画笔大小（世界空间，米）- 圆形为半径，矩形为宽度")]
    [Range(0.05f, 5f)]
    public float brushSize = 0.3f;
    
    [Tooltip("矩形长度（世界空间，米）- 沿物体前向方向")]
    [Range(0.05f, 10f)]
    public float brushLength = 1.0f;

    [Tooltip("边缘羽化度（0=硬边，1=完全羽化）")]
    [Range(0f, 1f)]
    public float feather = 1.0f;

    [Header("射线检测")]
    [Tooltip("射线检测方向")]
    public Vector3 rayDirection = Vector3.down;

    [Tooltip("射线长度")]
    public float rayDistance = 1f;

    [Header("可视化")]
    [Tooltip("显示影响范围")]
    public bool showGizmo = true;
    public Color gizmoColor = new Color(1.0f, 0.38f, 1f, 0.95f);

    [Header("引用")]
    [Tooltip("为空时自动查找")]
    public SnowDeformManager manager;

    private Vector2 _lastPaintUV;
    private bool _initialized;
    private Vector3 _lastPosition;
    private static readonly float MinMoveThreshold = 0.001f; // 最小移动阈值（米）

    void Start() => Initialize();

    void Update()
    {
        if (!_initialized) Initialize();
        if (manager == null) return;

        // 位置未变化时跳过射线检测和绘制（性能优化）
        Vector3 pos = transform.position;
        if ((pos - _lastPosition).sqrMagnitude < MinMoveThreshold * MinMoveThreshold)
            return;
        _lastPosition = pos;

        Ray ray = new Ray(pos, rayDirection);
        if (Physics.Raycast(ray, out RaycastHit hit, rayDistance, manager.snowLayer))
        {
            float distToSurface = hit.distance;
            float penetration = rayDistance - distToSurface;
            float brushStrength = Mathf.Clamp01(penetration / rayDistance);

            Vector2 uv = manager.WorldToUV(hit.point);
            float uvBrushSize = manager.WorldBrushToUV(brushSize);

            if (brushShape == BrushShape.Rectangle)
            {
                // 矩形：计算物体前向在XZ平面的UV方向角度
                Vector3 fwd = transform.forward;
                float angle = Mathf.Atan2(fwd.z, fwd.x);
                float uvLength = manager.WorldBrushToUV(brushLength);
                manager.PaintRect(uv, uvBrushSize, uvLength, angle, brushStrength, feather);
            }
            else
            {
                // 圆形：线段绘制
                if (_lastPaintUV != Vector2.zero)
                {
                    float uvDist = Vector2.Distance(uv, _lastPaintUV);
                    if (uvDist > 0.0001f)
                    {
                        manager.PaintLine(_lastPaintUV, uv, uvBrushSize, brushStrength, feather);
                    }
                }
                else
                {
                    manager.PaintAtUV(uv, uvBrushSize, brushStrength, feather);
                }
            }

            _lastPaintUV = uv;
        }
        else
        {
            _lastPaintUV = Vector2.zero;
        }
    }

    private void Initialize()
    {
        if (manager == null) manager = FindObjectOfType<SnowDeformManager>();
        _initialized = true;
    }

    // ─── Gizmo ───
    void OnDrawGizmos() { if (showGizmo) DrawGizmo(0.4f); }
    void OnDrawGizmosSelected() { DrawGizmo(1f); }

    private void DrawGizmo(float alpha)
    {
        Vector3 rayDir = rayDirection;
        float rayDist = rayDistance;
        LayerMask layer = manager != null ? manager.snowLayer : (LayerMask)(~0);

        Color col = gizmoColor;
        col.a *= alpha;
        Gizmos.color = col;

        Vector3 center = transform.position;
        Vector3 normal = Vector3.up;

        if (Physics.Raycast(center, rayDir, out RaycastHit hit, rayDist, layer))
        {
            center = hit.point + hit.normal * 0.02f;
            normal = hit.normal;
        }

        if (brushShape == BrushShape.Rectangle)
        {
            DrawRect(center, normal, transform.forward, brushSize, brushLength);
        }
        else
        {
            DrawCircle(center, normal, brushSize, 32);
        }

        // 射线
        Gizmos.color = new Color(col.r, col.g, col.b, col.a * 0.3f);
        Gizmos.DrawLine(transform.position, transform.position + rayDir * rayDist);
    }

    private static void DrawCircle(Vector3 center, Vector3 normal, float radius, int seg)
    {
        Vector3 r = Vector3.Cross(normal, Vector3.forward).normalized;
        if (r.sqrMagnitude < 0.01f) r = Vector3.Cross(normal, Vector3.right).normalized;
        Vector3 f = Vector3.Cross(r, normal).normalized;
        Vector3 prev = center + r * radius;
        for (int i = 1; i <= seg; i++)
        {
            float a = (float)i / seg * Mathf.PI * 2f;
            Vector3 p = center + (r * Mathf.Cos(a) + f * Mathf.Sin(a)) * radius;
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }

    private static void DrawRect(Vector3 center, Vector3 normal, Vector3 forward, float width, float length)
    {
        // 投影forward到平面上
        Vector3 fwd = Vector3.ProjectOnPlane(forward, normal).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(Vector3.forward, normal).normalized;
        Vector3 right = Vector3.Cross(normal, fwd).normalized;

        float hw = width * 0.5f;
        float hl = length * 0.5f;

        Vector3 p0 = center + fwd * hl + right * hw;
        Vector3 p1 = center + fwd * hl - right * hw;
        Vector3 p2 = center - fwd * hl - right * hw;
        Vector3 p3 = center - fwd * hl + right * hw;

        Gizmos.DrawLine(p0, p1);
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p0);
    }
}
