# SW2URDF Wiki

**简体中文** | [English](Home)

SW2URDF 是社区维护的 SolidWorks 机器人模型导出插件。它把装配体中的 Link、Joint、坐标系、
质量、碰撞和外观整理成 ROS、OpenUSD 或 MuJoCo 可以继续使用的文件。

项目地址：<https://github.com/osrbot/solidworks_urdf_exporter_pro>

## 为什么使用

- 减少手工整理层级、坐标系、网格路径、质量和惯性的重复工作。
- 支持深层组件、同名或中文参考几何，并要求用户确认 Joint 的真实含义。
- 导出前可以检查惯性和预览 Collision，错误信息与报告也更容易定位问题。
- 除 ROS 1 外，还能导出 ROS 2、OpenUSD 和 MuJoCo MJCF。
- Link、Joint、惯性、碰撞和外观分区配置，长路径显示和页面切换更适合复杂装配体。

## 先看哪一页

- 第一次使用：[快速开始](Quick-Start-zh-CN)
- 安装和升级：[安装](Installation-zh-CN)
- Link 层级与保存：[Link 树](Link-Tree-zh-CN)
- 关节类型、坐标系和约束：[Joint 属性](Joint-zh-CN)
- 质量、质心与惯性：[惯性](Inertia-zh-CN)
- 碰撞策略与预览：[碰撞](Collision-zh-CN)
- 颜色与自动配色：[外观](Appearance-zh-CN)
- 输出选择与导出：[模型与导出](Export-zh-CN)
- USD 文件与使用：[OpenUSD](OpenUSD-zh-CN)
- MuJoCo 文件与使用：[MuJoCo MJCF](MJCF-zh-CN)
- 导出失败：[问题排查](Troubleshooting-zh-CN)

## 可以导出什么

| 目标 | 交付目录 | 主要内容 |
| --- | --- | --- |
| ROS 1 | `ROS1/<package>` | URDF、网格、配置和报告 |
| ROS 2 | `ROS2/<package>` | URDF、网格、配置和报告 |
| OpenUSD | `USD/<package>` | `robot.usd`、几何依赖、名称映射和报告 |
| MuJoCo MJCF | `MuJoCo/<robot>` | `robot.xml`、`scene.xml`、网格资产、名称映射和报告 |

四个目标可以独立选择，但至少选择一个。OpenUSD 不要求本机安装 Isaac Sim 或填写版本；
MJCF 输出机器人模型，不生成控制器、任务或强化学习工程。导出后仍要在实际目标软件中检查
坐标、碰撞、惯性和运动方向。

## 提问与贡献

- 问题反馈：<https://github.com/osrbot/solidworks_urdf_exporter_pro/issues>
- 贡献代码与文档：[参与贡献](Contributing-zh-CN)
- 当前维护分支贡献者：[kitso666](https://github.com/kitso666)、
  [W472351926](https://github.com/W472351926)、[dajianli](https://github.com/dajianli)、
  [sunmaxwll](https://github.com/sunmaxwll)
- 完整记录：[贡献者名单](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/CONTRIBUTORS.md)
