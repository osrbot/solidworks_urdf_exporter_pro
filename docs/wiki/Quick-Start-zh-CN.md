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
`fixed`。正式导出前必须解析成标准类型。
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

设置 ROS 包名、模型许可证、维护者和精确版本。选择 Lyrical/Jetty 或 Jazzy/Harmonic；需要
ros2_control 时选择显式控制 profile，需要 Isaac Lab 时同时选择 Isaac Sim、精确版本和 actuator
profile。Robot Bundle 始终生成，ROS 1、ROS 2 和 Isaac 为可选派生目标。进度窗口保持在
SolidWorks 前方并阻止导出重入；完成窗口显示本次变化文件数、总大小、耗时和输出根目录。
“导出 URDF（不含网格）”是例外：它仅供 XML 调试，使用轻量兼容路径，不生成
Robot Bundle、Isaac 或新 profile。

## 8. 检查结果

依次查看：

1. `Bundle/<package>.osurdf/manifest.json` 与 `checksums.sha256`：载荷、profile 和完整性；
2. `reports/validation.json`：规范模型阻断项与警告；
3. 输出根目录 `export_report.md`：本次 v2 导出总览（只选 Bundle 时也存在）；所选 ROS 包内的
   `config/export_report.md` 保留包级副本；
4. `config/inertial_validation.csv`：质量、COM、张量和误差；
5. `config/mesh_manifest.csv`：请求/实际碰撞策略、文件和网格记录；
6. URDF Viewer、MuJoCo、Isaac Sim 或目标求解器中的 Visual、Collision、Inertia、COM、轴和
   Joint 运动。

导出成功只表示插件校验和文件写入完成，不替代目标仿真器中的工程验证。
