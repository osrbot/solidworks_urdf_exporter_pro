# OSURDF schemas

- `robot.schema.v3.json` is the canonical SI-unit robot model and target-profile contract. It adds
  explicit OpenUSD simulation intent and declares joint-drive gains as SI values (angular gains are
  per radian).
- `robot.schema.v2.json` remains the strict historical v2 contract. Core readers migrate v2
  documents to v3 by adding conservative OpenUSD defaults. They also preserve the known pre-release
  v2 `usdSimulation` extension while adding `gainUnits: SI`; writers emit v3 only.
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
