# Choose an Export Target

Select only the formats you currently need. The four outputs are independent, and you may select
one or more in a single export.

| Target | Best For | Main Output |
| --- | --- | --- |
| ROS 1 | Maintaining an existing ROS 1 project | URDF, meshes, configuration, and reports |
| ROS 2 | ROS 2 description, visualization, and later control setup | URDF, meshes, configuration, and reports |
| OpenUSD | Isaac Sim or other USD tools | `robot.usd`, geometry files, name mapping, and report |
| MuJoCo MJCF | MuJoCo scene and control development | `robot.xml`, `scene.xml`, meshes, and report |

## Common Rules

- Select at least one target.
- Output directories for unselected formats are not overwritten by the current export.
- A failed format does not discard other successful outputs. The results window lists each target and its error, if any.
- Failed formats preserve their previous valid output, explicitly marked as not updated this run.
- Partial failures keep the export form open so you can select only failed formats for another attempt. Assembly-reading or common model-validation failures still stop the whole export.
- OpenUSD and MJCF require exported STL meshes.
- After export, verify coordinates, collisions, inertia, and motion direction in the target tool.
