# Collision

**简体中文** | [English](Collision)

## 原则

Collision 服务仿真接触和物理求解，不应机械复制 Visual。目标是使用尽可能简单的几何，同时
保留任务相关接触形状。轮胎接地、夹爪接触、底盘离地间隙等关键区域不能因过度简化而丢失。

Collision 和 Inertial 独立：改变碰撞策略不会改变质量、COM 或惯性张量。

## 策略

| UI/配置策略 | 用途 | 正式输出 |
| --- | --- | --- |
| `VisualMesh` | 最大兼容或查看 | 复制 Visual mesh 作为 Collision mesh |
| `SimplifiedMesh` | 原语不适合但希望降低网格成本 | 与可视 STL 使用同一目标减面比例；失败时回退 |
| `AccurateMesh` | 必须保留接触细节 | 不做减面的碰撞 STL |
| `BoxPrimitive` | 底盘、板、盒体、支架 | 引用箱体 STL |
| `CylinderPrimitive` | 轮子、轴、管、圆柱壳 | 引用圆柱 STL |
| `SpherePrimitive` | 球形传感器或结构 | 引用球体 STL |
| `ComponentBoxes` | 多组件装配体的稳定默认近似 | 多个组件 Link-local 包围盒合并为一个 STL |
| `ConvexHull` | 单一复杂但可凸近似的形状 | 由 Link-local 顶点/三角面生成凸包 STL |

配置中的历史 `Primitive` 值是兼容入口，不应作为新的用户策略名称传播。

碰撞近似写入 `meshes/collision/<link>.STL`，每个 Link 引用一个碰撞网格，不再在 URDF 中展开
组件包围盒列表。中心和轴向已写入 Link-local 顶点，因此生成的基本体网格使用零位移、零旋转。
这能缩短机器人描述文本，但不能据此认定消息长度就是 TF 故障的原因。合并网格与多个原生
基本体在仿真器中的接触处理可能不同，尤其是非凸形状；需要在目标仿真器中验证接触行为。

## 推荐顺序

STL 精简比例旁显示目标剩余大小，例如精简 67% 对应约剩余 33%。导出结果显示各 Link 的实际
原始大小和最终大小。相连区域分开减面，未通过校验的区域保留原始几何，并显示警告。
“精简比例”是目标移除比例，不是文件大小保证：0% 不精简，100% 尽可能精简，实际大小以导出结果为准。
没有当前 STL 实测大小时只显示百分比；精简不改变质量、质心或惯性。

1. 装配体先尝试 `ComponentBoxes`。
2. 规则外形使用 Box/Cylinder/Sphere。
3. 单一复杂外形使用 `ConvexHull`。
4. 原语不满足接触需求时使用 `SimplifiedMesh`。
5. 只有确实依赖完整表面细节时使用 `AccurateMesh`。

文件更大不代表仿真更真实。复杂碰撞网格会增加接触对数量、求解成本和数值不稳定风险。

## 示例装配体实测

用户使用 `5656c5d77e18` 版本完成导出，精简比例为 70%，SolidWorks 网格粗细为粗糙。
ROS 1、ROS 2、OpenUSD 和 MuJoCo 四个目标全部成功，总耗时 8 分 38 秒。
以下比较使用本次导出记录的精简前 STL 大小，不与旧导出文件夹混比。MB 按 1,000,000 字节计算。

| 可视 STL | 精简前 | 导出后 | 大小减少 |
| --- | ---: | ---: | ---: |
| 大型 Link | 32.10 MB | 10.64 MB | 66.86% |
| 复杂小型 Link | 6.36 MB | 4.33 MB | 31.91% |
| 全部可视 STL | 41.35 MB | 16.20 MB | 60.82% |

实际总大小剩余约 39.18%，不是目标的 30%。十个 Link 中八个未达到 70% 移除目标，
仅两个较小 Link 接近目标。这是形状保护下的部分精简，不等于导出失败。
ROS 网格引用和校验和有效，四个目标的对应 STL 哈希一致。
独立 MuJoCo 检查重新加载 `robot.xml` 和 `scene.xml` 并分别执行一次零控制步；OpenUSD
结构重开确认 17 个依赖层、16 个非空网格且无未解析依赖。未运行 Isaac Sim，未验证长时间仿真或训练。
大型 Link 和复杂小型 Link 固定视角对比未见明显几何缺失，但不构成严格全表面误差或自相交检查。

本次验证发现两类报告问题：四个原生圆柱轮子的 CSV 记录了暂存碰撞 STL 大小和
`collision_exists=true`，但最终包不需要这些 STL，可读报告正确显示为 false；
顶层警告重复列出大型 Link 和复杂小型 Link 信息，未区分可视与碰撞且仍为英文。
这些报告问题不表示轮子丢失或重复发生几何失败，应以实际输出和各 Link 结果核对。

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
- Simplified 的最终 STL 按共用比例减面；CAD 预览仅作为外形参考；
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
