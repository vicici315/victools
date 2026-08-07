// RoadScrollEditor —— RoadScroll 的自定义 Inspector。
// 在 "滚动设置" 组的 segmentLength 字段下方加一行三个按钮（X / Y / Z）：
//   - 自动设置 scrollDirection = -对应轴（段"流向远方"）
//   - 自动设置 segmentLength = 主对象（roadSegments[0]）对应轴向的长度
//     （优先 MeshFilter.sharedMesh.bounds 本地空间，回退 Renderer.bounds 世界空间）
//   - 按对应轴向**等距排布所有段**，以父对象位置为几何中心
//     （共用 <see cref="VicToolsBoundsUtility.AlignSegmentsAlongAxis"/>，
//     与 RoadScrollEditorHelper.CreateRoadScroll 逻辑完全一致，避免重复实现）
// 在 "道路模型设置" 组的 roadSegments 列表下方加 "目标数量 + 生成" 控件行：
//   - 用户输入目标段数（包含现有段）
//   - 点击 "生成" 自动把 roadSegments[0] 复制到目标数量，并把所有段沿当前 scrollDirection 等距排布
//   - 完整支持 Undo（复制 + 父级调整 + 段位置改动都可撤销）
//
// 共享辅助：
//   - <see cref="RoadScrollEditorHelper.ApplyAxisToRoadScroll"/>：
//     RoadScrollEditorHelper.CreateRoadScroll 与 ApplyAxis 共用的"排布 + 设置字段 + 标记 dirty"入口。

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Vic.Runtime;

namespace VicTools
{
    [CustomEditor(typeof(RoadScroll))]
    [CanEditMultipleObjects]
    public class RoadScrollEditor : Editor
    {
        // 道路模型设置
        private SerializedProperty roadSegments;

        // 特效配置
        private SerializedProperty volume;
        private SerializedProperty radialBlur;

        // 滚动设置
        private SerializedProperty segmentLength;
        private SerializedProperty scrollSpeed;
        private SerializedProperty scrollDirection;
        // private SerializedProperty swapThreshold;
        private SerializedProperty autoStart;

        // UI 显示
        private SerializedProperty distanceText;
        private SerializedProperty textOffset;

        // 调试信息
        private SerializedProperty showDebug;

        // 复制工具：目标数量（不持久化，每次打开 Inspector 时初始化为当前 roadSegments 数量）
        private int _targetSegmentCount = 1;

        private void OnEnable()
        {
            roadSegments = serializedObject.FindProperty("roadSegments");

            volume = serializedObject.FindProperty("volume");
            radialBlur = serializedObject.FindProperty("radialBlur");

            segmentLength = serializedObject.FindProperty("segmentLength");
            scrollSpeed = serializedObject.FindProperty("scrollSpeed");
            scrollDirection = serializedObject.FindProperty("scrollDirection");
            // swapThreshold = serializedObject.FindProperty("swapThreshold");
            autoStart = serializedObject.FindProperty("autoStart");

            distanceText = serializedObject.FindProperty("distanceText");
            textOffset = serializedObject.FindProperty("textOffset");

            showDebug = serializedObject.FindProperty("showDebug");

            // 初始化复制工具的目标数量为当前 roadSegments 数量
            // （取第一个 target；多选时所有 target 共享同一目标数）
            if (targets != null && targets.Length > 0)
            {
                RoadScroll first = targets[0] as RoadScroll;
                if (first != null && first.roadSegments != null)
                    _targetSegmentCount = Mathf.Max(1, first.roadSegments.Count);
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // ── 道路模型设置 ──
            EditorGUILayout.PropertyField(roadSegments, true);
            DrawDuplicateControls();  // 目标数量 + 生成（在 roadSegments 列表下方）

            // ── 特效配置 ──
            EditorGUILayout.PropertyField(volume);
            EditorGUILayout.PropertyField(radialBlur);

            // ── 滚动设置 ──
            EditorGUILayout.PropertyField(segmentLength);
            DrawAxisButtons();  // 紧贴 segmentLength，便于联动编辑
            EditorGUILayout.PropertyField(scrollSpeed);
            EditorGUILayout.PropertyField(scrollDirection);
            // EditorGUILayout.PropertyField(swapThreshold);
            EditorGUILayout.PropertyField(autoStart);

            // ── UI 显示 ──
            EditorGUILayout.PropertyField(distanceText);
            EditorGUILayout.PropertyField(textOffset);

            // ── 调试信息 ──
            EditorGUILayout.PropertyField(showDebug);

            serializedObject.ApplyModifiedProperties();
        }

        // ====================================================================
        // 滚动设置 - 轴向按钮行
        // ====================================================================

        /// 在 segmentLength 字段下方绘制 X/Y/Z 三个按钮。
        /// 点击后自动：
        ///   1. 计算 roadSegments[0] 沿目标轴的 OBB 长度 → segmentLength
        ///   2. scrollDirection = -目标轴
        ///   3. 按目标轴**等距排布所有段**，以父对象位置为几何中心
        ///      （共用 AlignSegmentsAlongAxis，与 RoadScrollEditorHelper.CreateRoadScroll 逻辑一致）
        private void DrawAxisButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("X轴", GUILayout.Height(22)))
                ApplyAxis(Vector3.right, 0);
            if (GUILayout.Button("Y轴", GUILayout.Height(22)))
                ApplyAxis(Vector3.up, 1);
            if (GUILayout.Button("Z轴", GUILayout.Height(22)))
                ApplyAxis(Vector3.forward, 2);
            EditorGUILayout.EndHorizontal();
        }

        /// 应用轴向：计算 OBB 长度 + 调用共享辅助 <see cref="RoadScrollEditorHelper.ApplyAxisToRoadScroll"/>
        /// 完成"排布 + 设置字段 + 标记 dirty"。与 <see cref="RoadScrollEditorHelper.CreateRoadScroll"/> 共用同一入口。
        private void ApplyAxis(Vector3 axis, int axisIndex)
        {
            RoadScroll rs = target as RoadScroll;
            if (rs == null || rs.roadSegments == null || rs.roadSegments.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先在 roadSegments 列表中填入至少一段滚动模型", "确定");
                return;
            }

            Transform primary = rs.roadSegments[0];
            if (primary == null)
            {
                EditorUtility.DisplayDialog("提示", "roadSegments[0] 为空，无法计算轴向长度", "确定");
                return;
            }

            // 1. 用 OBB 算法计算沿 axis 方向的准确长度（共享 VicToolsBoundsUtility）
            float newLength = VicToolsBoundsUtility.GetAccurateLengthAlongAxis(primary, axis);
            if (newLength <= 0f)
            {
                EditorUtility.DisplayDialog("提示",
                    $"[{primary.name}] 沿 {RoadScrollEditorHelper.AxisName(axis)} 轴无法计算长度（无 MeshFilter/Renderer 或 mesh 无顶点）",
                    "确定");
                return;
            }

            // 2. 调用共享入口完成"排布 + 设置字段 + 标记 dirty"（与 CreateRoadScroll 共用）
            RoadScrollEditorHelper.ApplyAxisToRoadScroll(rs, axis, newLength, "Apply RoadScroll Axis");

            // 3. 同步 SerializedProperty
            serializedObject.Update();

            string axisName = RoadScrollEditorHelper.AxisName(axis);
            int segCount = rs.roadSegments.Count;
            Debug.Log($"[VicTools] RoadScroll 已设置 {axisName} 轴：\n" +
                      $"  ScrollDirection={-axis}（段流向远方，即 -{axisName} 方向）\n" +
                      $"  SegmentLength={newLength:F2}（OBB 准确长度）\n" +
                      $"  {segCount} 段已按 -{axisName} 方向等距排布（以父对象位置 [{rs.transform.position}] 为几何中心）\n" +
                      $"  共享入口：RoadScrollEditorHelper.ApplyAxisToRoadScroll（与 CreateRoadScroll / DuplicateAndArrange 一致）");
        }

        // ====================================================================
        // 滚动模型设置 - 复制控件（目标数量 + 生成）
        // ====================================================================

        /// 在 roadSegments 列表下方绘制 "目标数量 + 生成" 控件行。
        /// <para>
        /// <b>目标数量</b>：用户期望的最终段数（包含现有的）。最小值 1。
        /// </para>
        /// <para>
        /// <b>生成按钮</b>：把 roadSegments 中第一个非空 transform 复制到目标数量，
        /// 然后沿当前 <c>scrollDirection</c> 等距排布所有段（以父对象位置为几何中心）。
        /// </para>
        /// <para>
        /// 多选时按钮会对每个选中的 RoadScroll 都执行一次。
        /// </para>
        private void DrawDuplicateControls()
        {
            EditorGUILayout.BeginHorizontal();

            _targetSegmentCount = EditorGUILayout.IntField("目标数量", _targetSegmentCount);
            if (_targetSegmentCount < 1) _targetSegmentCount = 1;

            if (GUILayout.Button(new GUIContent("生成",
                    "把 roadSegments[0] 复制到目标数量，并自动按当前 scrollDirection 等距排布所有段"),
                    GUILayout.Width(60f)))
            {
                // 多选时对每个选中的 RoadScroll 各执行一次
                for (int i = 0; i < targets.Length; i++)
                {
                    RoadScroll rs = targets[i] as RoadScroll;
                    if (rs != null) DuplicateAndArrange(rs, _targetSegmentCount);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        /// 把 <paramref name="rs"/> 的 roadSegments 复制到 <paramref name="targetCount"/> 段，
        /// 然后沿当前 <c>scrollDirection</c> 等距排布所有段。
        /// <para>
        /// <b>复制策略</b>：
        /// <list type="bullet">
        ///   <item>模板：roadSegments 中第一个非空 transform（不区分原 roadSegments[0] 是否为空）</item>
        ///   <item>复制数：<c>max(0, targetCount - 当前段数)</c>（已达标则不复制）</item>
        ///   <item>复制方式：<c>Object.Instantiate(template.gameObject)</c>，父级设为 <c>rs.transform</c></item>
        ///   <item>段命名：<c>原名 + "_Dup{i}"</c>（i 从 1 开始）</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>排布策略</b>（复制后无论是否新增都会执行）：
        /// 调用 <see cref="VicToolsBoundsUtility.AlignSegmentsAlongAxis"/> 沿
        /// <c>-scrollDirection.normalized</c> 方向等距排布，相邻段中心间距 = <c>rs.segmentLength</c>，
        /// 几何中心 = <c>rs.transform.position</c>。
        /// <b>注意</b>：此处直接用 <see cref="VicToolsBoundsUtility.AlignSegmentsAlongAxis"/> 而不
        /// 走 <see cref="RoadScrollEditorHelper.ApplyAxisToRoadScroll"/>，避免覆盖用户已设置的
        /// <c>scrollDirection</c> / <c>segmentLength</c>（本方法只负责"排布"）。
        /// </para>
        /// <para>
        /// <b>Undo 支持</b>：
        /// <list type="bullet">
        ///   <item>RecordObject(rs) — 记录 roadSegments 列表变化</item>
        ///   <item>RegisterCreatedObjectUndo(copy) — 记录新 GameObject 的创建</item>
        ///   <item>SetTransformParent — 记录父级调整</item>
        ///   <item>RecordObjects(segments) — 记录段位置变化</item>
        /// </list>
        /// 单次操作可一键撤销。
        /// </para>
        /// <param name="rs">目标 RoadScroll 组件</param>
        /// <param name="targetCount">用户输入的目标段数（已保证 ≥ 1）</param>
        private void DuplicateAndArrange(RoadScroll rs, int targetCount)
        {
            if (rs == null) return;
            if (rs.roadSegments == null) rs.roadSegments = new List<Transform>();
            if (targetCount < 1) return;

            // 1. 找第一个非空 segment 作为复制模板
            Transform template = null;
            for (int i = 0; i < rs.roadSegments.Count; i++)
            {
                if (rs.roadSegments[i] != null) { template = rs.roadSegments[i]; break; }
            }
            if (template == null)
            {
                EditorUtility.DisplayDialog("提示",
                    "请先在 roadSegments 列表中填入至少一段滚动模型",
                    "确定");
                return;
            }

            int currentCount = rs.roadSegments.Count;
            int toAdd = Mathf.Max(0, targetCount - currentCount);

            // 2. 复制（如果需要）
            if (toAdd > 0)
            {
                // 先 RecordObject 让 roadSegments.Add 进入 Undo 栈
                Undo.RecordObject(rs, "Duplicate Road Segments");

                for (int i = 0; i < toAdd; i++)
                {
                    GameObject copy = Object.Instantiate(template.gameObject);
                    copy.name = template.gameObject.name + "_Dup" + (i + 1);
                    Undo.RegisterCreatedObjectUndo(copy, "Duplicate Road Segment");
                    // 父级设为 RoadScroll 所在对象（worldPositionStays=true 保留世界坐标，
                    // 后续 AlignSegmentsAlongAxis 会整体重排，所以初始坐标不重要）
                    Undo.SetTransformParent(copy.transform, rs.transform, "Parent Duplicated Segment");
                    rs.roadSegments.Add(copy.transform);
                }

                EditorUtility.SetDirty(rs);
                Debug.Log($"[VicTools] RoadScroll [{rs.name}] 已复制 {toAdd} 段（基于 [{template.name}]），" +
                          $"当前共 {rs.roadSegments.Count} 段（目标 {targetCount}）");
            }
            else
            {
                Debug.Log($"[VicTools] RoadScroll [{rs.name}] 当前已有 {currentCount} 段，已达目标 {targetCount}，无需复制");
            }

            // 3. 沿当前 scrollDirection 等距排布所有段
            //    axis = -scrollDirection.normalized（-axis = scrollDirection = 流向）
            //    scrollDirection 为零向量时 fallback 到 X 轴
            Vector3 arrAxis = -rs.scrollDirection;
            if (arrAxis.sqrMagnitude < 1e-6f) arrAxis = Vector3.right;
            arrAxis.Normalize();

            // 收集 Undo 目标：所有非空段 transform
            var segUndoTargets = new List<UnityEngine.Object>(rs.roadSegments.Count);
            for (int i = 0; i < rs.roadSegments.Count; i++)
                if (rs.roadSegments[i] != null) segUndoTargets.Add(rs.roadSegments[i]);
            if (segUndoTargets.Count == 0) return;

            Undo.RecordObjects(segUndoTargets.ToArray(), "Arrange Road Segments After Duplicate");

            VicToolsBoundsUtility.AlignSegmentsAlongAxis(
                rs.roadSegments, arrAxis, rs.segmentLength, rs.transform.position);

            for (int i = 0; i < rs.roadSegments.Count; i++)
                if (rs.roadSegments[i] != null) EditorUtility.SetDirty(rs.roadSegments[i]);

            // 4. 同步 SerializedProperty（让 Inspector 立刻看到新的 roadSegments 列表）
            serializedObject.Update();

            Debug.Log($"[VicTools] RoadScroll [{rs.name}] 已自动沿 -scrollDirection 方向等距排布 " +
                      $"{rs.roadSegments.Count} 段（segmentLength={rs.segmentLength:F2}，" +
                      $"几何中心 [{rs.transform.position}]）");
        }
    }
}
