# Visual and Collision

This page configures the Visual mesh origin and the Collision generation method. Visual geometry is used for display, while Collision geometry is used for contact calculations. They do not need the same level of detail.

![Visual and Collision page](/screenshots/link-collision.png)

## Fields

- Visual/Collision origin position and orientation.
- Collision strategy.
- Collision preview.
- Mesh detail.
- STL or 3DXML Visual format.
- STL simplification ratio (%) (Chinese UI: 精简比例).

## Simplification ratio

This is the fraction of triangles to remove, not the fraction to retain or a promise of file-size reduction.
Changing this ratio applies it to every Link's visual STL and collision STL using the Simplified Mesh strategy.
Both use the same target reduction ratio. Visual Mesh collision copies the reduced visual mesh without reducing it again.
Accurate Mesh is not decimated. Boxes, cylinders, spheres, component boxes, and convex hulls keep their respective generation strategies.

- 0%: no decimation.
- 50%: target removal of half the triangles.
- 100%: reduce as much as possible; actual size is reported after export.

The ratio shows a target STL size beside it: removing 67% targets about 33% of the original size.
This reduces triangles, not the model's physical dimensions. Without measured source STL statistics,
only a percentage is shown. Export results list each Link's measured original and final STL sizes.

Connected regions are reduced separately while shared interfaces are retained. A region that cannot
be safely reduced does not prevent other regions from shrinking. Time limits or failed checks retain
original geometry and produce warnings; parts, mass and inertia are not removed or recalculated.

An existing saved value of `0.5` now means a target of removing 50% of the triangles; `0` means no decimation, and `1` means maximum simplification within shape safeguards. Existing values use this meaning directly, with no conversion required.

Shape preservation may prevent reaching the target. After export, inspect outlines, holes, slots, and important contact features. Check actual triangle counts and actual reduction statistics in the export report; neither the target percentage nor a pre-export estimate is an actual result.

## Measured example assembly export

The user completed an export with version `5656c5d77e18`, requesting 70% removal with coarse
SolidWorks tessellation. ROS 1, ROS 2, OpenUSD and MuJoCo all succeeded in 8 minutes 38 seconds.
The comparison uses pre-reduction STL sizes recorded for this export, not an older output folder.
MB means 1,000,000 bytes.

| Visual STL | Original | Exported | Size reduction |
| --- | ---: | ---: | ---: |
| Large Link | 32.10 MB | 10.64 MB | 66.86% |
| Complex smaller Link | 6.36 MB | 4.33 MB | 31.91% |
| All visual STLs | 41.35 MB | 16.20 MB | 60.82% |

The total remaining size is about 39.18%, not the target 30%. Eight of ten Links did not reach
70% removal; only two smaller Links approximately reached it. This is partial reduction within
shape safeguards, not an export failure. Reduction leaves mass, center of mass, inertia and
the source assembly unchanged.

Independent checks found valid ROS mesh references and checksums, with matching corresponding
STL hashes across all four targets. MuJoCo reloaded `robot.xml` and `scene.xml` and ran one
zero-control step each. OpenUSD structurally reopened with 17 dependent layers, 16 nonempty
meshes and no unresolved dependencies. Isaac Sim, long-running simulation and training were
not tested. Fixed-camera comparisons of the large Link and complex smaller Link showed no obvious missing
geometry, but do not establish strict whole-surface error or absence of self-intersections.

### Report issues found during validation

- Four wheels use native cylinder collisions and need no corresponding collision STL in the
  final package. The CSV still lists staging STL sizes and `collision_exists=true`; the readable
  export report correctly says false. This is a reporting mismatch, not missing wheel geometry.
- Top-level warnings repeat large Link and complex smaller Link messages without distinguishing visual from
  collision, and remain in English. Duplicate messages are not evidence of repeated geometry
  failures; check each Link's actual result.

## Choosing a collision strategy

| Structure | Suggested starting point |
| --- | --- |
| Regular housings and links | Box or per-component bounding box |
| Wheels, shafts, and cylinders | Cylinder primitive |
| Spherical structures | Sphere primitive |
| Irregular shape requiring only overall contact | Convex hull |
| More outline detail required | Simplified mesh |
| Small contact features must be preserved | Exact mesh |

Start with a simple strategy. Increase mesh complexity only when simple geometry cannot represent an important contact feature.

## Preview and reports

Use the preview to confirm that the collision geometry covers the correct Link, keeps important regions, and does not pass through adjacent structures. If the exporter falls back to another strategy, the report records both the requested strategy and the actual result.
In simplified-mesh mode, the live preview shows the original CAD shape as a reference, not the final decimated result. Inspect the exported collision mesh for the final shape.
