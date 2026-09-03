# MuJoCo MJCF 模型

## 第一职责

MJCF 输出提供一个能由固定 MuJoCo 官方工具载入的机器人模型。它不生成完整世界、控制器、
传感器、奖励函数或强化学习工程。

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

`scene.xml` 只是引用 `robot.xml` 的最小入口，方便验证和继续搭建场景。

## Joint 映射

| 源 Joint | 本导出器的 MJCF 写法 |
| --- | --- |
| fixed | 不生成可动 Joint |
| revolute / continuous | `hinge` |
| prismatic | `slide` |
| floating | 三个正交 `slide` 加一个 `ball` |
| planar | 明确拒绝并提示用户处理 |

这里描述的是 SW2URDF 的映射策略，不表示 MuJoCo 格式本身只能这样建模。

## 自动检查到哪里

安装包固定使用 MuJoCo `3.12.0`，对 `robot.xml` 和 `scene.xml` 执行编译、规范保存、重载和一步
零控制推进。控制器、接触参数、长时间稳定性和任务效果仍需在用户工程中验证。

更多细节见 [MJCF Wiki](/wiki/MJCF-zh-CN)。
