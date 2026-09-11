# 惯性诊断：不分享装配体也能定位数据在哪一步改变

此功能采集诊断信息，不会自动翻转非对角项，不会用主惯量重建结果替换源数据。
当前不能据此声称已修复 GitHub #1 或 SolidWorks 2025 原生崩溃。

## 启用

先安装包含此功能的构建。保存并正常退出自己的 SolidWorks，然后从 Windows PowerShell 启动：

```powershell
$env:SW2URDF_INERTIA_DIAGNOSTICS = '1'
$env:SW2URDF_LOG_FILE = Join-Path $env:TEMP 'sw2urdf-inertia-diagnostic.log'
& 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe'
```

将程序路径换成实际路径。这只设置该终端及其子进程的环境；已运行的 SolidWorks 不会继承。
不用 setx，不修改系统永久配置。诊断结束后正常关闭 SolidWorks 和此终端，下次从原入口启动即可恢复。
日志包含 Link/组件名称和配置名，可在本地检查后提供诊断段；不要求上传 CAD 工程。

先只执行一次惯性读取/重新计算，再单独点击惯性预览。失败后立即保存日志和同目录滚动备份，
避免下一次启动重新初始化日志。比较正常模式与诊断模式是否都失败；额外 COM getter 本身也可能触发宿主缺陷。

## 日志判读

搜索 `inertia-diagnostic`，同一 `id` 的日志属于同一次调用，嵌套调用列出 parent。
日志沿用插件立即刷新到文件的 appender。

| 阶段 | 内容 | 定位依据 |
|---|---|---|
| A.api-document | API 原始质量/COM/矩阵、覆盖标志 | 与同对象 API 主惯量、主轴比较；两次数值对象的质量分别记录 |
| frame | 变换矩阵、正交误差、行列式 | 非有限、非仿射、缩放/剪切被明确拒绝；不静默修正矩阵 |
| B.link-frame | 坐标变换后矩阵 | 正交变换应保留迹、主惯量 |
| C.final | 编辑策略后的最终矩阵及编辑标志 | 与 B 的区别可能来自手动编辑、旧值保留或质量校准 |
| begin/end/failed | API 创建、范围设置、重算、数值读取、选择/图形恢复、预览 | 最后未结束步骤缩小原生崩溃范围；不是根因证明 |

主惯量 getter 的托管异常记为 unavailable，并继续必需的恢复；必需 API 的异常仍抛出。
日志接收器异常不会中断恢复。原生 access violation/进程终止不能由这套日志保证恢复。
常规模式不调用新增的主惯量/主轴 getter；保持原有两个数值对象的读取顺序。

错误提示现在区分 computed、edited/preserved、after mass editing，避免把编辑后的矩阵归咎 API。
这不清除用户编辑，也不自动改变旧数据。

若 A 的谱与 API 主惯量不同，应在同次配置/范围下检查 API 符号、缓存或覆盖行为。
若 A 一致但 B 的谱变化，查变换；若 B 一致但 C 意外变化，查编辑状态。
不要根据哪种符号“能通过三角不等式”自动选符号。若将来证实 API 输出惯性积，
转换必须发生在 A 的读取边界、旋转之前，并以非主轴、多组件/覆盖回归测试固定契约。

## 原生崩溃

在发生问题的机器本地，用微软 [ProcDump](https://learn.microsoft.com/en-us/sysinternals/downloads/procdump)
监视目标 SolidWorks 的 PID（多实例时必须指定准确 PID）：

```powershell
# 将 12345 换成目标 SolidWorks PID，两个路径换成实际路径。
& 'C:\Tools\procdump64.exe' -ma -e 12345 'C:\Temp\SW2URDF-Dumps'
```

输出目录须预先存在。此命令监视未处理异常，不设置永久事后调试器。
按 Ctrl+C 结束监视。若 SolidWorks 自身拦截异常，未处理异常监视可能不产生 dump；
需要结合事件日志、最后未结束步骤，再针对具体异常制定采集方式。
完整 dump 含进程内存，可能包含无法分享的模型数据，应留在本机分析，不默认上传。
同时记录准确 SW/SP、插件构建版本、最后一次操作，以及诊断开关状态。

本改动提供采集能力和定位步骤；没有客户模型和 SW2025 dump 时，不宣称修复原生崩溃。
