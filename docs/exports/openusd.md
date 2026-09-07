# OpenUSD

## 为什么使用

OpenUSD 适合把 SolidWorks 机器人带入 Isaac Sim 或其他支持 USD 的工具。插件直接生成机器人
资产，不要求导出电脑安装 Isaac Sim，也不要求填写 Isaac Sim 或 Isaac Lab 版本。

## 输出目录

```text
USD/<package>/
|-- robot.usd
|-- geometry/
|-- meshes/
|-- name_map.json
`-- export_report.json
```

`robot.usd` 是主入口，`geometry/` 保存它引用的 USD 几何，`meshes/` 保留源 STL。

::: warning 移动文件时
请整体复制 `USD/<package>` 文件夹。只复制 `robot.usd` 会丢失几何依赖。
:::

## OpenUSD 设置

“仿真设置”的公共部分选择基座方式和单自由度 Joint 的被动、位置、速度或力控制意图；
OpenUSD 独立标签页保存刚度、阻尼、自碰撞和机器人类型。不会使用 MJCF 标签页的增益。

公共设置缺省或为 `null` 时保持旧 USD 行为。公共基座为 `source` 时保留 USD 原设置，
`fixed` / `floating` 则覆盖它。公共配置存在时，其 Joint 列表是完整控制意图：未列出的关节按
被动处理，空列表清空全部驱动；旧 USD 条目只提供增益。主动 mimic 意图会报错。
`fixed` 增加连接世界的固定关节，`floating` 不注入该关节，不删除源模型内部关节。

默认不创建主动驱动。位置/速度模式使用 USD DriveAPI；速度模式刚度为零。
刚度和阻尼使用 SI 单位：转动增益输入按弧度计，写入 USD 时乘以 `pi/180`；平移增益保持不变。
切换到被动/力模式不写入刚度、阻尼或 DriveAPI。力模式只记录运行时控制意图和 effort 限值，
并在 `export_report.json` 中报告，仍需下游控制器实际施力，不等于已创建力执行器。
非法类型、重复或不存在的关节、非有限增益会报错，不会静默忽略。插件不猜控制参数，
不生成训练世界，也不声称已完成 Isaac Sim 的运行验证。

![OpenUSD 设置](/screenshots/openusd-settings.png)

## 路径和编码

主文件使用可读的 UTF-8 文本，几何文件使用相对路径。完整目录可以复制到另一台电脑，不依赖原
导出位置的盘符或用户目录。

![OpenUSD 本地预览](/screenshots/openusd-local-preview.png)

<p class="caption">从 robot.usd 载入几何的本地检查结果。</p>

## 导出后检查

插件会重新打开生成的 USD 并检查文件引用。进入 Isaac Sim 后，仍需确认材质、碰撞、关节驱动、
接触参数和实际任务行为。
