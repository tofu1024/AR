# AR 检测精度调研与优化方案

调研日期：2026-10-26。本文针对调研时的 1.1 源码记录分析和建议。后续 1.2.1 已实现第一阶段的状态过滤、稳定预览和 Anchor，见 [实现说明](稳定落点预览实现说明.md)；Depth 和几何重建仍未接入。

## 1. 当前检测方案

当前使用 Unity AR Foundation 6.0.8 和 ARCore XR Plugin 6.0.8，由手机上的 Google Play Services for AR 执行环境跟踪和平面估计。

实现链路：相机图像与运动传感器 → ARCore 跟踪和估计平面 → ARPlaneManager 接收水平与竖直平面 → LineRenderer 绘制 ARPlane.boundary → ARRaycastManager 以 PlaneWithinPolygon 命中 → 在命中点创建立方体。

绿色边界是 SDK 对几何平面覆盖范围的估计，并非桌面、墙壁或裂缝的精确物体轮廓。ARCore 通过视觉特征与惯性数据理解环境；无纹理表面可能检测不佳，估计结果会随观察更新。[Google：ARCore 基本原理](https://developers.google.com/ar/develop/fundamentals)

## 2. 已确认的代码限制

| 当前实现 | 影响 |
| --- | --- |
| 仅检查 ARSession 整体 SessionTracking | 未单独拒绝 trackingState 非 Tracking 的目标平面 |
| 线框只检查边界有效和未被合并 | 跟踪受限的旧平面仍可能显示绿色 |
| 每帧直接重绘 boundary | 边界变化直接呈现，无稳定程度提示 |
| 从命中列表取 hits[0] | 虽然是最近命中，但未再按目标类型、跟踪状态及交互目标筛选 |
| 直接放置普通 GameObject，没有 Anchor | 环境估计修正时，标记缺少专门的空间跟踪关联 |
| 没有开启 Depth 或深度命中 | 点击位置完全依赖已检测平面 |

这些限制可从源码确认，但仍需对具体现场测试，才能判断用户看到的主要误差来源。不能直接认定所有误差都由某一处代码造成。

## 3. 区分三类问题

1. 绿色框未贴合真实物体边缘：几何平面估计与物体轮廓不同，或扫描、纹理、跟踪质量不足。
2. 点击后落点偏离目标表面：命中平面的距离/姿态估计不够准确，或选中了不希望命中的平面。
3. 移动手机后标记漂移：跟踪和世界坐标估计更新，当前没有 Anchor 关联。

Anchor 用于在环境理解更新时调整附着内容的位置，能改善标记的空间稳定性，但不能让错误的平面边界变成精确物体边界。[Google：Anchors](https://developers.google.com/ar/develop/anchors)

## 4. 建议的第一阶段：保留原技术栈

- 仅显示 trackingState 为 Tracking 且未被 subsume 的平面；跟踪受限时隐藏或改变线框颜色并提示重新扫描。
- 放置前检查命中平面的跟踪状态，结合地面/墙面选择模式过滤目标，降低误选其他平面的概率。
- 添加候选落点预览和确认操作，对同一个 trackableId 的位置与法线做短时间稳定性判断。时间窗口和变化阈值属于本项目启发式参数，需要真机调节，不代表 SDK 提供了置信度保证。
- 用 ARAnchorManager 创建 Anchor，再将标记挂到 Anchor 下。适用且支持时可附着到目标平面；处理创建失败和移除旧 Anchor，提交前读取当前锚定位置所对应的命中点。
- 绿色边界仅作为扫描辅助，可使用更细的轮廓和半透明面显示。视觉平滑不能作为精度提升证据；边界顶点数量和顺序可能变化，不能直接按顶点索引做简单插值。

Unity 6.0 文档提供 TryAddAnchorAsync，并在平台支持时提供 AttachAnchor。锚点创建可能失败，应确认成功后再将内容挂载。[Unity：AR Anchor Manager](https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@6.0/manual/features/anchors/aranchormanager.html)

## 5. 建议的第二阶段：可选深度命中

增加 AROcclusionManager，检查当前设备和当前会话的环境深度能力及实际深度数据可用性。可用时评估 TrackableType.Depth 命中，不可用或结果不稳定时回退到有效平面命中。不能仅根据手机型号、是否安装 ARCore 或摄像头像素推断 Depth 已可用。[Google：Unity Depth 接入](https://developers.google.com/ar/develop/unity-arf/depth/developer-guide)

Google 的 Unity 命中文档明确提供 Depth 和 PlaneWithinPolygon 两种方式，并将平面命中列为深度命中的回退方案。Depth 能利用场景深度估计表面位置和方向，但不会自动把 ARPlane.boundary 变成精确墙面轮廓。[Google：Unity Raycast](https://developers.google.com/ar/develop/unity-arf/hit-test/developer-guide)

Depth 无需专用 ToF 传感器，但可能依赖相机运动。低纹理区域仍有局限，尤其不能承诺纯白墙或玻璃都能准确定位；Raw Depth 也可能在无纹理处没有可靠数据。[Google：Raw Depth](https://developers.google.com/ar/develop/unity-arf/depth/raw-depth)

建议优先验证“稳定、跟踪良好的平面”与“深度命中”在本机现场的实际误差后再选策略，不默认把所有 Depth 结果排在所有平面结果之前。

## 6. 其他方案的取舍

| 方案 | 适用情况 | 取舍 |
| --- | --- | --- |
| 平面状态过滤 + 预览 + Anchor | 当前面试原型、地面与墙面巡检 | 优先实施，改动集中在 Unity，接口不变 |
| 可选 Depth 命中 | 平面结果差、局部表面或低纹理场景 | 需要设备能力检查及真机比较，保留回退 |
| 已知尺寸参考图像/人工标识 | 固定设备、固定巡检点，可部署标识 | 提供已知参考，增加现场布置，不直接等价于自动墙面检测 |
| 稠密重建或自写点云平面拟合 | 必须重建几何、测量或提取真实轮廓 | 工程量和验证成本较高，当前原型不优先 |

## 7. 最小验证方法

在同一手机、同一光照和相近拍摄距离下，分别测试有纹理桌面、有纹理墙面、纯色墙面。先测现有版本，再比较第一阶段和可选 Depth 的效果。

- 记录首次可用平面的时间与失败情况。
- 用已知物理参照检查落点距离表面的误差。
- 放置后从多个角度移动观察，记录漂移与重新定位时的变化。
- 记录误选其他平面、跟踪受限时仍可放置等交互问题。
- 同时验证水平与竖直表面以及上报坐标，不只观察线框是否看起来平滑。

结论：优先补齐跟踪状态过滤、稳定预览和 Anchor，再用设备实测决定是否启用 Depth。当前不建议为面试原型引入独立重建服务或承诺厘米级精度。
