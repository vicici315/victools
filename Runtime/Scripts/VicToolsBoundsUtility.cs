// VicToolsBoundsUtility - 边界/包围盒相关工具方法
// 公共工具集合，供 VicTools 内部各工具统一使用：
//   - OBB（Oriented Bounding Box）长度计算：
//     * GetAccurateLengthAlongAxis / ComputeOBBLength / DetectPrimaryAxis
//     * 调用方：RoadScrollEditorHelper.CreateRoadScroll、RoadScrollEditor.ApplyAxis
//   - 轴向自动排列：
//     * AlignSegmentsAlongAxis：段按 axis 方向等距排布，以 worldCenter 为几何中心
//       （相邻段中心间距 = segmentLength，队列对称分布，worldCenter 落在队列中心）
//     * 调用方：RoadScrollEditorHelper.CreateRoadScroll（创建时按 X 轴 / 主轴排列）、
//               RoadScrollEditor.ApplyAxis（X/Y/Z 按钮按下时按选定轴排列）
// 所有调用方共享同一套算法，保证结果一致。

using UnityEngine;

namespace Vic.Runtime
{
    /// 边界/包围盒相关工具
    public static class VicToolsBoundsUtility
    {
        /// 计算 Transform 下 mesh 沿 <paramref name="worldAxis"/> 方向的"准确长度"。
        /// 优先用 MeshFilter.sharedMesh.bounds（本地 AABB，旋转不影响）+ OBB 计算；
        /// 回退到 Renderer.bounds（世界 AABB，旋转敏感）。
        /// 失败返回 0f。
        /// <param name="t">目标 Transform（mesh 所在 GameObject 或其父级）</param>
        /// <param name="worldAxis">世界空间方向（任意向量，会自动归一化）</param>
        /// <returns>沿 worldAxis 方向的有效长度</returns>
        public static float GetAccurateLengthAlongAxis(Transform t, Vector3 worldAxis)
        {
            if (t == null) return 0f;
            Vector3 worldAxisN = worldAxis.sqrMagnitude > 1e-6f ? worldAxis.normalized : Vector3.right;

            // 优先 MeshFilter（本地空间 AABB + OBB 投影，旋转不影响）
            MeshFilter mf = t.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                return ComputeOBBLength(mf.transform, mf.sharedMesh.bounds.extents, worldAxisN);
            }

            // 回退 Renderer.bounds（世界空间 AABB，旋转敏感）
            Renderer rend = t.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                Vector3 ext = rend.bounds.extents;
                return 2f * (Mathf.Abs(ext.x * worldAxisN.x) +
                             Mathf.Abs(ext.y * worldAxisN.y) +
                             Mathf.Abs(ext.z * worldAxisN.z));
            }

            return 0f;
        }

        /// 计算 OBB（Oriented Bounding Box）在 <paramref name="worldAxisN"/> 方向上的有效长度。
        /// OBB 半轴向量 = <c>transform.TransformVector(本地半轴向量)</c>
        /// 有效长度 = <c>2 × Σ|半轴·worldAxis|</c>
        /// 对凸 mesh 100% 准确（与逐顶点遍历结果一致），对凹 mesh 略过估但仍比 AABB 准确得多。
        /// <param name="t">目标 Transform（用于 TransformVector 旋转+缩放）</param>
        /// <param name="localExtents">mesh 本地 AABB 的半尺寸（来自 mesh.bounds.extents）</param>
        /// <param name="worldAxisN">世界空间方向（应已归一化）</param>
        public static float ComputeOBBLength(Transform t, Vector3 localExtents, Vector3 worldAxisN)
        {
            if (t == null) return 0f;

            // OBB 半轴 = transform.TransformVector(本地半轴向量)
            //   TransformVector 应用旋转+缩放，不应用平移
            Vector3 exW = t.TransformVector(new Vector3(localExtents.x, 0f, 0f));
            Vector3 eyW = t.TransformVector(new Vector3(0f, localExtents.y, 0f));
            Vector3 ezW = t.TransformVector(new Vector3(0f, 0f, localExtents.z));

            float sum = Mathf.Abs(Vector3.Dot(exW, worldAxisN)) +
                        Mathf.Abs(Vector3.Dot(eyW, worldAxisN)) +
                        Mathf.Abs(Vector3.Dot(ezW, worldAxisN));
            return 2f * sum;
        }

        /// 检测 Transform 下 mesh 的"主轴"（最长轴）方向 + 长度，并返回**精确的世界标准轴**。
        /// <para>
        /// 算法（与 <see cref="GetAccurateLengthAlongAxis"/> 共享 OBB 实现）：
        /// <list type="number">
        ///   <item>优先用 <c>MeshFilter.sharedMesh.bounds.extents</c>（本地 AABB，旋转不影响）+ OBB 投影计算三轴长度</item>
        ///   <item>回退到 <c>Renderer.bounds.extents</c>（世界 AABB，旋转敏感）</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>axis 返回精确标准轴</b>（<see cref="Vector3.right"/>/<see cref="Vector3.up"/>/<see cref="Vector3.forward"/>），
        /// 避免 <c>transform.right</c> 等运行时 Quat×Vec3 计算产生的 1.19e-7（<see cref="float.Epsilon"/>）浮点误差。
        /// 这样 <c>scrollDirection = -axis</c> 也是精确的（如 (-1, 0, 0) 精确）。
        /// </para>
        /// <para>失败返回 <c>false</c>（目标没有 MeshFilter/Renderer 或 mesh 无顶点）。</para>
        /// <param name="t">目标 Transform（mesh 所在 GameObject 或其父级）</param>
        /// <param name="axis">输出：最长轴对应的精确世界标准轴（right/up/forward）</param>
        /// <param name="length">输出：最长轴方向上的 OBB 准确长度</param>
        /// <returns>是否成功检测</returns>
        public static bool DetectPrimaryAxis(Transform t, out Vector3 axis, out float length)
        {
            axis = Vector3.forward;  // 默认 fallback
            length = 0f;
            if (t == null) return false;

            // 优先 MeshFilter（本地 AABB + OBB，旋转不影响）
            MeshFilter mf = t.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Vector3 ext = mf.sharedMesh.bounds.extents;
                // 沿 mesh 自身轴向的 OBB 长度（用 mf.transform.* 让 length 反映 mesh 旋转）
                float lengthX = ComputeOBBLength(mf.transform, ext, mf.transform.right);
                float lengthY = ComputeOBBLength(mf.transform, ext, mf.transform.up);
                float lengthZ = ComputeOBBLength(mf.transform, ext, mf.transform.forward);

                // axis 强制用精确标准轴（编译时常量，零浮点误差）
                if (lengthX >= lengthY && lengthX >= lengthZ) { axis = Vector3.right;   length = lengthX; }
                else if (lengthY >= lengthZ)                  { axis = Vector3.up;      length = lengthY; }
                else                                          { axis = Vector3.forward; length = lengthZ; }
                return true;
            }

            // 回退 Renderer.bounds（世界 AABB，旋转敏感）
            Renderer rend = t.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                Vector3 ext = rend.bounds.extents;
                float lengthX = 2f * ext.x;
                float lengthY = 2f * ext.y;
                float lengthZ = 2f * ext.z;
                if (lengthX >= lengthY && lengthX >= lengthZ) { axis = Vector3.right;   length = lengthX; }
                else if (lengthY >= lengthZ)                  { axis = Vector3.up;      length = lengthY; }
                else                                          { axis = Vector3.forward; length = lengthZ; }
                return true;
            }

            return false;
        }

        /// 将一组 segment **按 <paramref name="axis"/> 方向等距排布**，以 <paramref name="worldCenter"/> 为几何中心。
        /// <para>
        /// 排布公式（i = 0..N-1）：
        /// <c>segments[i].position = worldCenter + (-axis) * (i - (N-1)/2) * segmentLength</c>
        /// </para>
        /// <para>
        /// 关键特性：
        /// <list type="bullet">
        ///   <item><b>段按 axis 方向等距排布</b>（相邻段中心间距 = <paramref name="segmentLength"/>）</item>
        ///   <item><b>worldCenter 天然落在队列几何中心</b></item>
        ///   <item>队列 <b>对称分布</b>：首段在 +axis 方向，尾段在 -axis 方向，队列中心 = worldCenter</item>
        /// </list>
        /// </para>
        /// <para>行为示例（axis 任意方向，<c>-axis</c> 为"流向"）：</para>
        /// <list type="bullet">
        ///   <item>N=1：段 0 落在 worldCenter</item>
        ///   <item>N=2：段 0/1 分别落在 ±L/2</item>
        ///   <item>N=3：段 0/1/2 分别落在 +L / 0 / -L</item>
        /// </list>
        /// <para>
        /// 配合 <see cref="RoadScroll"/> 的 UV 滚动（以父对象位置为参考中心），
        /// 段在父对象周围等距分布，UV 滚动时不会"向某轴偏离"。
        /// </para>
        /// <param name="segments">要排布的段（任意 <c>IList&lt;Transform&gt;</c>，如 <c>RoadScroll.roadSegments</c>）</param>
        /// <param name="axis">段中心排列方向（世界空间，<b>不需要</b>归一化，会自动归一化；
        /// 约定 <c>-axis</c> 为 <c>RoadScroll.scrollDirection</c> 方向）</param>
        /// <param name="segmentLength">相邻段中心间距（通常由 <see cref="GetAccurateLengthAlongAxis"/> 计算得到）</param>
        /// <param name="worldCenter">队列几何中心位置（一般填 <c>RoadScroll</c> 父对象的世界坐标）</param>
        public static void AlignSegmentsAlongAxis(
            System.Collections.Generic.IList<Transform> segments,
            Vector3 axis, float segmentLength, Vector3 worldCenter)
        {
            if (segments == null || segments.Count == 0) return;
            if (segmentLength <= 0f) return;
            if (axis.sqrMagnitude < 1e-6f) return;

            Vector3 axisN = axis.normalized;
            int n = segments.Count;
            float halfOffset = (n - 1) * 0.5f;                // 首/尾段中心到队列中点的"段数"
            Vector3 stepVec = -axisN * segmentLength;         // 段 i → 段 i+1 的位移（沿 -axis，与"流向"一致）

            for (int i = 0; i < n; i++)
            {
                if (segments[i] == null) continue;
                segments[i].position = worldCenter + stepVec * (i - halfOffset);
            }
        }
    }
}
