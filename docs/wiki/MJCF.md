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

## Simulation settings

Use **Simulation Settings** on the export page to choose a fixed or floating base and joint control intent. Floating base creates a root free joint. The MuJoCo tab holds its own gains and force limits, independently of OpenUSD.

- Position: positive stiffness and nonnegative damping.
- Velocity: positive velocity gain.
- Effort: a direct force/torque actuator.
- Passive: no actuator; the joint is not locked.

Fixed and Mimic follower joints do not receive independent active drives. Missing valid force limits block MJCF rather than producing an unrestricted actuator. Gains are not automatically tuned.

## After export

1. Open the minimum scene from `scene.xml`.
2. Check axes, ranges, inertia, and collision.
3. Check the configured actuators and add controllers, friction, contacts, sensors, and scene content for the real project.

`scene.xml` is an entry point for loading the robot, not a finished simulation or reinforcement
learning project.
