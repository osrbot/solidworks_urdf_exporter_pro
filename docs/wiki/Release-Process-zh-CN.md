# Release Process

**简体中文** | [English](Release-Process)

本流程适用于 OSRBot 维护分支。它把“源码提交”“本地专有 API 构建”“CI 检查”“人工 Live
验证”“公开发布”分开，避免把未经人工测试的候选包直接发布。

## 1. 准备源码

1. 更新 `CHANGELOG.md`，只记录实际完成内容。
2. 新增 `.github/release-notes/vYYYYMMDD.md`，并人工校对 `## English` 与
   `## 简体中文` 两部分。
3. 运行相关 pure tests、布局/序列化/导出测试和可用的 Live SolidWorks 测试。
4. 提交源码。
5. 除 `INSTALL/OUTPUT` 产物外，源码工作树必须干净。

## 2. 构建安装包

```powershell
.\scripts\BuildInstaller.ps1 -Configuration Release -Platform x64 `
  -SolidWorksInstallDir "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS" `
  -InnoCompilerPath "C:\Path\To\Inno Setup 6.3.3\ISCC.exe"
```

硬性条件：

- `Release|x64`；
- Inno Setup 6.3.0–6.3.3；
- 匹配本机 SolidWorks 的 API 输入；
- 锁定的 NuGet CLI、source 和 package hashes；
- 可解析的源码 commit、tree 和 commit time。

脚本在 detached 临时 worktree 中恢复依赖、暂存 SolidWorks API 输入、清空 Release 中间目录、
构建生产 DLL，并拒绝源码在构建中变化。

## 3. 产物

```text
sw2urdfSetup_YYYYMMDD_<source-commit>.exe
sw2urdfSetup_YYYYMMDD_<source-commit>.exe.sha256
sw2urdfSetup_YYYYMMDD_<source-commit>.exe.provenance.json
```

provenance 包含源码 commit/tree、构建模式、工具和依赖哈希、SolidWorks API 输入及安装 payload
哈希。它用于可追溯性，但不是：

- Authenticode 代码签名；
- GitHub Hosted Runner 从源码重建二进制的证明；
- 对所有 SolidWorks 版本的兼容认证。

## 4. 产物提交

- 安装包提交只能包含 `.exe`、`.sha256`、`.provenance.json`；
- 文件名中的 commit 必须是安装包真实源码父提交；
- 不覆盖已有不可变候选；
- 不把无关 CAD、日志、PDB、测试程序集或旧安装包删除混入提交。

## 5. CI 检查

Workflow 检查：

- 文件名和源码 commit 关系；
- SHA-256 与 provenance；
- 锁定依赖与工具；
- Inno 安装包解包清单；
- 每个 payload 文件哈希；
- 源码提交内存在经过校对的双语 Release Notes；
- 同日 tag/Release 不可覆盖。

Hosted CI 不拥有专有 SolidWorks API 构建环境，因此验证并提升可信维护者构建，而不是重新编译
插件。

## 6. 双语 Release Notes

`.github/release-notes/vYYYYMMDD.md` 是 Release 页面正文的唯一来源，必须包含：

- 同时包含英文和简体中文的标题；
- `## English`：变更、验证范围、限制和手动测试门禁；
- `## 简体中文`：与英文部分事实一致的内容；
- 日期、安装包文件名、源码提交和产物提交占位符。

CI 只替换占位符，不从 `CHANGELOG.md` 自动翻译或编造内容。缺少任一语言章节时直接失败。
已经公开的历史 Release 不回写；该规则用于当前 Draft 和后续候选版本。

## 7. 人工门禁

CI 只创建 Draft Candidate。公开前维护者必须至少验证：

- 安装/升级/卸载和 COM 注册；
- Link Tree 保存、关闭恢复和重新打开；
- 每 Link frame、COM、惯性张量与主惯量；
- Collision 策略选择、预览、正式导出和 fallback 报告；
- ROS1/ROS2 URDF 与 meshes 完整；
- 导出进度置顶与完成摘要；
- 生产模型在目标 Viewer/仿真器中的基本加载。

只有维护者明确说“可以发布到线上 GitHub Release”后，Draft 才能公开。测试完成之前不得因代码
中写了“fixed”就提前发布。

## 8. Tag 与不可变性

- Tag 使用 `vYYYYMMDD`；
- 每日公开 Release 不移动、不覆盖；
- 同日第二个候选必须等待后续日期或按维护策略处理，不能替换已经公开的资产；
- 发布说明应逐条列出修复、新功能、限制、验证范围和源码 commit。
