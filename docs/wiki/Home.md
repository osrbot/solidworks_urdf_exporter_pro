# SW2URDF Wiki

[简体中文](Home-zh-CN) | **English**

SW2URDF is a community-maintained SolidWorks robot-model exporter. It converts user-reviewed Link,
Joint, frame, mass-property, Visual, and Collision data into four concrete deliverables: ROS 1 and
ROS 2 packages, an OpenUSD robot asset, and a MuJoCo MJCF model.

Project: <https://github.com/osrbot/solidworks_urdf_exporter_pro>

## Start here

- First export: [Quick Start](Quick-Start)
- Installation and upgrades: [Installation](Installation)
- Link hierarchy and persistence: [Link Tree](Link-Tree)
- Mass, center of mass, and inertia: [Inertia](Inertia)
- Collision strategies and preview: [Collision](Collision)
- USD output and evidence: [OpenUSD](OpenUSD)
- MuJoCo output and evidence: [MuJoCo MJCF](MJCF)
- Export failures: [Troubleshooting](Troubleshooting)

## Outputs

| Target | Directory | Main contents |
| --- | --- | --- |
| ROS 1 | `ROS1/<package>` | URDF, meshes, configuration, and reports |
| ROS 2 | `ROS2/<package>` | URDF, meshes, configuration, and reports |
| OpenUSD | `USD/<package>` | `robot.usd`, geometry dependencies, name map, and report |
| MuJoCo MJCF | `MuJoCo/<robot>` | `robot.xml`, `scene.xml`, mesh assets, name map, and report |

At least one target must be selected. Robot Bundle is private temporary staging, not a fifth user
target. OpenUSD does not require a local Isaac installation or version field. MJCF does not generate
actuators, controllers, tasks, or reinforcement-learning projects.

## Keep the data roles separate

- `visual` serves rendering and recognition.
- `collision` serves contact solving with deliberately simpler geometry.
- `inertial` stores mass, center of mass, and the inertia tensor.

Changing a Collision strategy does not recompute Inertial data. SolidWorks previews support review;
the exported files and reports state what was actually delivered.

## Version domains

- The product version identifies the installer and DLL.
- `URDF Export Configuration (v2)` is the PID-backed configuration saved in SolidWorks.
- `robot.schema.v3` is the temporary canonical document used during export.
- `usd-core 26.8` and MuJoCo `3.12.0` are the currently pinned validation tools.

These versions have different jobs. UI or documentation changes do not justify a schema major
version bump; only an incompatible data-contract change does.

## Evidence boundary

- OpenUSD output is generated and reopened with the pinned runtime.
- MuJoCo output is compiled, canonically saved, reloaded, and advanced one zero-control step with
  pinned official tools.
- A manually triggered ROS 2 integration gate builds and launches a minimum fixture in selected
  ROS 2/Gazebo environments and checks its controllers.
- ROS 1 currently has structural and generation coverage.
- None of these checks replaces engineering acceptance of the user's controller, contact settings,
  or task.

Current live API maintenance focuses on SolidWorks 2023. SolidWorks 2018 SP5 is the inherited
historical minimum, not a claim that every release and service pack is covered. See the
[compatibility matrix](../development/compatibility-matrix) for the exact evidence.
