# Troubleshooting

## Export Validation Fails

First read the field location and suggested fix in the error dialog, then open the log. For movable
joints, the most common problem is a force, effort, or velocity value that is empty or not greater
than zero. For bounded joints, also check the lower and upper limits.

## A Coordinate System or Axis Cannot Be Found

- Confirm that the reference geometry was not deleted or replaced.
- Confirm that the component is not unresolved or in a lightweight state.
- If the assembly hierarchy changed, reopen the affected joint and select the correct reference.

## An Older Export Configuration Is Detected

Do not delete the old configuration first. For storage formats 1.0–1.5, a migration dialog preserves
the Link tree, names, joint design settings and component bindings while reviewing coordinate systems and axes.
Unique matches in the original owning scope are selected automatically; unresolved references
require an explicit selection.

Mass, inertia, poses and meshes are regenerated; old manual values and mesh settings are reset.
For mixed configurations, the mass reader temporarily activates the occurrence's referenced configuration for override metadata, restores the original document configuration and rebuilds the assembly. It preserves occurrence bindings and rejects activation, restoration or rebuild failures.

After confirming migration, review the normal export pages. Saving adds the current-format
configuration and retains the old one. Cancelling does not modify the original configuration.
Unknown storage formats or unreadable data are reported rather
than guessed.

## Inertia Preview Fails

Distinguish between a numerical validation failure and a temporary SolidWorks display failure.
Check the mass, center of mass, inertia values, and log. A display failure does not necessarily mean
that the values are incorrect.

For inertia validation failures, the error details give the retained diagnostic CSV path under the log directory's `failed-exports` subdirectory. Keep that file for troubleshooting; large meshes are not copied there.

## Initial Preview Preparation Is Slow

Mass calculations during initial **Preview and Export** preparation and pre-export inertia validation can still take a long time, temporarily leaving SolidWorks unresponsive. This remains a known limitation. Do not click repeatedly. Record assembly size, elapsed time, and logs, then follow [How to ask for help](/en/support/help-and-contribute).

## Invalid STL Reduction Settings

A custom deviation that is too large for a small Link can be accepted by SolidWorks but produce an
invalid tolerance. The new reduction setting no longer maps to SW tolerances: it removes triangles
after a normal STL export. Previously saved `0.5` values also mean a target of 50% triangles removed.
Reports show original/final counts and bytes. Shape protection may prevent reaching the target; if
no smaller valid result is available, the original mesh is retained with a warning. Units,
coordinate systems, mass, and inertia do not change.

For older builds reporting `invalid effective STL settings`, try setting the affected Link's
reduction ratio to 0 and selecting Fine quality. Keep the log; this error does not by itself mean
that the coordinate system is configured incorrectly.

## USD Opens Without Geometry

Confirm that you copied the complete `USD/<package>` directory, not only `robot.usd`. Check that
`geometry/` exists and review `export_report.json`.

## MJCF Does Not Work in Your Scene

First confirm that the basic export checks passed. Then inspect the actuators, include paths,
contacts, and solver settings you added. The plugin generates a robot model and a minimal scene,
not a complete task project.

## Log Location

Default log: `%USERPROFILE%\sw2urdf_logs\sw2urdf.log`

Before submitting an issue, keep the complete error text, the log, and the reports from the matching
output directory.
