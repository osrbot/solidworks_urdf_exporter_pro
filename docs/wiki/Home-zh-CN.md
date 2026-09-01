# SolidWorks to URDF Exporter Wiki

**简体中文** | [English](Home)

这是 OSRBot 维护分支的详细用户与维护文档。项目入口、支持范围和致谢见
[README](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/README.md)。

## 为什么需要这个维护分支

上游导出器提供了原始插件和 URDF 导出管线。本维护分支的目的不是重新打包历史二进制文件，
而是补齐生产使用中长期存在的工程缺口：

- Link 树现在具备事务化编辑、严格的 v2 PID 配置持久化、恢复草稿，以及覆盖预览和重开流程的
  严格校验。组件实例 PID 与特征 PID 可唯一识别深层组件中 Unicode 或同名的参考几何。
- STEP 和固定装配使用可审核的手工 Joint 工作流；Mate 识别必须显式触发，0 个剩余 DOF 不会被
  静默写成 `fixed`。
- 质量、COM 和惯性统一使用显式单位及零件/装配体坐标转换路线，并执行边界、物理张量和 API
  主惯量校验。
- 碰撞策略按 Link 局部拟合、在 SolidWorks 中预览，并记录所有回退，从而区分请求策略和实际
  导出结果。
- 维护流程增加确定性 Link 自动配色、简体中文 UI、导出进度、校验报告和可复现的 Draft-only
  安装包发布管线。
- OpenUSD 与 MJCF 是具有固定自动化检查的具体资产输出，不包装成控制器、任务或强化学习工程。

本分支保留上游历史、作者署名和 MIT 许可证。按日期整理的变更与提交证据见
[Changelog](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/CHANGELOG.md)。

## 项目定位

该插件运行在 Windows x64 SolidWorks 中，将明确配置的 Link、Joint、坐标系、质量属性、Visual
和 Collision 导出为四种具体目标：ROS 1 功能包、ROS 2 功能包、OpenUSD 机器人资产和 MuJoCo
MJCF 模型。

它坚持三个边界：

- `visual` 服务渲染和识别，目标是外观与几何可辨识。
- `collision` 服务接触求解，目标是尽量简单且保留任务相关接触形状。
- `inertial` 服务动力学，目标是尽可能真实地保存质量、质心和惯性张量。

Collision 策略不会重算或替换 Inertial。SolidWorks 中的临时碰撞体和等效惯性体用于检查，
正式导出结果以 URDF、`mesh_manifest.csv` 和 `inertial_validation.csv` 为准。

`Robot Bundle` 只是私有规范暂存表示。插件在系统临时目录中创建它，供所选目标导出器使用，
随后清理；它不是用户可选目标，也不会作为目录树交付。

## 文档导航

- [Installation](Installation)：安装、升级与版本边界
- [Quick Start](Quick-Start-zh-CN)：从 SolidWorks 装配体到四种导出目标
- [Link Tree](Link-Tree)：层级、事务编辑、持久化与恢复
- [Inertia](Inertia)：坐标系、单位、符号和物理校验
- [Collision](Collision)：碰撞策略、预览和回退
- [OpenUSD](OpenUSD-zh-CN)：USD 交付文件与验证边界
- [MuJoCo MJCF](MJCF-zh-CN)：MJCF 交付文件与官方运行时验证边界
- [Troubleshooting](Troubleshooting)：按症状排查
- [Contributing](Contributing)：开发、测试与问题报告
- [Release Process](Release-Process)：可追溯安装包与人工发布门禁

## 输出结果

| 目标 | 交付目录 | 主要内容 |
| --- | --- | --- |
| ROS 1 功能包 | `ROS1/<package>` | URDF、Visual/Collision 网格、配置、Markdown/CSV 报告 |
| ROS 2 功能包 | `ROS2/<package>` | URDF、Visual/Collision 网格、配置、Markdown/CSV 报告 |
| OpenUSD 资产 | `USD/<package>` | `robot.usd`、几何依赖、源网格证据、`name_map.json`、`export_report.json` |
| MuJoCo MJCF | `MuJoCo/<robot>` | `robot.xml`、`scene.xml`、Visual/Collision 资产、`name_map.json`、`export_report.json` |

四个目标可独立选择，但至少选择一个。USD 与 MJCF 要求 STL 几何。主流程不再要求 Isaac 版本、
Isaac Lab profile、actuator profile 或 Bundle 目标目录。

每次导出只原子替换本次选中的目标目录。未选目标的既有目录会保留，可能是较早一次导出的结果；
请以顶层 `export_report.md` 记录的本次生成和验证目标为准。

## 支持与证据边界

- **生成能力**：导出器从同一份已校验规范模型写出约定的目标文件。
- **自动化验证**：USD 使用固定的内置 OpenUSD 运行时生成并重新打开；两个 MJCF 入口使用固定
  的 MuJoCo 官方工具完成编译、规范保存、重载和一步零控制推进。
- **实际应用运行验证**：USD 结果不声称已运行 Isaac Sim/Isaac Lab；ROS 结果不声称已在
  ROS/Gazebo 中启动；MJCF 结果不声称控制器质量、接触调参、长时间稳定性、任务行为、性能或
  强化学习已经验证。
- 历史最低要求为 SolidWorks 2018 SP5。
- 当前维护和 Live API 验证重点是 SolidWorks 2023。
- Live 测试覆盖 SolidWorks 2023 不代表每个版本和 Service Pack 都已验证。
- 深层参考几何和临时预览变更仍须维护者完成 Live SolidWorks 测试后才能公开发布。
- 软件依据 MIT License 按原样提供；进入生产仿真前必须在目标求解器中复核。

## 致谢

- 原项目：[ros/solidworks_urdf_exporter](https://github.com/ros/solidworks_urdf_exporter)
- 原作者及历史维护者：Stephen Brawner
- 原 README 记录的历史支持者：PickNik Consulting、Verb Surgical、Open Robotics、Willow Garage
- 3DXML 贡献：Kento Matsuo 及提交 `22cb778` 记录的贡献者
- 当前维护者：`kitso666 <kitso@osrbot.com>`
- 维护者提供的社区惯性参考：
  [SolidWorks 到 URDF 惯性文章](https://zhuanlan.zhihu.com/p/1887859297221845818)

该文章作为背景阅读资料致谢；插件实际使用的 API 路径、代码、测试与导出报告才是当前实现行为
的事实来源。
