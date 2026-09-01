# SolidWorks to URDF 导出器

[English](README.md) | **简体中文**

[![许可证：MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![平台](https://img.shields.io/badge/platform-Windows%20x64-blue.svg)](#支持环境)
[![框架](https://img.shields.io/badge/.NET%20Framework-4.8-blueviolet.svg)](#开发)

本仓库是 ROS 原项目
[`solidworks_urdf_exporter`](https://github.com/ros/solidworks_urdf_exporter) 的 OSRBot
持续维护分支。它保留原有 SolidWorks 插件工作流，并维护 Link 树编辑、坐标系感知质量属性、
碰撞策略与预览、ROS1/ROS2 功能包、OpenUSD/MJCF 机器人资产、校验报告、简体中文 UI 和
可审计安装包构建。

> **项目状态**
>
> 这是社区维护分支，不是 Dassault Systemes 或 ROS 官方发行版。当前维护和 Live API 验证重点
> 是 SolidWorks 2023。继承自上游的历史最低要求是 SolidWorks 2018 SP5；这不表示每个版本和
> Service Pack 都已完成回归测试。

## 为什么需要这个维护分支

上游项目建立了 SolidWorks 到 URDF 的核心工作流。本分支保留该基础，集中解决在新版
SolidWorks、复杂装配体、物理参数校验和长期发布维护中暴露的生产问题：

| 生产遗留问题 | 本维护分支的处理 |
| --- | --- |
| Link 树编辑可能在预览、PropertyManager 切换或重新打开后丢失 | 事务化编辑、严格的 v2 PID 配置持久化、恢复草稿以及更严格的重复名称和陈旧状态校验 |
| 深层参考几何、Unicode 名称和同名特征无法安全地依赖显示文本解析 | 使用组件实例/特征 persistent ID 和 occurrence-aware `GetCorresponding`；UI 名称不再承担对象身份 |
| STEP/固定装配缺少可靠 Joint 语义，0 DOF 容易被误判为 `fixed` | 手工 Joint 标注为主流程；Mate 识别只做显式辅助，所有建议必须由用户确认 |
| 质量属性可能出现全零、符号错误或坐标系错位 | 显式系统单位、统一零件/装配体坐标转换、COM/边界检查、物理张量校验以及 API 主惯量对照 |
| 导出前难以判断碰撞策略是否符合当前 Link | 按 Link 局部拟合、全部策略的 SolidWorks 临时预览、回退报告以及请求/实际策略记录 |
| 外观配置和大型导出过程不易检查 | SolidWorks 外观读取、确定性 Link 自动配色、双语 UI、置顶进度和导出摘要 |
| 历史流程没有提供边界清晰、可审计的便携 USD/MJCF 资产 | 新增 OpenUSD/MJCF 目标，使用固定结构/运行时检查，并明确不代替应用、控制器和任务验证 |
| 历史安装包难以复现和审计 | 哈希与 provenance sidecar、载荷校验、双语 Release Notes 和 Draft 人工发布门禁 |

本分支保留上游 Git 历史、作者署名和 MIT 许可证。具体变更及提交证据见
[Changelog](CHANGELOG.md)。

## 工程边界

导出器明确分离 URDF 的三类责任：

| URDF 数据 | 工程目标 | 导出器行为 |
| --- | --- | --- |
| `visual` | 保留可识别外观和几何 | 导出 STL 或 3DXML Visual，并写入 URDF material ID/RGBA |
| `collision` | 在保留任务相关接触形状的前提下简化求解几何 | 支持网格、原语、组件包围盒和凸包，并记录请求/实际策略 |
| `inertial` | 保留质量、质心和惯性张量 | 使用系统单位读取 SolidWorks 质量属性，并转换到所选 Link 坐标系 |

碰撞策略不改变质量属性来源。碰撞预览用于工程判断，不证明最终仿真行为正确；实际导出内容以
生成的 URDF 与报告为准。

## 导出目标与证据边界

主导出页只提供四种可交付目标。`Robot Bundle` 不是第五种目标：它是插件在系统临时目录中创建
的私有规范暂存表示，仅供所选导出器消费，并在成功或失败后清理。

所有所选目标作为一个可恢复事务发布。体检报告出现阻断级失败时会恢复原目标目录；若进程中断，
下一次导出会先根据持久化 journal 完成恢复，再开始新的导出。

| 用户目标 | 交付文件 | 自动化证据 | 不代表 |
| --- | --- | --- | --- |
| ROS 1 功能包 | `ROS1/<package>`，包含 URDF、网格、配置和报告 | 规范模型校验与事务化功能包生成 | 已在 ROS 1 中启动、控制或执行任务 |
| ROS 2 功能包 | `ROS2/<package>`，包含 URDF、网格、配置和报告 | 规范模型校验与事务化功能包生成 | 已在 ROS 2/Gazebo 或 `ros2_control` 中运行 |
| OpenUSD 机器人资产 | `USD/<package>/robot.usd`、几何依赖、源网格证据、名称映射和 JSON 报告 | 使用安装包固定的 OpenUSD 运行时生成并重新打开 stage | 已导入或运行于 Isaac Sim/Isaac Lab |
| MuJoCo MJCF 模型 | `MuJoCo/<robot>/robot.xml`、`scene.xml`、资产、名称映射和 JSON 报告 | 使用安装包固定的 MuJoCo 官方工具对两个 XML 入口完成编译、规范保存、重载及一步零控制推进 | 已生成执行器、PID、控制器、任务、接触调参或强化学习工程 |

文档严格区分三层证据：

1. **生成能力**：证明导出器由已校验模型写出了约定文件。
2. **自动化验证**：只证明上表明确列出的检查。
3. **实际应用运行验证**：必须由用户在自己的 ROS、Isaac、MuJoCo、控制器和任务环境中完成；
   “导出成功”不包含这层结论。

每次导出只原子替换本次选中的目标目录。未选择目标的既有目录会保留，可能来自较早一次导出；
顶层 `export_report.md` 会明确记录本次实际生成和验证的目标，避免把旧目录误认为本次结果。

## 主要功能

- 导出四种明确的用户目标：ROS 1 功能包、ROS 2 功能包、OpenUSD 机器人资产和 MuJoCo
  MJCF 模型。私有规范暂存模型保证各导出器使用同一份已校验数据，但不会成为用户交付物。
- 使用固定的内置 OpenUSD 运行时生成 USD 并验证 stage 可重新打开；使用固定的 MuJoCo 官方
  工具生成 MJCF，并要求编译、规范保存、重载和一步零控制验证通过后才交付本地结果。
- 为 Link/Joint 保存稳定 ID 与来源证据。SolidWorks Mate 识别只对原生可动装配提供需人工确认的
  建议，不作为 STEP 几何的兜底推断。
- 在装配体特征 `URDF Export Configuration (v2)` 中保存 Link/Joint 配置。显式根文档参考使用
  `OwnerScope=RootDocument` + 特征 PID；组件实例参考使用 `OwnerScope=ComponentInstance` +
  组件 PID + 特征 PID。显示名只用于 UI，不参与身份判断。解析时先按 PID 找到 owner feature，
  再通过 `IComponent2.GetCorresponding` 映射到准确的装配实例，不进行名称查找或活动配置切换。
  v1.x 名称型配置不会自动迁移；
  需要删除旧配置特征、重新创建并人工审核。v2 写入使用 canonical 与 hidden recovery 两个槽位：
  写入现有槽位前先以 `revision=0` 使其失效，更新 payload 后最后提交非零 revision；每个槽位都
  经过完整校验，加载时选择最新的有效 revision，避免中断的 COM 原位写覆盖最后有效状态。停在
  `revision=0` 的槽位只表示未完成的准备写：加载时忽略，后续保存可直接重试。每个 SolidWorks
  会话只缓存完整注册成功的 schema definition；初始化失败后重试会改用新的唯一定义，部分初始化的
  `AttributeDef` 不会污染后续保存。
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
- RGBA 数值与选色器是逐 Link 外观的直接输入，URDF material ID 根据最终颜色稳定派生并只读显示。
- 支持整树自动配色：层级从冷色过渡到暖色，规范化后的左右对应 Link 使用同一稳定颜色。
- 导出进度窗口保持置顶且防止重入，完成摘要显示变化文件数、总大小、耗时和输出目录。
- 维护流程提供简体中文 UI，同时配置和 URDF 输出保留规范英文 Joint 类型和值。

详细行为和限制见[项目 Wiki](https://github.com/osrbot/solidworks_urdf_exporter_pro/wiki)。

## 支持环境

| 项目 | 支持或验证状态 |
| --- | --- |
| 操作系统 | Windows x64 |
| 目标框架 | .NET Framework 4.8 |
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

公开发布流程使用人工门禁：同一份维护者本地构建安装包必须先完成 Live SolidWorks 手动测试，并
通过 `THIRD_PARTY_NOTICES.md` 中 `solidworkstools.dll` 的再分发许可审核；之后维护者才可触发 CI
校验该已提交候选并创建 Draft Release。CI 不会自动公开 Draft，公开发布仍需维护者再次明确批准。

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
6. 选择 Visual 格式、Collision 策略、RGBA 和 STL 精简比例。URDF material ID 会自动派生；
   使用 SolidWorks 碰撞预览检查覆盖关系，以导出 manifest 作为最终策略记录。
7. 至少选择一种明确目标：ROS 1 功能包、ROS 2 功能包、OpenUSD 机器人资产或 MuJoCo MJCF
   模型。USD 与 MJCF 要求 STL 几何；插件不要求填写 Isaac 版本、actuator profile，也不要求
   用户管理中间 Bundle。
8. 导出后先检查公共 `export_report.md` 和目标目录内的报告。ROS 包检查
   `config/export_report.md`、`config/inertial_validation.csv` 和 `config/mesh_manifest.csv`；USD/MJCF 检查各自的
   `export_report.json` 与 `name_map.json`。然后在实际应用和任务环境中运行验证。

首次教程状态按 Windows 用户保存在：

```text
%LOCALAPPDATA%\OSRBot\SW2URDF\urdf-export-tutorial-v1.state
```

## 输出目录

```text
<output-root>/
  export_report.md                  # 公共导出摘要
  ROS1/<package>/                   # 可选 ROS 1 功能包
    urdf/
    meshes/visual/
    meshes/collision/
    config/export_report.md
    config/inertial_validation.csv
    config/mesh_manifest.csv
  ROS2/<package>/                   # 可选 ROS 2 功能包
    urdf/
    meshes/visual/
    meshes/collision/
    config/export_report.md
    config/inertial_validation.csv
    config/mesh_manifest.csv
  USD/<package>/                    # 可选 OpenUSD 机器人资产
    robot.usd
    geometry/
    meshes/
    name_map.json
    export_report.json
  MuJoCo/<robot>/                   # 可选 MJCF 模型
    robot.xml
    scene.xml
    assets/visual/
    assets/collision/
    name_map.json
    export_report.json
```

私有暂存 Bundle 有意不出现在输出目录。`mesh_manifest.csv` 分别记录请求策略与实际策略，因而
可以发现 `ConvexHull`、原语或精简网格失败后回退到 `VisualMesh` 的情况；USD/MJCF 的 JSON
报告则记录实际验证运行时和证据边界。

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

维护者感谢这篇
[SolidWorks 到 URDF 惯性社区文章](https://zhuanlan.zhihu.com/p/1887859297221845818)
提供背景阅读。当前插件行为以本仓库 API 路径、代码、测试和导出报告为准；致谢不表示该文章
向本仓库提供了源代码。

## 碰撞策略建议

装配体优先尝试 `ComponentBoxes`；盒状结构使用 `BoxPrimitive`，轮子/轴/管使用
`CylinderPrimitive`，球状结构使用 `SpherePrimitive`。复杂单一近似使用 `ConvexHull`；原语
无法满足接触要求时使用 `SimplifiedMesh`；只有任务确实依赖详细接触面时使用 `AccurateMesh`。

所有用户可选策略都有临时 SolidWorks 预览路径。预览用于快速判断覆盖关系，不承诺 mesh 策略
的显示体与最终 STL 字节级一致。最终结果以 `mesh_manifest.csv`、`export_report.md` 和外部 Viewer
校验为准。

## 外观与自动配色

每个 Link 具有一组 RGBA 和一个由该颜色稳定派生的 URDF material ID。选色器与 RGBA 数值是
直接编辑入口；material ID 只用于识别和 URDF 引用，不再表现为另一套颜色预设。用户未显式
覆盖时，插件可以读取 SolidWorks 外观。

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
- [Joint 语义与来源](docs/architecture/joint-semantics-and-provenance.md)
- [兼容性矩阵](docs/development/compatibility-matrix.md)
- [OpenUSD](docs/wiki/OpenUSD-zh-CN.md)
- [MuJoCo MJCF](docs/wiki/MJCF-zh-CN.md)
- [OpenUSD 与下游 Isaac 边界](docs/isaac/README.zh-CN.md)
- [CHANGELOG](CHANGELOG.md)

`docs/wiki` 是公开 GitHub Wiki 的版本化事实源。英文页使用标准文件名，简体中文页使用
`-zh-CN` 后缀；两种语言的对应页面必须在同一变更中更新。

## 开发

要求：

1. Windows x64；
2. Visual Studio 2017、`.NET desktop development` 与 .NET Framework 4.8 targeting tools；
3. .NET SDK 8（用于 portable Core、CLI 与 hosted 单元测试）；
4. SolidWorks 与匹配的 API Tools/Interop assemblies。

打开 `SW2URDF.sln`。调试 SolidWorks 时，Debug 配置需要启动目标安装目录的 `SLDWORKS.exe`。

构建示例：

```powershell
MSBuild.exe SW2URDF\SW2URDF.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 `
  "/p:SolidWorksInstallDir=C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
```

运行全部 Debug 测试：

```powershell
TestRunner\bin\Debug\net48\TestRunner.exe
```

按类或名称过滤：

```powershell
TestRunner\bin\Debug\net48\TestRunner.exe TestMassPropertyFrameConverter
```

运行安装包构建使用的确定性插件门禁，不启动本机 SolidWorks：

```powershell
TestRunner\bin\Debug\net48\TestRunner.exe --exclude-live-solidworks
```

Pure tests 可在 SolidWorks 不可用时运行。Live COM tests 需要兼容的本地 SolidWorks；RPC/COM
不可用时可能失败。标记为 `Category=LiveSolidWorks` 的测试（包括旧的
`Requires SW Test Collection` 集合）有意不参与可复现安装包构建，必须作为独立的 Live API
证据显式运行；安装包 provenance 会如实记录这一边界，不会把未请求的 Live
测试写成通过。SolidWorks 2023 Live 覆盖不代表所有版本均已验证。

显式运行 Live 测试时必须设置 `SW2URDF_RUN_SW_INTEGRATION_TESTS=1`；缺少 opt-in 或夹具输入会
直接失败，不会被计为通过。

深层参考几何 Live 测试使用一次性的五级装配体。运行前先关闭 SolidWorks；生成器会启动并独占
一个隔离的 SolidWorks 进程；未传入 `--output-directory` 时在系统临时目录写入夹具，并在返回前
关闭该进程。Live 测试有意只接受这种默认临时目录夹具：

```powershell
python -m pip install pywin32
$fixture = python scripts\create_deep_reference_fixture.py `
  examples\3_DOF_ARM\3_DOF_ARM.SLDASM
$env:SW2URDF_RUN_DEEP_REFERENCE_TESTS = "1"
$env:SW2URDF_TEST_DEEP_REFERENCE_ASSEMBLY = $fixture
TestRunner\bin\Debug\net48\TestRunner.exe TestDeepReferenceGeometryIntegration
```

生成器只依赖公开的 `pywin32` 与 SolidWorks COM API。如果无法自动发现装配体模板，可显式传入
`--assembly-template C:\path\assembly.asmdot`。

## 可复现安装包构建

```powershell
.\scripts\BuildInstaller.ps1 -Configuration Release -Platform x64 `
  -SolidWorksInstallDir "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS" `
  -DotNetPath "C:\Path\To\dotnet-sdk-8.0.424\dotnet.exe" `
  -InnoCompilerPath "C:\Path\To\Inno Setup 6.3.3\ISCC.exe"
```

构建要求精确使用 .NET SDK 8.0.424 和 Inno Setup 6.3.0–6.3.3。

输出：

```text
INSTALL/OUTPUT/sw2urdfSetup_YYYYMMDD_<commit>.exe
INSTALL/OUTPUT/sw2urdfSetup_YYYYMMDD_<commit>.exe.sha256
INSTALL/OUTPUT/sw2urdfSetup_YYYYMMDD_<commit>.exe.provenance.json
```

构建使用 detached worktree，校验锁定的 NuGet、OpenUSD、MuJoCo 官方运行时输入和暂存的
SolidWorks API assemblies，并记录 payload 哈希。provenance 是维护者构建追踪信息，不是
Authenticode 签名；Hosted CI 也不会在缺少专有 SolidWorks assemblies 的环境中重新编译插件。

构建候选版本前，需要新增 `.github/release-notes/vYYYYMMDD.md`，并人工校对 `## English` 与
`## 简体中文`。CI 只渲染可追踪占位符；缺少任一语言或必要占位符时直接失败，不对 CHANGELOG
进行机器翻译。

## 已知限制

- 安装/升级要求 SolidWorks 已关闭；没有自动结束进程或热加载。
- 未保存装配体没有稳定路径，不能使用按装配体隔离的恢复草稿。
- 删除、替换或 Save As 后，组件 persistent ID 可能失效，需要人工重新绑定。
- STEP 等纯几何输入没有可靠 Joint 语义；固定或完全约束的装配也不会被自动判定为固定关节机器人。
- 坐标系具有工程语义，必须由用户在 SolidWorks 中创建和选择，插件不会从几何自动猜测。
- 空心/凹形刚体的合法 COM 可以位于空腔中；包围范围检查不是材料内部点测试。
- 碰撞预览是临时 SolidWorks 几何；mesh 策略不承诺预览与最终 STL 字节一致。
- STL 不携带 UV 纹理坐标，维护 UI 不提供纹理创作。
- 3DXML 用于 Visual 交换；不能据此推断通用 DAE/Collision/纹理流程已经验证。
- 安装包 provenance 不等于代码签名或第三方可复现构建证明。
- 深层/隐藏 Link 预览变更须在维护者目标 SolidWorks 版本中完成 Live 验证后才能公开发布。
- USD 自动化验证只证明 OpenUSD stage 可以生成和重开，不证明已导入或运行于 Isaac Sim/Isaac Lab。
- MJCF 自动化验证只证明 MuJoCo 官方工具完成编译、保存、重载和一步零控制；不证明控制器、
  接触调参、长时间稳定性、性能、任务行为或强化学习。
- 任何生产模型都必须在目标 Viewer、仿真器或实际求解器中复核。

## 致谢与参考

- 原项目：[ROS SolidWorks URDF Exporter](https://github.com/ros/solidworks_urdf_exporter)
- 原作者及历史维护者：[Stephen Brawner](mailto:brawner@gmail.com)
- 上游 README 记录的历史支持者：[PickNik Consulting](https://picknik.ai)、Verb Surgical、
  Open Robotics、Willow Garage
- 当前维护者：`kitso666 <kitso@osrbot.com>`
- 3DXML 支持：Kento Matsuo 及提交 `22cb778` 中记录的共同贡献者
- 维护者提供的社区惯性参考：
  [SolidWorks 到 URDF 惯性文章](https://zhuanlan.zhihu.com/p/1887859297221845818)
- 原 ROS 文档：[sw_urdf_exporter](http://wiki.ros.org/sw_urdf_exporter) 与
  [教程](http://wiki.ros.org/sw_urdf_exporter/Tutorials)

参考文章是理论说明来源，不表示该文章向本仓库提供了源代码。

## 许可证

MIT，见 [LICENSE](LICENSE)。原 `Copyright 2020 Stephen Brawner` 声明与许可条款继续适用于
继承代码。
