# OpenUSD 机器人资产

## 第一职责

OpenUSD 输出的第一职责是一个可移动、可重新打开的机器人资产。它不是 Isaac Sim 扩展，也不是
Isaac Lab 训练工程，因此不要求用户在 Windows 上安装 Isaac，也不要求填写上游应用版本。

## 输出目录

```text
USD/<package>/
|-- robot.usd
|-- geometry/
|-- meshes/
|-- name_map.json
`-- export_report.json
```

`robot.usd` 保存层级、几何引用、刚体、质量、质心、惯性和 Joint。`geometry/` 是 USD 网格依赖，
`meshes/` 保留源 STL 证据。所有内部资产路径均应为相对路径。

::: warning 搬运方式
请整体复制 `USD/<package>`。只复制 `robot.usd` 会丢失它引用的几何文件。
:::

## 可选仿真意图

设置对话框可以记录基座模式、自碰撞、机器人分类和单自由度 Joint 的驱动意图。被动 Joint 不
创建主动 DriveAPI；位置和速度驱动只使用用户明确填写的刚度、阻尼与限位。插件不会从 CAD 猜
控制增益。

![OpenUSD 仿真设置](/screenshots/openusd-settings.png)

## 自动检查到哪里

安装包固定使用 `usd-core 26.8` 生成并重新打开 stage，检查 Link、Joint、刚体、几何解析和相关
schema。该结果证明资产结构可读，不证明已经在某个 Isaac Sim 版本中完成物理或任务验证。

更完整的 Stage 合同见 [OpenUSD Wiki](/wiki/OpenUSD-zh-CN)。
