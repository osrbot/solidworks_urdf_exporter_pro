# OpenUSD 机器人资产

**简体中文** | [English](OpenUSD)

## 为什么使用

OpenUSD 适合把 SolidWorks 机器人带入 Isaac Sim 或其他支持 USD 的工具。插件生成机器人资产，
不要求导出电脑安装 Isaac Sim，也不要求填写 Isaac Sim 或 Isaac Lab 版本。

## 交付文件

```text
USD/<package>/
|-- robot.usd
|-- geometry/                 # robot.usd 引用的 USD 几何
|-- meshes/                   # 保留的源 STL
|-- name_map.json             # 原名称和导出名称的对应关系
`-- export_report.json        # 导出数量和检查结果
```

`robot.usd` 包含机器人层级、Visual、Collision、质量、质心、惯性和 Joint。

## OpenUSD 设置

主导出页只显示一个 OpenUSD 选项。勾选后，可以按需打开设置：

- **基座方式**：保持源模型、固定基座或浮动基座；
- **机器人类型**：为下游工具提供分类；
- **自碰撞**：是否允许机器人各 Link 之间发生碰撞；
- **Joint 驱动**：被动、位置、速度或力控制意图；
- **刚度和阻尼**：只使用用户明确填写的值，不根据 CAD 外形猜测。

不确定时可以保留默认设置，先导出并在目标工具中检查。

## 路径和编码

主文件使用可读的 UTF-8 文本，几何引用使用相对路径。复制到其他电脑时必须整体移动
`USD/<package>` 文件夹；只复制 `robot.usd` 会丢失几何。

## 自动检查

插件会在交付前重新打开 `robot.usd`，确认层级、Joint 和本地几何引用可以读取。该检查只说明
USD 文件结构完整，不表示模型已经在某个 Isaac Sim 版本中完成物理、控制或任务验证。

## 导出后检查

在目标工具中确认：

1. Link 层级和缩放；
2. Visual 与材质；
3. Collision 和接触；
4. 质量、质心和惯性；
5. Joint 方向、限位和驱动；
6. 实际控制器和任务行为。
