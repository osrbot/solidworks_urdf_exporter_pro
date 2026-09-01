# Installation

**简体中文** | [English](Installation)

## 普通用户安装

1. 从维护分支的 [GitHub Releases](https://github.com/osrbot/solidworks_urdf_exporter_pro/releases)
   下载已公开的 `sw2urdfSetup_YYYYMMDD_<commit>.exe`。
2. 如果同时提供 `.sha256`，先校验安装包 SHA-256。
3. **关闭所有 SolidWorks 进程。** 当前安装器不会主动结束 SolidWorks，也不会把新 DLL 热加载
   到已运行的进程。
4. 以管理员身份运行 x64 安装器，选择 English 或简体中文。
5. 启动 SolidWorks，从 `Tools > Export as URDF` 打开导出器。

安装包同时包含 USD/MJCF 目标使用的固定 OpenUSD 运行时和 MuJoCo 官方验证工具。Windows CAD
工作站无需安装 Isaac Sim、Isaac Lab 或独立 MuJoCo。这些内置工具只校验资产结构，不会安装或
运行下游应用。

安装器默认安装到 64 位 Program Files 下的
`SolidWorks Corp\SolidWorks\URDFExporter`，通过 64 位 .NET `RegAsm.exe /codebase` 注册插件。
卸载器只在当前安装目录仍拥有对应 COM `CodeBase` 时执行注销，避免旧安装器破坏较新的安装。

## 升级

- 升级前关闭 SolidWorks。
- 安装器保留用户选择过的安装目录。
- 当前配置使用 `URDF Export Configuration (v2)`，以组件实例 PID 和特征 PID 绑定 CAD 对象。
  v1.x 名称型配置不会迁移；请删除旧配置特征、重新创建配置并逐项审核 CAD 绑定。
- 升级后先用非生产装配体验证 Link Tree、坐标系、惯性和碰撞预览，再处理生产模型。

## 支持范围

| 项目 | 当前事实 |
| --- | --- |
| OS/架构 | Windows x64 |
| 目标框架 | .NET Framework 4.8 |
| 历史最低 SolidWorks | 2018 SP5 |
| 当前 Live API 验证重点 | SolidWorks 2023 |

生成的 USD 会使用内置 OpenUSD 运行时自动重开；生成的 MJCF 会使用内置 MuJoCo 官方工具自动
完成编译、规范保存、重载和一步零控制推进。这些检查与 Live SolidWorks API 覆盖相互独立，
不能证明资产已在 Isaac、ROS 或任务化 MuJoCo 工程中运行。

不能据此推断所有 SolidWorks 版本均受支持。SolidWorks 2017 或更早版本只保留上游项目的
“可能可用”说明，不属于维护验证承诺。

## 安装后菜单不存在

1. 确认使用的是 x64 安装包并以管理员身份安装。
2. 完全退出并重启 SolidWorks。
3. 在 SolidWorks Add-Ins 中确认 SW2URDF 是否存在并已启用。
4. 检查 `%USERPROFILE%\sw2urdf_logs\sw2urdf.log`。
5. 若手工构建，确认使用了当前 SolidWorks 安装目录中的 API 程序集。

## 历史上游版本

需要原版行为或旧 SolidWorks 安装包时，使用
[ros/solidworks_urdf_exporter Releases](https://github.com/ros/solidworks_urdf_exporter/releases)，
不要把维护分支的功能说明套用到旧版二进制。
