using System.Collections.Generic;
using UnityEngine;

/// 位置重置触发器：挂在带 Collider（isTrigger=true）的体积对象上
/// 当指定对象进入该体积时，将其位置重置到游戏开始时记录的初始位置
/// 支持多个目标对象
// ResetPositionVolume 1.0 重置指定对象运行时的位置
// ResetPositionVolume 1.1 添加碰触随机基础颜色功能

[RequireComponent(typeof(Collider))]
public class ResetPositionVolume : MonoBehaviour
{
    [Header("目标对象")]
    [Tooltip("拖入需要被重置位置的对象，进入体积时会被传送回初始位置")]
    public List<GameObject> targetObjects = new List<GameObject>();

    [Header("设置")]
    [Tooltip("是否同时重置旋转")]
    public bool resetRotation = false;

    [Tooltip("是否同时重置速度（需要 Rigidbody）")]
    public bool resetVelocity = true;

    [Header("碰撞变色")]
    [Tooltip("碰撞时是否随机改变目标对象材质的色相（H），保持饱和度和明度不变")]
    public bool randomizeHueOnCollision = false;

    [Tooltip("材质颜色属性名称")]
    public string colorPropertyName = "_BaseColor";

    // 记录每个目标的初始状态
    private Dictionary<GameObject, Vector3> _startPositions = new Dictionary<GameObject, Vector3>();
    private Dictionary<GameObject, Quaternion> _startRotations = new Dictionary<GameObject, Quaternion>();

    void Start()
    {
        RecordStartPositions();
    }

    private void RecordStartPositions()
    {
        _startPositions.Clear();
        _startRotations.Clear();

        foreach (var target in targetObjects)
        {
            if (target == null) continue;
            _startPositions[target] = target.transform.position;
            _startRotations[target] = target.transform.rotation;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (_startPositions.Count == 0) return;

        foreach (var target in targetObjects)
        {
            if (target == null) continue;

            // 检查进入体积的是否是目标对象（或目标对象的子对象）
            if (other.gameObject == target || other.transform.IsChildOf(target.transform))
            {
                ResetTarget(target);
                break;
            }
        }
    }

    private void ResetTarget(GameObject target)
    {
        if (!_startPositions.ContainsKey(target)) return;

        target.transform.position = _startPositions[target];

        if (resetRotation)
            target.transform.rotation = _startRotations[target];

        if (resetVelocity)
        {
            Rigidbody rb = target.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        if (randomizeHueOnCollision)
            RandomizeHue(target);
    }

    private void RandomizeHue(GameObject target)
    {
        Renderer rend = target.GetComponent<Renderer>();
        if (rend == null) return;

        Material mat = rend.material;
        if (!mat.HasProperty(colorPropertyName)) return;

        Color currentColor = mat.GetColor(colorPropertyName);
        Color newColor = new Color(Random.value, Random.value, Random.value, currentColor.a);
        mat.SetColor(colorPropertyName, newColor);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.2f);
        Collider col = GetComponent<Collider>();
        if (col is BoxCollider box)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.DrawWireCube(box.center, box.size);
        }
        else if (col is SphereCollider sphere)
        {
            Gizmos.DrawSphere(transform.position + sphere.center, sphere.radius * transform.lossyScale.x);
            Gizmos.DrawWireSphere(transform.position + sphere.center, sphere.radius * transform.lossyScale.x);
        }
    }
}
