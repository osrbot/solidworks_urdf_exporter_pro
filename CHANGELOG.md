# Changelog

All notable OSRBot-maintained changes to this fork are documented here.

## 2026-06-29

### Added

- Added a built-in exporter guide window with collision strategy guidance, common material names, the project URL, and the current maintainer contact.
- Added common material name presets for exported URDF materials, including `aluminum`, `steel`, `rubber_black`, and `transparent_blue`.
- Added GitHub Actions release publishing for installer artifacts named `INSTALL/OUTPUT/sw2urdfSetup_YYYYMMDD_<commit>.exe`.

### Changed

- Shortened the ROS package output hint in the Link page to `ROS1/<name> | ROS2/<name>`.
- Documented the maintained fork, installer naming convention, and release automation in `README.md`.

### Fixed

- Fixed Link page footer layout feedback where repeated layout passes could push export buttons downward and leave a stale vertical scrollbar.
- Fixed high-DPI Link and Joint page layout regressions around footer buttons, mimic-joint controls, and inertia matrix display.
- Hardened ROS2 package export so meshes are copied alongside the generated URDF.
- Improved UTF-8 English logging for export diagnostics to avoid mojibake in log files.

### Packaging

- Published installer: `INSTALL/OUTPUT/sw2urdfSetup_20260629_598c7dd.exe`.

## Earlier OSRBot Work

- Added native ROS1 and ROS2 package output support.
- Improved SolidWorks mass property and inertia tensor export, including per-link comparison reporting against SolidWorks values.
- Added collision mesh strategy support, mesh reduction controls, and mesh export manifest/report output.
- Added Chinese UI localization while preserving ROS-compatible package and link naming.
