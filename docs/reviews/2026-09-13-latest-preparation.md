# SW2-7 v20260913 release preparation / 正式发布准备

## Current decision / 当前状态

The exact candidate completed source-material, VM runtime, installation lifecycle, stable-upgrade and native acceptance. This record supports formal publication as v20260913; actual publication is recorded by the GitHub release.

同一候选的源码材料、虚拟机运行时、安装生命周期、稳定版升级和实机验收均已完成，支持正式发布 v20260913；实际发布状态以 GitHub Release 为准。

## Immutable candidate identity / 不变的候选身份

- Source: `a0af2c5a1ea934fe027fb935df94516880c7a33f`.
- Source tree: `93a77efd07036dfd6cf354249b3d3fc4195e9477`.
- Installer: `sw2urdfSetup_20260911_a0af2c5.exe`, 67,694,476 bytes.
- SHA-256: `bf7c25db6779b9eb1d6c4b1bfa1d369f4155e1c9a1799320283a9b2bc245eb0f`.
- Release x64 provenance records 1,466 payload files. The build log records
  1,195 plugin tests, 134 Core tests and 9 MuJoCo integration tests passed,
  with no failures/skips in those suites; bundled runtime gates also passed.

The installer is reused byte-for-byte. Release documentation and qualification
scripts have newer commits; they are not the binary's build source. The
2026-09-11 filename remains unchanged for the 2026-09-13 release.
安装包逐字节沿用原件；文档和验证脚本的新提交不冒充二进制构建源码，安装包不改名。

## Completed evidence / 已完成证据

| Area | Verified result |
| --- | --- |
| Source and notices | 28 source archives, 384 notice/attribution files, pinned recipes/manifests and mappings for 97 native wheel files; five binary origins matched original archives |
| Runtime prerequisite | Microsoft VC++ v14 x64 14.51.36247.0, with pinned package digest and valid Microsoft signature |
| Independent runtime | [34706743791](https://github.com/osrbot/solidworks_urdf_exporter_pro/actions/runs/34706743791): exact installed payload, native QEM 1,280→80, worker 128→32, zero-ratio byte identity, no DLLs from outside payload/Windows |
| Installation lifecycle | [34707960756](https://github.com/osrbot/solidworks_urdf_exporter_pro/actions/runs/34707960756): initial install, same-version overwrite, uninstall and reinstall; Inno/RegAsm exits 0, 1,466 hashes matched, uninstall removed payload and registration |
| Published stable upgrade | [34709482749](https://github.com/osrbot/solidworks_urdf_exporter_pro/actions/runs/34709482749): direct v20260906 `fc88a14`→`a0af2c5`; 249 old and 1,466 new hashes matched their provenance; ARP version updated and upgraded worker passed |

These were independent Windows Server 2022 x64 VMs, build 20348, image
`20260907.297.1`. The image already contained the selected VC runtime.
They establish the recorded installation and runtime combinations, not a
bare Windows desktop, local UAC interaction or live SolidWorks activation.
以上为独立 Windows Server 2022 虚拟机证据；镜像原已安装指定 VC 运行时，不能写成裸 Windows 或本机 UAC/插件激活验证。

The [source-material report](2026-09-13-source-materials.md) closes the
missing archive/notice collection, external recipe identities and the five
binary-origin mappings from the earlier audit. Its follow-up found no
specific missing historical modification; exact historical reproduction
was not performed. This is engineering evidence, not legal certification.
The unchanged source ZIP is 1,214,417,657 bytes, SHA256
`f9d98e4cfa19048c821db8363104d263e2ad6d1ee8b00469a862404684b7156e`.
源码材料报告给出了具体收集和来源核对结果；不以历史构建不可逐位重现为由虚构新缺口，也不宣称法律认证。

Release assets include the original installer and sidecars, source materials and follow-up, three VM evidence archives, and the native acceptance report/archive.

Detailed hashes and scope are in [the Windows qualification report](2026-09-13-clean-runtime.md).

## Exact-candidate native acceptance / 同包原生验收

- SolidWorks 2023 SP1.0: all eight strategies passed ROS1, ROS2, OpenUSD and MuJoCo (32 target outputs); independent checks found no fallback or missing collision mesh.
- Legacy migration, strict v2 round-trip, save/reopen, mixed component configurations and installed add-in activation passed.
- Hidden depth-two part: inertia and eight collision previews passed Show/Hide/Dispose and restored visibility, appearance and component inventory.
- Real export progress/results UI passed with four successes and zero failures. All 11 original CAD hashes remained unchanged.
- Official MuJoCo 3.12.0 viewer loaded the unmodified example. This is basic loading evidence; no controller, long-simulation, ROS/Gazebo or Isaac acceptance is claimed.

八种策略四类目标、迁移、插件加载、深层隐藏预览与真实导出 UI 均已通过；详见 [native acceptance report](2026-09-13-native-release.md)。

The included production changes remain legacy XML decoding (`c0c75d6`), structural migration (`eb2e172`), mixed-configuration mass reads (`6d6ee3c`), collision STL references (`a0af2c5`), and the earlier changes since v20260906 described in the bilingual notes. Qualification scripts and documentation did not alter the installer.

The source example retains unresolved/suppressed brace references. Long Windows paths can fail MuJoCo validation; a short output directory passed. Native tests used disposable copies and user-authorized low-memory prompt continuation. The host installer was not rerun; installation and upgrade evidence comes from the separate VMs.
