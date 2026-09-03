# MuJoCo MJCF 模型

**简体中文** | [English](MJCF)

## 为什么使用

MJCF 输出用于把 SolidWorks 机器人带入 MuJoCo，继续配置场景、执行器和控制。它生成可载入的
机器人模型，不生成控制器、任务环境或强化学习工程。导出时不要求用户另行安装 MuJoCo。

## 会得到什么

```text
MuJoCo/<robot>/
|-- robot.xml
|-- scene.xml
|-- assets/visual/
|-- assets/collision/
|-- name_map.json
`-- export_report.json
```

`robot.xml` 是机器人主体，`scene.xml` 是引用它的最小场景入口。模型保留 Link 层级、显示与
碰撞几何，以及来自 CAD 的质量、质心和惯性。

## Joint 如何转换

| 插件中的 Joint | MJCF 结果 |
| --- | --- |
| fixed | 不生成可动 Joint |
| revolute / continuous | `hinge` |
| prismatic | `slide` |
| floating | 三个 `slide` 加一个 `ball` |
| planar | 提示用户处理，不做静默近似 |

## 导出后做什么

1. 从 `scene.xml` 打开最小场景，确认模型可以载入。
2. 检查关节轴、范围、惯性和碰撞。
3. 根据实际项目添加 actuator、控制器、摩擦、接触参数、传感器和场景。

`scene.xml` 只是便于载入机器人模型的入口，不是完整仿真或强化学习工程。
