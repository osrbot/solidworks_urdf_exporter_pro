# OSURDF schemas

- `robot.schema.v2.json` is the canonical SI-unit robot model and target-profile contract.
- `robot-bundle-manifest.schema.v1.json` describes the portable Bundle inventory.
- `isaac-profile.schema.v1.json`, `isaaclab-profile.schema.v1.json`, and
  `isaaclab-actuator-profile.example.json` are internal legacy compatibility contracts. They remain
  in source so historical data can still be parsed, but are not installed and are not loaded by the
  current SolidWorks UI.
- `ros2-control-profile.schema.v1.json` is the standalone ros2_control hardware/controller profile used by the SolidWorks UI.
- `ros2-control-profile.example.json` shows the supported built-in controller contracts; replace all Joint and interface declarations with project-approved values.

The JSON Schema checks document shape. `osurdf validate` performs graph,
inertia, Joint provenance, target-version, controller, and actuator coverage
checks that JSON Schema alone cannot express.
