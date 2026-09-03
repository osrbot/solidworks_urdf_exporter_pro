# Joint Properties

The Joint page has three tabs: Basic, Limits and Safety, and Mimic. Each tab edits only the Joint selected in the list on the left.

## Basic

Confirm the following:

- Parent Link and child Link.
- Joint name and type.
- SolidWorks reference coordinate system.
- Axis of motion.
- Joint origin position and orientation.

The coordinate system and axis selectors each use a full row so long component paths remain readable. Coordinate values come from SolidWorks reference geometry. If the direction does not match the mechanical design, correct the reference geometry in the model.

## Limits and Safety

| Field | When to enter it |
| --- | --- |
| Lower and upper limits | Required for `revolute` and `prismatic`; not needed for position-unbounded `continuous` Joints |
| Torque or force | Every movable Joint needs a reasonable positive value |
| Speed | Every movable Joint needs a reasonable positive value |
| Friction and damping | Enter known values; leave blank when unknown |
| Calibration and safety controller | Enter only when downstream software uses these URDF fields |

The plugin supplies the smallest valid default for missing required positive values, but these defaults only provide basic physical validity. Replace them with real robot parameters. Out-of-range or non-positive values are reported immediately.

![Joint limits and safety](/screenshots/joint-constraints.png)

## Mimic

Enable Mimic only when one Joint explicitly follows another. Select the source Joint and confirm the multiplier and offset. Do not enable Mimic for ordinary serial Joints.

## Check before export

- Confirm rotation and translation directions.
- Confirm units: radians, metres, newtons, or newton-metres.
- Distinguish correctly between `continuous` and bounded `revolute`.
- Confirm that limits match the real mechanical range.
