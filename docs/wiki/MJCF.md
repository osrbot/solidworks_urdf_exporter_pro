# MuJoCo MJCF Model

**English** | [简体中文](MJCF-zh-CN)

## Purpose

The MJCF target produces a standalone MuJoCo robot model from the validated CAD-derived robot data.
Its first responsibility is a loadable robot asset, not a controller stack, task environment, or
reinforcement-learning project.

No separate MuJoCo installation is required for export validation. The installer pins official
MuJoCo `3.12.0` tools for the validation sequence and records the actual version in
`export_report.json`.

## Delivered Files

```text
MuJoCo/<robot>/
|-- robot.xml
|-- scene.xml                   # minimal include entry point for robot.xml
|-- assets/visual/
|-- assets/collision/
|-- name_map.json
`-- export_report.json
```

The model preserves the Link hierarchy, Visual/Collision separation, CAD mass, COM, and full inertia
tensor. Joint mappings are explicit: fixed contributes no movable MJCF Joint, continuous/revolute
maps to hinge, prismatic maps to slide, and floating maps to three orthogonal `slide` Joints plus
one `ball` Joint. Planar Joint export fails with an actionable error rather than silently
approximating a different mechanism. These are exporter mapping decisions, not limitations of what
MJCF itself can model.

## Automated Validation

The export succeeds only when the pinned official MuJoCo tools validate both `robot.xml` and
`scene.xml`:

1. compile MJCF to MJB;
2. save a canonical MJCF representation;
3. reload the canonical result;
4. advance one zero-control step;
5. record the MuJoCo version and result in `export_report.json`.

The public exporter fails closed: a missing validator, missing validation result, incomplete success
evidence, or failure in any step leaves the existing published directory unchanged.

This proves basic official-parser and one-step runtime compatibility. It does not prove physical
fidelity, contact tuning, controller quality, long-horizon stability, rendering fidelity,
performance, or task behavior.

## Intentionally Not Generated

- actuators, transmissions, controllers, PID gains, or control policies;
- sensors, keyframes, explicit contact pairs, friction/solver tuning, or timestep tuning;
- world geometry, ground plane, lights, cameras, task environments, rewards, observations, resets,
  or domain randomization;
- reinforcement-learning training code or task definitions.

`scene.xml` is a minimal entry point that includes `robot.xml`; it is not a finished simulation
scene.

## Evidence Vocabulary

- **Generated:** all documented MJCF files and assets were written.
- **Official MuJoCo validated:** both XML entry points passed the exact compile/save/reload/one-step
  sequence above with the reported pinned runtime.
- **Task validated:** not performed by this exporter; users must test the model with their actual
  contacts, actuators, controllers, timestep, and workload.
