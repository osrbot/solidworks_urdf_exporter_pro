# 兼容性矩阵与验证层级

更新日期：2026-08-30。版本声明按精确版本门禁管理；“代码支持”不等于已完成对应运行时验收。

| 目标 | 生成支持 | 自动静态/宿主验证 | 专用环境门禁 | 当前结论 |
| --- | --- | --- | --- | --- |
| SolidWorks 2023 / Windows x64 | 是，.NET Framework 4.8 Add-in | immutable worktree 同时构建 Test/Release，TestRunner 通过后才封装安装器 | SolidWorks 2023 加载、真实装配导出、重启/卸载 | 代码完成；本轮未在 Windows/SolidWorks 执行 |
| SolidWorks 2018 SP5 | 保留历史最低目标说明 | 无持续矩阵 | 需要单独机器回归 | 兼容目标，不宣称已验证每个 SP |
| ROS 1 catkin | 是，legacy profile | Bundle/文件结构测试 | 对应 ROS 1 环境构建与显示 | 仅维护兼容，不新增现代仿真能力 |
| ROS 2 Lyrical + Gazebo Jetty | 是，推荐组合 | hosted CI 生成并检查 ament、launch、URDF、controller YAML | self-hosted ROS/Gazebo 启动及 controller active | 当前主组合；运行时门禁独立执行 |
| ROS 2 Jazzy + Gazebo Harmonic | 是，兼容组合 | 同一生成器与 validator | Jazzy/Harmonic self-hosted 回归 | 兼容保留，用于现有 LTS 工程 |
| ros2_control + gz_ros2_control | 是，显式 JSON profile | Joint/interface/controller 一致性测试 | controller_manager 加载与 active 状态 | 内置支持 trajectory 与 forward command 两类受控模板 |
| Isaac Sim 6.0.0 | 是，URDF → USD adapter | Python preflight、精确版本和 importer API 检查 | Isaac Python/GPU 转换与 USD 加载 smoke | 当前 API 基线；必须精确匹配运行时 |
| Isaac Lab 2.3.2 | 是，生成 articulation/actuator 配置 | profile 覆盖和生成测试 | 精确版本运行 `smoke_test.py` | 稳定参考基线，不猜 gains |
| Isaac Lab 3.x beta | 版本门禁可接入 | 不宣称通用兼容 | 单独 self-hosted 验证后才升级矩阵 | 预览路线，不作为默认稳定目标 |

## 版本策略

- UI 只提供经过维护的 ROS/Gazebo 组合，不允许任意拼接不匹配的发行版；
- Isaac Sim 与 Isaac Lab 分别保存精确版本，转换时与实际包版本逐字匹配；
- 上游 API 变化必须先在专用 workflow 通过，再修改默认矩阵；
- 外部 controller 插件可以由项目扩展，但 OSURDF 不为未知参数结构生成 YAML；
- 输出始终以 Robot Bundle 为基线，旧 ROS 1 包是派生产物。

## 证据等级

| 等级 | 证明内容 | 不证明内容 |
| --- | --- | --- |
| S1 源码/Schema | 契约存在、格式可解析 | 编译和运行 |
| S2 单元/端到端 | Core、CLI、Bundle、生成器行为 | SolidWorks COM、ROS/Isaac 运行时 |
| S3 平台构建 | Windows Add-in 或目标 ROS 包可构建 | 真实 CAD 和动力学正确 |
| S4 运行时 smoke | 插件加载、Gazebo/Isaac 可启动并推进 | 机器人控制质量和物理真实性 |
| S5 工程验收 | 指定模型、控制器、硬件/仿真任务通过 | 其他机型自动继承通过 |

CI 文件分别对应 hosted、SolidWorks、ROS 和 Isaac 门禁，不能用一个绿色状态替代其余证据。
