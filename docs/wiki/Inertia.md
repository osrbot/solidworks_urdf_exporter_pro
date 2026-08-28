# Inertia

## URDF 需要的量

每个 Link 的 `<inertial>` 包含：

- `mass`：千克；
- `origin xyz`：质心在 Link 坐标系中的位置，米；
- `origin rpy`：惯性坐标系相对 Link 的方向；
- `ixx ixy ixz iyy iyz izz`：关于质心的对称惯性张量，`kg*m^2`。

URDF 存储的是关于质心的张量。Joint 轴处所需的动力学量由求解器结合质心位置、Link/Joint
变换和平行轴定理处理，插件不应把关于 Joint 原点的张量直接写入 URDF。

## Link 坐标系

- 根 Link 使用 `Origin_global` 或用户明确选择的根坐标系。
- 非根 Link 通常使用其 child-Joint 坐标系作为 Link frame。
- 坐标系必须由用户在 SolidWorks 中创建；插件不会从几何自动猜测工程参考系。
- 下拉框只列出当前工程真实存在的坐标系。

更改 Link frame 后，插件重新计算并保存：

- COM 在新 Link frame 中的坐标；
- COM 惯性张量在新 frame 方向下的旋转结果；
- 相邻 Joint origin 关系。

质量和主惯量不应因为纯坐标系变换而改变。

## SolidWorks API 路线

维护实现明确启用系统单位，并把两类读取分开：

1. 一个 MassProperty 对象读取质量和 COM；
2. 独立对象读取关于 COM 的惯性张量；
3. 两者从文档坐标系统一转换到选定 Link frame。

分开对象是针对 SolidWorks 2023 Live API 中实际观察到的读取顺序问题：同一对象先读张量再读
COM，或反向读取，可能使缓存结果异常。该实现是工程规避，不是对所有 SolidWorks 版本内部
行为的概括。

## 符号约定

SolidWorks 质量属性对话框中的惯性积显示容易让人额外加一次负号，但本插件调用的
`GetMomentOfInertia` API 已返回选定坐标系下的物理对称张量。当前实现将 3x3 API 数组抽取为
URDF 的 `ixx ixy ixz iyy iyz izz`，并保留非对角项符号，不做第二次取反。随后把该张量的
特征值与 API 主惯量比较；任何多余的符号转换都会使这项独立校验失败。

概念参考：Winter，
[掌握 URDF 中的惯性张量：从 SolidWorks 到强化学习机器人的关键一步](https://zhuanlan.zhihu.com/p/1887859297221845818)。
文章帮助解释“由重心决定并对齐输出坐标系”的张量，以及界面惯性积记号可能造成的混淆；本
项目 API 读取路径、实现与测试才是当前插件符号映射的事实源。

## 坐标变换

关于 COM 的张量只做旋转：

```text
I_link = R * I_document * R^T
```

COM 做刚体点变换。因为输出张量仍关于 COM，插件不会在这里只因 Link frame 原点移动就对
`I_link` 应用平行轴偏移。

## 导出前校验

导出器会检查：

- 数值有限且质量为正；
- 张量对称；
- 主惯量为正并满足刚体三角不等式；
- 张量特征值与 API 主惯量匹配；
- 坐标变换不改变质量和主惯量；
- 计算 COM 位于所选组件的 Link-local 包围范围内。

最后一项用于捕获错误组件和错误坐标变换。凹形或空心刚体的合法质心可能位于空腔中，因此
“在包围范围内”不等于“位于实体材料内部”。

任何物理或数值校验失败都会在写入正式网格/URDF 前停止导出，并标识 Link 和失败项。

## 惯性预览

预览显示 COM、等效惯性长方体和主轴方向，用于检查位置、比例与方向。临时体使用
SolidWorks Modeler/Display3 显示，并要求有效的顶层 Part 作为显示宿主。

预览显示失败不等同于质量属性错误；反之，预览可见也不证明张量物理正确。以
`inertial_validation.csv` 和最终 URDF 为准。
