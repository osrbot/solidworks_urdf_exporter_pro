# SW2URDF Wiki

**简体中文** | [English](Home)

SW2URDF 是社区维护的 SolidWorks 机器人模型导出插件。它把用户确认过的 Link、Joint、坐标系、
质量属性、Visual 和 Collision 整理成四种明确交付物：ROS 1 功能包、ROS 2 功能包、OpenUSD
机器人资产和 MuJoCo MJCF 模型。

项目地址：<https://github.com/osrbot/solidworks_urdf_exporter_pro>

## 先看哪一页

- 第一次使用：[快速开始](Quick-Start-zh-CN)
- 安装和升级：[安装](Installation-zh-CN)
- Link 层级与保存：[Link 树](Link-Tree-zh-CN)
- 质量、质心与惯性：[惯性](Inertia-zh-CN)
- 碰撞策略与预览：[碰撞](Collision-zh-CN)
- USD 文件与验证范围：[OpenUSD](OpenUSD-zh-CN)
- MuJoCo 文件与验证范围：[MuJoCo MJCF](MJCF-zh-CN)
- 导出失败：[问题排查](Troubleshooting-zh-CN)

## 输出结果

| 目标 | 交付目录 | 主要内容 |
| --- | --- | --- |
| ROS 1 | `ROS1/<package>` | URDF、网格、配置和报告 |
| ROS 2 | `ROS2/<package>` | URDF、网格、配置和报告 |
| OpenUSD | `USD/<package>` | `robot.usd`、几何依赖、名称映射和报告 |
| MuJoCo MJCF | `MuJoCo/<robot>` | `robot.xml`、`scene.xml`、网格资产、名称映射和报告 |

四个目标可以独立选择，但至少选择一个。Robot Bundle 是系统临时目录中的内部暂存，不是第五个
目标，也不会交付给用户。OpenUSD 不要求本机安装 Isaac Sim/Isaac Lab 或填写其版本；MJCF 不
生成 actuator、控制器、任务或强化学习工程。

## 三类数据不要混用

- `visual` 负责显示，重点是外观和可辨识几何。
- `collision` 负责接触求解，重点是简单并保留关键接触形状。
- `inertial` 负责动力学，保存质量、质心和惯性张量。

更换 Collision 策略不会重算 Inertial。SolidWorks 临时预览用于检查，正式结果以导出文件和报告
为准。

## 版本怎么理解

- 插件产品版本用于安装包和 DLL。
- `URDF Export Configuration (v2)` 是保存在 SolidWorks 装配体中的 PID 配置。
- `robot.schema.v3` 是导出过程中使用的临时规范文档。
- `usd-core 26.8` 与 MuJoCo `3.12.0` 是当前固定验证工具版本。

这些版本服务不同职责。小的 UI 或文档更新不应推动 schema 主版本变化；只有不兼容的数据合同
变化才需要增加 schema 主版本。

## 验证边界

- OpenUSD 会使用固定运行时生成并重新打开 stage。
- MuJoCo 会使用固定官方工具编译、保存、重载并推进一步零控制。
- ROS 2 最小夹具可通过手动触发的集成门禁，在指定 ROS 2/Gazebo 环境构建、启动并激活控制器。
- ROS 1 当前主要覆盖结构和生成测试。
- 上述检查都不代替用户模型在实际控制器、接触参数和任务中的工程验收。

当前维护和 Live API 验证重点是 SolidWorks 2023；继承自上游的历史最低要求是 SolidWorks 2018
SP5，这不表示每个版本和 Service Pack 都已回归。完整证据见
[兼容性矩阵](../development/compatibility-matrix)。
