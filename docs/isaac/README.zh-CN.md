# 面向下游 Isaac 工作流的 OpenUSD 资产

[English](README.md) | **简体中文**

本页用于界定边界，不定义 Isaac 专用导出器合同。

SolidWorks 导出器可以生成 `USD/<package>/robot.usd` 及其几何依赖、保留的源网格、
`name_map.json` 和 `export_report.json`。生成过程使用安装包固定的 OpenUSD 运行时；只有该运行时
重新打开 stage 并检查预期机器人结构后，导出才成功。

插件不会：

- 要求或检测 Windows CAD 工作站上的 Isaac Sim/Isaac Lab；
- 要求填写 Isaac Sim/Isaac Lab 版本；
- 调用 Isaac 专用 importer API 或扩展；
- 生成 actuator group、PID 增益、控制器配置、任务环境、传感器、观测、奖励、重置、域随机化
  或强化学习代码；
- 声称资产已经在 Isaac Sim/Isaac Lab 中完成导入、渲染或仿真。

下游使用时，应将完整 `USD/<package>` 目录复制到目标机器，按目标 Isaac 版本的官方流程导入
`robot.usd`，并在该实际环境中检查 articulation 映射、单位、材质、Collision 行为、质量属性、
控制器设置和任务行为。

证据术语有意保持收敛：

- **已生成 OpenUSD**：约定文件已经写出。
- **已重开 OpenUSD**：内置 OpenUSD 运行时已重开并检查 stage 结构。
- **已通过 Isaac 验证**：本插件不执行。

完整文件布局与映射边界见 Wiki [OpenUSD 页面](../wiki/OpenUSD-zh-CN.md)。
