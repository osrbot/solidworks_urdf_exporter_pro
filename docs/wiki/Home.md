# SW2URDF Wiki

[简体中文](Home-zh-CN) | **English**

SW2URDF is a community-maintained SolidWorks robot-model exporter. It turns assembly Link, Joint,
frame, mass, collision, and appearance data into files that ROS, OpenUSD, or MuJoCo can use.

Project: <https://github.com/osrbot/solidworks_urdf_exporter_pro>

## Why use it

- Reduce repeated manual work on hierarchy, frames, mesh paths, mass, and inertia.
- Resolve deep, duplicate, and non-English reference geometry while keeping Joint intent explicit.
- Review inertia and Collision before export, with clearer errors and reports when something fails.
- Export ROS 2, OpenUSD, and MuJoCo MJCF in addition to ROS 1.
- Configure Link, Joint, inertia, collision, and appearance in focused pages that handle long paths.

## Start here

- First export: [Quick Start](Quick-Start)
- Installation and upgrades: [Installation](Installation)
- Link hierarchy and persistence: [Link Tree](Link-Tree)
- Joint type, frames, and limits: [Joint Properties](Joint)
- Mass, center of mass, and inertia: [Inertia](Inertia)
- Collision strategies and preview: [Collision](Collision)
- Color and automatic coloring: [Appearance](Appearance)
- Output selection and export: [Model and Export](Export)
- USD files and use: [OpenUSD](OpenUSD)
- MuJoCo files and use: [MuJoCo MJCF](MJCF)
- Export failures: [Troubleshooting](Troubleshooting)

## What it exports

| Target | Directory | Main contents |
| --- | --- | --- |
| ROS 1 | `ROS1/<package>` | URDF, meshes, configuration, and reports |
| ROS 2 | `ROS2/<package>` | URDF, meshes, configuration, and reports |
| OpenUSD | `USD/<package>` | `robot.usd`, geometry dependencies, name map, and report |
| MuJoCo MJCF | `MuJoCo/<robot>` | `robot.xml`, `scene.xml`, mesh assets, name map, and report |

At least one target must be selected. OpenUSD does not require a local Isaac installation or version
field. MJCF delivers a robot model rather than controllers, tasks, or a reinforcement-learning
project. Check frames, collision, inertia, and motion again in the actual target application.

## Questions and contributions

- Report a problem: <https://github.com/osrbot/solidworks_urdf_exporter_pro/issues>
- Contribute code or documentation: [Contributing](Contributing)
- Maintained-fork contributors: [kitso666](https://github.com/kitso666),
  [W472351926](https://github.com/W472351926), [dajianli](https://github.com/dajianli), and
  [sunmaxwll](https://github.com/sunmaxwll)
- Complete record: [Contributors](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/CONTRIBUTORS.md)
