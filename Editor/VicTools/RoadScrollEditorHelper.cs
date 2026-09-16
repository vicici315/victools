// RoadScrollEditorHelper —— RoadScroll 组件的共享编辑器辅助方法。
// 集中所有"RoadScroll 编辑器侧的非 Inspector 操作"，避免这些逻辑分散在 VicToolsWindow 等大文件中。
//
// 公共 API：
//   - CreateRoadScroll：创建工具（从场景选中对象一键生成 RoadScroll 控制器）
//   - ApplyAxisToRoadScroll：把整个 segment 列表按指定轴等距排布，并设置 scrollDirection/segmentLength
//                            v2.4 起 axis 参数语义改为"父对象局部轴"（X/Y/Z 按钮代表父对象的局部 X/Y/Z 轴），
//                            内部 TransformDirection 到世界轴用于排 worldPos，scrollDirection 直接存父局部轴，
//                            保证段队列方向 = 滚动方向 = 父局部 axis，避免父对象旋转后整排对象平移
//   - ComputeSegmentLength：v2.5+ 共享函数，用段对象本身（roadSegments[0]）的世界空间 OBB 长度
//                            作为 segmentLength；ApplyAxis / DuplicateAndArrange / CreateRoadScroll 统一调用
//   - DetectPrimaryAxisWithFallback：检测主对象的主轴 + 长度（无主轴时 fallback 到主对象的 transform 轴）
//   - AxisName：把精确世界标准轴（Vector3.right/up/forward）映射为单字符轴名（"X"/"Y"/"Z"）
//   - ComputeSegmentLength：v2.5+ 用段对象本身计算世界空间段长，ApplyAxis/DuplicateAndArrange/CreateRoadScroll 共享
//
// 所有调用方：
//   - VicToolsWindow 菜单 "创建 无限循环滚动道路（RoadScroll）" → CreateRoadScroll
//   - RoadScrollEditor.ApplyAxis（X/Y/Z 按钮按下时按选定轴排列）          → ApplyAxisToRoadScroll
//   - RoadScrollEditor.DuplicateAndArrange（复制后等距排布）              → 间接通过 AlignSegmentsAlongAxis
//
// 设计原则：所有 RoadScroll 编辑器辅助集中在此，Inspector 渲染逻辑留在 RoadScrollEditor，
//          运行时组件在 Runtime/Scripts/RoadScroll.cs。

// RoadScrollEditorHelper v2.6 添加 ESC 快捷键：选中 roadSegment 时按 ESC 跳转到所属 RoadScroll 父对象
// v2.6 修复 ESC 选中脚本对象功能在 roadSegment 包含子对象时失效：
//   - 原 FindOwnerRoadScroll 只做精确匹配 rs.roadSegments[j] == segment，
//     选中 roadSegment 的子物体（如子 mesh、装饰子物体、Collider 子物体）时匹配失败，ESC 跳转失效
//   - 现增加"向上遍历父链匹配"：精确匹配失败后从 segment.parent 逐级向上查找任一 rs.roadSegments[j]，
//     命中即跳回所属 RoadScroll
//   - roadSegment 经常带子物体（mesh/装饰/Collider 子级），用户选中子级时 ESC 仍能跳回控制器

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
        /// 并设置 <c>scrollDirection = -localAxis</c> + <c>segmentLength = segmentLength</c>。
        /// <para>
        /// <b>v2.3 起 scrollDirection 含义改为父对象的局部轴</b>（运行时使用 Space.Self 移动，
        /// ScrollDirection 作为父坐标系下的滚动方向）。所以写入前必须把世界标准轴 <paramref name="axis"/>
        /// 用 <c>InverseTransformDirection</c> 变换到 <paramref name="rs"/>.transform 的局部空间，
        /// 父对象旋转时滚动方向才会跟随旋转，避免"ApplyAxis 后错误滚动"。
        /// </para>
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

            // 2. 把父局部 axis 变换到世界轴用于段 worldPos 排布（v2.4+：X/Y/Z 按钮代表父对象局部轴）。
            //    段 worldPos 沿"父对象 axis 在世界空间的方向"等距排，
            //    运行时 worldDir = R * scrollDirection = R * (-localAxis) = -worldAxis，
            //    滚动方向与队列方向一致 → 滚动衔接正确（避免父对象旋转后整排对象平移的 bug）。
            Vector3 localAxis = axis.normalized;
            Vector3 worldAxis = rs.transform.TransformDirection(localAxis);
            VicToolsBoundsUtility.AlignSegmentsAlongAxis(
                rs.roadSegments, worldAxis, segmentLength, rs.transform.position);

            // 3. 设置字段：scrollDirection 存父对象的局部轴（v2.3+ 语义，与运行时 Space.Self 移动一致）
            rs.scrollDirection = -localAxis;
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
        // 实时排布：RearrangeAlongScrollDirection（v2.8+ Inspector 实时编辑用）
        // ====================================================================

        /// 沿 <paramref name="rs"/> 当前的 <c>scrollDirection</c>（父对象<b>局部</b>轴）等距重排所有段，
        /// 间距使用 <c>rs.segmentLength</c>，几何中心 <c>rs.transform.position</c>。
        /// <para>
        /// v2.8+ 用于 Inspector 实时编辑：用户拖动 <c>segmentLength</c> / <c>scrollDirection</c> 滑块时，
        /// 段世界坐标实时跟随更新，无需点 ApplyAxis 按钮。
        /// </para>
        /// <para>
        /// <b>v2.8.2 关键修复</b>：完全移除 Undo API 调用（包括 RecordObjects / CollapseUndoOperations）。
        /// Unity 对 <c>Transform.position</c> 的修改会自动加入 Undo 栈（Transform 是原生 SerializedObject），
        /// 原代码在 delayCall 中反复调用 Undo API 会把 Undo 系统打爆，导致 IMGUI Inspector 焦点丢失，
        /// 滑块拖动事件无法被正确接收（表现为"无法修改 SegmentLength"）。
        /// 现在只做"修改段 position + 标记 dirty"，Undo 由 Unity 自动处理，用户撤销（Ctrl+Z）可一键还原。
        /// </para>
        /// <para>
        /// 不修改任何 <see cref="RoadScroll"/> 字段，只移动段 transform。
        /// </para>
        public static void RearrangeAlongScrollDirection(RoadScroll rs, string undoLabel)
        {
            if (rs == null) return;
            if (rs.roadSegments == null || rs.roadSegments.Count == 0) return;

            // scrollDirection 为零向量时 fallback 到父对象 X 轴
            Vector3 localAxis = -rs.scrollDirection;
            if (localAxis.sqrMagnitude < 1e-6f) localAxis = Vector3.right;
            localAxis.Normalize();

            // 父局部轴 → 世界轴，与 ApplyAxisToRoadScroll 内部 worldAxis 计算完全一致
            Vector3 worldAxis = rs.transform.TransformDirection(localAxis);

            // 直接修改段 position，**不调用任何 Undo API**——避免反复 RecordObjects / CollapseUndoOperations
            // 打爆 Undo 系统导致 Inspector focus 丢失，滑块拖动事件中断。
            VicToolsBoundsUtility.AlignSegmentsAlongAxis(
                rs.roadSegments, worldAxis, rs.segmentLength, rs.transform.position);

            for (int i = 0; i < rs.roadSegments.Count; i++)
                if (rs.roadSegments[i] != null) EditorUtility.SetDirty(rs.roadSegments[i]);
        }

        // ====================================================================
        // 段长度计算：ComputeSegmentLength（v2.5+ 共享）
        // ====================================================================

        /// 计算 <paramref name="rs"/> 的段队列中"段对象本身"（<c>roadSegments[0]</c>）在
        /// <paramref name="localAxis"/>（父对象<b>局部</b>轴，如 <see cref="Vector3.right"/>/<see cref="Vector3.up"/>/<see cref="Vector3.forward"/>）
        /// 方向上的世界空间准确长度（OBB 投影），用于写入 <c>segmentLength</c>。
        /// <para>
        /// <b>为什么必须用段对象本身</b>：
        /// <list type="bullet">
        ///   <item>段的 mesh 真实长度 = mesh 在段 GameObject 上经 localToWorld 旋转+缩放后的世界空间尺寸</item>
        ///   <item><see cref="VicToolsBoundsUtility.GetAccurateLengthAlongAxis"/> 内部用
        ///         <c>mf.transform.TransformVector(extents)</c> 已自动包含段 GameObject 自身及其父链
        ///         （RoadScroll 父对象）的旋转 + 缩放（lossyScale）</item>
        ///   <item>段队列沿父对象 <paramref name="localAxis"/> 等距排，间距 = 段 worldPos 上沿 worldAxis 的真实长度
        ///         = 段对象在该方向的世界长度 → 滚动衔接正确</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>为什么要在 ApplyAxis / DuplicateAndArrange / CreateRoadScroll 统一调用</b>：
        /// 集中在一处避免三处重复实现（之前 ApplyAxis 写一份、DuplicateAndArrange 没算、CreateRoadScroll 写另一份），
        /// 同时让"目标数量 + 生成"按钮在段尺寸/模板修改后自动刷新 segmentLength，避免衔接错位。
        /// </para>
        /// <param name="rs">目标 RoadScroll 组件</param>
        /// <param name="localAxis">段排列方向（父对象<b>局部</b>轴；v2.4+ 语义，与 <see cref="ApplyAxisToRoadScroll"/> 一致）</param>
        /// <returns>段对象在世界空间沿父对象局部 <paramref name="localAxis"/> 方向的准确长度；
        /// 返回 0 表示无有效段或 mesh 无可计算长度（调用方应据此弹窗提示）。</returns>
        public static float ComputeSegmentLength(RoadScroll rs, Vector3 localAxis)
        {
            if (rs == null) return 0f;
            if (rs.roadSegments == null || rs.roadSegments.Count == 0) return 0f;

            // 用第一个非空段对象作为计算基准（段对象本身的 mesh 真实尺寸）
            Transform segmentObj = rs.roadSegments[0];
            if (segmentObj == null) return 0f;

            // 父对象局部轴 → 世界轴，与 ApplyAxisToRoadScroll 内部 worldAxis 计算保持完全一致
            Vector3 worldAxis = rs.transform.TransformDirection(localAxis.normalized);
            return VicToolsBoundsUtility.GetAccurateLengthAlongAxis(segmentObj, worldAxis);
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

    // ====================================================================
    // ESC 快捷键：选中 roadSegment 时按 ESC 跳转到所属 RoadScroll 父对象
    // ====================================================================

    /// 全局快捷键助手：当 Hierarchy / Scene / Game 窗口中选中某个
    /// <see cref="RoadScroll.roadSegments"/> 中的段对象时，按 ESC 键将选中切回挂载
    /// <see cref="RoadScroll"/> 脚本的父对象，便于用户在场景中"沿段找控制器"或
    /// "逃离深嵌套段层级"。
    /// <para><b>触发条件</b>：任意支持键盘的 Editor 窗口（Scene/Game/Hierarchy）
    /// + 按下 ESC + 当前 <see cref="Selection.activeTransform"/> 属于任一
    /// <see cref="RoadScroll.roadSegments"/> 列表。</para>
    /// <para><b>副作用</b>：在 Scene 窗口路径上调用 <c>Event.Use()</c> 阻止 ESC 冒泡
    /// 到 Unity 自身的 ESC 工具退出语义；不与"按 ESC 取消选区"等系统快捷键冲突
    /// （Selection 由我们重新置为目标父对象）。</para>
    /// <para><b>注册方式</b>：<see cref="InitializeOnLoadAttribute"/> 在 Editor 启动/域重载
    /// 时自动注册两条触发路径（<see cref="SceneView.duringSceneGui"/> +
    /// <see cref="EditorApplication.update"/>+<see cref="Input.GetKeyDown(KeyCode.Escape)"/>），
    /// 无需用户在 Inspector 启用任何开关。</para>
    [InitializeOnLoad]
    internal static class RoadScrollSegmentSelectionHelper
    {
        // RoadScroll 全场景查询缓存：避免 ESC 每帧触发都全场景 FindObjectsByType
        // 0.5s 缓存周期 = 用户一次 ESC 触发（≤数十 ms）内能命中同一帧缓存
        private static RoadScroll[] s_cachedRoadScrolls = new RoadScroll[0];
        private static double s_lastCacheTime;
        private const double kCacheIntervalSeconds = 0.5;

        // 双触发源防抖：SceneView GUI 事件 + EditorApplication.update/Input 在不同窗口焦点
        // 下可能都接收到同一帧的 ESC，100ms 内只允许跳转一次避免重复 SetSelection
        private static double s_lastTriggeredTime;
        private const double kDebounceSeconds = 0.1;

        [InitializeOnLoadMethod]
        private static void Register()
        {
            // 先 -= 防止域重载后重复订阅（静态事件不会自动清理）
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        // 路径 1：Scene 窗口焦点 + ESC（SceneView 重绘时 Event.current 携带键盘事件）
        private static void OnSceneGui(SceneView view)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown || e.keyCode != KeyCode.Escape) return;
            TryJumpToOwner(useEvent: true);
        }

        // 路径 2：Game / Hierarchy 等窗口焦点 + ESC（Input 系统在 SceneView 之外也能响应）
        // SceneView.duringSceneGui 仅在 Scene 窗口绘制时触发，覆盖不到 Game 视图焦点
        private static void OnEditorUpdate()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;
            TryJumpToOwner(useEvent: false);
        }

        private static void TryJumpToOwner(bool useEvent)
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - s_lastTriggeredTime < kDebounceSeconds) return;

            Transform selected = Selection.activeTransform;
            if (selected == null) return;

            RoadScroll owner = FindOwnerRoadScroll(selected);
            if (owner == null) return;

            s_lastTriggeredTime = now;
            Selection.activeTransform = owner.transform;
            if (useEvent) Event.current.Use();
        }

        private static RoadScroll FindOwnerRoadScroll(Transform segment)
        {
            double now = EditorApplication.timeSinceStartup;
            if (s_cachedRoadScrolls == null || now - s_lastCacheTime > kCacheIntervalSeconds)
            {
                s_cachedRoadScrolls = UnityEngine.Object.FindObjectsByType<RoadScroll>(FindObjectsSortMode.None);
                s_lastCacheTime = now;
            }

            // 1. 精确匹配：选中 roadSegment 自身时（零开销路径）
            for (int i = 0; i < s_cachedRoadScrolls.Length; i++)
            {
                RoadScroll rs = s_cachedRoadScrolls[i];
                if (rs == null || rs.roadSegments == null) continue;
                for (int j = 0; j < rs.roadSegments.Count; j++)
                {
                    if (rs.roadSegments[j] == segment) return rs;
                }
            }

            // 2. v2.6.1 父链向上匹配：选中 roadSegment 的子物体时
            //    roadSegment 经常带子 mesh / 装饰子物体 / 子 Collider，用户选中子级时
            //    精确匹配失败，从 segment.parent 逐级向上查找任一 rs.roadSegments[j]，
            //    命中即跳回所属 RoadScroll。roadSegment 不会互为祖先（每段独立），
            //    找到最近的祖先段归属合理；最坏 O(depth * N * M)，depth/N/M 均很小
            Transform t = segment.parent;
            while (t != null)
            {
                for (int i = 0; i < s_cachedRoadScrolls.Length; i++)
                {
                    RoadScroll rs = s_cachedRoadScrolls[i];
                    if (rs == null || rs.roadSegments == null) continue;
                    for (int j = 0; j < rs.roadSegments.Count; j++)
                    {
                        if (rs.roadSegments[j] == t) return rs;
                    }
                }
                t = t.parent;
            }

            return null;
        }
    }
}
