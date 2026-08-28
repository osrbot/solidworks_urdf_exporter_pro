# SolidWorks to URDF Exporter

**English** | [简体中文](README.zh-CN.md)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-blue.svg)](#supported-environment)
[![Framework](https://img.shields.io/badge/.NET%20Framework-4.5.2-blueviolet.svg)](#development)

This repository is the OSRBot-maintained fork of the ROS
[`solidworks_urdf_exporter`](https://github.com/ros/solidworks_urdf_exporter). It keeps the
original SolidWorks add-in workflow and adds maintained Link-tree editing, frame-aware mass
properties, collision strategies and previews, ROS1/ROS2 package output, validation reports,
Chinese localization, and auditable installer packaging.

> **Project status**
>
> This is a community-maintained fork, not an official Dassault Systemes or ROS distribution.
> Current maintenance and live API verification focus on SolidWorks 2023. The historical minimum
> requirement inherited from the upstream project is SolidWorks 2018 SP5; that statement is not a
> claim that every version and service pack has been regression-tested.

## Why This Fork Exists

The upstream project established the SolidWorks-to-URDF workflow. This fork keeps that foundation
but addresses production gaps that accumulated around newer SolidWorks use, complex assemblies,
physical validation, and release maintenance:

| Production gap | Maintained fork response |
| --- | --- |
| Link-tree edits could be lost across preview, PropertyManager, or reopen transitions | Transactional editing, persisted v1.5 configurations, recovery drafts, and stricter duplicate/stale-state validation |
| Mass properties could be zero, sign-inverted, or expressed in the wrong frame | Explicit system units, one part/assembly frame-conversion path, COM/bounds checks, physical tensor validation, and API-principal-moment comparison |
| Collision choices were difficult to verify before export | Link-local fitting, temporary SolidWorks previews for every strategy, fallback reporting, and requested/effective strategy records |
| Visual/material controls and large exports were hard to inspect consistently | SolidWorks appearance loading, deterministic Link coloring, bilingual UI, topmost progress, and export summaries |
| Historical installers were difficult to reproduce or audit | Hash and provenance sidecars, payload verification, bilingual release notes, and a draft-only manual publication gate |

The fork preserves the upstream Git history, authorship, and MIT license. Detailed changes and
their commit evidence are recorded in the [Changelog](CHANGELOG.md).

## Engineering Scope

The exporter keeps three URDF responsibilities separate:

| URDF data | Engineering objective | Exporter behavior |
| --- | --- | --- |
| `visual` | Preserve appearance and recognizable geometry | Exports STL or 3DXML visual geometry and URDF material ID/RGBA |
| `collision` | Keep contact geometry simple while preserving task-relevant shape | Supports mesh, primitive, component-box, and convex-hull strategies with requested/effective strategy reporting |
| `inertial` | Preserve mass, center of mass, and inertia tensor | Reads SolidWorks mass properties in system units and converts them into the selected Link frame |

Collision selection never changes the mass-property source. A collision preview is an engineering
preview of the selected strategy, not proof that the final simulator behavior is correct. The
generated package reports remain authoritative for what was actually exported.

## Main Features

- Generates matching `ROS1/<package>` and `ROS2/<package>` description packages.
- Stores Link/Joint configuration in the assembly feature
  `URDF Export Configuration (v1.5)` and migrates older readable configurations when saved.
- Provides a transactional Link-tree canvas with add, rename, reparent, automatic layout, box
  selection, and branch copy/paste/delete.
- Provides Markdown-style Link-tree outline editing where `#`, `##`, and `###` define hierarchy.
- Restores recoverable unsaved sessions from `%LOCALAPPDATA%\OSRBot\SW2URDF\export-drafts` for
  assemblies that have a stable saved path.
- Uses per-Link SolidWorks coordinate systems for mass, COM, Joint origins, and inertia conversion.
- Validates finite values, tensor symmetry and physical principal moments before export.
- Supports `VisualMesh`, `SimplifiedMesh`, `AccurateMesh`, box/cylinder/sphere primitives,
  `ComponentBoxes`, and `ConvexHull` collision strategies.
- Shows temporary SolidWorks collision bodies for every user-facing collision strategy and an
  independent COM/equivalent-inertia preview.
- Records collision fallbacks instead of silently presenting the requested strategy as successful.
- Loads SolidWorks component/document appearance when no explicit user override exists.
- Treats material name as the URDF material ID; built-in IDs update RGBA, and manual RGBA remains
  editable per Link.
- Provides whole-tree automatic Link coloring: hierarchy progresses from cool to warm colors, while
  normalized left/right counterparts receive the same stable color.
- Shows a topmost, non-reentrant export progress window and a completion summary with changed file
  count, total size, elapsed time, and output directory.
- Includes Simplified Chinese UI text for the maintained workflow while preserving canonical URDF
  names and Joint type values in saved data and output.

See the [Wiki](https://github.com/osrbot/solidworks_urdf_exporter_pro/wiki) for detailed behavior and
limitations.

## Supported Environment

| Item | Supported or verified state |
| --- | --- |
| Operating system | Windows x64 |
| Target framework | .NET Framework 4.5.2 |
| Historical minimum SolidWorks version | SolidWorks 2018 SP5 |
| Current live API verification focus | SolidWorks 2023 |
| Release build | `Release|x64` |
| Installer languages | English and Simplified Chinese |

SolidWorks 2017 or earlier may work, as noted by the upstream project, but is not a maintained or
verified target. See the upstream discussion in
[`ros/solidworks_urdf_exporter#73`](https://github.com/ros/solidworks_urdf_exporter/issues/73).

## Installation

1. Download a published installer from
   [GitHub Releases](https://github.com/osrbot/solidworks_urdf_exporter_pro/releases). Maintainer
   builds use the name `sw2urdfSetup_YYYYMMDD_<commit>.exe`.
2. Verify the accompanying `.sha256` file when it is provided.
3. Close SolidWorks before installing or upgrading. The current installer registers the add-in but
   does **not** terminate SolidWorks or hot-reload the DLL into a running process.
4. Run the x64 installer as an administrator and choose English or Simplified Chinese.
5. Restart SolidWorks and use `Tools > Export as URDF`.

The public release process is intentionally manual-gated: CI validates a committed maintainer-built
installer and creates a draft candidate; it does not publish a release until live SolidWorks testing
has completed and the maintainer explicitly approves publication.

Historical upstream installers remain available from
[`ros/solidworks_urdf_exporter` releases](https://github.com/ros/solidworks_urdf_exporter/releases).

## Quick Start

1. Work on a saved assembly copy. Resolve components, assign valid material density, rebuild, save,
   and verify SolidWorks Mass Properties.
2. Create `Origin_global`, each required Joint coordinate system, and each motion axis using one
   consistent right-handed convention.
3. Open `Tools > Export as URDF` and build the Link tree. The first-use tutorial can guide the same
   real workflow and can later be reopened from `Tools > URDF Export Tutorial`.
4. Configure Joint names, canonical Joint types, parent/child relationships, origins, axes, limits,
   dynamics, and optional Mimic relationships.
5. For every Link, select the intended Link frame and inspect mass, COM, inertia values, and the
   equivalent inertia preview.
6. Choose Visual format, collision strategy, material ID/RGBA, and STL reduction. Preview collision
   coverage in SolidWorks, but treat the exported manifest as the final strategy record.
7. Export the ROS1 and ROS2 packages.
8. Review the generated reports before simulation:
   - `config/export_report.md`
   - `config/inertial_validation.csv`
   - `config/mesh_manifest.csv`

## Output Layout

```text
<export-root>/
|-- ROS1/<package>/
|   |-- meshes/
|   |-- urdf/
|   `-- config/
`-- ROS2/<package>/
    |-- meshes/
    |-- urdf/
    `-- config/
```

The report files identify the effective collision strategy, fallbacks, mesh records, and per-Link
inertial validation results. Reusing an output directory is supported; the completion summary counts
only files created or changed by the current export.

## Inertia Conventions

- Mass-property queries explicitly use SolidWorks system units: kilograms, meters, and
  `kg*m^2`.
- The root Link uses `Origin_global` or another explicitly selected root coordinate system.
- A non-root Link normally uses its child-Joint coordinate system as the URDF Link frame.
- Mass/COM and the COM inertia tensor are read from independent SolidWorks mass-property objects to
  avoid a SolidWorks 2023 read-order failure observed in live API testing.
- COM and tensor orientation are converted from the SolidWorks document frame into the selected
  Link frame.
- URDF inertia is stored about the COM. A Link-frame change rotates the COM tensor and changes COM
  coordinates, but does not apply a parallel-axis shift to the stored URDF tensor.
- SolidWorks' Mass Properties dialog can present products of inertia using a notation that invites
  an extra sign flip. The `GetMomentOfInertia` API used here already returns the physical symmetric
  tensor in the requested frame, so the exporter preserves its off-diagonal signs when writing
  URDF. An independent eigenvalue check requires the exported tensor to match the API principal
  moments and catches an accidental second sign conversion.

The article
[“掌握 URDF 中的惯性张量：从 SolidWorks 到强化学习机器人的关键一步”](https://zhuanlan.zhihu.com/p/1887859297221845818)
by Winter is acknowledged as a useful conceptual reference for COM-relative tensors, output frames,
and the distinction between tensor terms and displayed products of inertia. The implementation and
its tests remain the source of truth for the API-to-URDF mapping used by this exporter.

## Collision Guidance

Start with `ComponentBoxes` for assemblies. Use `BoxPrimitive` for box-like structures,
`CylinderPrimitive` for wheels/shafts/tubes, and `SpherePrimitive` for spherical geometry. Use
`ConvexHull` for one complex approximation, `SimplifiedMesh` when primitives are insufficient, and
`AccurateMesh` only when detailed contact geometry is necessary.

If a requested strategy cannot be generated, the exporter falls back to `VisualMesh` and records
both requested and effective strategies in `mesh_manifest.csv` and `export_report.md`. Native
primitive, component-box, convex-hull, and simplified collision generation is intended for STL
output. 3DXML is primarily a visual interchange path, not the documented collision path.

## Appearance and Link Colors

The maintained UI exposes one color model:

- URDF material ID identifies the material.
- Built-in IDs set the corresponding RGBA values.
- The color picker and numeric RGBA fields directly edit the selected Link.
- `Auto Links` applies deterministic whole-tree colors and persists material ID/RGBA through the
  normal configuration model.
- A manual edit after automatic coloring is an explicit per-Link override.

Texture-image editing was removed from the normal assembly and part UI because STL has no UV
coordinates and SolidWorks does not provide a convenient DAE export route here. Existing serialized
texture metadata remains readable/exportable for backward compatibility, but the maintained UI does
not claim to author or validate texture mapping.

## Documentation

- [Simplified Chinese README / 简体中文 README](README.zh-CN.md)
- [Installation](docs/wiki/Installation.md)
- [Quick Start](docs/wiki/Quick-Start.md)
- [Link Tree](docs/wiki/Link-Tree.md)
- [Inertia](docs/wiki/Inertia.md)
- [Collision](docs/wiki/Collision.md)
- [Troubleshooting](docs/wiki/Troubleshooting.md)
- [Contributing](docs/wiki/Contributing.md)
- [Release Process](docs/wiki/Release-Process.md)
- [Changelog](CHANGELOG.md)

The files under `docs/wiki` are the version-controlled source for the public GitHub Wiki. Canonical
filenames are English; paired Simplified Chinese pages use the `-zh-CN` suffix. Update both language
versions in the same change when behavior changes.

## Development

Prerequisites:

1. Visual Studio 2017 with `.NET desktop development`.
2. SolidWorks and the matching SolidWorks API tools/assemblies.
3. An x64 developer environment. Run Visual Studio as administrator when COM registration or
   SolidWorks debugging requires it.

Open `SW2URDF.sln`. For debugging, configure the `SW2URDF` project to start the installed
`SLDWORKS.exe` for the target SolidWorks version.

Build the production project with the actual SolidWorks install directory:

```powershell
MSBuild.exe SW2URDF\SW2URDF.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 `
  "/p:SolidWorksInstallDir=C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
```

Run all locally available tests after building Debug:

```powershell
TestRunner\bin\x64\Debug\net452\TestRunner.exe
```

Run a focused class or name filter:

```powershell
TestRunner\bin\x64\Debug\net452\TestRunner.exe TestCollisionPreview
```

Pure tests can run without SolidWorks. Live COM tests require a compatible installed SolidWorks and
can fail with an RPC/COM error when SolidWorks is unavailable or the automation process terminates.
Live coverage on SolidWorks 2023 is not evidence of compatibility with every release or service
pack.

## Reproducible Installer Build

From a clean source commit:

```powershell
.\scripts\BuildInstaller.ps1 -Configuration Release -Platform x64 `
  -SolidWorksInstallDir "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
```

The script requires Inno Setup 6.3.0 through 6.3.3 and produces:

```text
INSTALL/OUTPUT/sw2urdfSetup_YYYYMMDD_<commit>.exe
INSTALL/OUTPUT/sw2urdfSetup_YYYYMMDD_<commit>.exe.sha256
INSTALL/OUTPUT/sw2urdfSetup_YYYYMMDD_<commit>.exe.provenance.json
```

Packaging runs from a detached worktree, verifies pinned NuGet inputs and the staged SolidWorks API
assemblies, and records payload hashes. The provenance file is a maintainer-build trace; it is not an
Authenticode signature and CI does not rebuild against proprietary SolidWorks assemblies.

Before building a candidate, add `.github/release-notes/vYYYYMMDD.md` with reviewed `## English` and
`## 简体中文` sections. CI renders only traceability placeholders and fails closed when either language
or a required placeholder is missing; it does not machine-translate the Changelog.

## Known Limits

- Installation/upgrading requires SolidWorks to be closed; no automatic process termination or hot
  reload is implemented.
- Unsaved assemblies do not have a stable path and cannot use per-assembly recovery drafts.
- Component persistent IDs can become invalid after delete/replace/save-as operations and then need
  manual rebinding.
- Collision previews are temporary SolidWorks geometry. For mesh strategies, preview geometry is not
  promised to be byte-identical to the final tessellated STL.
- STL does not carry UV texture coordinates. The maintained UI does not offer texture authoring.
- Strategy generation can fall back to `VisualMesh`; always review the effective strategy report.
- The project is provided under the MIT License without warranty. Validate the exported robot in the
  target simulator before production use.

## Credits and References

This fork preserves the original project history and MIT copyright. It does not replace or obscure
the work of the upstream authors and contributors.

- Original project: [ROS SolidWorks URDF Exporter](https://github.com/ros/solidworks_urdf_exporter)
- Original author and historical maintainer: [Stephen Brawner](mailto:brawner@gmail.com)
- Historical supporters named by the upstream project: [PickNik Consulting](https://picknik.ai),
  Verb Surgical, Open Robotics, and Willow Garage
- 3DXML export contribution: Kento Matsuo and the contributors recorded in commit `22cb778`
- Current OSRBot fork maintainer: `kitso666 <kitso@osrbot.com>`
- Inertia convention reference: Winter,
  [“掌握 URDF 中的惯性张量：从 SolidWorks 到强化学习机器人的关键一步”](https://zhuanlan.zhihu.com/p/1887859297221845818)
- Original ROS documentation: [sw_urdf_exporter](http://wiki.ros.org/sw_urdf_exporter) and
  [tutorials](http://wiki.ros.org/sw_urdf_exporter/Tutorials)

## License

MIT. See [LICENSE](LICENSE). The original `Copyright 2020 Stephen Brawner` notice and permission
terms must remain with copies or substantial portions of the software.
