# OpenUSD 机器人资产

**简体中文** | [English](OpenUSD)

## 用途

OpenUSD 目标使用与 ROS 导出器相同的已校验 Link、Joint、Visual、Collision 和 Inertial 数据，
生成可移植机器人资产。它是资产格式目标，不是 Isaac Sim 或 Isaac Lab 工程生成器。

用户不需要在本机安装 Isaac，插件也不会检测 Isaac。安装包提供固定版本的 OpenUSD 运行时，
只用于生成和结构校验。

## 交付文件

```text
USD/<package>/
|-- robot.usd
|-- geometry/                 # 由 STL 转换的 USD 网格依赖
|-- meshes/                   # 保留的规范源网格证据
|-- name_map.json             # 源名称到合法 USD 标识符的映射
`-- export_report.json        # 数量、运行时版本、检查结果和证据边界
```

`robot.usd` 包含机器人层级、Visual/Collision 形状、physics Joint、质量、质心和惯性。fixed、
revolute/continuous、prismatic 使用对应的核心 USD Physics schema。planar 使用通用 USD Physics
Joint，并按固定版本 OpenUSD schema 的规定，对 `transZ`、`rotX` 和 `rotY` 应用 `low > high` 的
`LimitAPI` 来锁定这些轴，只保留平面内 `transX`/`transY` 平移与 `rotZ` 旋转；其局部 Z 轴与源平面
法向对齐。若这些约束无法写入并重开验证，适配器会拒绝导出。floating 仍使用通用 USD Physics
Joint，并在报告中标记为非精确映射。

USD 与 MJCF 目标要求 STL 网格输入。适配器会拒绝 3DXML，而不是静默丢失几何。

## 可选仿真意图

主向导仍只显示一个 OpenUSD 勾选项。旁边的**设置...**按钮按需打开缓存对话框，不增加新的
向导页面，切换页面时也不会执行 USD 或文件系统工作。保守默认值为：**保持源语义**、机器人类型
**默认**、关闭自碰撞、所有受支持的单自由度 Joint 均为被动。

| 设置 | USD 结果 |
| --- | --- |
| 保持源语义 | 保留源模型根部行为，不注入 world Joint |
| 固定基座 | 自动增加 world 到源根 Link 的 fixed Joint |
| 浮动基座 | 显式记录移动基座意图，不注入 world Joint |
| 机器人类型 | 写入对应的官方 `isaac:robotType` token；它只做分类，不改写运动学 |
| 允许自碰撞 | 写入 `physxArticulation:enabledSelfCollisions`；默认关闭 |
| 被动 Joint | 不写入主动 `DriveAPI` |
| 位置或速度 Joint | 根据转动/直线运动写入对应 `DriveAPI`；只有用户显式填写时才使用非负刚度/阻尼 |
| effort Joint | 记录 `osurdf:driveIntent=effort` 供下游运行时配置，刻意不创建主动 `DriveAPI` |

对话框中的 Joint 力矩/力限值和速度限值来自已校验的 CAD/URDF 模型，只读显示。插件不会根据
几何猜测控制器增益，也不要求每个可动 Joint 都必须配置主动驱动。

## Stage 合同

`/Robot` 是默认 Prim 与 articulation root。机器人、Link、Joint 分别应用 `IsaacRobotAPI`、
`IsaacLinkAPI`、`IsaacJointAPI`，同时保留标准 USD Physics schema 作为可执行物理合同。根 Link
在 `isaac:physics:robotLinks` 中排第一。Collision Prim 使用 `guide` purpose，网格 Collision
使用 `convexHull` 近似。STL 转换后的几何以相对 USD 依赖保存，因此必须整体移动完整输出目录，
不能只拿走 `robot.usd`。

## 自动化验证

只有固定的内置 OpenUSD 运行时完成以下步骤，导出才成功：

1. 创建 stage 和几何依赖；
2. 重新打开 `robot.usd`；
3. 检查预期 Link、Joint 和刚体数量、本地资产解析，验证每个 planar Joint 的三个锁定与三个
   自由 DOF，并记录质量属性、Collision 数量、基座解析结果、Joint 意图、主动 DriveAPI 和组合后
   的网格拓扑；
4. 在 `export_report.json` 记录 OpenUSD 版本和校验结果。

这证明生成能力和 OpenUSD 结构可读性，但**不证明**已在 Isaac Sim 或 Isaac Lab 中完成导入、
渲染、物理运行或扩展兼容性验证。

## 下游使用

将完整 `USD/<package>` 目录复制到目标机器，再按下游应用的正常资产流程导入 `robot.usd`。
用户应在实际应用中检查 articulation 映射、碰撞/接触行为、单位、材质、控制器设置和任务行为。

插件有意不生成 Isaac 版本、扩展配置、actuator group、控制器/PID 文件、推测增益、传感器、
环境、奖励、观测、重置逻辑或强化学习代码；只会写入用户显式填写的 USD drive 刚度/阻尼。

## 证据术语

- **已生成**：约定资产文件均已写出。
- **已通过 OpenUSD 验证**：内置运行时已重开并检查 stage 结构。
- **已通过 Isaac 验证**：本插件不执行；只有用户在实际 Isaac 环境中的独立测试才能支持该结论。
