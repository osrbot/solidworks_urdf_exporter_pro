# Isaac Sim / Isaac Lab 工作流

## 设计原则

Isaac 输出不是另写一套机器人模型，而是消费已验证的 Robot Bundle。适配器不推断 Joint 类型、
actuator 分组、stiffness、damping、effort、velocity 或模型许可证。

## 配置

`profiles.isaac` 至少包含：

- `enabled: true`；
- 精确 `isaacSimVersion`；
- `robotType` 与三态 `baseType`（`Source` / `Fixed` / `Mobile`）；
- mesh/fixed-joint 合并、自碰撞、visual collider、调试与转换器开关；
- 四种可移植碰撞 token 之一，适配器再映射为 Isaac 官方枚举文本；
- Bundle 内的可移植 package mapping（需要时）。

启用 Isaac Lab 时，`profiles.isaacLab` 还必须包含精确 `isaacLabVersion`、backend、prim path、根姿态、
physics 参数、初始关节状态和完整 actuator groups。每个可动 Joint 必须且只能被明确配置覆盖。

## 操作

普通 Python 环境先执行不依赖 GPU 的门禁：

```bash
python3 tools/isaac_adapter/osurdf_isaac_adapter.py preflight \
  --bundle /path/to/robot.osurdf --require-isaac-lab

python3 tools/isaac_adapter/osurdf_isaac_adapter.py generate-isaaclab \
  --bundle /path/to/robot.osurdf --output /path/to/generated
```

USD 转换必须用目标 Isaac Sim 自带的 `python.sh` 或 `python.bat`：

```bash
./python.sh tools/isaac_adapter/osurdf_isaac_adapter.py convert \
  --bundle /path/to/robot.osurdf --output /path/to/isaac-asset
```

适配器在启动 `SimulationApp` 后导入 Isaac API，核对精确运行时版本，再调用当前
`URDFImporter`/`URDFImporterConfig` 接口。生成结果包括 USD、转换报告和 name map；Isaac Lab
生成结果包括 `robot_cfg.py`、`actuator_groups.json` 和 `smoke_test.py`。

所有输出目录必须与源 Bundle 互不包含。preflight/Isaac Lab 的非空目录以及已有转换目录都必须显式
传入 `--overwrite`；即使传入该参数，也不会允许把 Bundle 本身或包含 Bundle 的上级目录作为目标。
preflight 与 Isaac Lab 生成会先写入隔离 staging，再原子替换旧目录，不会把旧文件混入新结果；
转换 staging 在发布前还会递归拒绝 symlink。

## 验收

1. `preflight` 通过 Bundle、checksum、许可证、profile 和版本校验；
2. `convert` 在精确 Isaac Sim 环境生成并重新打开 USD；
3. `smoke_test.py` 在精确 Isaac Lab 环境加载 articulation，检查 Link/DOF 数并推进物理；
4. 项目测试检查关节方向、limit、自碰撞、接触、质量比例和控制稳定性；
5. 强化学习任务再检查 observation/action 映射、reset、reward 和并行环境性能。

只生成 Python 或 USD 文件不能记作 Isaac Lab 运行通过。

## 上游参考

- Isaac Sim URDF importer：<https://docs.isaacsim.omniverse.nvidia.com/latest/importer_exporter/import_urdf.html>
- Isaac Sim importer Python API：<https://docs.isaacsim.omniverse.nvidia.com/latest/py/source/extensions/isaacsim.asset.importer.urdf/config/python_api.html>
- Isaac Lab 2.3.2 articulation 配置：<https://isaac-sim.github.io/IsaacLab/v2.3.2/source/how-to/write_articulation_cfg.html>
