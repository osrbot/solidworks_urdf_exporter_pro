# Robot Bundle 规范暂存架构

> **当前状态（2026-09-01）**：本文记录内部规范模型的架构。Robot Bundle 已收敛为插件私有、
> 短生命周期的暂存表示，不再是 UI 可选目标，也不交付到用户输出目录。现行用户目标和验证边界以
> [README](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/README.zh-CN.md) 与
> [Wiki 首页](../wiki/Home-zh-CN.md) 为准。

本文文件名中的历史 `v2` 不表示当前 `robot.json` schema 版本。SolidWorks 持久化配置使用
`URDF Export Configuration (v2)`，而 Bundle 中当前规范 `robot.json` 使用 `robot.schema.v3`；
两者是独立版本域。

Robot Bundle 是各目标导出器之间共享的内部规范表示。ROS 1、ROS 2、OpenUSD 和 MJCF
导出器从同一份已验证模型派生，避免每个目标各自解释 CAD 数据后产生漂移；暂存目录在成功或失败
后都应清理。

## 目录契约

```text
<robot>.osurdf/
├── manifest.json
├── checksums.sha256
├── robot.json
├── robot.urdf
├── meshes/
│   ├── visual/
│   └── collision/
├── textures/
├── profiles/
│   ├── package.json
│   ├── ros1.json
│   ├── ros2.json
│   ├── isaac.json
│   └── isaaclab.json
└── reports/
    ├── validation.json
    └── cad/
```

`robot.json` 是 SI 单位的规范模型，`robot.urdf` 是互操作入口。`manifest.json` 记录生成器、
模型许可证、启用的目标配置、校验摘要和完整文件清单；`checksums.sha256` 覆盖除自身以外的
每个载荷文件。CAD 报告中的资源位置使用 Bundle 或 `package://` 路径，不保存开发机绝对路径。

## 生成与验证

Bundle 构建执行以下顺序：

1. 将历史 `robot.schema.v2` 输入以保守默认值迁移到 `robot.schema.v3`；v3 输入保持 v3，writer
   只生成 v3，且不会替用户补全 Joint 类型、许可证、控制器或 actuator 参数；
2. 校验 Link/Joint 图、数值、来源、目标版本和目标配置；
3. 解析 `package://` 和相对资源，复制到规范目录并重写为可移植路径；
4. 写入临时目录，生成清单和 SHA-256；
5. 验证临时 Bundle，通过后原子替换目标目录。

验证器拒绝路径穿越、绝对路径、符号链接、大小写冲突、保留文件覆盖、未列入清单的载荷、
校验和不一致、内容与声明 schema 不一致，以及清单和实际 profile 不一致。设置合法的
`SOURCE_DATE_EPOCH` 后，时间戳和文件排序可复现；非法值会直接失败。

## CLI

以下命令面向开发、诊断和测试，不代表桌面插件会向用户交付 Bundle：

```bash
osurdf import-urdf --input robot.urdf --output robot.json
osurdf validate --input robot.json
osurdf bundle --source-urdf robot.urdf --robot robot.json --output robot.osurdf
osurdf verify-bundle --bundle robot.osurdf
osurdf export-ros2 --bundle robot.osurdf --output output/ROS2
```

`import-urdf` 只建立迁移草稿。若 URDF 缺少许可证、明确来源或目标配置，后续验证仍会失败；
这是预期的 fail-closed 行为。

## URDF 规范投影边界

规范模型保留 Link、Joint、inertial、visual、collision、geometry、材质（包括顶层材质引用）、
limit、dynamics 与 mimic。任意厂商自定义 XML、Gazebo 扩展、transmission、传感器插件和复杂并联/
柔性体语义不会被静默当成已支持能力；需要这些数据时，应通过版本化 profile/schema 扩展并增加
对应运行时门禁。`ros2_control` 已采用独立显式 profile，因此不会依赖旧 transmission 的隐式迁移。

## 证据边界

- 内部 Bundle 校验通过：证明暂存结构、数值约束、文件清单和校验和一致；
- ROS 包生成通过：证明插件写出了约定结构，不等于已在 ROS/Gazebo 中启动；
- OpenUSD 自动验证通过：证明固定 OpenUSD 运行时已生成并重开 stage，不等于 Isaac 验证；
- MJCF 自动验证通过：证明固定 MuJoCo 官方工具已完成编译、规范保存、重载和一步零控制推进；
- 机器人运动符合设计：仍需项目级动力学、控制和物理验收。

这些证据不能相互替代。
