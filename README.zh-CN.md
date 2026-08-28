# SolidWorks to URDF 导出器

[English](README.md) | **简体中文**

[![许可证：MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![平台](https://img.shields.io/badge/platform-Windows%20x64-blue.svg)](#支持环境)
[![框架](https://img.shields.io/badge/.NET%20Framework-4.5.2-blueviolet.svg)](#开发)

本仓库是 ROS 原项目
[`solidworks_urdf_exporter`](https://github.com/ros/solidworks_urdf_exporter) 的 OSRBot
持续维护分支。它保留原有 SolidWorks 插件工作流，并维护 Link 树编辑、坐标系感知质量属性、
碰撞策略与预览、ROS1/ROS2 包导出、校验报告、简体中文 UI 和可审计安装包构建。

> **项目状态**
>
> 这是社区维护分支，不是 Dassault Systemes 或 ROS 官方发行版。当前维护和 Live API 验证重点
> 是 SolidWorks 2023。继承自上游的历史最低要求是 SolidWorks 2018 SP5；这不表示每个版本和
> Service Pack 都已完成回归测试。

## 工程边界

导出器明确分离 URDF 的三类责任：

| URDF 数据 | 工程目标 | 导出器行为 |
| --- | --- | --- |
| `visual` | 保留可识别外观和几何 | 导出 STL 或 3DXML Visual，并写入 URDF material ID/RGBA |
| `collision` | 在保留任务相关接触形状的前提下简化求解几何 | 支持网格、原语、组件包围盒和凸包，并记录请求/实际策略 |
| `inertial` | 保留质量、质心和惯性张量 | 使用系统单位读取 SolidWorks 质量属性，并转换到所选 Link 坐标系 |

碰撞策略不改变质量属性来源。碰撞预览用于工程判断，不证明最终仿真行为正确；实际导出内容以
生成的 URDF 与报告为准。

## 主要功能

- 同一次事务生成匹配的 `ROS1/<package>` 与 `ROS2/<package>` 描述包。
- 在装配体特征 `URDF Export Configuration (v1.5)` 中保存 Link/Joint 配置，并在正式保存时
  迁移可读的旧配置。
- 提供事务化 Link 树画布：添加、重命名、重设父级、自动布局、框选以及分支复制/粘贴/删除。
- 提供 Markdown 风格 Outline 编辑，使用 `#`、`##`、`###` 表示层级。
- 对具有稳定保存路径的装配体，从 `%LOCALAPPDATA%\OSRBot\SW2URDF\export-drafts` 恢复未正式
  保存的会话。
- 使用每 Link 的 SolidWorks 坐标系统一处理质量、COM、Joint origin 和惯性转换。
- 导出前校验有限数值、张量对称性、主惯量和刚体物理约束。
- 支持 `VisualMesh`、`SimplifiedMesh`、`AccurateMesh`、盒/圆柱/球原语、
  `ComponentBoxes` 与 `ConvexHull`。
- 为所有用户可选碰撞策略显示临时 SolidWorks 几何，并独立显示 COM/等效惯性体。
- 记录碰撞回退，避免把请求策略错误标记为成功策略。
- 用户没有显式覆盖时，读取 SolidWorks 组件/文档外观。
- 材质名称作为 URDF material ID；内置 ID 联动 RGBA，用户仍可逐 Link 手工修改 RGBA。
- 支持整树自动配色：层级从冷色过渡到暖色，规范化后的左右对应 Link 使用同一稳定颜色。
- 导出进度窗口保持置顶且防止重入，完成摘要显示变化文件数、总大小、耗时和输出目录。
- 维护流程提供简体中文 UI，同时配置和 URDF 输出保留规范英文 Joint 类型和值。

详细行为和限制见[项目 Wiki](https://github.com/osrbot/solidworks_urdf_exporter_pro/wiki)。

## 支持环境

| 项目 | 支持或验证状态 |
| --- | --- |
| 操作系统 | Windows x64 |
| 目标框架 | .NET Framework 4.5.2 |
| 历史最低 SolidWorks 版本 | SolidWorks 2018 SP5 |
| 当前 Live API 验证重点 | SolidWorks 2023 |
| Release 构建 | `Release|x64` |
| 安装器语言 | English、简体中文 |

按照上游说明，SolidWorks 2017 或更早版本可能可用，但不属于当前维护验证目标。参见上游讨论
[`ros/solidworks_urdf_exporter#73`](https://github.com/ros/solidworks_urdf_exporter/issues/73)。

## 安装

1. 从维护分支的 [GitHub Releases](https://github.com/osrbot/solidworks_urdf_exporter_pro/releases)
   下载已公开安装包。维护者构建命名为 `sw2urdfSetup_YYYYMMDD_<commit>.exe`。
2. 提供 `.sha256` 时先校验安装包。
3. 安装或升级前关闭 SolidWorks。当前安装器负责注册插件，但不会结束 SolidWorks 进程，也不会
   将新 DLL 热加载到运行中的进程。
4. 以管理员身份运行 x64 安装器，选择 English 或简体中文。
5. 重启 SolidWorks，使用 `Tools > Export as URDF`。

公开发布流程使用人工门禁：CI 验证已提交的维护者构建并创建 Draft Candidate；只有完成 Live
SolidWorks 手动测试并得到维护者明确发布许可后才能公开。

历史上游安装包见
[`ros/solidworks_urdf_exporter` Releases](https://github.com/ros/solidworks_urdf_exporter/releases)。

## 快速开始

1. 使用已保存的装配体副本。解析轻量化组件，设置有效材料密度，重建、保存，并核对
   SolidWorks Mass Properties。
2. 按统一右手系约定创建 `Origin_global`、每个 Joint 所需坐标系和运动轴。
3. 打开 `Tools > Export as URDF` 建立 Link 树。首次教程使用同一真实流程，并可从
   `Tools > URDF Export Tutorial` 再次打开。
4. 配置 Joint 名称、规范类型、父子关系、origin、axis、limit、dynamics 和可选 Mimic。
5. 为每个 Link 选择明确的 Link 坐标系，检查质量、COM、惯性值和等效惯性预览。
6. 选择 Visual 格式、Collision 策略、material ID/RGBA 和 STL 精简比例。使用 SolidWorks
   碰撞预览检查覆盖关系，以导出 manifest 作为最终策略记录。
7. 导出 ROS1/ROS2 包。进度窗口保持在 SolidWorks 上方，完成摘要显示本次文件变化。
8. 检查 `export_report.md`、`inertial_validation.csv`、`mesh_manifest.csv`，并在目标 Viewer 或
   仿真器中验证 Visual、Collision、Inertia、COM、轴和 Joint 运动。

首次教程状态按 Windows 用户保存在：

```text
%LOCALAPPDATA%\OSRBot\SW2URDF\urdf-export-tutorial-v1.state
```

## 输出目录

```text
<output-root>/
  ROS1/<package>/
    urdf/
    meshes/visual/
    meshes/collision/
    config/export_report.md
    config/inertial_validation.csv
    config/mesh_manifest.csv
  ROS2/<package>/
    urdf/
    meshes/visual/
    meshes/collision/
    config/export_report.md
    config/inertial_validation.csv
    config/mesh_manifest.csv
```

`mesh_manifest.csv` 分别记录请求策略与实际策略，因此可以发现 `ConvexHull`、原语或精简网格
生成失败后回退到 `VisualMesh` 的情况。

## 惯性约定

- 质量属性查询显式使用 SolidWorks 系统单位：千克、米、`kg*m^2`。
- 根 Link 使用 `Origin_global` 或用户明确选择的根坐标系。
- 非根 Link 通常使用其 child-Joint 坐标系作为 URDF Link frame。
- 质量/COM 与 COM 惯性张量由两个独立 MassProperty 对象读取，规避 SolidWorks 2023 Live API
  中观察到的读取顺序异常。
- COM 与张量方向从 SolidWorks 文档坐标系统一转换到所选 Link frame。
- URDF 惯性张量关于 COM。修改 Link frame 会改变 COM 坐标并旋转 COM 张量，但不会因 Link
  原点移动对所存张量应用平行轴偏移。
- SolidWorks 质量属性界面的惯性积记号容易让人多取一次负号；本项目使用的
  `GetMomentOfInertia` API 已返回物理对称张量，因此导出时保留非对角项符号。独立特征值校验
  要求导出张量与 API 主惯量一致，从而捕获重复符号转换。

感谢 Winter 的文章
[《掌握 URDF 中的惯性张量：从 SolidWorks 到强化学习机器人的关键一步》](https://zhuanlan.zhihu.com/p/1887859297221845818)
对 COM 张量、输出坐标系和惯性积记号的解释。当前插件行为以本仓库 API 路径、代码和测试为准。

## 碰撞策略建议

装配体优先尝试 `ComponentBoxes`；盒状结构使用 `BoxPrimitive`，轮子/轴/管使用
`CylinderPrimitive`，球状结构使用 `SpherePrimitive`。复杂单一近似使用 `ConvexHull`；原语
无法满足接触要求时使用 `SimplifiedMesh`；只有任务确实依赖详细接触面时使用 `AccurateMesh`。

所有用户可选策略都有临时 SolidWorks 预览路径。预览用于快速判断覆盖关系，不承诺 mesh 策略
的显示体与最终 STL 字节级一致。最终结果以 `mesh_manifest.csv`、`export_report.md` 和外部 Viewer
校验为准。

## 外观与自动配色

每个 Link 具有一个 URDF material ID 和一组 RGBA。选择内置 material ID 会更新 RGBA；手工 RGBA
仍可覆盖单个 Link。用户未显式覆盖时，插件可以读取 SolidWorks 外观。

`Auto Links` 对整棵 Link 树应用确定性配色：层级由冷色过渡到暖色；去除
`left/right/lhs/rhs/port/starboard` 后名称对应的 Link 使用相同颜色。结果通过正常配置路径保存，
不修改拓扑、CAD 绑定、Collision 或 Inertial。

维护 UI 已删除纹理图片编辑入口：STL 不包含 UV 坐标，本项目也没有便利、可靠的 SolidWorks DAE
导出路线。旧配置中的纹理元数据仍可读取/导出以保持兼容，但维护 UI 不声明可编辑或校验贴图。

## 文档

- [English README](README.md)
- [安装](docs/wiki/Installation-zh-CN.md)
- [快速开始](docs/wiki/Quick-Start-zh-CN.md)
- [Link Tree](docs/wiki/Link-Tree-zh-CN.md)
- [惯性](docs/wiki/Inertia-zh-CN.md)
- [碰撞](docs/wiki/Collision-zh-CN.md)
- [问题排查](docs/wiki/Troubleshooting-zh-CN.md)
- [参与贡献](docs/wiki/Contributing-zh-CN.md)
- [发布流程](docs/wiki/Release-Process-zh-CN.md)
- [CHANGELOG](CHANGELOG.md)

`docs/wiki` 是公开 GitHub Wiki 的版本化事实源。英文页使用标准文件名，简体中文页使用
`-zh-CN` 后缀；两种语言的对应页面必须在同一变更中更新。

## 开发

要求：

1. Windows x64；
2. Visual Studio 2017 与 `.NET desktop development`；
3. .NET Framework 4.5.2；
4. SolidWorks 与匹配的 API Tools/Interop assemblies。

打开 `SW2URDF.sln`。调试 SolidWorks 时，Debug 配置需要启动目标安装目录的 `SLDWORKS.exe`。

构建示例：

```powershell
MSBuild.exe SW2URDF\SW2URDF.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 `
  "/p:SolidWorksInstallDir=C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
```

运行全部 Debug 测试：

```powershell
TestRunner\bin\x64\Debug\net452\TestRunner.exe
```

按类或名称过滤：

```powershell
TestRunner\bin\x64\Debug\net452\TestRunner.exe TestMassPropertyFrameConverter
```

Pure tests 可在 SolidWorks 不可用时运行。Live COM tests 需要兼容的本地 SolidWorks；RPC/COM
不可用时可能失败。SolidWorks 2023 Live 覆盖不代表所有版本均已验证。

## 可复现安装包构建

```powershell
.\scripts\BuildInstaller.ps1 -Configuration Release -Platform x64 `
  -SolidWorksInstallDir "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS" `
  -InnoCompilerPath "C:\Path\To\Inno Setup 6.3.3\ISCC.exe"
```

输出：

```text
INSTALL/OUTPUT/sw2urdfSetup_YYYYMMDD_<commit>.exe
INSTALL/OUTPUT/sw2urdfSetup_YYYYMMDD_<commit>.exe.sha256
INSTALL/OUTPUT/sw2urdfSetup_YYYYMMDD_<commit>.exe.provenance.json
```

构建使用 detached worktree，校验锁定的 NuGet 输入和暂存的 SolidWorks API assemblies，并记录
payload 哈希。provenance 是维护者构建追踪信息，不是 Authenticode 签名；Hosted CI 也不会在
缺少专有 SolidWorks assemblies 的环境中重新编译插件。

构建候选版本前，需要新增 `.github/release-notes/vYYYYMMDD.md`，并人工校对 `## English` 与
`## 简体中文`。CI 只渲染可追踪占位符；缺少任一语言或必要占位符时直接失败，不对 CHANGELOG
进行机器翻译。

## 已知限制

- 安装/升级要求 SolidWorks 已关闭；没有自动结束进程或热加载。
- 未保存装配体没有稳定路径，不能使用按装配体隔离的恢复草稿。
- 删除、替换或 Save As 后，组件 persistent ID 可能失效，需要人工重新绑定。
- 坐标系具有工程语义，必须由用户在 SolidWorks 中创建和选择，插件不会从几何自动猜测。
- 空心/凹形刚体的合法 COM 可以位于空腔中；包围范围检查不是材料内部点测试。
- 碰撞预览是临时 SolidWorks 几何；mesh 策略不承诺预览与最终 STL 字节一致。
- STL 不携带 UV 纹理坐标，维护 UI 不提供纹理创作。
- 3DXML 用于 Visual 交换；不能据此推断通用 DAE/Collision/纹理流程已经验证。
- 安装包 provenance 不等于代码签名或第三方可复现构建证明。
- 任何生产 URDF 都必须在目标 Viewer、MuJoCo、Isaac Sim 或实际求解器中复核。

## 致谢与参考

- 原项目：[ROS SolidWorks URDF Exporter](https://github.com/ros/solidworks_urdf_exporter)
- 原作者及历史维护者：[Stephen Brawner](mailto:brawner@gmail.com)
- 上游 README 记录的历史支持者：[PickNik Consulting](https://picknik.ai)、Verb Surgical、
  Open Robotics、Willow Garage
- 当前维护者：`kitso666 <kitso@osrbot.com>`
- 3DXML 支持：Kento Matsuo 及提交 `22cb778` 中记录的共同贡献者
- 惯性概念参考：Winter，
  [《掌握 URDF 中的惯性张量：从 SolidWorks 到强化学习机器人的关键一步》](https://zhuanlan.zhihu.com/p/1887859297221845818)
- 原 ROS 文档：[sw_urdf_exporter](http://wiki.ros.org/sw_urdf_exporter) 与
  [教程](http://wiki.ros.org/sw_urdf_exporter/Tutorials)

参考文章是理论说明来源，不表示该文章向本仓库提供了源代码。

## 许可证

MIT，见 [LICENSE](LICENSE)。原 `Copyright 2020 Stephen Brawner` 声明与许可条款继续适用于
继承代码。
