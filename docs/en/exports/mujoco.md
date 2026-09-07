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

## Simulation Settings and Actuators

Common simulation settings choose the base and single-DOF joint modes. The separate MuJoCo tab stores MJCF parameters; USD stiffness/damping are not reused. Explicit common joint modes and a non-`source` base take priority; `source` preserves the target's previous base behavior. Defaults and passive joints generate no actuators.

- Position mode generates `position` with `kp` and `kv`.
- Velocity mode generates `velocity` with `kv`.
- Effort mode generates `motor` with `gear="1"`, unlike USD's runtime-only effort intent.

Required gains must be finite and nonnegative. Maximum force must be finite and positive; when omitted, it falls back to a valid joint effort limit. Explicit invalid values are errors, not fallback requests. Missing required parameters must be corrected, not inferred from CAD. `scene.xml` is only a minimal loading entry point, not a training world; rewards, policies and training projects are not generated.

## Runtime Validation

The plugin uses the MuJoCo tools included with the installer to verify that both XML entry points
load and complete a minimal simulation step. You must still validate selected actuators and add and validate controllers,
friction, contacts, simulation timestep, and task parameters in your own project.
