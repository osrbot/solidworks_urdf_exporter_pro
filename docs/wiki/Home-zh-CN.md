# SolidWorks to URDF Exporter Wiki

**简体中文** | [English](Home)

这是 OSRBot 维护分支的详细用户与维护文档。项目入口、支持范围和致谢见
[README](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/README.md)。

## 为什么需要这个维护分支

上游导出器提供了原始插件和 URDF 导出管线。本维护分支的目的不是重新打包历史二进制文件，
而是补齐生产使用中长期存在的工程缺口：

- Link 树现在具备事务化编辑、v1.5 配置持久化、恢复草稿，以及覆盖预览和重开流程的严格校验。
- 质量、COM 和惯性统一使用显式单位及零件/装配体坐标转换路线，并执行边界、物理张量和 API
  主惯量校验。
- 碰撞策略按 Link 局部拟合、在 SolidWorks 中预览，并记录所有回退，从而区分请求策略和实际
  导出结果。
- 维护流程增加确定性 Link 自动配色、简体中文 UI、导出进度、校验报告和可复现的 Draft-only
  安装包发布管线。

本分支保留上游历史、作者署名和 MIT 许可证。按日期整理的变更与提交证据见
[Changelog](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/CHANGELOG.md)。

## 项目定位

该插件运行在 Windows x64 SolidWorks 中，将装配体中的 Link、Joint、坐标系、质量属性、
Visual 和 Collision 配置导出为 URDF，并生成对应的 ROS1/ROS2 描述包。

它坚持三个边界：

- `visual` 服务渲染和识别，目标是外观与几何可辨识。
- `collision` 服务接触求解，目标是尽量简单且保留任务相关接触形状。
- `inertial` 服务动力学，目标是尽可能真实地保存质量、质心和惯性张量。

Collision 策略不会重算或替换 Inertial。SolidWorks 中的临时碰撞体和等效惯性体用于检查，
正式导出结果以 URDF、`mesh_manifest.csv` 和 `inertial_validation.csv` 为准。

## 文档导航

- [Installation](Installation)：安装、升级与版本边界
- [Quick Start](Quick-Start)：从 SolidWorks 装配体到 ROS 包
- [Link Tree](Link-Tree)：层级、事务编辑、持久化与恢复
- [Inertia](Inertia)：坐标系、单位、符号和物理校验
- [Collision](Collision)：碰撞策略、预览和回退
- [Troubleshooting](Troubleshooting)：按症状排查
- [Contributing](Contributing)：开发、测试与问题报告
- [Release Process](Release-Process)：可追溯安装包与人工发布门禁

## 输出结果

一次完整导出生成 `ROS1/<package>` 和 `ROS2/<package>`，并包含：

- `urdf/`：URDF 模型；
- `meshes/`：Visual/Collision 网格；
- `config/export_report.md`：导出健康摘要与回退信息；
- `config/inertial_validation.csv`：逐 Link 惯性校验；
- `config/mesh_manifest.csv`：逐 Link 网格和碰撞策略记录。

## 支持与证据边界

- 历史最低要求为 SolidWorks 2018 SP5。
- 当前维护和 Live API 验证重点是 SolidWorks 2023。
- Live 测试覆盖 SolidWorks 2023 不代表每个版本和 Service Pack 都已验证。
- 软件依据 MIT License 按原样提供；进入生产仿真前必须在目标求解器中复核。

## 致谢

- 原项目：[ros/solidworks_urdf_exporter](https://github.com/ros/solidworks_urdf_exporter)
- 原作者及历史维护者：Stephen Brawner
- 原 README 记录的历史支持者：PickNik Consulting、Verb Surgical、Open Robotics、Willow Garage
- 3DXML 贡献：Kento Matsuo 及提交 `22cb778` 记录的贡献者
- 当前维护者：`kitso666 <kitso@osrbot.com>`
- 惯性理论参考：Winter，
  [掌握 URDF 中的惯性张量：从 SolidWorks 到强化学习机器人的关键一步](https://zhuanlan.zhihu.com/p/1887859297221845818)

参考文章帮助解释以质心为原点的张量、输出坐标系和惯性积记号陷阱；插件实际使用的 API 路径、
代码、测试与导出报告才是当前实现行为的事实来源。
