# 雪地凹陷交互系统

基于 RenderTexture 的实时雪地凹陷方案，适用于移动端。角色/物体接触雪地时通过 Raycast 获取碰撞点世界坐标，投影到 RT 上绘制痕迹，雪地 Shader 采样 RT 做顶点位移和材质变化。

## 工作原理

1. `SnowFootprintMarker` 向下发射射线，碰到雪地 Layer 的碰撞体时获取 `hit.point` 世界坐标
2. 通过 `SnowDeformManager.WorldToUV()` 将世界坐标 XZ 投影为 RT 的 UV 坐标
3. 调用 `SnowDeformManager.PaintLine()` 在 RT 上绘制线段画笔（通过 SnowPaint shader）
4. `Custom_Snow.shader` 用世界空间 XZ 投影采样 RT，沿世界 Y 轴做顶点凹陷 + 颜色变化 + 闪烁抑制
5. RT 每帧做微量衰减（SnowFade shader），痕迹渐渐恢复
6. 雪面整体上抬 `sinkDepth`，物体视觉上陷入雪中模拟雪厚度

## 使用要求

- 雪地模型需要碰撞体（任意类型均可，不再要求 MeshCollider）
- 雪地碰撞体所在对象的 Layer 需与 Manager 的 `snowLayer` 匹配
- 材质使用 `Custom/Snow` shader
- 雪地模型基本水平放置（世界空间 XZ 投影方案）

## 快速搭建

1. 场景中创建空对象，挂 `SnowDeformManager`
   - 拖入 SnowPaint 和 SnowFade shader 到 Shader 引用槽位
   - 拖入雪地模型的 Renderer 到 `Snow Renderer` 槽位（自动计算投影区域）
   - 设置 `snowLayer` 为雪地所在 Layer
2. 角色脚底/滑雪板底部挂 `SnowFootprintMarker`（可多个，各自设置大小和强度）
   - 调整 `brushSize`（世界空间，米）和 `brushStrength`
   - 调整 `rayDirection`方向 和 `rayDistance`触发压痕触手长度 适配角色高度
3. 在 Custom_Snow 材质的"雪地交互 (压痕)"分组中调整压痕颜色和过渡

---

## 文件结构

### Shader 文件

| 文件 | 路径 | 用途 |
|------|------|------|
| `Custom_Snow.shader` | `Runtime/Shaders/` | 雪地渲染主 Shader。包含冰晶闪烁、Fresnel 边缘光、世界空间投影 RT 凹陷位移、压痕颜色、闪烁抑制、雪厚度模拟 |
| `SnowPaint.shader` | `Runtime/Shaders/` | 内部工具 Shader（Hidden）。用于 `Graphics.Blit` 在 RT 上绘制线段画笔（胶囊形），累积叠加（取 max 防止过饱和） |
| `SnowFade.shader` | `Runtime/Shaders/` | 内部工具 Shader（Hidden）。用于 `Graphics.Blit` 对 RT 做每帧微量衰减，实现痕迹渐渐恢复 |

### 脚本文件

| 文件 | 路径 | 用途 |
|------|------|------|
| `SnowDeformManager.cs` | `Runtime/Scripts/` | 全局管理器（场景唯一）。管理 RT 创建/销毁、投影区域计算、全局 Shader 参数设置、痕迹恢复逻辑、碰撞下沉控制。提供 RT 预览（Scene/Game 视图） |
| `SnowFootprintMarker.cs` | `Runtime/Scripts/` | 凹陷控制器（可创建多个）。挂在角色脚底/滑雪板等接触点。每个实例独立设置：画笔大小、凹陷强度、射线方向和长度。使用线段绘制保证高速移动时痕迹连续 |

### Editor 文件

| 文件 | 路径 | 用途 |
|------|------|------|
| `CustomSnowGUI.cs` | `Editor/VicTools/` | Custom_Snow shader 的自定义 Inspector GUI。按功能分组显示参数（含雪地交互分组），支持存档/读档/预设 |

---

## 参数说明

### SnowDeformManager（全局共用）

| 参数 | 说明 |
|------|------|
| `rtResolution` | RT 分辨率，移动端建议 512-1024 |
| `snowRenderer` | 雪地 Renderer 引用，用于自动计算投影区域范围 |
| `areaCenter` | RT 投影区域中心（世界 XZ），指定 snowRenderer 时自动计算 |
| `areaSize` | RT 投影区域边长（米），指定 snowRenderer 时自动计算 |
| `showRTPreview` | 是否在 Scene/Game 视图显示 RT 预览窗口 |
| `previewSize` | 预览窗口大小（像素） |
| `maxDeformDepth` | 全局最大凹陷深度（米） |
| `sinkDepth` | 碰撞下沉深度（米），雪面视觉上抬让物体陷入雪中 |
| `deformDarken` | 凹陷区域颜色变暗程度 0-2 |
| `brushSoftness` | 画笔边缘柔和度 0-1 |
| `enableFade` | 是否启用痕迹恢复 |
| `fadeSpeed` | 恢复速度（每秒衰减量） |
| `snowLayer` | 雪地 Layer |
| `paintShader` | SnowPaint shader 引用（必须手动拖入，防止打包剥离） |
| `fadeShader` | SnowFade shader 引用（必须手动拖入，防止打包剥离） |

### SnowFootprintMarker（每个实例独立）

| 参数 | 说明 |
|------|------|
| `brushSize` | 画笔大小（世界空间，米） |
| `brushStrength` | 凹陷强度 0-1（写入 RT 的值） |
| `rayDirection` | 射线检测方向（默认向下） |
| `rayDistance` | 射线长度（米） |
| `manager` | SnowDeformManager 引用（为空时自动查找） |

### Custom_Snow 材质 - 雪地交互参数

| 参数 | 说明 |
|------|------|
| `Deform Color` | 压痕颜色（凹陷区域的染色） |
| `Deform Color Strength` | 压痕染色强度 |
| `Deform Edge Softness` | 压痕过渡柔和度（pow 曲线，越大边缘越软） |

---

## 技术细节

### 世界空间 XZ 投影

采用世界空间 XZ 坐标投影到 RT，而非模型 UV 采样。优势：
- 彻底消除 UV 接缝处的顶点劈裂
- 不依赖 MeshCollider，任何碰撞体类型都可以
- 不需要 mesh 开启 Read/Write Enabled
- 模型 UV 布局不影响效果

### 统一 Y 轴位移

顶点位移统一沿世界空间 Y 轴方向，而非各自顶点法线。解决了硬边（split normals）处因法线方向不同导致的破面问题。

### 雪厚度模拟（sinkDepth）

通过 shader 将雪面顶点整体上抬 `sinkDepth`，碰撞体位置不变。物体碰到碰撞面停下时，视觉上已经陷入雪面，模拟雪的厚度感。压痕凹陷在此基础上进一步向下位移。

### 线段画笔

SnowPaint shader 使用胶囊形线段画笔（点到线段距离），一次 Blit 即可绘制从上一帧到当前帧的连续轨迹，避免高速移动时出现断点。

---

## 注意事项！

- 雪地模型FBX需要勾选`Read/Write`选项，打包游戏时模型压痕变形才能生效

- SnowPaint 和 SnowFade 是 `Hidden/` shader，**必须**通过 Inspector 拖入 Manager 的引用字段，否则打包后会被 Unity shader stripping 剥离
- 投影区域由 `snowRenderer` 的 Bounds 自动计算，确保拖入正确的雪地 Renderer
- RT 精度受 `areaSize` 影响：区域越大每像素覆盖面积越大，小脚印可能模糊。可通过提高 `rtResolution` 或缩小区域补偿
- 当前方案假设雪地基本水平（XZ 平面），如果雪地有大角度旋转需要额外处理
- RT 是运行时动态创建的内存纹理，不是文件资源，不需要手动管理
- 压痕区域会同时压暗闪光点，与压痕颜色过渡保持一致
