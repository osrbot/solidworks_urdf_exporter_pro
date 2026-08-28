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

`Automatically Detect` is an exporter configuration state, not a legal final URDF Joint type. It
must resolve to a canonical type before export.

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

Set the ROS package name and destination, then export ROS1/ROS2 packages. The progress window remains
above SolidWorks and blocks export re-entry. The completion summary reports changed file count,
total size, elapsed time, and output root.

## 8. Inspect the Result

Review:

1. `config/export_report.md`: summary, failures, and collision fallbacks;
2. `config/inertial_validation.csv`: mass, COM, tensor, and errors;
3. `config/mesh_manifest.csv`: requested/effective strategy, files, and mesh records;
4. Visual, Collision, Inertia, COM, axes, and Joint motion in a URDF viewer, MuJoCo, Isaac Sim, or
   the intended solver.

A successful export means the exporter checks and file transaction completed. It does not replace
task-specific simulator validation.
