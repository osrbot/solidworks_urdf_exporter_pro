# Joint Properties

**English** | [简体中文](Joint-zh-CN)

The Joint page defines how two Links connect and move. It has Basic, Limits and Safety, and Mimic
tabs.

## Basic

- Confirm the parent Link, child Link, Joint name, and type.
- Select the SolidWorks reference frame and motion axis.
- Review the Joint origin position and orientation.
- Choose an explicit Joint type for STEP, imported, or fixed assemblies.

## Limits and Safety

- `revolute` and `prismatic` need position limits; `continuous` does not.
- Every movable Joint needs reasonable positive effort and velocity limits.
- Enter friction, damping, calibration, and safety fields only when reliable values are available
  and the downstream application uses them.

The exporter can supply a minimum positive placeholder for missing required values. Replace it with
the real mechanism and actuator limits.

## Mimic

Enable Mimic only when one Joint explicitly follows another, then verify its multiplier and offset.

Check direction, units, and mechanical range again before export.
