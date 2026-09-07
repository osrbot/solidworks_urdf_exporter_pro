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

Common simulation settings select base behavior and passive, position, velocity, or effort intent for single-DOF joints. The separate OpenUSD tab stores stiffness, damping, self-collision and robot type. MJCF gains are not used by USD.

Absent or `null` common settings preserve legacy USD behavior. A common `source` base preserves the previous USD choice; `fixed` / `floating` overrides it. When common settings are present, their joint list is authoritative: unlisted joints are passive and an empty list clears all drives. Legacy USD entries supply gains only. Active mimic intents are rejected. `fixed` adds a world-fixed joint; `floating` does not inject one and does not remove internal source joints.

Defaults create no active drives. Position/velocity modes author USD DriveAPI; velocity stiffness is zero. Gains use SI units: angular gains entered per radian are multiplied by `pi/180` when authored to USD, while linear gains remain unchanged. Switching to passive/effort removes gains and DriveAPI from the resolved output. Effort is runtime intent plus an effort limit, reported in `export_report.json`, not an authored force actuator; a downstream controller must apply force.

Invalid types, duplicate or missing joints, and non-finite gains fail explicitly. The plugin does not infer control parameters, generate a training world, or claim Isaac Sim runtime validation.

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
