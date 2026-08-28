# SolidWorks to URDF Exporter Wiki

**English** | [简体中文](Home-zh-CN)

This is the detailed user and maintainer documentation for the OSRBot-maintained fork. See the
[README](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/README.md) for the project
entry point, support boundaries, and credits.

## Why the Maintained Fork Exists

The upstream exporter supplied the original add-in and URDF pipeline. The maintained fork is needed
to close production gaps rather than merely repackage the historical binaries:

- Link-tree sessions now have transactional editing, saved v1.5 configurations, recovery drafts,
  and stricter validation across preview and reopen transitions.
- Mass, COM, and inertia use explicit units and one frame-conversion route for parts and assemblies,
  with bounds, physical-tensor, and API-principal-moment checks.
- Collision strategies are fitted per Link, previewed in SolidWorks, and recorded with any fallback
  so the requested and actually exported geometry can be distinguished.
- The maintained workflow adds deterministic Link coloring, Simplified Chinese UI, export progress,
  validation reports, and reproducible draft-only installer packaging.

The upstream history, authorship, and MIT license remain intact. See the
[Changelog](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/CHANGELOG.md) for dated
changes and commit evidence.

## Project Scope

The Windows x64 SolidWorks add-in exports explicitly configured Links, Joints, coordinate systems,
mass properties, Visual geometry, and Collision geometry into URDF plus matching ROS1/ROS2
description packages.

It keeps three responsibilities separate:

- `visual` serves rendering and recognition; recognizable appearance and geometry matter.
- `collision` serves contact solving; geometry should be as simple as possible while preserving
  task-relevant contact shape.
- `inertial` serves dynamics; mass, center of mass, and inertia tensor should remain physically
  faithful.

Changing a Collision strategy does not recompute or replace Inertial. Temporary collision and
equivalent-inertia bodies in SolidWorks are inspection aids. The exported URDF,
`mesh_manifest.csv`, and `inertial_validation.csv` record the formal result.

## Documentation

- [Installation](Installation): installation, upgrade, and version boundaries
- [Quick Start](Quick-Start): SolidWorks assembly to ROS package
- [Link Tree](Link-Tree): hierarchy, transactional editing, persistence, and recovery
- [Inertia](Inertia): frames, units, conventions, and physical validation
- [Collision](Collision): strategies, previews, and fallbacks
- [Troubleshooting](Troubleshooting): symptom-based diagnosis
- [Contributing](Contributing): development, testing, and issue reports
- [Release Process](Release-Process): traceable installers and the manual publication gate

## Exported Result

A complete export writes `ROS1/<package>` and `ROS2/<package>`, including:

- `urdf/`: URDF model;
- `meshes/`: Visual and Collision meshes;
- `config/export_report.md`: export health summary and fallback information;
- `config/inertial_validation.csv`: per-Link inertia validation;
- `config/mesh_manifest.csv`: per-Link mesh and Collision strategy records.

## Evidence Boundaries

- The historical minimum requirement is SolidWorks 2018 SP5.
- Current maintenance and Live API verification focus on SolidWorks 2023.
- Live coverage on SolidWorks 2023 is not evidence that every release and service pack is verified.
- The software is provided under the MIT License. Production models still require validation in the
  intended simulator and task.

## Credits

- Upstream project: [ros/solidworks_urdf_exporter](https://github.com/ros/solidworks_urdf_exporter)
- Original author and historical maintainer: Stephen Brawner
- Historical supporters named by upstream: PickNik Consulting, Verb Surgical, Open Robotics, and
  Willow Garage
- 3DXML contribution: Kento Matsuo and the contributors recorded by commit `22cb778`
- Current maintainer: `kitso666 <kitso@osrbot.com>`
- Inertia concept reference: Winter,
  [掌握 URDF 中的惯性张量：从 SolidWorks 到强化学习机器人的关键一步](https://zhuanlan.zhihu.com/p/1887859297221845818)

The article helps explain COM-relative tensors, output frames, and product-of-inertia notation
traps. The API path, code, tests, and export reports remain the source of truth for this exporter.
