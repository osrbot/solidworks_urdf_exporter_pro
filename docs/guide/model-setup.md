# Link、Joint 与几何

## Link 负责什么

一个 Link 对应机器人中的刚体。它至少要有明确的组件归属和坐标系，通常还包含质量、质心、
惯性、Visual、Collision 与外观信息。

## Joint 负责什么

Joint 连接父 Link 与子 Link。插件从 SolidWorks 参考几何读取坐标系和轴，但不会凭形状猜测
机械设计意图。导出前至少确认类型、原点、轴和适用的约束值。

`continuous` 表示无限旋转，因此没有上下角度限位，但仍需要合理的力矩与速度上限。`revolute`
和 `prismatic` 还需要上下位置限位。所有值都应与真实机构、执行器和安全要求一致。

## Visual 与 Collision 分开处理

Visual 服务显示，Collision 服务接触求解。高精度 Visual 不代表 Collision 也应使用同样复杂的
网格。优先使用能保留关键接触形状的简单 Collision；回退和实际导出策略会写入报告。

![Link 的可视与碰撞设置](/screenshots/link-collision.png)

<p class="caption">碰撞策略、网格格式和精简比例集中在“可视/碰撞”页。</p>

## 外观单独设置

外观页集中显示 URDF 材质 ID、RGBA、选色器和自动配色。材质 ID 由 RGBA 稳定生成，用户通常
只需要关注颜色本身。

![Link 的外观设置](/screenshots/link-appearance.png)

<p class="caption">外观与碰撞分开后，颜色配置不会挤占网格设置空间。</p>

## 惯性不能靠碰撞体替代

Collision 是求解接触的几何，Inertial 是刚体动力学参数，两者用途不同。插件可以显示等效惯性
体帮助检查，但正式结果以质量、质心、惯性张量和导出报告为准。

详细说明：[惯性](/wiki/Inertia)、[碰撞](/wiki/Collision-zh-CN)、[Joint 语义与来源](/architecture/joint-semantics-and-provenance)。
