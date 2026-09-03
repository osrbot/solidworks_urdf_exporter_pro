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

设置页可以选择基座方式、是否允许自碰撞，以及每个可动 Joint 使用被动、位置、速度或力控制
意图。刚度和阻尼只使用用户填写的值，插件不会根据 CAD 外形猜控制参数。

![OpenUSD 设置](/screenshots/openusd-settings.png)

## 路径和编码

主文件使用可读的 UTF-8 文本，几何文件使用相对路径。完整目录可以复制到另一台电脑，不依赖原
导出位置的盘符或用户目录。

![OpenUSD 本地预览](/screenshots/openusd-local-preview.png)

<p class="caption">从 robot.usd 载入几何的本地检查结果。</p>

## 导出后检查

插件会重新打开生成的 USD 并检查文件引用。进入 Isaac Sim 后，仍需确认材质、碰撞、关节驱动、
接触参数和实际任务行为。
