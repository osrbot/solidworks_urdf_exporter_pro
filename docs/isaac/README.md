# OpenUSD Asset for Downstream Isaac Workflows

**English** | [简体中文](README.zh-CN.md)

This page defines a boundary, not an Isaac-specific exporter contract.

The SolidWorks exporter can produce `USD/<package>/robot.usd` plus its geometry dependencies,
retained source meshes, `name_map.json`, and `export_report.json`. Generation uses the pinned
OpenUSD runtime bundled with the installer. Export succeeds only after that runtime reopens the
stage and verifies the expected robot structure.

The exporter does not:

- require or detect Isaac Sim or Isaac Lab on the Windows CAD workstation;
- ask for an Isaac Sim or Isaac Lab version;
- call Isaac-specific importer APIs or extensions;
- infer gains or generate actuator groups, PID/controller files, task environments, sensors,
  observations, rewards, resets, domain randomization, or reinforcement-learning code;
- claim that the asset has been imported, rendered, or simulated in Isaac Sim or Isaac Lab.

The optional OpenUSD settings dialog records only portable asset intent: source/fixed/floating base,
official robot classification, articulation self-collision, and passive/position/velocity/effort
intent for supported single-DOF Joints. Position and velocity may author `DriveAPI` with explicitly
entered stiffness/damping. Effort remains metadata for downstream runtime setup and does not create
an active drive.

To use the asset downstream, copy the complete `USD/<package>` directory to the target machine and
import `robot.usd` through the target Isaac version's documented workflow. Validate articulation
mapping, units, materials, Collision behavior, mass properties, controller setup, and task behavior
in that actual environment.

The evidence labels are intentionally narrow:

- **OpenUSD generated**: the documented files were written.
- **OpenUSD reopened**: the bundled OpenUSD runtime reopened and structurally checked the stage.
- **Isaac validated**: not performed by this exporter.

See the wiki [OpenUSD page](../wiki/OpenUSD.md) for the complete file layout and known mappings.
