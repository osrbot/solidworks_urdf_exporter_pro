# MuJoCo MJCF

## 为什么使用

MJCF 输出适合把 CAD 机器人带入 MuJoCo，再继续添加场景、执行器、控制器和任务。插件负责生成
可载入的机器人模型，不生成强化学习工程。

## 输出目录

```text
MuJoCo/<robot>/
|-- robot.xml
|-- scene.xml
|-- assets/visual/
|-- assets/collision/
|-- name_map.json
`-- export_report.json
```

`robot.xml` 是机器人主体，`scene.xml` 是引用它的最小场景入口。

## Joint 转换

| SolidWorks/URDF Joint | MJCF 结果 |
| --- | --- |
| fixed | 不生成可动 Joint |
| revolute / continuous | `hinge` |
| prismatic | `slide` |
| floating | 三个 `slide` 加一个 `ball` |
| planar | 当前会提示用户处理，不做静默近似 |

## 仿真设置与执行器

公共仿真设置选择基座和单自由度关节模式，MuJoCo 独立标签页保存 MJCF 参数；不会借用 USD
刚度或阻尼。公共非 `source` 基座和显式关节模式优先，`source` 保留目标原有基座行为。
默认不生成 actuator，被动关节也不生成 actuator。

- 位置模式生成 `position`，使用 `kp` 和 `kv`。
- 速度模式生成 `velocity`，使用 `kv`。
- 力模式生成 `motor`，`gear="1"`，而 USD 的力模式仅保存运行时意图。

所需增益必须是有限、非负数；最大力必须为有限正数。未指定最大力时使用关节的有效 effort
限值；显式非法值不能通过回退掩盖。缺少所需参数时应修正配置，不会从 CAD 自动估算。
`scene.xml` 只是最小加载入口，不是训练世界；不生成奖励函数、策略或训练工程。

## 运行验证

插件会使用随安装包提供的 MuJoCo 工具检查两个 XML 入口能否载入并完成最小运行步骤。用户仍需
在自己的工程中验证所选 actuator，并添加和验证控制器、摩擦、接触、仿真步长和任务参数。
