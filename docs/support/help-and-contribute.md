# 提问与贡献代码

项目地址：<https://github.com/osrbot/solidworks_urdf_exporter_pro>

## 提交问题

在 [GitHub Issues](https://github.com/osrbot/solidworks_urdf_exporter_pro/issues) 新建问题，并提供：

1. SolidWorks 年份版本和 Service Pack。
2. 插件版本、提交号或安装包文件名。
3. 可以重复执行的操作步骤。
4. 期望结果和实际结果。
5. 完整错误文字，不要只提供一张截断的弹窗截图。
6. `%USERPROFILE%\sw2urdf_logs\sw2urdf.log`。
7. 对应导出目录中的 `export_report`、惯性和网格报告。
8. 许可允许时提供最小装配体或去除敏感信息后的示例。

坐标、惯性或运动方向问题还应说明参考坐标系、单位和正确结果应是什么。

## 提交功能建议

说明实际工作流程、当前做不到的步骤和期望输出。优先描述要解决的问题，不要只写一个模糊功能
名称。若涉及特定 ROS、Isaac Sim、MuJoCo 或 SolidWorks 版本，请明确版本和验证环境。

## 贡献代码

1. Fork 项目并从当前维护分支创建功能分支。
2. 保持修改范围集中，不顺手重写无关模块。
3. 为转换规则、校验和错误路径增加测试。
4. UI 修改同时检查中文、英文、常用 DPI 和窗口尺寸。
5. 更新对应用户文档和 Changelog。
6. 提交 Pull Request，写清问题、实现、测试结果和仍未验证的范围。

构建与测试命令见仓库中的
[贡献说明](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/docs/wiki/Contributing-zh-CN.md)。

## 贡献文档

文字应优先回答“用户要做什么”和“会得到什么”。避免把内部类名、临时数据格式和尚未实现的计划
写进普通用户页面。截图不得包含个人路径、邮箱、令牌或无关窗口。
