# 安装

## 安装步骤

1. 从项目的 [GitHub Releases](https://github.com/osrbot/solidworks_urdf_exporter_pro/releases)
   下载已发布的 x64 安装包。
2. 关闭所有 SolidWorks 进程。
3. 以管理员身份运行安装包，并选择 English 或简体中文。
4. 重新启动 SolidWorks。
5. 从 `工具 > Export as URDF` 打开插件。

安装 OpenUSD 或 MuJoCo 输出支持时，不需要另外在这台 Windows 电脑上安装 Isaac Sim 或 MuJoCo。

## 升级

- 升级前完全退出 SolidWorks。
- 建议先用装配体副本测试新版。
- 很早的社区版配置可能无法可靠识别深层组件，请重新建立配置并逐项确认。
- 升级后先检查 Link 树、坐标系、Joint、惯性和碰撞预览，再处理生产模型。

## 当前使用环境

| 项目 | 说明 |
| --- | --- |
| 操作系统 | Windows x64 |
| 运行框架 | .NET Framework 4.8 |
| 当前主要实机测试 | SolidWorks 2023 |
| 社区原版历史最低版本 | SolidWorks 2018 SP5 |

历史最低版本不表示中间每个 SolidWorks 年份和 Service Pack 都经过测试。

## 安装后找不到菜单

1. 确认安装的是 x64 版本。
2. 完全退出并重新启动 SolidWorks。
3. 在 SolidWorks 插件列表中确认 SW2URDF 已启用。
4. 查看 `%USERPROFILE%\sw2urdf_logs\sw2urdf.log`。
5. 仍无法加载时，按[提问说明](/support/help-and-contribute)提交日志和版本信息。
