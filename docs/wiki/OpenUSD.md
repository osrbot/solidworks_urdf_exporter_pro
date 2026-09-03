# OpenUSD Robot Asset

[简体中文](OpenUSD-zh-CN) | **English**

## Why use it

OpenUSD is intended for moving a SolidWorks robot into Isaac Sim or another USD-capable tool. The
exporter creates a robot asset without requiring Isaac Sim on the Windows export computer or asking
for an Isaac Sim or Isaac Lab version.

## Delivered files

```text
USD/<package>/
|-- robot.usd
|-- geometry/                 # USD geometry referenced by robot.usd
|-- meshes/                   # retained source STL files
|-- name_map.json             # source-to-exported name mapping
`-- export_report.json        # counts and validation results
```

`robot.usd` contains the robot hierarchy, Visuals, Collisions, mass, center of mass, inertia, and
Joints.

## OpenUSD settings

The main export page has one OpenUSD target. Its optional settings cover:

- **Base behavior:** keep the source behavior, fixed base, or floating base.
- **Robot type:** classification for downstream tools.
- **Self-collision:** whether robot Links may collide with each other.
- **Joint drive:** passive, position, velocity, or effort intent.
- **Stiffness and damping:** only explicit user values are used; they are not guessed from CAD.

Keep the defaults when unsure, export the asset, and inspect it in the target tool.

## Paths and encoding

The root file is readable UTF-8 text and uses relative geometry references. Move the complete
`USD/<package>` directory between computers. Copying only `robot.usd` drops its geometry.

## Automatic check

Before delivery, the exporter reopens `robot.usd` and checks that the hierarchy, Joints, and local
geometry references can be read. This proves the USD asset is structurally complete; it does not
prove physical, controller, or task behavior in a particular Isaac Sim release.

## What to inspect next

1. Link hierarchy and scale.
2. Visuals and materials.
3. Collision and contacts.
4. Mass, center of mass, and inertia.
5. Joint directions, limits, and drives.
6. The actual controller and task behavior.
