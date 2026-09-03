# Model and Export

**English** | [简体中文](Export-zh-CN)

The final page sets robot-level information, selects outputs, and starts the export.

## Basic information

ROS packages need a package name, version, description, maintainer, email, and license. A clear name
and license are also useful for identifying OpenUSD and MJCF output directories and reports.

## Output choices

- ROS 1 package
- ROS 2 package
- OpenUSD robot asset
- MuJoCo MJCF model

Select at least one target. After selecting OpenUSD, open its settings only when base behavior,
self-collision, or Joint drive intent needs adjustment; the defaults can be exported directly.

Export URDF Without Meshes is useful for a quick structure and value check. Export URDF and Meshes
creates the deliverable directory and is required for OpenUSD and MJCF. Read the export report before
opening the target output.
