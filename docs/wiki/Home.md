# SolidWorks to URDF Exporter Wiki

**English** | [简体中文](Home-zh-CN)

This is the detailed user and maintainer documentation for the OSRBot-maintained fork. See the
[README](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/README.md) for the project
entry point, support boundaries, and credits.

## Why the Maintained Fork Exists

The upstream exporter supplied the original add-in and URDF pipeline. The maintained fork is needed
to close production gaps rather than merely repackage the historical binaries:

- Link-tree sessions now have transactional editing, strict v2 PID-backed configurations, recovery
  drafts, and stricter validation across preview and reopen transitions. Component-instance and
  feature PIDs keep nested Unicode or duplicate reference-geometry names unambiguous.
- STEP and fixed assemblies use a reviewable manual Joint workflow. Mate detection is an explicitly
  triggered assistant, and zero remaining DOF is never silently exported as `fixed`.
- Mass, COM, and inertia use explicit units and one frame-conversion route for parts and assemblies,
  with bounds, physical-tensor, and API-principal-moment checks.
- Collision strategies are fitted per Link, previewed in SolidWorks, and recorded with any fallback
  so the requested and actually exported geometry can be distinguished.
- The maintained workflow adds deterministic Link coloring, Simplified Chinese UI, export progress,
  validation reports, and reproducible draft-only installer packaging.
- OpenUSD and MJCF are concrete asset outputs with pinned automated checks; neither target is
  presented as a controller, task, or reinforcement-learning project.

The upstream history, authorship, and MIT license remain intact. See the
[Changelog](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/CHANGELOG.md) for dated
changes and commit evidence.

## Project Scope

The Windows x64 SolidWorks add-in exports explicitly configured Links, Joints, coordinate systems,
mass properties, Visual geometry, and Collision geometry into four concrete targets: ROS 1 package,
ROS 2 package, OpenUSD robot asset, and MuJoCo MJCF model.

It keeps three responsibilities separate:

- `visual` serves rendering and recognition; recognizable appearance and geometry matter.
- `collision` serves contact solving; geometry should be as simple as possible while preserving
  task-relevant contact shape.
- `inertial` serves dynamics; mass, center of mass, and inertia tensor should remain physically
  faithful.

Changing a Collision strategy does not recompute or replace Inertial. Temporary collision and
equivalent-inertia bodies in SolidWorks are inspection aids. The exported URDF,
`mesh_manifest.csv`, and `inertial_validation.csv` record the formal result.

`Robot Bundle` is a private canonical staging representation. It is created in the system temporary
directory, consumed by the selected target exporters, and cleaned after export. It is not a
user-selectable target or a delivered file tree.

## Documentation

- [Installation](Installation): installation, upgrade, and version boundaries
- [Quick Start](Quick-Start): SolidWorks assembly to the four export targets
- [Link Tree](Link-Tree): hierarchy, transactional editing, persistence, and recovery
- [Inertia](Inertia): frames, units, conventions, and physical validation
- [Collision](Collision): strategies, previews, and fallbacks
- [OpenUSD](OpenUSD): delivered USD files and validation boundary
- [MuJoCo MJCF](MJCF): delivered MJCF files and official-runtime validation boundary
- [Troubleshooting](Troubleshooting): symptom-based diagnosis
- [Contributing](Contributing): development, testing, and issue reports
- [Release Process](Release-Process): traceable installers and the manual publication gate

## Exported Results

| Target | Delivered directory | Main contents |
| --- | --- | --- |
| ROS 1 package | `ROS1/<package>` | URDF, Visual/Collision meshes, configuration, Markdown/CSV reports |
| ROS 2 package | `ROS2/<package>` | URDF, Visual/Collision meshes, configuration, Markdown/CSV reports |
| OpenUSD asset | `USD/<package>` | `robot.usd`, geometry dependencies, source mesh evidence, `name_map.json`, `export_report.json` |
| MuJoCo MJCF | `MuJoCo/<robot>` | `robot.xml`, `scene.xml`, Visual/Collision assets, `name_map.json`, `export_report.json` |

The four targets are independent selections, but at least one is required. USD and MJCF require STL
geometry. There is no user-facing Isaac version, Isaac Lab profile, actuator profile, or Bundle
destination.

An export atomically replaces only its selected target directories. Unselected target directories
are retained and may contain results from an earlier run; check the top-level `export_report.md` for
the targets generated and validated by the current run.

## Evidence Boundaries

- **Generation capability:** the exporter writes the documented target files from one validated
  canonical model.
- **Automated validation:** OpenUSD is generated and reopened with the pinned bundled OpenUSD
  runtime. Both MJCF entry points are compiled, canonically saved, reloaded, and advanced one
  zero-control step with pinned official MuJoCo tools.
- **Application runtime validation:** no USD export result claims Isaac Sim or Isaac Lab execution;
  no ROS result claims a ROS/Gazebo launch; no MJCF result claims controller quality, contact tuning,
  long-horizon stability, task behavior, performance, or reinforcement-learning validation.
- The historical minimum requirement is SolidWorks 2018 SP5.
- Current maintenance and Live API verification focus on SolidWorks 2023.
- Live coverage on SolidWorks 2023 is not evidence that every release and service pack is verified.
- Deep-reference and temporary-preview changes still require the maintainer's live SolidWorks test
  before a public release.
- The software is provided under the MIT License. Production models still require validation in the
  intended simulator and task.

## Credits

- Upstream project: [ros/solidworks_urdf_exporter](https://github.com/ros/solidworks_urdf_exporter)
- Original author and historical maintainer: Stephen Brawner
- Historical supporters named by upstream: PickNik Consulting, Verb Surgical, Open Robotics, and
  Willow Garage
- 3DXML contribution: Kento Matsuo and the contributors recorded by commit `22cb778`
- Current maintainer: `kitso666 <kitso@osrbot.com>`
- Community inertia reference supplied by the maintainers:
  [SolidWorks-to-URDF inertia article](https://zhuanlan.zhihu.com/p/1887859297221845818)

The reference is acknowledged for background reading. The API path, code, tests, and export reports
remain the source of truth for this exporter.
