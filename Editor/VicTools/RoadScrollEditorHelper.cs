// RoadScrollEditorHelper —— RoadScroll 组件的共享编辑器辅助方法。
// 集中所有"RoadScroll 编辑器侧的非 Inspector 操作"，避免这些逻辑分散在 VicToolsWindow 等大文件中。
//
// 公共 API：
//   - CreateRoadScroll：创建工具（从场景选中对象一键生成 RoadScroll 控制器）
//   - ApplyAxisToRoadScroll：把整个 segment 列表按指定轴等距排布，并设置 scrollDirection/segmentLength
//   - DetectPrimaryAxisWithFallback：检测主对象的主轴 + 长度（无主轴时 fallback 到主对象的 transform 轴）
//   - AxisName：把精确世界标准轴（Vector3.right/up/forward）映射为单字符轴名（"X"/"Y"/"Z"）
//
// 所有调用方：
//   - VicToolsWindow 菜单 "创建 无限循环滚动道路（RoadScroll）" → CreateRoadScroll
//   - RoadScrollEditor.ApplyAxis（X/Y/Z 按钮按下时按选定轴排列）          → ApplyAxisToRoadScroll
//   - RoadScrollEditor.DuplicateAndArrange（复制后等距排布）              → 间接通过 AlignSegmentsAlongAxis
//
// 设计原则：所有 RoadScroll 编辑器辅助集中在此，Inspector 渲染逻辑留在 RoadScrollEditor，
//          运行时组件在 Runtime/Scripts/RoadScroll.cs。

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Vic.Runtime;

namespace VicTools
{
    /// RoadScroll 组件的共享编辑器辅助方法（创建 + 排布 + 字段工具 + 轴名映射）。
    public static class RoadScrollEditorHelper
    {
        // ====================================================================
        // 创建工具：CreateRoadScroll
        // ====================================================================

        /// 在当前场景创建一个无限循环道路滚动控制器（Packages/com.youdoo.victools/Runtime/Scripts/RoadScroll.cs）。
        /// <para>
        /// 完整流程：
        /// <list type="number">
        ///   <item>选中多个道路模型对象 → 自动创建空父对象（位置 = 第一个选中对象的位置）</item>
        ///   <item>将 RoadScroll 组件挂载到父对象</item>
        ///   <item>自动将选中对象收纳为父对象子物体（保持世界位置），并加入 roadSegments 列表</item>
        ///   <item><b>默认按 X 轴排列</b>：segmentLength = 主对象 X 轴 OBB 长度，scrollDirection = -X</item>
        ///   <item>若 X 轴无长度（mesh 不在 X 方向延伸），回退到主轴（最长轴）检测</item>
        ///   <item>按所选轴等距排布所有段（以父对象位置为几何中心）</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>共享入口</b>：本方法的"按轴等距排布 + 设置字段 + 标记 dirty"步骤委托给
        /// <see cref="ApplyAxisToRoadScroll"/>，与 Inspector 的 X/Y/Z 按钮完全一致。
        /// </para>
        /// <para><b>Undo 完整支持</b>：创建父对象、修改子物体父级、添加组件、修改字段、段排布均可一键撤销。</para>
        /// <para>
        /// <b>位置说明</b>：本方法从 VicToolsWindow（原 3000+ 行的菜单宿主）抽出，集中到 RoadScrollEditorHelper。
        /// 菜单注册单点委托：VicToolsWindow 第 1687 行的 "创建 无限循环滚动道路（RoadScroll）" → RoadScrollEditorHelper.CreateRoadScroll。
        /// </para>
        public static void CreateRoadScroll()
        {
            var selectedObjects = Selection.gameObjects;
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先在场景中选中滚动模型对象（支持多选）", "确定");
                return;
            }

            // 主对象 = 选中的第一个对象，用于父对象命名 + 主轴检测 + 父对象位置锚点
            GameObject primary = selectedObjects[0];

            // 1. 默认按 X 轴排列（与 X 轴按钮行为一致）
            //    - 优先用 X 轴 OBB 长度作为 segmentLength
            //    - 若 X 轴无长度（mesh 不在 X 方向延伸），回退到主轴（最长轴）检测
            //    - 两者都失败 → 主对象无 MeshFilter/Renderer，弹窗报错
            Vector3 kDefaultAxis = Vector3.right;  // X 轴为创建工具默认方向
            float segmentLength = VicToolsBoundsUtility.GetAccurateLengthAlongAxis(
                primary.transform, kDefaultAxis);
            Vector3 primaryAxis;
            string axisSource;
            if (segmentLength > 0f)
            {
                primaryAxis = kDefaultAxis;
                axisSource = "X 轴（默认）";
            }
            else if (VicToolsBoundsUtility.DetectPrimaryAxis(primary.transform, out primaryAxis, out segmentLength))
            {
                axisSource = $"{AxisName(primaryAxis)} 轴（X 轴无长度，回退到主轴）";
            }
            else
            {
                EditorUtility.DisplayDialog("错误",
                    $"主对象 [{primary.name}] 及其子物体都没有 MeshFilter/Renderer 组件，且 X 轴无有效长度，无法自动检测主轴",
                    "确定");
                return;
            }

            // 2. 父对象位置 = primary 原世界位置（作为队列几何中心 / UV 滚动参考中心）
            Vector3 basePos = primary.transform.position;

            // 3. 创建父对象（位置 = primary 位置，rotation = identity）
            GameObject parent = new GameObject("RoadScroll_" + primary.name);
            Undo.RegisterCreatedObjectUndo(parent, "Create RoadScroll Parent");
            parent.transform.position = basePos;

            // 4. 收纳子物体 —— Undo.SetTransformParent 默认 worldPositionStays=true，
            //    即收纳到父对象下时世界位置不变（之后会被步骤 6 的轴向自动排列整体重排）
            foreach (var obj in selectedObjects)
            {
                if (obj == null) continue;
                Undo.SetTransformParent(obj.transform, parent.transform, "Parent Road Segment");
            }

            // 5. 挂载 RoadScroll 组件
            RoadScroll rs;
            try
            {
                rs = Undo.AddComponent<RoadScroll>(parent);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VicTools] CreateRoadScroll 失败：{ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("错误", $"创建 RoadScroll 失败：\n{ex.Message}", "确定");
                return;
            }

            // 6. 填充 roadSegments 列表 + 调用共享入口完成"按主轴等距排布 + 设置字段"
            //    共享入口 ApplyAxisToRoadScroll 与 X/Y/Z 按钮完全一致
            rs.roadSegments.Clear();
            foreach (var obj in selectedObjects)
            {
                if (obj != null) rs.roadSegments.Add(obj.transform);
            }
            ApplyAxisToRoadScroll(rs, primaryAxis, segmentLength, "Create RoadScroll");

            // 7. 选中父对象 + 标记 dirty + 标记场景 dirty
            Selection.activeGameObject = parent;
            EditorUtility.SetDirty(parent);
            EditorSceneManager.MarkSceneDirty(parent.scene);

            string axisName = AxisName(primaryAxis);
            int segCount = rs.roadSegments.Count;
            Debug.Log($"[VicTools] 已创建 RoadScroll 父对象 [{parent.name}]，含 {segCount} 段模型，\n" +
                      $"  轴向: {axisName}（{axisSource}），长度: {segmentLength:F2}（OBB 准确长度）\n" +
                      $"  滚动方向: {rs.scrollDirection}（沿 -{axisName}）\n" +
                      $"  父对象位置 [{parent.transform.position}] 作为队列几何中心\n" +
                      $"  {segCount} 段已按 -{axisName} 方向等距排布（共享 RoadScrollEditorHelper.ApplyAxisToRoadScroll）\n" +
                      $"  提示：Inspector 的 X/Y/Z 按钮或「目标数量 + 生成」可重新调整轴向/段数");
        }

        // ====================================================================
        // 排布工具：ApplyAxisToRoadScroll
        // ====================================================================

        /// 把 <paramref name="rs"/> 的 segment 列表按 <paramref name="axis"/> 等距排布在父对象位置周围，
        /// 并设置 <c>scrollDirection = -axis</c> + <c>segmentLength = segmentLength</c>。
        /// <para>
        /// 完整支持 Undo（RecordObjects 包括组件 + 所有段），并标记 dirty。
        /// </para>
        /// <para>
        /// 调用方：
        /// <list type="bullet">
        ///   <item><see cref="CreateRoadScroll"/>（创建工具时按 X 轴/主轴自动排列）</item>
        ///   <item><see cref="RoadScrollEditor.ApplyAxis"/>（X/Y/Z 按钮按下时按选定轴排列）</item>
        /// </list>
        /// </para>
        /// <param name="rs">目标 RoadScroll 组件</param>
        /// <param name="axis">段中心排列方向（世界空间；约定 -axis 为 scrollDirection 方向）</param>
        /// <param name="segmentLength">相邻段中心间距（OBB 准确长度）</param>
        /// <param name="undoLabel">Undo 操作标签（显示在 Edit 菜单中）</param>
        public static void ApplyAxisToRoadScroll(
            RoadScroll rs,
            Vector3 axis,
            float segmentLength,
            string undoLabel)
        {
            if (rs == null) return;
            if (rs.roadSegments == null || rs.roadSegments.Count == 0) return;

            // 1. 收集 Undo 目标：组件 + 所有非空段 transform
            var undoTargets = new List<UnityEngine.Object>(rs.roadSegments.Count + 1) { rs };
            foreach (var t in rs.roadSegments)
                if (t != null) undoTargets.Add(t);
            Undo.RecordObjects(undoTargets.ToArray(), undoLabel);

            // 2. 按 axis 方向等距排布所有段，以父对象位置为几何中心
            VicToolsBoundsUtility.AlignSegmentsAlongAxis(
                rs.roadSegments, axis, segmentLength, rs.transform.position);

            // 3. 设置字段
            rs.scrollDirection = -axis;
            rs.segmentLength = segmentLength;

            // 4. 标记 dirty
            EditorUtility.SetDirty(rs);
            foreach (var t in rs.roadSegments)
                if (t != null) EditorUtility.SetDirty(t);
        }

        // ====================================================================
        // 主轴检测：DetectPrimaryAxisWithFallback
        // ====================================================================

        /// 检测主对象（roadSegments[0]）的主轴（最长轴）方向 + 长度，
        /// 并返回精确的世界标准轴（<see cref="Vector3.right"/>/<see cref="Vector3.up"/>/<see cref="Vector3.forward"/>）。
        /// <param name="anchor">主对象（roadSegments[0]）</param>
        /// <param name="axis">输出：主轴对应的精确世界标准轴</param>
        /// <param name="segmentLength">输出：主轴方向上的 OBB 准确长度</param>
        /// <param name="fallbackSegmentLength">当主对象没有 MeshFilter/Renderer 时的回退段长度</param>
        /// <returns>是否成功检测到主轴</returns>
        public static bool DetectPrimaryAxisWithFallback(
            Transform anchor,
            out Vector3 axis,
            out float segmentLength,
            float fallbackSegmentLength)
        {
            if (VicToolsBoundsUtility.DetectPrimaryAxis(anchor, out axis, out segmentLength))
                return true;

            // fallback：没有 MeshFilter/Renderer 时用 transform 轴 + 回退段长度
            axis = anchor != null ? anchor.right : Vector3.right;
            segmentLength = fallbackSegmentLength;
            return false;
        }

        // ====================================================================
        // 轴名映射：AxisName
        // ====================================================================

        /// 把精确世界标准轴（<see cref="Vector3.right"/>/<see cref="Vector3.up"/>/<see cref="Vector3.forward"/>）
        /// 映射为单字符轴名（"X"/"Y"/"Z"）。供 <see cref="CreateRoadScroll"/> 创建工具、
        /// <see cref="RoadScrollEditor"/> X/Y/Z 按钮、复制工具等日志/提示统一使用。
        /// <para>
        /// 非标准轴（如经过旋转的斜轴）fallback 为 <c>"F2"</c> 格式的字符串，方便诊断。
        /// </para>
        public static string AxisName(Vector3 axis)
        {
            if (axis == Vector3.right) return "X";
            if (axis == Vector3.up) return "Y";
            if (axis == Vector3.forward) return "Z";
            return axis.ToString("F2");
        }
    }
}
