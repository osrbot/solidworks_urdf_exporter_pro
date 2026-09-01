# Joint 语义与来源策略

## 结论

OSURDF 采用“人工定义为主、识别建议为辅、导出前必须确认”的策略。STEP 等去参数化中间格式
通常不包含装配 Mate，更不包含 URDF Joint 类型；固定装配或人形机器人外形装配也不能仅凭
当前没有运动自由度推断为 `fixed`。工具必须保留“不知道”，不能生成看似完整但语义错误的模型。

## 可信来源分级

| 来源 | 可提供的信息 | 默认状态 | 导出要求 |
| --- | --- | --- | --- |
| 用户显式配置 | Joint 类型、轴、限制、控制含义 | `manual_configuration` | 通过数值和拓扑校验后可导出 |
| 原生 SolidWorks 可动装配 Mate | 约束、剩余自由度、参考几何 | `solidworks_mate_suggestion` | UI 展示证据，用户确认后可导出 |
| 已有 URDF | 原文件中的 Joint 语义 | `imported_urdf` | 校验来源、图结构和数值后可导出 |
| 旧版插件配置 | 历史人工数据 | `legacy_configuration` / `migrated_config` | 迁移后要求用户复核；缺失证据不视为已确认 |
| STEP/中性 CAD/纯几何 | 形状和层级，偶尔有装配实例 | `geometry_only` | 不允许自动补 Joint 类型 |

每个 Link/Joint 都保存稳定 ID 和 `source.kind`、`source.evidence`、`source.reference`、
`source.userConfirmed`。报告和内部规范暂存继续携带这些字段，方便回溯“参数从哪里来、谁确认过”。

## Mate 建议的安全边界

只在原生、已解析、确实可动且证据单一的装配中生成候选：

- 单一转轴剩余自由度可建议 `revolute` 或 `continuous`，但上下限不能凭几何猜测；
- 单一平移剩余自由度可建议 `prismatic`，但行程、速度和 effort 仍需人工输入；
- 多自由度、柔性子装配、齿轮/凸轮/路径 Mate、矛盾或抑制 Mate 只报告证据，不给强结论；
- 零剩余自由度可能是固定、完全约束、错误约束或运动语义丢失，不能自动写成 `fixed`；
- Mimic、闭环、差动、并联机构和传动比必须由明确工程关系定义。

候选在 UI 中必须显式标为“建议”，最终保存时转换为标准 URDF 类型并记录用户确认。未确认的
Mate 建议由验证器以阻断错误报告。
当用户在 Joint 页选择 `Automatically Detect` 并点击“下一步”时，插件会在可恢复的装配状态中
运行识别，回填类型、轴和证据，然后停留在 Joint 页。用户需逐个打开这些 Joint 并显式保存
最终类型；旋转候选先显示为 `continuous`，如果实际存在位置边界，必须改为 `revolute` 并填写
limit。识别失败时恢复原 Joint 配置，不保留半完成建议。

## 人形机器人

人形机器人 CAD 常用于结构、外观、布线或加工，装配本身可能全部固定；实际控制关节则由电机、
减速器和控制架构定义。对这类模型，推荐流程是：

1. CAD 提供 Link 几何、质量和坐标参考；
2. 设计者按电机/减速器/编码器定义 Joint、限制和传动；
3. 下游 ROS、Isaac 或 MuJoCo 项目使用实测或项目批准的硬件接口、控制器、stiffness、
   damping、effort 和 velocity 参数；
4. 在目标仿真器检查轴方向、限位、自碰撞、足底接触和控制稳定性。

固定 CAD 装配并不是缺陷，但它不能替代机器人运动学模型。

## 验收规则

- 空白或非标准 Joint 类型阻断导出；
- `Automatically Detect` 不是可写入 URDF 的最终类型；
- 未确认的 Mate 建议阻断所选导出目标；
- 非 fixed Joint 必须具有适用的轴、limit 和有限数值；
- 控制器、actuator、gain 和任务限位属于下游工程配置，不由 CAD 几何猜测。
