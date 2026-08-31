# Link Tree

**简体中文** | [English](Link-Tree)

## 数据所有权

Link Tree 的三个责任分开保存：

- Topology：Link 层级和稳定节点身份；
- URDF configuration：名称、Joint、惯性、Visual、Collision 等可复用配置；
- CAD binding：SolidWorks 对象与 persistent reference PID。

画布只操作工作副本。`Cancel` 丢弃结构变更；`Apply` 在完整验证通过后原子提交。UI 投影不是
独立事实源，配置持久化和 URDF 导出都从已提交会话生成投影。

## 装配体配置

正式配置保存于装配体特征 `URDF Export Configuration (v2)`。显式根文档参考保存
`OwnerScope=RootDocument` + 特征 PID；显式组件实例参考保存 `OwnerScope=ComponentInstance` +
组件 PID + 特征 PID。目录会扫描全部装配深度；Unicode 名称和同名显示标签只用于 UI，不参与
身份判断。解析时先按 PID 找到 owner feature，再通过 `IComponent2.GetCorresponding` 映射到准确的
装配实例，不进行名称查找或活动配置切换。

v2 持久化使用 canonical 与 hidden recovery 两个槽位。写入现有槽位前先以 `revision=0` 使其失效，
更新 payload 后最后写入非零 revision 作为提交标记；每个候选槽位都经过完整校验，加载时选择最新
的有效 revision。因此，中断的 COM 原位写不会替换最后有效配置。停在 `revision=0` 的槽位属于未完成
准备写，加载时忽略，后续保存可直接重试。每个 SolidWorks 会话只缓存完整注册成功的 schema
definition；初始化失败后重试会改用新的唯一定义，部分初始化的 `AttributeDef` 不会污染后续保存。

v1.x 名称型配置无法安全识别深层同名参考几何，因此不会读取或自动迁移。必须删除旧
配置特征、重新创建 Link 树，并逐项审核绑定。PropertyManager 尚未完成时，插件不会创建或替换
正式配置。

重新打开装配体时，组件和参考几何特征分别通过保存的 PID 重连。删除、替换或 Save As 可能使
PID 失效，此时必须人工重新绑定；显示名称或配置名称相同不能证明 CAD 身份相同。

## 画布操作

- 添加、重命名、拖动重设父级；
- 自动布局和框选；
- 复制、粘贴、删除完整分支；
- `Ctrl+C`、`Ctrl+V`、`Delete` 使用相同分支语义；
- 父子选择重叠时合并为不重复的分支集合。

粘贴保留拓扑和可复用 URDF 配置，但主动清除 CAD 组件绑定。将同一个 SolidWorks 实体同时分配
给两个 Link 会制造错误模型，因此新副本保持 incomplete，直到用户绑定新的组件。

Joint 重命名会迁移 Mimic 引用；删除仍被 Mimic 引用的 Joint 会被拒绝。重设父级后，旧父子关系
下计算的 Joint kinematics 和 limits 会被标记为需要重新计算。

## Outline 编辑

一行一个 Link，Markdown 标题深度表示层级：

```text
# base_link
## camera_link
## left_steering_hinge_link
### left_front_wheel_link
## right_steering_hinge_link
### right_front_wheel_link
```

支持 `#base_link` 和 `# base_link`。以下情况不会替换当前有效文档：

- 非法 ROS 名称；
- 重复 Link；
- 多个根节点；
- 标题层级跳级；
- 产生悬空 Mimic 的结构。

新 Link 默认生成 `fixed` Joint；`camera_link` 对应 `camera_joint`，没有 `_link` 后缀的名称追加
`_joint`。已有节点在无歧义匹配或同位置重命名时尽量保留稳定身份、配置和 CAD 绑定。

## 自动配色

`Auto Links` 对当前 UI LinkNode 层级应用整树配色：

- 深度由冷色过渡到暖色；
- 去除 `left/right/lhs/rhs/port/starboard` 侧向词后名称相同的对应 Link 使用相同颜色；
- 生成的 URDF material ID 和 RGBA 立即通过正常配置路径保存；
- 用户之后仍可手动覆盖单个 Link。

自动配色只修改 Visual material ID/RGBA，不修改拓扑、CAD 绑定、Collision 或 Inertial。

## 恢复草稿

窗口在正式保存前关闭时，插件可把当前会话写入：

```text
%LOCALAPPDATA%\OSRBot\SW2URDF\export-drafts
```

草稿按完整装配体路径隔离，并保存 Link/Joint 配置、ROS 包名和最后输出目录。成功保存正式配置或
完成导出后删除草稿。未保存装配体没有稳定路径，因此不支持该恢复机制。
