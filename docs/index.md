---
title: SW2URDF 文档
description: SolidWorks 机器人模型导出手册
---

# SW2URDF 文档

SW2URDF 在 SolidWorks 中读取用户明确配置的 Link、Joint、坐标系、质量属性、外观与碰撞几何，
再导出 ROS、OpenUSD 或 MuJoCo 可以继续使用的机器人资产。

::: tip 先说清楚职责
插件负责整理 CAD 数据、生成目标文件并完成约定的结构检查。它不会替用户猜控制器参数，也不会
把一个机器人模型包装成完整的仿真或强化学习工程。
:::

## 从这里开始

1. 第一次使用：按[快速开始](/guide/getting-started)走完一次完整导出。
2. 不清楚 Link、Joint 或碰撞怎么填：看[模型配置](/guide/model-setup)。
3. 不确定该勾选哪种输出：看[先选对输出](/exports/)。
4. 遇到失败：先看输出根目录的 `export_report.md`，再查[常见问题](/wiki/Troubleshooting-zh-CN)。

## 可以得到什么

<div class="output-list">
  <div><strong>ROS 1 功能包</strong><p>URDF、网格、配置和导出报告。</p></div>
  <div><strong>ROS 2 功能包</strong><p>现代 ROS 2 描述包及可选控制配置。</p></div>
  <div><strong>OpenUSD 机器人资产</strong><p>可移动目录中的 robot.usd、几何依赖和报告。</p></div>
  <div><strong>MuJoCo MJCF 模型</strong><p>robot.xml、scene.xml、网格资产和报告。</p></div>
</div>

这些是四种独立交付物。内部使用的 Robot Bundle 不显示为导出目标，也不会写进用户选择的输出
目录。

## 当前重点

- 三步向导分别处理 Joint、Link 和最终导出，页面切换不执行文件导出工作。
- 外观从碰撞设置中分离，RGBA、选色与按 Link 层级自动配色集中在同一页。
- OpenUSD 设置只在需要时打开，用于基座、自碰撞和单自由度 Joint 驱动意图。
- `robot.usd` 使用 UTF-8 可读文本，几何依赖使用相对路径；移动时应整体复制 `USD/<package>`。
- 导出器给出结构与运行时检查结果，但目标应用中的控制、接触和任务效果仍需用户确认。

完整变化见[本次主要变化](/guide/whats-new)，兼容性与验证边界见[版本与验证](/reference/versions)。
