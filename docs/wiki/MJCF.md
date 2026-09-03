# MuJoCo MJCF Model

**English** | [简体中文](MJCF-zh-CN)

## Why use it

MJCF output moves a SolidWorks robot into MuJoCo for later scene, actuator, and controller work. It
creates a loadable robot model, not a controller stack, task environment, or reinforcement-learning
project. Export does not require a separate MuJoCo installation.

## What it exports

```text
MuJoCo/<robot>/
|-- robot.xml
|-- scene.xml
|-- assets/visual/
|-- assets/collision/
|-- name_map.json
`-- export_report.json
```

`robot.xml` is the robot model. `scene.xml` is a minimum scene that includes it. The model retains
the Link hierarchy, visual and collision geometry, CAD mass, center of mass, and inertia.

## Joint conversion

| Exporter Joint | MJCF result |
| --- | --- |
| fixed | no movable Joint |
| revolute / continuous | `hinge` |
| prismatic | `slide` |
| floating | three `slide` Joints plus one `ball` |
| planar | asks the user to handle it instead of silently approximating it |

## After export

1. Open the minimum scene from `scene.xml`.
2. Check axes, ranges, inertia, and collision.
3. Add actuators, controllers, friction, contacts, sensors, and scene content for the real project.

`scene.xml` is an entry point for loading the robot, not a finished simulation or reinforcement
learning project.
