# Quick Start

**English** | [简体中文](Quick-Start-zh-CN)

## 1. Prepare the Assembly

- Work on a saved assembly copy.
- Resolve lightweight components so selected bodies are accessible.
- Assign valid material or density to participating parts.
- Rebuild, save, and verify mass and COM in SolidWorks Mass Properties.
- Unsaved models have no stable path and cannot use per-assembly recovery drafts.

## 2. Create Reference Geometry

Create these features explicitly in SolidWorks:

- a root Link coordinate system such as `Origin_global`;
- a child-Joint coordinate system for every non-root Link;
- a reference axis for every revolute or prismatic Joint.

The exporter does not guess an engineering frame from geometry. Use one consistent right-handed
convention.

## 3. Build the Link Tree

Open `Tools > Export as URDF`:

- on first use, choose `Start tutorial`, `Skip once`, or `Do not remind`;
- reopen the tutorial later from `Tools > URDF Export Tutorial`;
- edit hierarchy through the PropertyManager, free canvas, or Markdown-style Outline;
- new Links receive generated Joint names but remain without a selected Joint type until you choose one;
- bind each component to one Link only; do not assign both a parent component and its child geometry
  to different Links.

## 4. Configure Joints

For every non-root Link, check:

- Joint name;
- canonical type: `fixed`, `revolute`, `continuous`, `prismatic`, `floating`, or `planar`;
- parent and child Link;
- Origin and Axis;
- Limit and Dynamics;
- optional Mimic relationship.

Choose explicit Joint types for STEP, imported, or fixed assemblies. `Automatically Detect` means
“try SolidWorks Mate detection” and is only suitable for a native movable assembly with correct
Mates; it is not a legal final URDF Joint type. Zero remaining DOFs may mean fixed, fully
constrained, or missing Mate semantics, so it is not automatically mapped to `fixed`. Detection
must resolve to a canonical type before export.
After selecting `Automatically Detect`, press Next to run the assist for those Joints. The result is
filled as a provisional, blocked suggestion. Open every suggested Joint, explicitly select its final
type, and enter required limits. A single rotational DOF is displayed as `continuous` because CAD
motion alone cannot distinguish it from a bounded `revolute` Joint.

## 5. Validate Inertia

- Select an explicit existing Link frame for every Link.
- Check mass in `kg`, COM in `m`, and inertia in `kg*m^2`.
- Show the COM and equivalent-inertia preview to inspect position and principal-axis direction.
- A preview display failure is a graphics-layer failure. Numerical validity comes from export guards
  and reports.

## 6. Choose Visual and Collision

- Visual serves appearance; Collision serves contact solving.
- Start with `ComponentBoxes` for a multi-component assembly.
- Use `BoxPrimitive` for box-like structures, `CylinderPrimitive` for wheels/shafts/tubes, and
  `SpherePrimitive` for spherical geometry.
- If primitives are insufficient, consider `ConvexHull`, then `SimplifiedMesh`; use `AccurateMesh`
  only when the task needs detailed contact geometry.
- Use the collision preview to inspect coverage, but do not treat temporary geometry as a byte-level
  copy of the final STL.

## 7. Export

Select at least one concrete deliverable:

- **ROS 1 package**: legacy URDF package under `ROS1/<package>`;
- **ROS 2 package**: modern description package under `ROS2/<package>`;
- **OpenUSD robot asset**: portable USD stage under `USD/<package>`;
- **MuJoCo MJCF model**: MJCF model under `MuJoCo/<robot>`.

ROS targets use the package metadata and maintained ROS/Gazebo combination shown by the UI. USD and
MJCF require STL geometry. They do not require an Isaac installation, Isaac/Isaac Lab version,
actuator profile, or user-provided controller file. At least one target must be selected. The
progress window remains above SolidWorks and blocks export re-entry.

The exporter uses a private canonical Bundle only as temporary staging. It is not shown as a target,
written into the delivery tree, or retained after export.

## 8. Inspect the Result

Review:

1. Common: `<export-root>/export_report.md` for the exporter summary;
2. ROS: `config/export_report.md`, `config/inertial_validation.csv`, and
   `config/mesh_manifest.csv` inside each selected package;
3. OpenUSD: `robot.usd`, `name_map.json`, and `export_report.json` under `USD/<package>`;
4. MJCF: `robot.xml`, `scene.xml`, `name_map.json`, and `export_report.json` under
   `MuJoCo/<robot>`;
5. Visual, Collision, Inertia, COM, axes, and Joint motion in the actual target application.

Read the evidence literally:

- generation success means the documented files were written from a validated model;
- USD automated validation means the bundled pinned OpenUSD runtime reopened the generated stage;
- MJCF automated validation means pinned official MuJoCo tools compiled, canonically saved,
  reloaded, and advanced both XML entry points one zero-control step;
- no result claims a ROS/Gazebo launch, Isaac Sim/Isaac Lab import, controller quality, contact
  tuning, long-horizon stability, task behavior, performance, or reinforcement-learning validation.
