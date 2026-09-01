# OpenUSD Robot Asset

**English** | [简体中文](OpenUSD-zh-CN)

## Purpose

The OpenUSD target produces a portable robot asset from the same validated Link, Joint, Visual,
Collision, and Inertial data used by the ROS exporters. It is an asset-format target, not an Isaac
Sim or Isaac Lab project generator.

No local Isaac installation is required or detected. The installer supplies a pinned OpenUSD
runtime used only for generation and structural validation.

## Delivered Files

```text
USD/<package>/
|-- robot.usd
|-- geometry/                 # USD mesh dependencies converted from STL
|-- meshes/                   # retained canonical source mesh evidence
|-- name_map.json             # source names to valid USD identifiers
`-- export_report.json        # counts, runtime version, checks, and evidence boundary
```

`robot.usd` contains the robot hierarchy, Visual and Collision shapes, physics Joints, mass, center
of mass, and inertia. Fixed, revolute/continuous, and prismatic Joints receive their corresponding
core USD Physics schemas. A planar Joint uses a generic USD Physics Joint with `LimitAPI` instances
on `transZ`, `rotX`, and `rotY`, authored with `low > high` as required by the pinned OpenUSD schema
to lock those axes. This leaves only in-plane `transX`/`transY` motion and `rotZ` rotation. Its local
Z axis is aligned to the source plane normal. The adapter fails the export if those constraints
cannot be authored and verified. Floating Joints remain generic USD Physics Joints and are reported
as non-exact mappings.

USD and MJCF targets require STL mesh input. The adapter rejects 3DXML instead of silently dropping
geometry.

## Automated Validation

The exporter succeeds only when the pinned bundled OpenUSD runtime:

1. creates the stage and geometry dependencies;
2. reopens `robot.usd`;
3. confirms expected Link, Joint, and rigid-body counts, resolves local assets, verifies every
   planar Joint's three locked and three free degrees of freedom, and records mass-property and
   Collision counts;
4. writes the OpenUSD version and validation result to `export_report.json`.

This proves generation and OpenUSD structural readability. It does **not** prove import, rendering,
physics behavior, or extension compatibility in Isaac Sim or Isaac Lab.

## Downstream Use

Copy the complete `USD/<package>` directory to the target machine and import `robot.usd` through the
downstream application's normal asset workflow. Perform application-specific checks there,
including articulation mapping, collision/contact behavior, units, materials, controller setup,
and task behavior.

The exporter intentionally does not generate Isaac versions, extension settings, actuator groups,
PID gains, sensors, environments, rewards, observations, reset logic, or reinforcement-learning
code.

## Evidence Vocabulary

- **Generated:** all documented asset files were written.
- **OpenUSD validated:** the bundled runtime reopened and structurally checked the stage.
- **Isaac validated:** not performed by this exporter; only a separate test in the user's actual
  Isaac environment can support that statement.
