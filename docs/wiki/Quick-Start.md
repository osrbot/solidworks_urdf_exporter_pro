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

Set the ROS package name, model license, maintainer, and exact target versions. Select either
Lyrical/Jetty or Jazzy/Harmonic. A ros2_control target requires an explicit control profile; Isaac
Lab requires Isaac Sim, exact versions, and an actuator profile. The Robot Bundle is always created,
while ROS 1, ROS 2, and Isaac are selected derived targets. The progress window remains above
SolidWorks and blocks export re-entry.
`Export URDF Without Meshes` is the exception: it is a lightweight XML-debug compatibility path and
does not create the Robot Bundle, Isaac output, or new profiles.

## 8. Inspect the Result

Review:

1. `Bundle/<package>.osurdf/manifest.json` and `checksums.sha256` for payload/profile integrity;
2. `reports/validation.json` for canonical model blockers and warnings;
3. `<export-root>/export_report.md` for the v2 export summary (also present for Bundle-only), plus
   the package-local `config/export_report.md` in each selected ROS package;
4. `config/inertial_validation.csv` for mass, COM, tensor, and error checks;
5. `config/mesh_manifest.csv` for requested/effective collision strategies and mesh records;
6. Visual, Collision, Inertia, COM, axes, and Joint motion in a URDF viewer, MuJoCo, Isaac Sim, or
   the intended solver.

A successful export means the exporter checks and file transaction completed. It does not replace
task-specific simulator validation.
