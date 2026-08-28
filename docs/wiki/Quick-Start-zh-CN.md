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
- 每个组件只应绑定到一个 Link，父/子组件引用不能重复承担两个 Link 的几何职责。

## 4. 配置 Joint

检查每个非根 Link 的：

- Joint 名称；
- `fixed`、`revolute`、`continuous`、`prismatic`、`floating`、`planar` 类型；
- 父/子 Link；
- Origin、Axis；
- Limit、Dynamics；
- 可选 Mimic 关系。

`Automatically Detect` 是导出器配置态，不是合法的最终 URDF Joint 类型。正式导出前必须解析成
标准类型。

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

设置 ROS 包名和输出目录，然后导出 ROS1/ROS2 包。进度窗口保持在 SolidWorks 前方，并阻止
导出重入。完成窗口显示本次变化文件数、总大小、耗时和输出根目录。

## 8. 检查结果

依次查看：

1. `config/export_report.md`：总览、失败项、碰撞回退；
2. `config/inertial_validation.csv`：质量、COM、张量和误差；
3. `config/mesh_manifest.csv`：请求/实际碰撞策略、文件和网格记录；
4. URDF Viewer、MuJoCo、Isaac Sim 或目标求解器中的 Visual、Collision、Inertia、COM、轴和
   Joint 运动。

导出成功只表示插件校验和文件写入完成，不替代目标仿真器中的工程验证。
