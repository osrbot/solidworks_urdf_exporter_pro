# MJCF 驱动参数预检查

远端 a422ed7 在 2026-09-08 10:56 导出 Neo 时，两个位置关节和四个速度关节触发 MJCF_POSITION_GAINS / MJCF_VELOCITY_GAIN。ROS1、ROS2、USD 成功。日志不能证明用户未填还是曾填写后丢失；能够确认 UI 允许空值，而 MJCF 后端要求主动驱动有明确增益，报错发生在网格导出之后。

## 修改

- 复用 RobotValidator.ValidateMjcfSimulation，以当前关节描述和目标参数进行纯配置检查，不调用 CAD，不生成网格。
- 设置页标记必填增益的空单元格；不完整配置可经确认保存草稿，默认返回补齐，不自动生成参数。
- 带网格导出入口在保存配置和生成网格前检查已选 MJCF。只有 MJCF 时提示补齐；多目标时允许明确跳过 MJCF，默认返回编辑。
- 不复制 USD 参数、不降级关节模式、不修改 CAD 质量；未选择 MJCF 和旧版无主动驱动配置不受影响。

## 验证

Test|x64 编译通过，关闭 COM 注册及安装操作。配置恢复/预检查 14 项、仿真设置 11 项、原有 OpenUSD 参数与 DPI 回归 8 项，共 33 项通过，无失败或跳过，覆盖不完整草稿、正增益、零位置阻尼、Joint effort 回退、目标取消和恢复错误隔离。

以上为最初的本地专项测试结果。完整构建与远端安装结果见下文；已发布 v20260908-rc1 未替换，远端原装配体未修改。

## 打包前回归补充

并行完整回归曾出现仿真设置区域高度或宽度为 1 像素；相同截图测试单独运行通过。此前试加行高定义、修改标签停靠方式，并未稳定解决，不能据此认定是产品 DPI 布局缺陷。这些试探性界面修改已撤回。

本机 .NET Framework 反射确认 `TableLayout.MinSizeProxy` / `MaxSizeProxy` 含进程共享的静态 `instance` 和可变 `strip`；[微软对应布局源码](https://github.com/dotnet/winforms/blob/main/src/System.Windows.Forms/System/Windows/Forms/Layout/TableLayout.MinSizeProxy.cs)也采用共享实例。在不同测试类的 UI 线程中同时进行布局，会引入共享状态竞争，不等同于 SolidWorks 单 UI 线程上的使用场景。

将 `TestAssemblyExportLayout` 放入 `DisableParallelization = true` 的独占 xUnit 集合，避免与其他测试同时布局。没有跳过用例或放宽文本、控件边界断言。生产 UI 与 `9af4ba7` 一致，保留 MJCF 预检查；修正仅涉及测试调度。修正后 4 项截图测试和独立构建完整回归均通过。

此前失败构建均未安装到远端；原装配体未修改。安装状态与构建结果分开核验。

## 完整构建及远端安装

- 源提交：`49c01cb8c9ad996a9bf1b74047a104851a0aa5f2`。
- 完整插件回归：997 项通过，0 失败，0 跳过；Core 129 项、官方 MuJoCo 9 项通过。
- 执行日志核对：94 个布局用例与其他测试的重叠次数为 0；旧失败日志存在 979 次重叠观测。
- 安装包：`sw2urdfSetup_20260908_49c01cb.exe`，22,750,818 字节。
- SHA-256：`d87f309ca090027b7c9a02445e5624e38c91a8a48f98f1a0967813a20463c6ad`，远端传输后读回一致。
- 远端 `kitso@192.168.31.108` 的 SolidWorks 退出后安装，退出码 0；249 个载荷文件哈希全部匹配，版本 `49c01cb8c9ad`，COM CodeBase 与 SolidWorks Addins 注册正常。
- 本次未运行远端装配体的交互式导出验收，也未替换 GitHub 上的候选发布。

## 用户手动验收

2026-09-08，用户在安装后手动验证并反馈“暂时未发现新问题”，同意发布新的候选 Release。该反馈不等于对所有导出目标、DPI、SolidWorks 版本或长时间仿真的全面验收。RC2 使用上述同一安装包，不重新编译。
