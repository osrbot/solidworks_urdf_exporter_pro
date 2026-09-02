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

## Versioning policy

- `robot.schema.vN` is a compatibility epoch, not a product or release number. Increment `N` only
  when a reader cannot interpret the new canonical document without an explicit migration.
- Optional exporter features, UI changes, additional output targets, and backward-compatible
  defaults do not require a new robot schema version. Evolve target-specific data inside its own
  profile contract when a versioned contract is necessary.
- The Bundle manifest has its own schema version. Its `robotSchemaVersion` field records the schema
  used by `robot.json`; the manifest schema deliberately does not hard-code the current robot
  version. Bundle verification checks that the recorded value matches the packaged document and
  that the installed reader supports it.

## 简体中文

- `robot.schema.v3.json` 是当前规范的 SI 单位机器人模型与目标配置合同，新增显式 OpenUSD
  仿真意图；Joint drive 增益在该边界保持 SI，角增益以每弧度表示。
- `robot.schema.v2.json` 仅作为严格的历史读取合同保留。Core reader 会加入保守 OpenUSD 默认值
  并迁移到 v3，也会保留已知预发布 v2 `usdSimulation` 扩展并补上 `gainUnits: SI`；writer 只写 v3。
- 这里的 robot schema 版本与 SolidWorks 装配体中的 `URDF Export Configuration (v2)` 持久化
  特征无关。前者可在内存中迁移，后者为避免错误绑定组件实例，不自动迁移名称型 v1.x 配置。

### 版本策略

- `robot.schema.vN` 表示兼容性代数，不是产品版本或 Release 版本。只有读取器无法在不执行显式
  迁移的情况下解释新的规范文档时，才递增 `N`。
- 可选导出能力、UI 修改、新增输出目标和向后兼容的默认值都不触发 robot schema 升级；确有
  独立版本需求时，在对应 profile 合同内演进。
- Bundle manifest 使用自己的 schema 版本。`robotSchemaVersion` 只记录包内 `robot.json` 的实际
  schema，不再硬编码当前数字；验证器负责检查两者一致，并确认当前读取器支持该版本。
