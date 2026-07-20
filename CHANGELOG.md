# Changelog

All notable OSRBot-maintained changes to this fork are documented here.

## 2026-07-20

### Changed

- Split Link tree topology, reusable URDF configuration, and SolidWorks CAD bindings into separate stores coordinated by a transactional session.
- Changed legacy TreeView and export models into generated projections so UI edits no longer mutate the committed Link tree state indirectly.
- Copy/paste now preserves reusable URDF configuration while intentionally clearing CAD component bindings on copied Links.
- Changed CSV configuration merge to a modal operation so a stale merge snapshot cannot overwrite concurrent Link tree edits.
- Removed the legacy export side effect that detached the root node from the PropertyManager TreeView before creating the robot.

### Fixed

- Preserved canvas node identity when reopening the editor after editing Link properties or structure in the legacy PropertyManager tree.
- Prevented stale TreeView structure from diverging from the tree used for configuration serialization or URDF export.
- Migrated mimic references when a Joint is renamed and rejected deletion when surviving Joints still reference the removed name.
- Forced Joint kinematics recomputation after drag-to-reparent so an Origin calculated for the old parent cannot be exported.

## 2026-07-17

### Added

- Added a transactional Link tree canvas to the SolidWorks PropertyManager workflow.
- Added free node placement, drag-to-reparent, automatic layout, box selection, and structure-only group copy/paste with automatic Link and Joint name deduplication.
- Added Link tree validation for a single root, unique ROS-compatible names, valid parents, and cycle prevention.

### Changed

- Existing Link configuration values and SolidWorks bindings remain intact when canvas structure changes are applied.
- New Links start empty; pasted Links copy reusable URDF settings but are marked incomplete and start without SolidWorks component bindings.

## 2026-06-29

### Added

- Added a built-in exporter guide window with collision strategy guidance, common material names, the project URL, and the current maintainer contact.
- Added common material name presets for exported URDF materials, including `aluminum`, `steel`, `rubber_black`, and `transparent_blue`.
- Added GitHub Actions release publishing for installer artifacts named `INSTALL/OUTPUT/sw2urdfSetup_YYYYMMDD_<commit>.exe`.
- Added user-facing documentation for automatic Link tree configuration loading, CSV configuration merge, and export report files.

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
