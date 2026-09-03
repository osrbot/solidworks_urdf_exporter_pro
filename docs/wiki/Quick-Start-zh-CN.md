# Quick Start

**简体中文** | [English](Quick-Start)

## 1. 准备装配体

- 建议使用装配体副本。
- 解析轻量化组件，确保参与导出的实体可访问。
- 为零件设置有效材料或密度。
- 重建、保存，并在 SolidWorks Mass Properties 中检查质量与质心。
- 保存装配体；未保存模型没有稳定路径，无法使用按装配体隔离的恢复草稿。

## 2. 创建参考几何

用户应在 SolidWorks 中明确创建：

- 根 Link 坐标系，例如 `Origin_global`；
- 每个非根 Link 对应的 child-Joint 坐标系；
- 转动或移动 Joint 使用的参考轴。

插件不会从几何猜测一个具有工程含义的坐标系。所有坐标系应遵循一致的右手系约定。

## 3. 建立 Link Tree

打开 `Tools > Export as URDF`：

- 首次运行可选择 `Start tutorial`、`Skip once` 或 `Do not remind`；
- 教程之后仍可从 `Tools > URDF Export Tutorial` 打开；
- 使用 PropertyManager、自由画布或 Outline 编辑 Link 层级；
- 新 Link 只自动生成 Joint 名称，Joint 类型保持待选择；必须明确选择后才能应用或导出；
- 每个组件只应绑定到一个 Link，父/子组件引用不能重复承担两个 Link 的几何职责。

## 4. 配置 Joint

检查每个非根 Link 的：

- Joint 名称；
- `fixed`、`revolute`、`continuous`、`prismatic`、`floating`、`planar` 类型；
- 父/子 Link；
- Origin、Axis；
- Limit、Dynamics；
- 可选 Mimic 关系。

STEP、导入模型或固定装配应手动选择 Joint 类型。`Automatically Detect` 是“尝试从
SolidWorks Mate 识别”的可选配置态，只适用于保留正确 Mate 的原生可动装配，不是合法的最终
URDF Joint 类型。0 个剩余自由度可能表示固定、完全约束或 Mate 语义缺失，不能据此自动推断为
`fixed`。正式导出前必须明确选择一种类型。
选择 `Automatically Detect` 后，点击“下一步”才会对这些 Joint 运行辅助识别。结果会以待确认
建议回填；用户必须逐个打开建议 Joint、明确选择最终类型并按需填写 limit。单旋转自由度
暂以 `continuous` 显示，因为 CAD 自由度无法区分 `continuous` 与有界 `revolute`。

## 5. 校验惯性

- 为每个 Link 选择明确存在的 Link 坐标系。
- 检查质量 `kg`、COM `m`、惯性 `kg*m^2`。
- 显示 COM/等效惯性体，检查位置和主轴方向。
- 预览显示失败是显示层问题；数值是否合格以导出校验和报告为准。

## 6. 选择 Visual 与 Collision

- Visual 用于外观；Collision 用于接触求解。
- 复杂装配体优先尝试 `ComponentBoxes`。
- 盒体使用 `BoxPrimitive`，轮子/轴/筒体使用 `CylinderPrimitive`，球形结构使用
  `SpherePrimitive`。
- 原语无法满足时，依次考虑 `ConvexHull`、`SimplifiedMesh`；只有确有接触细节需求时使用
  `AccurateMesh`。
- 开启碰撞预览检查覆盖关系，但不要把临时预览当作最终 STL 的字节级副本。

## 7. 导出

至少选择一种明确交付物：

- **ROS 1 功能包**：写入 `ROS1/<package>` 的 legacy URDF 包；
- **ROS 2 功能包**：写入 `ROS2/<package>` 的现代描述包；
- **OpenUSD 机器人资产**：写入 `USD/<package>` 的可移植 USD stage；
- **MuJoCo MJCF 模型**：写入 `MuJoCo/<robot>` 的 MJCF 模型。

ROS 目标使用 UI 中显示的功能包元数据和维护的 ROS/Gazebo 组合。USD 与 MJCF 要求 STL 几何，
不要求安装 Isaac，不要求填写 Isaac/Isaac Lab 版本、actuator profile 或用户控制器文件。至少要
勾选一个目标。进度窗口保持在 SolidWorks 前方并阻止导出重入。

## 8. 检查结果

依次查看：

1. 公共：输出根目录的 `export_report.md`，用于查看导出器摘要；
2. ROS：各功能包中的 `config/export_report.md`、`config/inertial_validation.csv` 和
   `config/mesh_manifest.csv`；
3. OpenUSD：`USD/<package>` 中的 `robot.usd`、`name_map.json` 和 `export_report.json`；
4. MJCF：`MuJoCo/<robot>` 中的 `robot.xml`、`scene.xml`、`name_map.json` 和
   `export_report.json`；
5. 在实际目标应用中检查 Visual、Collision、Inertia、COM、轴和 Joint 运动。

导出成功表示文件已经生成并通过基础结构检查。它不代替目标软件中的最终检查，也不会自动完成
控制器、接触参数、任务或强化学习配置。
