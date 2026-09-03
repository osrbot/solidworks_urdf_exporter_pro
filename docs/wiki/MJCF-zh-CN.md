# MuJoCo MJCF 模型

**简体中文** | [English](MJCF)

## 用途

MJCF 目标根据已校验的 CAD 机器人数据生成独立的 MuJoCo 机器人模型。它的第一职责是可载入的
机器人资产，不是控制器栈、任务环境或强化学习工程。

导出验证不要求用户另行安装 MuJoCo。安装包固定使用 MuJoCo `3.12.0` 官方工具执行验证流程，
实际版本也会写入 `export_report.json`。

## 交付文件

```text
MuJoCo/<robot>/
|-- robot.xml
|-- scene.xml                   # 引用 robot.xml 的最小入口
|-- assets/visual/
|-- assets/collision/
|-- name_map.json
`-- export_report.json
```

模型保留 Link 层级、Visual/Collision 分离，以及来自 CAD 的质量、COM 和完整惯性张量。Joint
映射是显式的：fixed 不生成可动 MJCF Joint；continuous/revolute 映射为 hinge；prismatic 映射
为 slide；floating 映射为三个正交 `slide` Joint 加一个 `ball` Joint。planar 会以可操作错误
终止导出，不会静默近似为另一种机构。这些是本导出器的映射策略，不表示 MJCF 格式本身缺少
其他建模方式。

## 自动化验证

只有固定的 MuJoCo 官方工具对 `robot.xml` 与 `scene.xml` 都完成以下步骤，导出才成功：

1. 将 MJCF 编译为 MJB；
2. 保存规范 MJCF 表示；
3. 重新载入规范结果；
4. 推进一步零控制仿真；
5. 在 `export_report.json` 记录 MuJoCo 版本和结果。

公共导出 API 采用 fail closed：validator 缺失、未返回结果、成功证据不完整或任一步失败时，
已发布目录保持不变。

这证明官方解析器与一步基础运行兼容，不证明物理保真度、接触调参、控制器质量、长时间稳定性、
渲染保真度、性能或任务行为。

## 有意不生成

- actuator、transmission、控制器、PID 增益或控制策略；
- 传感器、关键帧、显式接触对、摩擦/求解器调参或仿真步长调参；
- 世界几何、地面、灯光、相机、任务环境、奖励、观测、重置或域随机化；
- 强化学习训练代码或任务定义。

`scene.xml` 只是引用 `robot.xml` 的最小入口，不是完整仿真场景。

## 证据术语

- **已生成**：约定的 MJCF 文件与资产均已写出。
- **已通过 MuJoCo 官方验证**：两个 XML 入口均使用报告中的固定运行时完成上述
  编译/保存/重载/一步流程。
- **已通过任务验证**：本插件不执行；用户必须结合实际接触、actuator、控制器、步长和工作负载
  进行测试。
