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

## 简体中文

- `robot.schema.v3.json` 是当前规范的 SI 单位机器人模型与目标配置合同，新增显式 OpenUSD
  仿真意图；Joint drive 增益在该边界保持 SI，角增益以每弧度表示。
- `robot.schema.v2.json` 仅作为严格的历史读取合同保留。Core reader 会加入保守 OpenUSD 默认值
  并迁移到 v3，也会保留已知预发布 v2 `usdSimulation` 扩展并补上 `gainUnits: SI`；writer 只写 v3。
- 这里的 robot schema 版本与 SolidWorks 装配体中的 `URDF Export Configuration (v2)` 持久化
  特征无关。前者可在内存中迁移，后者为避免错误绑定组件实例，不自动迁移名称型 v1.x 配置。
