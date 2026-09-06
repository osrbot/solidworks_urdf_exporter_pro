---
search: false
---

# 首次预览等待排查

日期：2026-09-06。基线：`fc88a14`，SolidWorks 2023 SP1.0。
本次只排查并做临时对照实验，没有修改、打包或替换已安装的插件。

## 结论

首次等待主要发生在插件读取 SW 有效质量属性的阶段，不是打开装配体，也不是属性窗口绘制本身。
存在两类开销：每个 Link 重复创建质量属性对象，以及大型 Link 逐个查询子组件的覆盖属性。
入口同步执行，属性窗口要等整树计算结束才显示，因此用户同时遇到等待时间长和界面不响应。

## 已安装版本的日志证据

来源：`E:/桌面/SW2URDF/test-output/fc88a14/export.log`。
从 12:17:30.381 开始准备，到 12:19:38.494 关闭属性管理器并转入预览，共 128.113 秒。
10 个 Link 的惯性计算日志区间合计 127.716 秒，约占 99.7%。这些区间包含 SW 读取及后续惯性处理，不应称为单个 COM 接口的纯计算耗时。

| Link | 秒 |
| --- | ---: |
| base_link | 56.711 |
| camera_link | 6.800 |
| laser | 7.011 |
| imu_link | 7.244 |
| left_steering_hinge_link | 7.455 |
| left_front_wheel_link | 9.369 |
| right_steering_hinge_link | 7.240 |
| right_front_wheel_link | 9.115 |
| left_rear_wheel_link | 8.139 |
| right_rear_wheel_link | 8.632 |

正式导出还会重新读取当前 SW 属性做校验。这是另一轮开销，不能拿来解释首次预览的 128 秒，也不能靠删除最终校验解决首次等待。

## 本次分段实测

原 SW 实例已退出。本次另启拥有明确 PID/启动时间的测试实例，只读打开既有装配体副本。
装配体打开耗时 20.155 秒，明确排除在以下计时之外。
诊断程序在外部 STA 进程中编译读取器源码的内存副本，串行调用 SW COM；因此数值用于定位热点，不冒充插件内部整段耗时。

相机 Link 的原始读取连续两次为 6.755、6.834 秒：

| 操作 | 次数 | 第一次合计耗时 |
| --- | ---: | ---: |
| CreateMassProperty2 | 3 | 5.532 秒 |
| GetOverrideOptions | 2 | 0.970 秒 |
| Recalculate | 2 | 0.054 秒 |

质量、质心及惯性矩 getter 本身均不到 3 毫秒。主要时间不在最终读取数值，而在创建对象和查询覆盖属性。

底盘选择 `CHASSIS_ASM-1` 与 `OSRACER-ARC-E01-33_ASM_2_ASM_1_ASM-1`，原始读取为 51.545 秒：

| 操作 | 次数 | 合计耗时 |
| --- | ---: | ---: |
| GetOverrideOptions | 254 | 38.327 秒 |
| CreateMassProperty2 | 3 | 5.449 秒 |
| SetSelectedItems | 256 | 2.011 秒 |
| Recalculate | 2 | 0.979 秒 |

覆盖查询占底盘读取约 74%。不能简单跳过它，否则可能丢失 SW 的质量、质心和惯性覆盖。

## 临时优化对照

仅改变两个数值对象的创建方式：在隔离的临时选择列表里先选中并核验组件，再创建对象；仍保留两个独立数值对象、显式作用域、重算及原有覆盖检查。

- 相机第一组：6.421 秒降到 5.210 秒；第二组：6.435 秒降到 4.719 秒。
- 相机、底盘、相机三组均比较质量、3 个质心分量、9 个张量分量及全部覆盖标志，最大绝对差约 `6.94e-18`。
- 底盘对照为 51.545 秒与 39.870 秒，但覆盖查询本身也由 38.327 秒下降到 26.870 秒，而该部分算法没有改动。因此此差值受缓存/状态及运行波动影响，不能宣称该优化使底盘稳定快 23%。
- 154 个已加载副本的配置和 dirty 状态不变；选择数量及三个刷新开关不变。选择身份、非空选择以及隐藏组件等仍需后续专项回归，当前检查不替代这些测试。
- 原始模型 156 个文件的 SHA256 再次核验全部不变。

**此原型未通过稳定性验收，不得合入或安装。** 数值对照完成、状态核验通过后，调用 `CloseAllDocuments(true)` / `ExitApp()` 清理本次测试实例时 COM 返回 `0x800706BE`。Windows Application 事件确认 PID 3816 在 23:38:03 发生 `0xc0000374` 堆损坏，模块 `ntdll.dll`；报告 ID 为 `581cba6a-e36d-4263-9c44-f8d19afa97c2`。
这不是正常退出提示。由于本次同时包含外部诊断进程及临时预选择原型，尚不能断言是哪一项导致堆损坏；也不能拿数值相等证明调用顺序安全。应先分别隔离基线与原型，验证关闭文档、退出及 COM 生命周期，再考虑性能修改。
还需要覆盖隐藏组件、选择失败回退、质量/质心/惯性覆盖、不同配置等已有实机回归。

## 建议的修复顺序

1. 首次预览准备复用现有独立进度窗口，逐 Link 显示步骤及耗时。COM 操作继续留在 SW 调用线程；不使用 `Task.Run` 移动 COM 计算，也不通过 `DoEvents` 引入重入。此项改善反馈，不声称缩短计算。
2. 在生产代码加入低开销分段统计，分别记录创建对象、覆盖检查、数值重算和刷新恢复，便于之后比较完整预览而不是仅比较一个 Link。
3. 暂停采用“先限定范围再创建”的原型，先隔离上述退出崩溃。如果后续证实可安全使用，预选择不完整时也不能继续使用错误范围；必须保持原读取路径的正确性与选择恢复。不要合并两个数值对象来绕过此前 SW2023 缓存问题。
4. 研究仅限一次稳定预览事务的元数据复用，减少重复的顶层/祖先查询。先验证生命周期和配置稳定性；不能仅按文件名缓存，也不能为了提速跳过底盘子树覆盖检查。当前数据尚不能证明此项会消除主要底盘开销。
5. 暂不跨非模态预览窗口与最终导出缓存属性。用户可能期间修改材料、几何、配置、覆盖属性或组件状态；没有可靠失效机制时，保留最终的新鲜校验。

## 代码入口与本地证据

- `SW2URDF/URDFExport/ExportPropertyManager.cs`：`ExportButtonPress` 同步准备并随后创建窗口。
- `SW2URDF/URDFExport/ExportHelperExtension.cs`：`ComputeInertialProperties`、`ReadEffectiveLinkMassProperty` 以及最终校验调用链。
- `SW2URDF/URDFExport/SolidWorksMassPropertyReader.cs`：`CreateProperty`、`CreateScopedProperty`、`ReadOverrides`、`ReadSubtreeOverrides`。
- `.codex-build/review-fixes/ProfilePreviewMass.ps1`：临时源码计时与对照脚本，未注册到 SW。
- `.codex-build/preview-latency/camera-profile.txt`：原始两次相机计时。
- `.codex-build/preview-latency/comparison-profile.txt`：相机/底盘/相机对照。
- `.codex-build/preview-latency/components.txt`、`owner.txt`：组件及实例记录。
- `.codex-build/preview-latency/close-crash-events.xml`：本次退出崩溃的 Windows 事件记录。
