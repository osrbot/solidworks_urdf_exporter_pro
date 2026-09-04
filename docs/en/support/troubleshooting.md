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

Do not delete the old configuration first. For v1.5 configurations, a migration dialog preserves
the Link tree, joint parameters, and component bindings while reviewing coordinate systems and axes.
Unique matches in the original owning scope are selected automatically; unresolved references
require an explicit selection.

After confirming migration, review the normal export pages. Saving adds the current-format
configuration and retains the old one. Cancelling does not modify the original configuration.
Only v1.5 migration is supported currently; other versions or unreadable data are reported rather
than guessed.

## Inertia Preview Fails

Distinguish between a numerical validation failure and a temporary SolidWorks display failure.
Check the mass, center of mass, inertia values, and log. A display failure does not necessarily mean
that the values are incorrect.

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
