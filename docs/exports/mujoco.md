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

## 导出后检查

插件会使用随安装包提供的 MuJoCo 工具检查两个 XML 入口能否载入并完成最小运行步骤。用户仍需
在自己的工程中添加和验证 actuator、控制器、摩擦、接触、仿真步长和任务参数。
