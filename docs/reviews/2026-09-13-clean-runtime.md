# Windows release runtime qualification

Date: 2026-09-13. Candidate source:
`a0af2c5a1ea934fe027fb935df94516880c7a33f`.

Installer: `sw2urdfSetup_20260911_a0af2c5.exe`, 67,694,476 bytes.
SHA256: `bf7c25db6779b9eb1d6c4b1bfa1d369f4155e1c9a1799320283a9b2bc245eb0f`.

## Status and evidence boundary

The exact candidate passed qualification on a fresh GitHub-hosted Windows
Server VM with the explicit VC prerequisite and isolated runtime search
paths. This establishes the tested runtime combination; it does not claim
bare Windows desktop coverage or SolidWorks COM activation on that image.
The subsequent installation lifecycle and published-stable upgrade runs
also passed; both evidence sets are recorded below.

[Successful run 34706743791](https://github.com/osrbot/solidworks_urdf_exporter_pro/actions/runs/34706743791)
executed workflow revision `8b198e914a71244d6258b75ba4e93b58bc28d6c1`
on 2026-09-13 at 00:56-00:57 Asia/Shanghai. The installed payload retains
the candidate's original `a0af2c5` identity; qualification scripts do not
change the shipped installer.

| Observation | Recorded result |
| --- | --- |
| Operating system | Windows Server 2022 Datacenter x64, build 20348 |
| GitHub runner image | `win22`, `20260907.297.1` |
| Microsoft prerequisite | x64 14.51.36247.0; pinned digest and valid signature; installer exit 0 |
| Candidate installation | Expected installer digest; Inno exit 0; RegAsm process exit 0 |
| Python / NumPy / PyMeshLab / USD | 3.11.9 x64 / 2.2.6 / 2025.7.post1 / 26.8 |
| Native sphere QEM | 1,280 to 80 faces |
| Actual worker QEM | 128 to 32 triangles; source unchanged; strict progress protocol passed |
| Zero-ratio worker | 128 triangles, byte-identical output |
| Import process module origins | 112 payload + 54 Windows; zero external |
| Reduction worker module origins | 93 payload + 47 Windows; zero external |
| Zero-ratio worker module origins | 17 payload + 23 Windows; zero external |

The image already contained VC 14.51.36247.0 before the explicit installer
ran. This is prerequisite-present qualification, not a test of installing
VC on a system where it was absent. The reduction process loaded
`MSVCP140.dll`, `MSVCP140_1.dll`, and `VCOMP140.dll` from System32 at
14.51.36247.0. Python's two bundled `VCRUNTIME140` DLLs loaded from the
candidate payload at 14.38.33126.1. Module paths and hashes confirm that
runner development-tool directories did not supply any loaded DLL.

Full downloaded evidence is retained locally at
`.codex-build/clean-runtime/run-34706743791/`, including `environment.json`,
`vc-install.json`, `installer-result.json`, `installed-payload.json`,
`loaded-vc-modules.json`, `qualification.json`, and per-process probe logs.
The CI artifact [`windows-release-runtime-34706743791`](https://github.com/osrbot/solidworks_urdf_exporter_pro/actions/runs/34706743791/artifacts/10301831778)
is 120,530 bytes, SHA256
`45742627ba3f7c475471cff5ab320bc51c5dc161a30c5bcc2058dbfb1229a58b`.
Its CI retention expires on 2026-10-12 at 16:57 UTC; preserve a release
copy of the evidence for long-term access.

The developer computer runs Windows 11 Home China, build 26200. The Sandbox
executable is absent. Hyper-V services and its PowerShell module exist,
but non-elevated VM enumeration is denied; the read-only elevated inventory
request was canceled. No host VM, OS feature, VC runtime, or installed
SolidWorks plugin was changed by this qualification work.

## Explicit supported prerequisite

Install **Microsoft Visual C++ v14 Redistributable x64 14.51.36247.0** before
using this candidate. This is the selected qualification baseline, not a
claim that older versions cannot work or that an upstream compiler minimum
has been reconstructed. The candidate installer does not itself detect or
install the VC prerequisite.

[Microsoft's supported runtime guidance](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)
requires matching architecture and a runtime at least as recent as the
compiler used by the native binaries. Its official x64 latest-supported
download resolved on this date to the package pinned below. The current
v14 runtime supports Windows 10/11 and Windows Server 2016 or later.

- [Pinned Microsoft download](https://download.visualstudio.microsoft.com/download/pr/ebdab8e5-1d7b-4d9f-a11b-cbb1720c3b12/843068991DAAA1F73AD9F6239BCE4D0F6A07A51F18C37EA2A867E9BECA71295C/VC_redist.x64.exe).
- SHA256: `843068991daaa1f73ad9f6239bce4d0f6a07a51f18c37ea2a867e9beca71295c`.
- Size: 18,731,856 bytes; file/product version: `14.51.36247.0`.
- Authenticode verified locally: valid; signer organization Microsoft Corporation.
- The workflow verifies URL payload hash, file version and signature again.
  It rejects every nonzero prerequisite installer exit, including a newer
  version conflict or a reboot requirement.

The download is used on the disposable runner. It is not embedded in or
redistributed with the candidate installer by this change.

## Fresh VM method

[`verify-release-runtime.yml`](../../.github/workflows/verify-release-runtime.yml)
uses the explicit `windows-2022` GitHub-hosted image and authenticated
GET requests to download the exact asset from the existing draft.
The installer SHA256 must match before execution.
The workflow defaults to `contents: read`; the qualification job requests
`contents: write` because GitHub hides unpublished draft assets from its
read-only installation token. The job does not issue repository or release
write operations. Initial attempts with both tag lookup and a direct asset
GET using the read-only token failed before any runtime was executed.

[`Test-CleanReleaseRuntime.ps1`](../../scripts/Test-CleanReleaseRuntime.ps1)
records OS build, image version, workflow revision, package digest, runtime
DLL versions before/after prerequisite installation, Inno installer exit,
and registration-related log lines. The installer runs only when the
workflow identifies a GitHub-hosted runner. RegAsm logging is retained
separately; successful runtime checks do not assert SolidWorks activation.

It clears Python, Qt and virtual-environment variables, sets PATH to
Windows system locations, and runs the installed embedded Python with
`-I -B`. No runner Python, pip install, rebuilt wheel, or copied developer
DLL is used. Each imported Python search path must belong to the installed
runtime.

[`release_runtime_probe.py`](../../scripts/release_runtime_probe.py) verifies:

- Exact Python 3.11.9 x64, NumPy 2.2.6, PyMeshLab 2025.7.post1 and USD 26.8.
- Actual native sphere QEM reduction and the required PyMeshLab filter.
- The shipped worker in fresh child processes: zero-ratio byte identity
  and real 128-triangle grid reduction with strict JSON progress protocol,
  source hash preservation, valid triangle/byte counts and preserved header.
- Loaded native module paths and SHA256 in the import process and both
  worker processes. Any module outside the installed payload or Windows
  directory fails qualification. Loaded VC module versions are also saved.

The workflow always uploads JSON, protocol output and logs, including on
failure. It does not upload installed binaries. Its image is a fresh VM
with preinstalled development tools, not a bare desktop. Sanitized search
paths plus observed DLL provenance constrain what can influence the tested
execution; they do not turn this image into bare Windows.

## Harness verification on the developer host

Read-only execution against the already installed candidate passed. Native
sphere reduction was 1,280 to 80 faces; worker reduction was 128 to 32
triangles; zero-ratio output was identical. Native module provenance stayed
inside the candidate payload and Windows directories. Evidence resides in
`.codex-build/clean-runtime/host-harness-check-v2/`.

The PowerShell script parsed successfully, and its installation guard
rejected execution outside GitHub-hosted CI before running installers.
These checks validate the harness implementation; the fresh VM result
above is the separate deployment-environment evidence.

## Installation lifecycle extension

The workflow now includes
[`Test-ReleaseInstallLifecycle.ps1`](../../scripts/Test-ReleaseInstallLifecycle.ps1)
after the runtime probe.
[Run 34707960756](https://github.com/osrbot/solidworks_urdf_exporter_pro/actions/runs/34707960756)
passed at workflow revision `c5f2da28e7b576d8b78ca03ed39c956c79672e9b`.
It used Windows Server 2022 build 20348, image `20260907.297.1`, and the
same exact installer digest recorded above. Runtime qualification passed
again before the lifecycle started.

| Stage | Inno exit | Logged RegAsm exit | Payload and registration result |
| --- | --- | --- | --- |
| Initial installation | 0 | 0 | 1,466 hashes matched; CLSID, CodeBase, Addins, Startup and ARP present |
| Same-version overwrite | 0 | 0 | All 1,466 hashes and COM/add-in/startup values matched initial installation |
| Uninstallation | 0 | 0 | No remaining files; CLSID, Inproc, Addins, Startup and ARP all absent |
| Reinstallation | 0 | 0 | All 1,466 hashes and COM/add-in/startup values matched initial installation |

All installed stages recorded CodeBase
`file:///D:/a/_temp/runtime-evidence/installed/SW2URDF.DLL`, Startup value
1, and ARP paths targeting that same installation. The lifecycle result
records `passed: true` and `recoveryAttempted: false`. After downloading
the evidence, an independent comparison of each installed-stage file
inventory against the initial inventory confirmed all 1,466 hashes again.

The retained [CI artifact 10302332645](https://github.com/osrbot/solidworks_urdf_exporter_pro/actions/runs/34707960756/artifacts/10302332645)
is 401,252 bytes, SHA256
`14df53c465c2ed4f4c08eb287890949ba29c5e8828f671a965337cf69bfc08ae`,
and expires on 2026-10-12 at 17:21 UTC. Downloaded files reside in
`.codex-build/clean-runtime/run-34707960756/`. The independently repacked
durable copy `windows-release-lifecycle-34707960756-evidence.zip` has
SHA256 `c0f326622ad7b5922e7dfa456851e487ddab6d80ece06af56e58119e76d83af6`.
It includes the new lifecycle evidence and this run's original runtime
evidence; the earlier run's evidence is preserved separately.

The extension uses the same downloaded candidate and the first verified
installation's 1,466 payload hashes, excluding Inno's two generated
uninstaller metadata files. On the same disposable VM it overwrites the
same version, uninstalls through the candidate's own `unins000.exe`, then
reinstalls the unchanged candidate. Every stage verifies installer and
logged RegAsm exit codes. Installed states verify payload hashes and
CLSID, InprocServer32 CodeBase, SolidWorks Addins, current-user
AddInsStartup, and ARP registration. The uninstalled state requires the
payload files and all those registration keys to be absent. Original
runtime evidence remains intact; lifecycle JSON and logs are appended in
a separate `lifecycle/` directory.

The extension never manually deletes the application directory, changes
registry values itself, or activates COM. It checks for unexpected files,
foreign COM ownership, and a running SolidWorks process before mutation.
After a failure it attempts one guarded reinstall through the same package
and retains the failed overall result even if restoration succeeds.

The local elevation prompt was canceled and host installation did not
start. A prepared local helper remains unexecuted. The VM lifecycle scope
does not claim local desktop UAC interaction or live SolidWorks activation.

## Upgrade from published stable v20260906

[Run 34709482749](https://github.com/osrbot/solidworks_urdf_exporter_pro/actions/runs/34709482749)
at revision `b4a4aebb50569027ad3309cfa06d857ace142a6c` passed the separate
[`Published v20260906 to exact candidate upgrade` job](https://github.com/osrbot/solidworks_urdf_exporter_pro/actions/runs/34709482749/job/103595477963).
The existing runtime and same-version lifecycle job passed again in that
run. The new job used its own fresh Windows Server 2022 x64 VM, build
20348, image `20260907.297.1`.

The starting installer was verified as an asset of the published,
non-prerelease [v20260906 release](https://github.com/osrbot/solidworks_urdf_exporter_pro/releases/tag/v20260906):

- File: `sw2urdfSetup_20260906_fc88a14.exe`, asset ID `547403854`,
  22,717,827 bytes.
- SHA256: `da18423ec7087fa775c2b65d2d48b72f557005aa88377e95f4c31b04f5ed3334`.
- Source: `fc88a146747ec5de444add4b57c6d2b4705ef82d`.
- Provenance asset ID `547403856`, SHA256
  `bc2a47f09e3aed764127a916dce66cc5edeb9a35c150b46d4f082fb9ba64cd72`.

The downloaded installer, published `.sha256`, GitHub asset digest, and
provenance installer digest agreed. Both old and candidate installers and
their provenance files were hash-pinned again inside the VM before any
installation. [`Test-StableReleaseUpgrade.ps1`](../../scripts/Test-StableReleaseUpgrade.ps1)
first verified that the destination and SW2URDF registrations were absent,
installed the old version, and checked all 249 old payload hashes and its
COM/add-in/startup/ARP registration. It then ran the exact candidate
installer directly over that installation. There was no intermediate
uninstall, manual payload cleanup, extraction substitute, or binary rebuild.

| Check | Actual result |
| --- | --- |
| Stable installation Inno / RegAsm exit | 0 / 0 |
| Direct upgrade Inno / RegAsm exit | 0 / 0 |
| Stable payload | All 249 files matched old provenance |
| Upgraded payload | All 1,466 files matched candidate provenance; no unexpected files |
| ARP DisplayVersion | `fc88a146747e` → `a0af2c5a1ea9` |
| COM CodeBase and installation directory | Same VM destination before and after upgrade |
| Addins / Startup / ARP | Present after upgrade; Startup value 1 |
| Upgraded runtime | Pinned imports, native sphere 1,280→80, worker 128→32, zero-ratio byte identity all passed |

The manifests share 249 paths: six files changed hash and 1,217 paths were
added. There were no old-only paths in this particular version transition;
the result's `obsoleteStablePathsRemoved: 0` does not claim an exercised
old-only-file removal case. After downloading the evidence, independent
comparisons of the old and upgraded file inventories against their
respective provenance manifests passed for all 249 and 1,466 files.

[CI artifact 10303260009](https://github.com/osrbot/solidworks_urdf_exporter_pro/actions/runs/34709482749/artifacts/10303260009)
is 216,445 bytes with SHA256
`25113b2578e411c61b22d52d04a840170230358cf0e49424dfd97e37ce217152`;
its CI retention expires on 2026-10-12 at 17:52 UTC. Local downloaded
evidence is in `.codex-build/clean-runtime/stable-upgrade-run-34709482749/`.
The durable repack `windows-stable-upgrade-34709482749-evidence.zip` is
214,046 bytes, SHA256
`02abd4bd83fee67d3c095896323d2b7f416251587b9d150f972d9cee17275719`.

This closes the tested published-stable-to-candidate installer transition
on that independent VM. It does not assert local desktop UAC interaction,
SolidWorks activation, or migration of user-created CAD/export projects.
