# SolidWorks to URDF Exporter

**English** | [简体中文](README.zh-CN.md)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-blue.svg)](#supported-environment)
[![Framework](https://img.shields.io/badge/.NET%20Framework-4.8-blueviolet.svg)](#supported-environment)

SW2URDF is a maintained continuation of the ROS community project
[`solidworks_urdf_exporter`](https://github.com/ros/solidworks_urdf_exporter). It configures Links,
Joints, frames, inertia, collision, and appearance in SolidWorks, then exports robot models for ROS,
OpenUSD, or MuJoCo workflows.

> This is a community-maintained project, not an official Dassault Systemes, ROS, NVIDIA, or MuJoCo
> distribution.

## Why use it

Turning CAD into a robot description by hand means repeatedly managing hierarchy, frames, mesh
paths, mass, inertia, and Joints. SW2URDF puts those steps in one three-stage wizard and checks common
problems before delivery.

It is useful for:

- maintaining ROS 1 or ROS 2 robot-description packages from SolidWorks;
- moving a robot asset into Isaac Sim or another USD-capable tool;
- moving a robot model into MuJoCo for further scene and controller work;
- reviewing mass, inertia, collision, and Joint direction before export.

## Compared with the community version

| Practical problem | Maintained version |
| --- | --- |
| Deep, duplicate, or non-English coordinate-system names are easy to confuse | Resolves the actual assembly branch for coordinate systems and axes |
| STEP or fixed assemblies do not provide reliable Joint intent | Uses explicit user choices; Mate detection only offers suggestions for review |
| Mass and inertia mistakes are hard to spot | Checks units, center of mass, inertia tensors, and principal moments |
| Collision output is difficult to judge before export | Adds primitives, component boxes, convex hulls, simplified meshes, and previews |
| Appearance and collision controls compete for space | Adds a separate appearance page with RGBA, color picker, and automatic coloring |
| Output is centered on traditional URDF | Adds ROS 2, OpenUSD, and MuJoCo MJCF |
| Error dialogs are difficult to reuse | Provides copyable details, logs, and export reports |
| Long names are clipped and old pages repaint slowly | Reworks layout for full paths and faster page switching |

## Export targets

| Target | Main result | Typical use |
| --- | --- | --- |
| ROS 1 package | URDF, meshes, configuration, and reports | Existing ROS 1 projects |
| ROS 2 package | URDF, meshes, configuration, and reports | ROS 2 description, display, and later control setup |
| OpenUSD | `robot.usd`, geometry, name map, and report | Isaac Sim or another USD tool |
| MuJoCo MJCF | `robot.xml`, `scene.xml`, meshes, and report | MuJoCo scene and controller development |

The exporter does not guess PID gains, controllers, friction, or task parameters from CAD geometry.
Those values must come from the real robot or a validated simulation model.

## Installation

1. Download a published x64 installer from
   [GitHub Releases](https://github.com/osrbot/solidworks_urdf_exporter_pro/releases).
2. Close every SolidWorks process.
3. Run the installer as administrator.
4. Restart SolidWorks.
5. Open `Tools > Export as URDF`.

See the [installation guide](docs/guide/installation.md) for details.

## Quick workflow

1. Save the assembly and assign material or density to parts.
2. Build the Link tree and verify component ownership.
3. Configure each non-root Joint, frame, axis, and limit.
4. Review mass, center of mass, and inertia.
5. Select and preview Collision, then configure appearance.
6. Select the required export formats.
7. Read the `export_report` and target-specific reports.
8. Verify the result in the actual ROS, Isaac Sim, USD Viewer, or MuJoCo environment.

See [Quick Start](docs/guide/getting-started.md).

## Feature pages

- [Link Tree](docs/features/link-tree.md)
- [Joint properties](docs/features/joint.md)
- [Inertia](docs/features/inertia.md)
- [Visual and Collision](docs/features/collision.md)
- [Appearance](docs/features/appearance.md)
- [Model and Export](docs/features/export-page.md)

Output guides: [ROS](docs/exports/ros.md) · [OpenUSD](docs/exports/openusd.md) ·
[MuJoCo MJCF](docs/exports/mujoco.md)

## Supported environment

- Windows x64
- .NET Framework 4.8
- Current primary live testing: SolidWorks 2023
- Historical community minimum: SolidWorks 2018 SP5

The historical minimum does not mean that every intervening release and service pack is tested.
Validate production use with your own assemblies.

## Ask a question

Project: <https://github.com/osrbot/solidworks_urdf_exporter_pro>

Open a [GitHub Issue](https://github.com/osrbot/solidworks_urdf_exporter_pro/issues) with the
SolidWorks version, exporter version, reproduction steps, complete error text, log, and export
reports. See [Questions and contributions](docs/support/help-and-contribute.md).

## Contributing

Reproducible bug reports, tests, documentation, and focused code fixes are welcome. Pull Requests
should explain the problem, implementation, test results, and anything not yet validated. See
[Contributing](docs/wiki/Contributing.md) for development and test commands.

## Documentation

The documentation source is under [`docs/`](docs/index.md). Run it locally with:

```powershell
pnpm install --frozen-lockfile
pnpm docs:dev
```

## License and credits

This project is released under the [MIT License](LICENSE) and preserves the upstream project
history, authors, and contributions. Thanks to original author Stephen Brawner, PickNik Consulting,
Verb Surgical, Open Robotics, Willow Garage, and later community contributors.
