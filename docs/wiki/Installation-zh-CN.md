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

导出 OpenUSD 或 MJCF 不要求这台 Windows CAD 工作站安装 Isaac Sim、Isaac Lab 或 MuJoCo。

## 升级

- 升级前关闭 SolidWorks。
- 安装器保留用户选择过的安装目录。
- 很早的社区版配置只按名称识别对象，升级后建议重新创建配置并逐项检查深层组件、坐标系和轴。
- 升级后先用非生产装配体验证 Link Tree、坐标系、惯性和碰撞预览，再处理生产模型。

## 支持范围

| 项目 | 当前事实 |
| --- | --- |
| OS/架构 | Windows x64 |
| 目标框架 | .NET Framework 4.8 |
| 历史最低 SolidWorks | 2018 SP5 |
| 当前 Live API 验证重点 | SolidWorks 2023 |

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
