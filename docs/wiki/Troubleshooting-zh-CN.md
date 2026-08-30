# Troubleshooting

**简体中文** | [English](Troubleshooting)

## 导出器菜单不存在

- 确认安装包为 x64，并以管理员身份安装。
- 完全关闭并重启 SolidWorks；当前安装器不支持热加载。
- 检查 SolidWorks Add-Ins 中是否存在并启用 SW2URDF。
- 查看 `%USERPROFILE%\sw2urdf_logs\sw2urdf.log`。

## Link Tree 编辑后丢失

- 已保存装配体：检查 `%LOCALAPPDATA%\OSRBot\SW2URDF\export-drafts` 是否存在恢复草稿。
- 未保存装配体：没有稳定完整路径，无法使用按装配体隔离的恢复草稿。
- 组件被删除、替换或 Save As：persistent reference PID 可能失效，需要重新绑定。
- `Apply` 与 `Cancel` 是事务边界；取消画布不会提交结构变更。

## 报重复 Joint，但树中看不出重复

- 根 Link 不应拥有 parent Joint；旧配置中的隐藏根 Joint 会在加载修复路径中清除。
- 检查 Mimic 引用和大小写不同但规范化后冲突的名称。
- 使用画布/Outline 的完整验证结果，不要只检查当前展开的 UI 分支。

## 惯性为零或导出被阻止

1. 在 SolidWorks Mass Properties 中确认所选组件有材料/密度和非零质量。
2. 确认 Link 绑定的是目标 Body，不是父子组件重复选择或空选择。
3. 确认 Link frame 坐标系真实存在且选择正确。
4. 查看 `config/inertial_validation.csv` 的质量、COM、张量、主惯量和误差。
5. 若提示 COM 超出组件范围，优先检查组件选择和坐标变换，而不是手工修改 URDF 数值。

SolidWorks COM/RPC 异常可能使 Live API 测试或预览失败。重启 SolidWorks 后重试；不要把显示层
故障等同于数值层通过或失败。

## 惯性预览或碰撞预览不可见

- 临时体需要有效、可见的顶层 Part 显示宿主；顶层子装配体本身不是有效宿主。
- 尝试线架图、隐藏线可见或着色视图确认显示状态。
- 切换 Link 后重新开启预览，避免观察旧 Link 的临时体。
- 预览报错时记录 SolidWorks `Display3` 返回码和日志。
- 预览不可见不代表正式数值或文件一定错误，继续检查报告；但不要在未确认的情况下发布模型。

## 碰撞策略回退

打开：

- `config/mesh_manifest.csv`：requested/effective strategy 和文件记录；
- `config/export_report.md`：fallback 汇总和原因。

生成失败会回退到 `VisualMesh`。不要只看 UI 中最后选择的策略。

## 导出文件缺失或 ROS2 meshes 不完整

- 优先查看 `export_report.md`；
- 确认输出目录可写且没有被其他程序锁定；
- 查看完成摘要中的本次变化文件数；
- 检查 URDF 的 `package://` 路径与 ROS1/ROS2 包名；
- 复用旧目录时，摘要只统计本次新增或变更文件，不统计无关旧文件。

## 测试失败

- Pure tests 不应要求 SolidWorks。
- Live tests 需要兼容的本地 SolidWorks 和可用 COM/RPC。
- `RPC 服务器不可用` 通常说明 SolidWorks 进程不可达或由自动化过程终止，不等于被测纯算法失败。
- TestRunner 使用临时目录中的进程专用 UTF-8 日志，避免与运行中的插件争用常规日志。

## 构建问题

- 检查 `SolidWorksInstallDir` 是否指向包含匹配 Interop DLL 的实际安装目录。
- 使用 x64 和 .NET Framework 4.8。
- 若资源/Interop 工具缺失，安装 Visual Studio `.NET desktop development` 和对应 SolidWorks API Tools。
- Release 安装包必须使用 Inno Setup 6.3.0–6.3.3；更新版本会被当前可审计打包脚本拒绝。
