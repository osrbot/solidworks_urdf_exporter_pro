# MuJoCo MJCF

## Why Use It

MJCF output is intended for bringing a CAD robot into MuJoCo, where you can continue adding the
scene, actuators, controllers, and task. The plugin generates a loadable robot model, not a
reinforcement-learning project.

## Output Directory

```text
MuJoCo/<robot>/
|-- robot.xml
|-- scene.xml
|-- assets/visual/
|-- assets/collision/
|-- name_map.json
`-- export_report.json
```

`robot.xml` contains the robot, while `scene.xml` is a minimal scene that references it.

## Joint Conversion

| SolidWorks/URDF Joint | MJCF Output |
| --- | --- |
| fixed | No movable joint is generated |
| revolute / continuous | `hinge` |
| prismatic | `slide` |
| floating | Three `slide` joints and one `ball` joint |
| planar | The plugin asks the user to handle it instead of applying a silent approximation |

## Checks After Export

The plugin uses the MuJoCo tools included with the installer to verify that both XML entry points
load and complete a minimal simulation step. You must still add and validate actuators, controllers,
friction, contacts, simulation timestep, and task parameters in your own project.
