---
title: SW2URDF 使用文档
description: 从 SolidWorks 导出机器人模型的实用说明
---

# SW2URDF 使用文档

SW2URDF 用于把 SolidWorks 装配体整理成机器人描述文件。你在插件中确认 Link、Joint、坐标系、
质量、碰撞和外观，然后选择导出 ROS、OpenUSD 或 MuJoCo 文件。

## 为什么使用这个版本

原社区版建立了 SolidWorks 到 URDF 的基础流程，但在新版 SolidWorks、深层装配体、中文名称、
碰撞检查和现代仿真格式上存在不少实际使用问题。当前版本重点解决这些问题：

- 深层组件中的坐标系和轴可以稳定识别，同名或中文名称不再依赖文字猜测。
- Joint 类型、轴和限位由用户明确确认，避免把固定装配或 STEP 模型误判。
- 质量、质心和惯性增加检查，碰撞几何可以在导出前预览。
- 外观颜色、碰撞设置和惯性设置分开，不再挤在同一页面。
- 可以直接导出 ROS 1、ROS 2、OpenUSD 和 MuJoCo MJCF。
- 中文界面、错误说明和导出报告更容易看懂，页面切换也更流畅。

[查看与社区原版的具体差异](/guide/whats-new)

## 第一次使用

1. [安装插件](/guide/installation)
2. [按快速开始完成一次导出](/guide/getting-started)
3. [逐页了解各项设置](/features/link-tree)
4. [选择真正需要的输出](/exports/)

## 四种输出

<div class="output-list">
  <div><strong>ROS 1 功能包</strong><p>用于已有 ROS 1 机器人描述项目。</p></div>
  <div><strong>ROS 2 功能包</strong><p>用于 ROS 2 描述、显示和后续控制配置。</p></div>
  <div><strong>OpenUSD 机器人资产</strong><p>用于 Isaac Sim 或其他支持 USD 的工具。</p></div>
  <div><strong>MuJoCo MJCF 模型</strong><p>用于继续搭建 MuJoCo 场景和控制。</p></div>
</div>

## 项目与反馈

项目地址：<https://github.com/osrbot/solidworks_urdf_exporter_pro>

遇到问题时请提供 SolidWorks 版本、插件版本、复现步骤、日志和导出报告。具体格式见
[提问与贡献代码](/support/help-and-contribute)。

当前维护分支由 [kitso666](https://github.com/kitso666)、[W472351926](https://github.com/W472351926)、
[dajianli](https://github.com/dajianli) 和 [sunmaxwll](https://github.com/sunmaxwll)
共同贡献。完整记录以仓库的
[贡献者名单](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/CONTRIBUTORS.md)
和 Git 历史为准。
