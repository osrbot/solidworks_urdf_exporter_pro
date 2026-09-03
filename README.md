# SolidWorks to URDF Exporter

**English** | [简体中文](README.zh-CN.md)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-blue.svg)](#supported-environment)
[![Framework](https://img.shields.io/badge/.NET%20Framework-4.8-blueviolet.svg)](#development)

[Documentation source](docs/index.md) | [Wiki](docs/wiki/Home.md) | [Quick start](docs/guide/getting-started.md)

This repository is the OSRBot-maintained fork of the ROS
[`solidworks_urdf_exporter`](https://github.com/ros/solidworks_urdf_exporter). It keeps the
original SolidWorks add-in workflow and adds maintained Link-tree editing, frame-aware mass
properties, collision strategies and previews, ROS1/ROS2 packages, OpenUSD and MJCF robot assets,
validation reports, Chinese localization, and auditable installer packaging.

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
| Link-tree edits could be lost across preview, PropertyManager, or reopen transitions | Transactional editing, strict v2 PID-backed configurations, recovery drafts, and stricter duplicate/stale-state validation |
| Deep reference geometry, Unicode names, and duplicate names were unsafe to resolve by display text | Component-instance and feature persistent IDs plus occurrence-aware `GetCorresponding` resolution; UI names no longer define identity |
| STEP/fixed assemblies lack reliable Joint semantics and zero DOF was easy to misread as `fixed` | Manual Joint annotation is primary; Mate detection is explicit assistance and every suggestion requires user confirmation |
| Mass properties could be zero, sign-inverted, or expressed in the wrong frame | Explicit system units, one part/assembly frame-conversion path, COM/bounds checks, physical tensor validation, and API-principal-moment comparison |
| Collision choices were difficult to verify before export | Link-local fitting, temporary SolidWorks previews for every strategy, fallback reporting, and requested/effective strategy records |
| Visual/material controls and large exports were hard to inspect consistently | SolidWorks appearance loading, deterministic Link coloring, bilingual UI, topmost progress, and export summaries |
| The historical workflow did not provide audited portable USD or MJCF assets | OpenUSD and MJCF targets with pinned structural/runtime checks and explicit non-claims for application, controller, and task validation |
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

## Export Targets and Evidence

The main export page exposes four deliverables. `Robot Bundle` is not a fifth target: it is a
private canonical staging representation created under the system temporary directory, consumed
by the selected exporters, and removed after success or failure.

Selected targets are published as one recoverable transaction. A blocking health-report failure
restores the previous target directories; an interrupted process is reconciled from a durable
journal before the next export starts.

| User target | Delivered files | Automated evidence | Not claimed |
| --- | --- | --- | --- |
| ROS 1 package | `ROS1/<package>` with URDF, meshes, configuration, and reports | Canonical model validation and transactional package generation | A ROS 1 launch, controller, or task run |
| ROS 2 package | `ROS2/<package>` with URDF, meshes, configuration, and reports | Canonical validation; a manual gate builds, launches Gazebo, and checks controllers for the fixed minimum fixture | Every user model passes in its target ROS 2/Gazebo environment |
| OpenUSD robot asset | `USD/<package>/robot.usd`, geometry dependencies, source mesh evidence, name map, and JSON report | Generated and reopened with pinned `usd-core 26.8` | Import or execution in Isaac Sim/Isaac Lab |
| MuJoCo MJCF model | `MuJoCo/<robot>/robot.xml`, `scene.xml`, assets, name map, and JSON report | Both XML entry points are compiled, canonically saved, reloaded, and advanced one zero-control step with pinned MuJoCo `3.12.0` tools | Actuators, PID gains, controllers, tasks, contact tuning, or RL code |

OpenUSD remains a single target on the main page. Its adjacent **Settings...** dialog is loaded only
when requested and stores portable simulation intent: source/fixed/floating base, official robot
classification, self-collision, and per single-DOF Joint passive/position/velocity/effort intent.
Defaults are conservative and version-independent. Position/velocity may author `DriveAPI` from
explicit values; effort is recorded as downstream intent without creating an active drive.

These are three different evidence levels:

1. **Generation capability** proves that the exporter wrote the documented files from a validated
   model.
2. **Automated validation** proves only the checks named in the table.
3. **Application runtime validation** belongs to the user's ROS, Isaac, MuJoCo, controller, and
   task environment. It is not implied by a successful export.

Each run atomically replaces only the selected target directories. Existing directories for
unselected targets are retained and may belong to an earlier run; the top-level `export_report.md`
records exactly which targets were generated and validated by the current run.

## Main Features

- Exports four concrete user targets: ROS 1 package, ROS 2 package, OpenUSD robot asset, and MuJoCo
  MJCF model. A private canonical staging model keeps all exporters on the same validated source
  without becoming a user-visible deliverable.
- Generates USD with a pinned bundled OpenUSD runtime and verifies that the stage reopens. Generates
  MJCF with pinned official MuJoCo tools and requires compile/save/reload/one-zero-control-step
  validation before publishing the local result.
- Authors a referenceable `/Robot` OpenUSD articulation with Isaac robot/link/joint classification,
  explicit base and self-collision semantics, collision-purpose metadata, and optional user-selected
  Joint drives without requiring an Isaac installation or version field.
- Records stable Link/Joint IDs and source evidence. SolidWorks Mate detection is an optional,
  user-confirmed suggestion for native movable assemblies, never a fallback for STEP geometry.
- Stores Link/Joint configuration in `URDF Export Configuration (v2)`. Explicit root-document
  references use `OwnerScope=RootDocument` plus feature PID; component-instance references use
  `OwnerScope=ComponentInstance` plus component PID and feature PID. Display names are UI labels,
  not identity. Resolution first finds the owner feature by PID, then maps component references to
  the exact assembly occurrence with `IComponent2.GetCorresponding`; no name lookup or active
  configuration switching is involved. Name-based v1.x configurations are intentionally
  not migrated; delete the
  legacy feature, recreate the configuration, and review it. V2 writes use canonical and hidden
  recovery slots: an existing slot is invalidated before its payload changes, a nonzero revision is
  committed last, each slot is fully validated, and loading selects the newest valid revision so an
  interrupted in-place COM write cannot replace the last valid state. A slot left at `revision=0` is
  treated only as an interrupted preparation: loading ignores it and the next save can retry it.
  A SolidWorks session caches only a fully registered schema definition; after initialization fails,
  the retry uses a fresh unique definition so a partial `AttributeDef` cannot poison later saves.
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
- Uses RGBA and the color picker as the direct per-Link appearance controls and derives a stable,
  read-only URDF material ID from the resulting color.
- Provides whole-tree automatic Link coloring: hierarchy progresses from cool to warm colors, while
  normalized left/right counterparts receive the same stable color.
- Shows a topmost, non-reentrant export progress window and a completion summary with changed file
  count, total size, elapsed time, and output directory.
- Includes Simplified Chinese UI text for the maintained workflow while preserving canonical URDF
  names and Joint type values in saved data and output.

See the [Wiki](https://github.com/osrbot/solidworks_urdf_exporter_pro/wiki) for detailed behavior and
limitations.

## Version Boundaries

Two independent version domains are intentionally visible in this repository:

- `URDF Export Configuration (v2)` is the PID-backed SolidWorks feature that persists Link and
  Joint selections in the assembly. Legacy name-based configuration v1.x is not migrated
  automatically because doing so could bind the wrong component occurrence.
- `robot.schema.v3` is the current canonical, temporary robot document used inside the export
  pipeline. Readers migrate historical robot schema v2 input to v3 with conservative OpenUSD
  defaults; writers emit v3 only. Compared with v2, v3 adds `profiles.usdSimulation` for base mode,
  robot classification, self-collision, SI gain units, and explicit per-Joint drive intent.

Saving SolidWorks configuration v2 and exporting through robot schema v3 is therefore the expected
combination, not a partial upgrade. The schema migration does not change the configured Link tree,
URDF Joint type, or CAD reference identity.

## Supported Environment

| Item | Supported or verified state |
| --- | --- |
| Operating system | Windows x64 |
| Target framework | .NET Framework 4.8 |
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

The public release process is intentionally manual-gated. The exact maintainer-built installer must
first pass live SolidWorks testing and the `solidworkstools.dll` redistribution review in
`THIRD_PARTY_NOTICES.md`. Only then may the maintainer invoke CI to validate that committed candidate
and create a Draft Release. CI never makes the Draft public; publication requires a separate,
explicit maintainer approval.

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
7. Select at least one concrete target: ROS 1 package, ROS 2 package, OpenUSD robot asset, or MuJoCo
   MJCF model. USD and MJCF require STL geometry. The exporter does not ask for Isaac versions,
   actuator profiles, or a user-managed staging Bundle. Open **Settings...** beside OpenUSD only
   when you need to override the conservative source-base, self-collision-off, passive-Joint defaults.
8. Review the common `export_report.md` and target-local reports before simulation. For ROS, inspect
   `config/export_report.md`, `config/inertial_validation.csv`, and `config/mesh_manifest.csv`. For USD or MJCF, inspect the
   target's `export_report.json` and `name_map.json`. Then run the asset in the actual application
   and task environment.

## Output Layout

```text
<export-root>/
|-- export_report.md                  # common exporter summary
|-- ROS1/<package>/                   # optional ROS 1 package
|   |-- meshes/
|   |-- urdf/
|   `-- config/
|-- ROS2/<package>/                   # optional ROS 2 package
|   |-- meshes/
|   |-- urdf/
|   `-- config/
|-- USD/<package>/                    # optional OpenUSD robot asset
|   |-- robot.usd
|   |-- geometry/
|   |-- meshes/
|   |-- name_map.json
|   `-- export_report.json
`-- MuJoCo/<robot>/                   # optional MJCF model
    |-- robot.xml
    |-- scene.xml
    |-- assets/visual/
    |-- assets/collision/
    |-- name_map.json
    `-- export_report.json
```

The private staging Bundle is intentionally absent from this layout. Report files identify the
effective collision strategy, fallbacks, mesh records, validation runtime, and evidence boundary.
Reusing an output directory is supported; the completion summary counts only files created or
changed by the current export.

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

The maintainers acknowledge this
[community SolidWorks-to-URDF inertia article](https://zhuanlan.zhihu.com/p/1887859297221845818)
as background reading. The implementation, tests, and export reports remain the source of truth for
the API-to-URDF mapping used by this exporter; the acknowledgement does not imply a source-code
contribution from the article.

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

- RGBA fields and the color picker directly set the selected Link color.
- A stable URDF material ID is derived from RGBA and shown for identification; it is not a second
  color-preset selector.
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
- [OpenUSD](docs/wiki/OpenUSD.md)
- [MuJoCo MJCF](docs/wiki/MJCF.md)
- [Troubleshooting](docs/wiki/Troubleshooting.md)
- [Contributing](docs/wiki/Contributing.md)
- [Release Process](docs/wiki/Release-Process.md)
- [Joint semantics and provenance](docs/architecture/joint-semantics-and-provenance.md)
- [Compatibility matrix](docs/development/compatibility-matrix.md)
- [OpenUSD downstream Isaac boundary](docs/isaac/README.md)
- [Changelog](CHANGELOG.md)

The files under `docs/wiki` are the version-controlled source for the public GitHub Wiki. Canonical
filenames are English; paired Simplified Chinese pages use the `-zh-CN` suffix. Update both language
versions in the same change when behavior changes.

## Development

Prerequisites:

1. Visual Studio 2017 with `.NET desktop development` and .NET Framework 4.8 targeting tools.
2. SolidWorks and the matching SolidWorks API tools/assemblies.
3. An x64 developer environment. Run Visual Studio as administrator when COM registration or
   SolidWorks debugging requires it.
4. .NET SDK 8 for the portable Core, CLI, and hosted unit tests.

Open `SW2URDF.sln`. For debugging, configure the `SW2URDF` project to start the installed
`SLDWORKS.exe` for the target SolidWorks version.

Build the production project with the actual SolidWorks install directory:

```powershell
MSBuild.exe SW2URDF\SW2URDF.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 `
  "/p:SolidWorksInstallDir=C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
```

Run all locally available tests after building Debug:

```powershell
TestRunner\bin\Debug\net48\TestRunner.exe
```

Run a focused class or name filter:

```powershell
TestRunner\bin\Debug\net48\TestRunner.exe TestCollisionPreview
```

Run the deterministic plugin gate used by the installer build without launching a local
SolidWorks process:

```powershell
TestRunner\bin\Debug\net48\TestRunner.exe --exclude-live-solidworks
```

Pure tests can run without SolidWorks. Live COM tests require a compatible installed SolidWorks and
can fail with an RPC/COM error when SolidWorks is unavailable or the automation process terminates.
Tests tagged `Category=LiveSolidWorks`, including tests in the legacy
`Requires SW Test Collection`, are intentionally excluded from reproducible installer packaging
and must be run explicitly as Live API evidence. The installer provenance
records that separation instead of treating an unrequested Live run as passed.
Live coverage on SolidWorks 2023 is not evidence of compatibility with every release or service
pack.

Explicit Live runs require `SW2URDF_RUN_SW_INTEGRATION_TESTS=1`; missing opt-in or fixture inputs
fail rather than being counted as a pass.

The deep-reference Live test uses a disposable five-level assembly. Close SolidWorks first; the
generator starts and owns an isolated SolidWorks process, writes the fixture under the system
temporary directory when `--output-directory` is omitted, and closes that process before returning.
The Live test deliberately accepts only that default temporary-directory fixture:

```powershell
python -m pip install pywin32
$fixture = python scripts\create_deep_reference_fixture.py `
  examples\3_DOF_ARM\3_DOF_ARM.SLDASM
$env:SW2URDF_RUN_DEEP_REFERENCE_TESTS = "1"
$env:SW2URDF_TEST_DEEP_REFERENCE_ASSEMBLY = $fixture
TestRunner\bin\Debug\net48\TestRunner.exe TestDeepReferenceGeometryIntegration
```

The generator depends only on public `pywin32` and SolidWorks COM APIs. If automatic template
discovery is unavailable, pass an explicit `--assembly-template C:\path\assembly.asmdot`.

## Reproducible Installer Build

From a clean source commit:

```powershell
.\scripts\BuildInstaller.ps1 -Configuration Release -Platform x64 `
  -SolidWorksInstallDir "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS" `
  -DotNetPath "C:\Path\To\dotnet-sdk-8.0.424\dotnet.exe" `
  -InnoCompilerPath "C:\Path\To\Inno Setup 6.3.3\ISCC.exe"
```

The script requires the exact .NET SDK 8.0.424 and Inno Setup 6.3.0 through 6.3.3, and produces:

```text
INSTALL/OUTPUT/sw2urdfSetup_YYYYMMDD_<commit>.exe
INSTALL/OUTPUT/sw2urdfSetup_YYYYMMDD_<commit>.exe.sha256
INSTALL/OUTPUT/sw2urdfSetup_YYYYMMDD_<commit>.exe.provenance.json
```

Packaging runs from a detached worktree, verifies pinned NuGet, OpenUSD, and official MuJoCo inputs
plus the staged SolidWorks API assemblies, and records payload hashes. The provenance file is a
maintainer-build trace; it is not an Authenticode signature and CI does not rebuild against
proprietary SolidWorks assemblies.

Before building a candidate, add `.github/release-notes/vYYYYMMDD.md` with reviewed `## English` and
`## 简体中文` sections. CI renders only traceability placeholders and fails closed when either language
or a required placeholder is missing; it does not machine-translate the Changelog.

## Known Limits

- Installation/upgrading requires SolidWorks to be closed; no automatic process termination or hot
  reload is implemented.
- Unsaved assemblies do not have a stable path and cannot use per-assembly recovery drafts.
- Component persistent IDs can become invalid after delete/replace/save-as operations and then need
  manual rebinding.
- STEP and other geometry-only imports do not carry reliable Joint semantics. A fixed or
  fully-constrained assembly is not automatically classified as a fixed-joint robot.
- Collision previews are temporary SolidWorks geometry. For mesh strategies, preview geometry is not
  promised to be byte-identical to the final tessellated STL.
- STL does not carry UV texture coordinates. The maintained UI does not offer texture authoring.
- Strategy generation can fall back to `VisualMesh`; always review the effective strategy report.
- Deep/hidden Link preview changes require live validation in the maintainer's target SolidWorks
  versions before a public release.
- USD validation proves OpenUSD stage generation and reopen only. It does not prove import or
  execution in Isaac Sim or Isaac Lab.
- MJCF validation proves official MuJoCo compile/save/reload and one zero-control step only. It does
  not prove controllers, contact tuning, long-horizon stability, performance, task behavior, or RL.
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
- Community inertia reference supplied by the maintainers:
  [SolidWorks-to-URDF inertia article](https://zhuanlan.zhihu.com/p/1887859297221845818)
- Original ROS documentation: [sw_urdf_exporter](http://wiki.ros.org/sw_urdf_exporter) and
  [tutorials](http://wiki.ros.org/sw_urdf_exporter/Tutorials)

## License

MIT. See [LICENSE](LICENSE). The original `Copyright 2020 Stephen Brawner` notice and permission
terms must remain with copies or substantial portions of the software.
