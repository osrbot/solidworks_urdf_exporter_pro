# OpenUSD

## Why Use It

OpenUSD is suitable for bringing a SolidWorks robot into Isaac Sim or another USD-compatible tool.
The plugin generates the robot asset directly. Isaac Sim does not need to be installed on the export
computer, and no Isaac Sim or Isaac Lab version is required.

## Output Directory

```text
USD/<package>/
|-- robot.usd
|-- geometry/
|-- meshes/
|-- name_map.json
`-- export_report.json
```

`robot.usd` is the main entry point. The `geometry/` directory contains the USD geometry it
references, while `meshes/` retains the source STL files.

::: warning When Moving Files
Copy the complete `USD/<package>` directory. Copying only `robot.usd` will leave its geometry
dependencies behind.
:::

## OpenUSD Settings

The settings page lets you choose the base behavior, enable or disable self-collision, and set each
movable joint to passive, position, velocity, or force control intent. Stiffness and damping use
only values entered by the user; the plugin does not infer control parameters from CAD geometry.

![OpenUSD settings](/screenshots/openusd-settings.png)

## Paths and Encoding

The main file is readable UTF-8 text, and geometry files use relative paths. The complete directory
can be copied to another computer without depending on the original drive letter or user directory.

![Local OpenUSD preview](/screenshots/openusd-local-preview.png)

<p class="caption">A local check with geometry loaded from robot.usd.</p>

## Checks After Export

The plugin reopens the generated USD and checks its file references. After importing it into Isaac
Sim, you should still verify materials, collisions, joint drives, contact parameters, and behavior
in the actual task.
