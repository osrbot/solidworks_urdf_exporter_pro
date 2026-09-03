# OpenUSD robot asset adapter / OpenUSD 机器人资产适配器

This adapter converts a verified private OSURDF staging bundle into a portable
OpenUSD robot asset. It uses only the OpenUSD APIs bundled with the installer;
Isaac Sim and Isaac Lab are not required and are not reported as tested.

此适配器把经过校验的私有 OSURDF 暂存包转换为可移植 OpenUSD 机器人资产。
它只调用安装包内置的 OpenUSD API，不要求安装 Isaac Sim 或 Isaac Lab，也不会声称完成了二者的运行验证。

Output / 输出：

- `robot.usd`: UTF-8 USDA text root layer containing the robot hierarchy, physics Joints, collision shapes, mass, COM and inertia.
- `geometry/*.usd`: binary USDC mesh dependencies converted from STL for compact loading.
- `meshes/**`: canonical source mesh evidence retained with original relative paths.
- `name_map.json`: original names to USD identifiers.
- `export_report.json`: OpenUSD reopen/structure validation and its explicit evidence boundary.

The adapter deliberately rejects 3DXML input. Select STL in the exporter for
USD or MJCF output; silently dropping visual geometry would be worse than an
actionable export error.

适配器会明确拒绝 3DXML 输入。导出 USD 或 MJCF 时应选择 STL；相比静默丢失可视几何，明确报错更可靠。
