# 兼容性矩阵与验证层级

更新日期：2026-09-01。生成能力、自动化验证和实际应用运行验证必须分开描述。

| 目标 | 插件生成内容 | 自动化验证 | 仍需用户验证 |
| --- | --- | --- | --- |
| SolidWorks 2023 / Windows x64 | .NET Framework 4.8 Add-in | 干净提交的隔离 worktree 同时构建 Test/Release，TestRunner 通过后封装安装器 | 插件加载、深层/隐藏 Link 预览、真实生产装配导出 |
| SolidWorks 2018 SP5 | 保留上游最低版本兼容目标 | 无持续版本矩阵 | 每个目标 SP 必须单独回归；当前不声明均已验证 |
| ROS 1 | `ROS1/<package>` 功能包、URDF、网格、配置与报告 | 规范模型校验、目标目录事务发布、结构测试 | 在实际 ROS 1 环境构建、显示、控制和任务运行 |
| ROS 2 + Gazebo | `ROS2/<package>` 功能包、URDF、网格、配置与报告 | 规范模型校验、目标目录事务发布、结构测试 | 在目标 ROS 2/Gazebo 组合中构建、启动和控制 |
| OpenUSD | `USD/<package>/robot.usd`、几何依赖、源网格证据、名称映射和报告 | 固定 OpenUSD 运行时生成并重新打开 stage | 在目标 Isaac 或其他 USD 应用中导入、渲染、物理和任务验证 |
| MuJoCo MJCF | `MuJoCo/<robot>/robot.xml`、`scene.xml`、Visual/Collision 资产、名称映射和报告 | 固定 MuJoCo 官方工具编译、规范保存、重载并推进一步零控制 | actuator、控制器、接触调参、长时间稳定性、性能和任务验证 |

## 产品边界

- UI 只展示四个用户交付目标：ROS 1、ROS 2、OpenUSD、MJCF。
- Robot Bundle 仅是系统临时目录中的私有规范暂存，不是第五个目标，也不进入用户输出目录。
- OpenUSD 导出不检测或要求 Windows 工作站安装 Isaac Sim/Isaac Lab，也不要求填写其版本。
- MJCF 导出不生成 actuator、PID、控制器、传感器、任务或强化学习工程。
- ROS 功能包生成不等于 `ros_control`/`ros2_control` 控制器已经在目标系统加载并运行。
- Joint 限位、控制参数和任务相关接触参数必须由了解机器人实际物理语义的用户确认。

## 证据等级

| 等级 | 证明内容 | 不证明内容 |
| --- | --- | --- |
| S1 源码/Schema | 契约存在、格式可解析 | 编译和运行 |
| S2 单元/端到端 | Core、CLI、内部暂存和各导出器行为 | SolidWorks COM 或目标应用行为 |
| S3 平台构建 | Windows Add-in 或目标资产能够生成 | 真实 CAD、控制和动力学正确 |
| S4 有界运行时检查 | OpenUSD 重开；MJCF 编译/保存/重载/一步推进 | Isaac 导入、控制器、任务、长期稳定性和物理真实性 |
| S5 工程验收 | 指定模型在指定应用、控制器和任务中通过 | 其他机型或版本自动继承通过 |

CI 和本地测试只代表其明确执行的等级。任何绿色状态都不能替代目标 SolidWorks、ROS、Isaac、
MuJoCo 或生产任务环境中的人工工程验收。
