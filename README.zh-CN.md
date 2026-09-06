# SolidWorks to URDF 导出器

[English](README.md) | **简体中文**

[![许可证：MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![平台](https://img.shields.io/badge/platform-Windows%20x64-blue.svg)](#支持环境)
[![框架](https://img.shields.io/badge/.NET%20Framework-4.8-blueviolet.svg)](#支持环境)

SW2URDF 是 ROS 社区原项目
[`solidworks_urdf_exporter`](https://github.com/ros/solidworks_urdf_exporter) 的持续维护版本。
它在 SolidWorks 中配置 Link、Joint、坐标系、惯性、碰撞和外观，并导出 ROS、OpenUSD 或
MuJoCo 可以继续使用的机器人模型。

> 这是社区维护项目，不是 Dassault Systemes、ROS、NVIDIA 或 MuJoCo 官方发行版。

## 本次更新

- 旧版配置可以在核对组件、坐标系和轴后迁移，原配置保留。
- 支持 SolidWorks 质量属性覆盖和实测质量校准，惯性预览与导出使用同一组结果。
- 修复 Link 多选、修改层级、Joint 重命名及惯性坐标系转换中的数据保留问题。
- 单个输出目标失败时，保留其他成功目标，并分别显示原因和目录。
- 改善显示缩放、页面重绘和 STL 导出状态反馈。大型装配体首次预览仍可能等待较长时间。

详见[更新说明](CHANGELOG.md)和[安装包下载](https://github.com/osrbot/solidworks_urdf_exporter_pro/releases)。

## 为什么使用

手工把 CAD 模型整理成机器人描述文件，需要反复处理层级、坐标系、网格路径、质量、惯性和
Joint，容易出错。SW2URDF 把这些步骤放进一个三步向导，并在输出前检查常见问题。

适合以下工作：

- 从 SolidWorks 维护 ROS 1 或 ROS 2 机器人描述包；
- 把机器人资产送入 Isaac Sim 或其他 USD 工具；
- 把机器人模型送入 MuJoCo，继续配置场景与控制；
- 在导出前检查质量、惯性、碰撞和 Joint 方向。

## 相比社区原版

| 使用问题 | 当前版本的处理 |
| --- | --- |
| 深层组件、同名或中文坐标系容易选错 | 按实际装配分支定位坐标系和轴 |
| STEP 或固定装配难以判断 Joint | 用户明确选择类型，Mate 识别只提供待确认建议 |
| 质量与惯性错误不容易发现 | 增加单位、质心、惯性张量和主惯量检查 |
| 碰撞几何导出前看不到效果 | 增加原语、组件包围盒、凸包、精简网格和预览 |
| 外观和碰撞设置互相挤压 | 独立外观页，支持 RGBA、选色和自动配色 |
| 输出主要停留在传统 URDF | 增加 ROS 2、OpenUSD 和 MuJoCo MJCF |
| 错误弹窗难以复用 | 错误详情可复制，并提供日志和导出报告 |
| 旧界面长名称易截断、切换卡顿 | 重新整理布局，长路径完整显示，减少重复渲染 |

## 可以导出什么

| 目标 | 主要结果 | 适合用途 |
| --- | --- | --- |
| ROS 1 功能包 | URDF、网格、配置和报告 | 已有 ROS 1 项目 |
| ROS 2 功能包 | URDF、网格、配置和报告 | ROS 2 描述、显示和后续控制 |
| OpenUSD | `robot.usd`、几何文件、名称映射和报告 | Isaac Sim 或其他 USD 工具 |
| MuJoCo MJCF | `robot.xml`、`scene.xml`、网格和报告 | MuJoCo 场景与控制开发 |

插件不会根据 CAD 外形猜 PID、控制器、摩擦或任务参数。这些参数需要用户根据真实机器人或经过
验证的仿真模型填写。

## 安装

1. 从 [GitHub Releases](https://github.com/osrbot/solidworks_urdf_exporter_pro/releases) 下载
   已发布的 x64 安装包。
2. 关闭所有 SolidWorks 进程。
3. 以管理员身份运行安装包。
4. 重新启动 SolidWorks。
5. 从 `工具 > Export as URDF` 打开插件。

详细说明见[安装文档](docs/guide/installation.md)。

## 快速使用

1. 保存装配体，并为零件设置材料或密度。
2. 建立 Link 树，确认每个组件属于正确的刚体。
3. 为每个非根 Link 配置 Joint、坐标系、轴和约束。
4. 检查质量、质心和惯性。
5. 选择并预览 Collision，设置外观颜色。
6. 勾选需要的输出格式并导出。
7. 查看 `export_report` 和各目标目录中的检查报告。
8. 在实际 ROS、Isaac Sim、USD Viewer 或 MuJoCo 环境中复核。

完整步骤见[快速开始](docs/guide/getting-started.md)。

## 功能页面

- [Link 树](docs/features/link-tree.md)
- [Joint 属性](docs/features/joint.md)
- [惯性](docs/features/inertia.md)
- [可视与碰撞](docs/features/collision.md)
- [外观](docs/features/appearance.md)
- [模型与导出](docs/features/export-page.md)

输出说明：[ROS](docs/exports/ros.md) · [OpenUSD](docs/exports/openusd.md) ·
[MuJoCo MJCF](docs/exports/mujoco.md)

## 支持环境

- Windows x64
- .NET Framework 4.8
- 当前主要实机测试：SolidWorks 2023
- 社区原版历史最低版本：SolidWorks 2018 SP5

历史最低版本不表示每个中间版本和 Service Pack 都经过测试。生产使用前请用自己的装配体完成
验收。

## 提问

项目地址：<https://github.com/osrbot/solidworks_urdf_exporter_pro>

请在 [GitHub Issues](https://github.com/osrbot/solidworks_urdf_exporter_pro/issues) 提交问题，并
提供 SolidWorks 版本、插件版本、复现步骤、完整错误文字、日志和导出报告。具体格式见
[提问与贡献代码](docs/support/help-and-contribute.md)。

## 贡献代码

欢迎提交可复现问题、测试、文档和代码修复。Pull Request 应说明解决的问题、实现方式、测试结果
以及仍未验证的范围。开发环境和测试命令见
[贡献说明](docs/wiki/Contributing-zh-CN.md)。

当前维护分支贡献者按项目展示顺序为 [kitso666](https://github.com/kitso666)、
[W472351926](https://github.com/W472351926)、[dajianli](https://github.com/dajianli) 和
[sunmaxwll](https://github.com/sunmaxwll)。完整说明见[贡献者名单](CONTRIBUTORS.md)。

## 文档

在线文档源码位于 [`docs/`](docs/index.md)。本地预览：

```powershell
pnpm install --frozen-lockfile
pnpm docs:dev
```

## Star 趋势

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/osrbot/solidworks_urdf_exporter_pro/master/assets/star-history-dark.svg">
  <img alt="SW2URDF GitHub Star 数量变化趋势" src="https://raw.githubusercontent.com/osrbot/solidworks_urdf_exporter_pro/master/assets/star-history.svg" width="720">
</picture>

## 许可证与致谢

项目按 [MIT License](LICENSE) 发布，并保留上游项目历史、作者和贡献记录。感谢原项目作者
Stephen Brawner，以及 PickNik Consulting、Verb Surgical、Open Robotics、Willow Garage 和
所有当前及历史社区贡献者。
