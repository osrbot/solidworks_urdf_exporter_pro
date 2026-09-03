# 兼容性矩阵与验证层级

更新日期：2026-09-03。生成文件、自动检查和实际应用验收是三件不同的事。

| 目标 | 当前输出或运行时 | 已执行的验证 | 触发方式 | 仍需用户验证 |
| --- | --- | --- | --- | --- |
| SolidWorks 2023 / Windows x64 | .NET Framework 4.8 Add-in | Test/Release 构建、TestRunner、`level_5` 实机 UI 与导出测试 | 本地人工 + Windows 工作流 | 用户生产装配、目标 Service Pack、长期稳定性 |
| SolidWorks 2018 SP5 | 保留上游最低版本目标 | 无持续版本矩阵 | 无 | 每个目标版本必须单独回归 |
| ROS 1 | `ROS1/<package>` | 规范校验、事务发布、结构测试 | 常规 CI | 实际 ROS 1 构建、显示、控制和任务运行 |
| ROS 2 Jazzy + Gazebo Harmonic | `ROS2/<package>` | 最小夹具构建、Gazebo 启动、`joint_state_broadcaster` 与 `arm_controller` 激活 | 手动 `ros-integration` | 用户模型、控制参数、接触与任务 |
| ROS 2 Lyrical + Gazebo Jetty | `ROS2/<package>` | 最小夹具构建、Gazebo 启动、`joint_state_broadcaster` 与 `arm_controller` 激活 | 手动 `ros-integration` | 用户模型、控制参数、接触与任务 |
| OpenUSD | `usd-core 26.8` | 生成 `robot.usd`、重开 stage、结构与本地依赖检查 | 常规 CI + 本地导出 | Isaac 或其他 USD 应用中的渲染、物理、控制和任务 |
| MuJoCo MJCF | MuJoCo `3.12.0` | 两个 XML 入口编译、规范保存、重载、一步零控制推进 | 常规 CI + 本地导出 | actuator、控制器、接触、稳定性、性能和任务 |

ROS 2 集成门禁是可运行的手动工作流，不是每次提交都自动执行的持续 CI。它覆盖固定的最小夹具，
不能替代用户机器人的工程验收。

## 产品边界

- UI 只展示四个用户交付目标：ROS 1、ROS 2、OpenUSD、MJCF。
- Robot Bundle 仅是系统临时目录中的私有暂存，不是第五个目标，也不进入用户输出目录。
- OpenUSD 不检测或要求 Windows 工作站安装 Isaac Sim/Isaac Lab，也不要求填写其版本。
- MJCF 不生成 actuator、PID、控制器、传感器、任务或强化学习工程。
- ROS 功能包生成不等于控制器已适配用户模型并通过目标系统验收。
- Joint 限位、控制参数和任务相关接触参数必须由了解机器人实际物理语义的用户确认。

## 证据等级

| 等级 | 证明内容 | 不证明内容 |
| --- | --- | --- |
| S1 源码/Schema | 契约存在、格式可解析 | 编译和运行 |
| S2 单元/端到端 | Core、CLI、内部暂存和各导出器行为 | SolidWorks COM 或目标应用行为 |
| S3 平台构建 | Windows Add-in 或目标资产能够生成 | 真实 CAD、控制和动力学正确 |
| S4 有界运行时检查 | OpenUSD 重开；MJCF 最小运行；ROS 2 最小夹具集成门禁 | 任意用户模型、控制器、任务和物理真实性 |
| S5 工程验收 | 指定模型在指定应用、控制器和任务中通过 | 其他机型或版本自动继承通过 |

绿色状态只代表对应工作流实际执行的层级。事实来源为 `.github/workflows/ci.yml`、
`.github/workflows/ros-integration.yml`、`.github/workflows/solidworks-integration.yml` 和各目标锁文件。
