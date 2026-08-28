# Collision

## 原则

Collision 服务仿真接触和物理求解，不应机械复制 Visual。目标是使用尽可能简单的几何，同时
保留任务相关接触形状。轮胎接地、夹爪接触、底盘离地间隙等关键区域不能因过度简化而丢失。

Collision 和 Inertial 独立：改变碰撞策略不会改变质量、COM 或惯性张量。

## 策略

| UI/配置策略 | 用途 | 正式输出 |
| --- | --- | --- |
| `VisualMesh` | 最大兼容或查看 | 复制 Visual mesh 作为 Collision mesh |
| `SimplifiedMesh` | 原语不适合但希望降低网格成本 | 使用更粗 STL tessellation；失败时回退 |
| `AccurateMesh` | 必须保留接触细节 | 使用更精细 Collision STL；成本最高 |
| `BoxPrimitive` | 底盘、板、盒体、支架 | URDF box/对应几何 |
| `CylinderPrimitive` | 轮子、轴、管、圆柱壳 | URDF cylinder/对应几何 |
| `SpherePrimitive` | 球形传感器或结构 | URDF sphere/对应几何 |
| `ComponentBoxes` | 多组件装配体的稳定默认近似 | 多个组件 Link-local 包围盒 |
| `ConvexHull` | 单一复杂但可凸近似的形状 | 由 Link-local 顶点/三角面生成凸包 STL |

配置中的历史 `Primitive` 值是兼容入口，不应作为新的用户策略名称传播。

## 推荐顺序

1. 装配体先尝试 `ComponentBoxes`。
2. 规则外形使用 Box/Cylinder/Sphere。
3. 单一复杂外形使用 `ConvexHull`。
4. 原语不满足接触需求时使用 `SimplifiedMesh`。
5. 只有确实依赖完整表面细节时使用 `AccurateMesh`。

文件更大不代表仿真更真实。复杂碰撞网格会增加接触对数量、求解成本和数值不稳定风险。

## 几何拟合

原语尺寸来自所选 Body 的 Link-local 几何范围，不来自等效惯性长方体：

- Box 使用几何包围范围；
- Cylinder 选择径向尺寸最接近的轴，并以剩余方向作为厚度；
- Sphere 使用最大包围尺度；
- ComponentBoxes 为各组件生成独立 box；
- ConvexHull 使用内存中的 Link-local 点和三角形。

这保证碰撞策略响应用户选择的组件，但仍属于近似。应在目标任务视角检查接触区域。

## SolidWorks 预览

所有用户可选策略都有临时显示路径：

- 原语、ComponentBoxes 和 ConvexHull 使用 Modeler 创建临时 BREP/sheet body；
- Visual/Accurate/Simplified mesh 预览复制非破坏性的 CAD body；
- Simplified 的最终 STL tessellation 可能比预览 CAD body 更粗；
- 预览不写回装配体，不改变源组件外观，并在关闭/切换时释放临时体。

预览目标是快速选择策略，不承诺 mesh 策略的预览与最终 STL 字节级一致。ConvexHull 预览与
写出器共享 Link-local 几何构建结果，但最终文件仍应通过 manifest 和外部 Viewer 检查。

## STL 与 3DXML

原生原语、ComponentBoxes、ConvexHull 和简化碰撞的维护路径以 STL 为基础。3DXML 支持用于
Visual 交换；不要把它描述为本项目已经验证的通用 Collision/DAE 纹理方案。

## 回退与报告

策略生成失败时，导出器回退到 `VisualMesh`，并分别记录：

- requested strategy；
- effective strategy；
- fallback reason；
- mesh 文件和统计信息。

检查 `config/mesh_manifest.csv` 和 `config/export_report.md`。如果 effective 与 requested 不同，
必须先理解回退原因，再接受进入 MuJoCo、Isaac Sim 或其他求解器的模型。
