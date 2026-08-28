# Collision

**English** | [简体中文](Collision-zh-CN)

## Principle

Collision geometry serves contact and physics solving. It should not mechanically duplicate Visual.
Use the simplest shape that preserves task-relevant contacts such as tire ground contact, gripper
contact, or chassis clearance.

Collision and Inertial are independent. Changing a Collision strategy never changes mass, COM, or
the inertia tensor.

## Strategies

| UI/configuration strategy | Typical use | Formal output |
| --- | --- | --- |
| `VisualMesh` | Maximum compatibility or inspection | Copies Visual mesh as Collision mesh |
| `SimplifiedMesh` | Primitives do not fit, lower mesh cost desired | Coarser STL tessellation; fallback on failure |
| `AccurateMesh` | Contact details are required | Finer Collision STL; highest cost |
| `BoxPrimitive` | Chassis, plate, box, bracket | Native URDF box/corresponding geometry |
| `CylinderPrimitive` | Wheel, shaft, tube, cylindrical shell | Native URDF cylinder/corresponding geometry |
| `SpherePrimitive` | Spherical sensor or structure | Native URDF sphere/corresponding geometry |
| `ComponentBoxes` | Stable default approximation for assemblies | Multiple component-local boxes |
| `ConvexHull` | One complex shape that permits a convex approximation | Convex-hull STL from Link-local points/faces |

The historical `Primitive` configuration value is a compatibility alias and should not be promoted
as a new UI strategy name.

## Recommended Order

1. Start with `ComponentBoxes` for an assembly.
2. Use Box/Cylinder/Sphere for regular shapes.
3. Use `ConvexHull` for one complex convex approximation.
4. Use `SimplifiedMesh` when primitives cannot preserve required contacts.
5. Use `AccurateMesh` only when the task genuinely needs full surface detail.

A larger file is not automatically more realistic. Complex collision meshes increase contact pairs,
solver cost, and numerical-instability risk.

## Geometry Fitting

Primitive dimensions come from selected bodies' Link-local geometric bounds, not the
equivalent-inertia cuboid:

- Box uses the geometry bounds;
- Cylinder chooses the axis whose radial dimensions match most closely and uses the remaining
  dimension as thickness;
- Sphere uses the largest bound extent;
- ComponentBoxes creates one box per component;
- ConvexHull uses in-memory Link-local points and triangles.

These choices respond to the components selected by the user but remain approximations. Inspect
task-relevant contact regions from the intended task viewpoint.

## SolidWorks Preview

Every user-selectable strategy has a temporary display path:

- primitives, ComponentBoxes, and ConvexHull use Modeler-created temporary BREP/sheet bodies;
- Visual/Accurate/Simplified mesh previews copy non-destructive CAD bodies;
- final Simplified STL tessellation can be coarser than the preview CAD body;
- previews do not write back to the assembly or mutate source appearance and are released on switch
  or close.

The preview supports strategy selection; it does not promise byte-identical mesh tessellation.
ConvexHull preview and writer share the same Link-local geometry builder, but the final manifest and
an external viewer remain the formal file checks.

## STL and 3DXML

Maintained primitive, ComponentBoxes, ConvexHull, and simplified Collision paths are STL-based.
3DXML support serves Visual exchange; it is not a verified general Collision or DAE texture path.

## Fallbacks and Reports

When strategy generation fails, the exporter falls back to `VisualMesh` and records:

- requested strategy;
- effective strategy;
- fallback reason;
- mesh file and statistics.

Inspect `config/mesh_manifest.csv` and `config/export_report.md`. When effective differs from
requested, understand the fallback before accepting the model in MuJoCo, Isaac Sim, or another
solver.
