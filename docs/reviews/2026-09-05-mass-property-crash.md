---
search: false
---

# 点击预览时 SolidWorks 崩溃的排查记录

## 问题

安装 `fe8dc64` 后，点击“预览并导出”会在读取 `base_link` 的质量属性时关闭 SolidWorks。
此前通过的 Mock 测试和安装文件校验没有覆盖这一段真实 COM 调用，不能证明它可以在 SW 中正常运行。

## 已复现的原因

使用 SolidWorks 2023、新建独立进程和仓库示例零件的临时副本，复现了以下问题：

1. 向 `IMassProperty2.SelectedItems` 写入 `null`，会触发 SW 原生接口异常 `0x80010105`。
2. 把组件放进普通 `object[]` 传入同一接口，也会触发原生异常。组件数组需要按 `IDispatch` 封送。
3. 质量属性对象会继承界面中已有的选择。传入空数组不一定清空这个范围，后续可能读取错误的组件。
4. 同一零件的两个实例引用不同配置时，SW 2023 的覆盖标志可能来自零件文档当前配置，而不是所选实例的引用配置。
   数值质量正确，不代表覆盖标志也正确；它会影响后续是否允许自动校准惯性。

第一、二项是崩溃原因。第三、四项是实机回归中发现的额外正确性问题。

## 修复范围

- 整体读取使用非空引用的空数组，不向接口写入 `null`；组件列表使用 `DispatchWrapper[]`。
- 读取时隔离临时选择列表，结束后恢复原来的选择；继续检查实际读取范围，不取消安全校验。
- 从 Link 编辑页进入预览时，屏蔽计算引起的选择回调，避免把临时计算选择写回 Link 的 CAD 绑定。
- 无法确认覆盖标志属于当前引用配置时，明确提示配置不匹配，不自动切换用户配置，也不忽略覆盖值继续导出。
- 保持质量、质心和惯性读取对象的生命周期由 SW 管理，不主动释放共享 COM 对象。

参考：[SW 的 IDispatch 数组传参约定](https://help.solidworks.com/2023/english/api/sldworksapiprogguide/Overview/IDispatch_Object_Arrays_as_Input_in_.NET.htm)、
[保存选择列表](https://help.solidworks.com/2023/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ISelectionMgr~SuspendSelectionList.html)、
[恢复原选择列表](https://help.solidworks.com/2023/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ISelectionMgr~ResumeSelectionList2.html)。

## 验证方式

可重复的实机脚本：`scripts/Test-EffectiveMassOwnedSolidWorks.ps1`。
脚本只操作自己新建且最初没有打开文档的 SW 进程；使用示例零件的临时副本，不打开、保存或删除用户原始装配体。

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass `
  -File scripts/Test-EffectiveMassOwnedSolidWorks.ps1 `
  -BuildDll <待测目录>/SW2URDF.dll `
  -InteropDirectory <SW互操作程序集目录>
```

检查零件配置切换、质量/质心/惯性覆盖、组件 A/B/A 反复读取、组件聚合、选择恢复及配置不匹配时的拒绝路径。
这不是完整用户装配体通过界面导出的替代测试。

修复后的验证结果：

- 质量属性读取的聚焦回归：33/33 通过，包括接口传参、范围检查及成功/失败时恢复选择。
- SolidWorks 2023 隔离样件：质量分别为 3.25 kg 和 5 kg 的两个配置来回读取通过；质量、质心、完整惯性矩阵和覆盖标志均符合预期。
- 装配体组件 A/B/A 读取及两组件聚合通过，聚合质量 6.5 kg；质心按实例平移独立计算，聚合惯性按平行轴定理独立检查。
- 读取前后检查了所选对象身份、顺序、选择标记、文档配置和保存状态；配置不匹配路径明确拒绝，没有切换配置。
- “界面选中 B、读取 A”，以及相反方向的交叉选择测试通过，结果没有受原选择影响。
- 实机测试记录：`.codex-build/crash-isolated/live-probe.log` 和 `cross-selection-live.log`。仅关闭了脚本创建的临时装配体和独立 SW 进程。

安装包仍需经过 `BuildInstaller.ps1` 的全量回归门禁，并以对应的 provenance 文件记录源码版本、测试结果和文件摘要。
旧安装包 `fe8dc64` 有已确认的实机崩溃问题，不应继续使用。用户完整装配体的界面验收仍需单独确认。
