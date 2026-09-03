# ROS 1 / ROS 2

## What You Get

ROS 1 and ROS 2 each produce a separate robot description package containing the URDF,
visual and collision meshes, configuration, and validation reports. Select only the ROS version
you use.

## Control-Related Output

The plugin can generate descriptions and configuration from confirmed joint information, but it
does not infer PID values from CAD geometry or guarantee suitable control behavior immediately
after export.

Before using the control configuration, confirm that:

- Joint names match the controller configuration.
- Position, velocity, force, or effort interfaces match the hardware or simulation plugin.
- Limits and PID values come from the real device or a validated model.
- Motion direction and range are first tested under low-risk conditions.

## Checks After Export

1. Open `config/export_report.md` and review errors and warnings.
2. Review `config/inertial_validation.csv`.
3. Review `config/mesh_manifest.csv`.
4. Check appearance, coordinates, and joints in RViz.
5. Check controller startup and motion in the target ROS/Gazebo environment.
