# Contributing

欢迎提交可复现的问题、测试、文档和代码修复。本页只描述仓库已经具备的流程，不虚构 CLA、响应
时限或未建立的分支规则。

## 开发环境

- Windows x64；
- Visual Studio 2017 与 `.NET desktop development`；
- .NET Framework 4.5.2；
- SolidWorks 与匹配的 API Tools/Interop assemblies；
- 调试 COM 注册或启动 SolidWorks 时可能需要管理员权限。

打开 `SW2URDF.sln`，并让 Debug 配置启动目标安装目录中的 `SLDWORKS.exe`。

## 构建

```powershell
MSBuild.exe SW2URDF\SW2URDF.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 `
  "/p:SolidWorksInstallDir=C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
```

不要把专有 SolidWorks API DLL 提交到不应包含它们的位置。Release 脚本只暂存本机已安装的
匹配 API 输入，并在 provenance 中记录版本和哈希。

## 测试

Debug 构建后：

```powershell
TestRunner\bin\x64\Debug\net452\TestRunner.exe
```

按测试类/名称过滤：

```powershell
TestRunner\bin\x64\Debug\net452\TestRunner.exe TestMassPropertyFrameConverter
```

- Pure unit tests 应可在 SolidWorks 不可用时运行。
- Live COM tests 依赖本机 SolidWorks，主要使用 `examples` 中模型。
- `SW2URDF_TEST_ASSEMBLY` 可把测试指向隔离构建的插件程序集。
- Live 测试必须只关闭由测试自身启动的 SolidWorks 进程。

## 代码边界

- Link Tree 的正式实现位于 `SW2URDF/UI/LinkTreeCanvas` 及对应 session/store 边界。
- 不要恢复或复制 `prototypes` 中已退役的独立实现。
- UI、配置持久化和 URDF 导出必须共享规范 Joint 类型与验证规则。
- SolidWorks COM 对象需要明确释放；预览、失败和取消路径都必须清理临时体和选择状态。
- 坐标变换、惯性符号和碰撞 fallback 变更必须带独立测试，不能只依赖截图。

## Issue 应包含

- SolidWorks 年份版本和 Service Pack；
- 导出器版本/提交或安装包文件名；
- 精确复现步骤；
- 期望与实际结果；
- 最小可复现装配体（在许可允许时）；
- 日志、`export_report.md`、`inertial_validation.csv`、`mesh_manifest.csv`；
- 若报告 URDF 错误，提供可验证的期望坐标/质量/张量或对照 URDF。

不要只提交 Viewer 截图就断言惯性算法错误；截图应配合导出数值、坐标系定义和 SolidWorks Mass
Properties 结果。

## 文档

- README 是项目入口；详细行为写入 `docs/wiki`。
- Wiki 源文件与代码一起版本控制，再同步到 GitHub Wiki。
- CHANGELOG 只记录已实现或明确的未发布变更，不把计划写成完成项。
- 引用外部资料时明确区分理论参考、代码来源和历史致谢。

## 许可

贡献按仓库 [MIT License](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/LICENSE)
发布。原版权声明和许可条款必须保留。
