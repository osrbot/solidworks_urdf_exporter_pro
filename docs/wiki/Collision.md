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
| `SimplifiedMesh` | Primitives do not fit, lower mesh cost desired | STL decimation using the same target ratio as Visual; fallback on failure |
| `AccurateMesh` | Contact details are required | Collision STL without decimation |
| `BoxPrimitive` | Chassis, plate, box, bracket | Native URDF box/corresponding geometry |
| `CylinderPrimitive` | Wheel, shaft, tube, cylindrical shell | Native URDF cylinder/corresponding geometry |
| `SpherePrimitive` | Spherical sensor or structure | Native URDF sphere/corresponding geometry |
| `ComponentBoxes` | Stable default approximation for assemblies | Multiple component-local boxes |
| `ConvexHull` | One complex shape that permits a convex approximation | Convex-hull STL from Link-local points/faces |

The historical `Primitive` configuration value is a compatibility alias and should not be promoted
as a new UI strategy name.

## Recommended Order

For STL reduction, the UI shows the target remaining size (67% removal means about 33% remaining).
Actual per-Link original and final sizes are shown after export. Connected regions are processed
independently; failed checks retain the affected original geometry and produce a warning.
The simplification ratio (Chinese UI: 精简比例) requests triangle removal, not guaranteed file-size
savings. At 0% no reduction is requested; at 100% reduce as much as possible and use the exported
size as the result. Without current measured STL sizes, only a percentage is shown. Mass, center
of mass and inertia are unchanged.

1. Start with `ComponentBoxes` for an assembly.
2. Use Box/Cylinder/Sphere for regular shapes.
3. Use `ConvexHull` for one complex convex approximation.
4. Use `SimplifiedMesh` when primitives cannot preserve required contacts.
5. Use `AccurateMesh` only when the task genuinely needs full surface detail.

A larger file is not automatically more realistic. Complex collision meshes increase contact pairs,
solver cost, and numerical-instability risk.

## Measured example assembly export

The user completed an export with version `5656c5d77e18`, 70% requested removal and coarse
SolidWorks tessellation. ROS 1, ROS 2, OpenUSD and MuJoCo all succeeded in 8 minutes 38 seconds.
Sizes below use this export's recorded pre-reduction STLs, not an older output folder.
MB means 1,000,000 bytes.

| Visual STL | Original | Exported | Size reduction |
| --- | ---: | ---: | ---: |
| Large Link | 32.10 MB | 10.64 MB | 66.86% |
| Complex smaller Link | 6.36 MB | 4.33 MB | 31.91% |
| All visual STLs | 41.35 MB | 16.20 MB | 60.82% |

About 39.18% of the total size remains, not the target 30%. Eight of ten Links did not reach
70% removal; only two smaller Links approximately reached it. Partial reduction within shape
safeguards is not an export failure. ROS mesh references and checksums passed, and corresponding
STL hashes matched across all four targets. Independent MuJoCo checks reloaded `robot.xml` and
`scene.xml` and ran one zero-control step each. OpenUSD structurally reopened with 17 dependent
layers, 16 nonempty meshes and no unresolved dependencies. Isaac Sim, long-running simulation
and training were not tested. Fixed-camera large Link and complex smaller Link comparisons showed no obvious
missing geometry, but are not strict whole-surface error or self-intersection checks.

Validation found two reporting issues: the four native cylinder wheels have stale staging
collision STL sizes and `collision_exists=true` in the CSV, although the final package needs no
such STLs and the readable report correctly says false; top-level English warnings repeat
large Link and complex smaller Link messages without visual/collision labels. These are not evidence of
missing wheels or repeated geometry failures. Check the delivered geometry and per-Link results.

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
- final Simplified STL uses the shared target triangle reduction ratio; the CAD preview is only a shape reference;
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
