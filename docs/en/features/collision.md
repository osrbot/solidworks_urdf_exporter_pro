# Visual and Collision

This page configures the Visual mesh origin and the Collision generation method. Visual geometry is used for display, while Collision geometry is used for contact calculations. They do not need the same level of detail.

![Visual and Collision page](/screenshots/link-collision.png)

## Fields

- Visual/Collision origin position and orientation.
- Collision strategy.
- Collision preview.
- Mesh detail.
- STL or 3DXML Visual format.
- STL target triangle reduction (%).

## Target triangle reduction

This is the fraction of triangles to remove, not the fraction to retain or a promise of file-size reduction.

- 0%: no decimation.
- 50%: target removal of half the triangles.
- 100%: maximum simplification within shape safeguards, never an empty mesh.

An existing saved value of `0.5` now means a target of removing 50% of the triangles; `0` means no decimation, and `1` means maximum simplification within shape safeguards. Existing values use this meaning directly, with no conversion required.

Shape preservation may prevent reaching the target. After export, inspect outlines, holes, slots, and important contact features. Check actual triangle counts and actual reduction statistics in the export report; neither the target percentage nor a pre-export estimate is an actual result.

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
