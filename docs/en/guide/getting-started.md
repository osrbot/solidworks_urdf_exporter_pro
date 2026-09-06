# Quick Start

## 1. Prepare the assembly

- Use a saved assembly. For the first test, work on a copy.
- Resolve lightweight components so the exporter can access every body involved.
- Assign a material or density to each part, then check mass and center of mass in SolidWorks.
- Rebuild and save the assembly.

## 2. Build the Link tree

Open `Tools > Export as URDF`. Create the root Link, then assign components to each Link. A component should belong to only one Link, and the parent-child structure should match the real mechanism.

[View the Link tree page](/en/features/link-tree)

## 3. Configure Joints

For every non-root Link, confirm the parent and child Links, Joint name, type, reference coordinate system, and axis of motion. Movable Joints also need a force or torque limit and a speed limit. Joints with bounded travel also need lower and upper position limits.

[View the Joint properties page](/en/features/joint)

## 4. Check inertia

Confirm that mass, center of mass, inertia tensor units, and directions are correct. The equivalent inertia body can help with inspection, but the values and export report remain the final reference.

The exporter reads effective SolidWorks mass properties, including mass, center of mass, and inertia overrides. A whole-subassembly override cannot be split across Links; assign the complete subassembly to one Link to use it.

Enter measured mass in kg and enable **Calibrate inertia with measured mass** to scale the full tensor by `measured mass / source mass`, preserving COM, principal directions, and equivalent cuboid dimensions. This assumes the source model's relative mass distribution is credible. Manually entered tensors and explicit SolidWorks inertia overrides are not automatically scaled.

Older configurations without inertia source metadata keep their existing values without enabling calibration. To use SW properties as the source again, click **Restore SW values**, then enter measured mass. This discards edits made in the exporter but does not clear overrides in SolidWorks. Preview and all targets use the same final values; non-positive mass and physically invalid inertia still block export.

Initial **Preview and Export** preparation and pre-export inertia validation can still be slow. SolidWorks may temporarily stop responding during mass calculations. This remains a known limitation; temporary unresponsiveness alone does not establish that export has failed.

[View the inertia page](/en/features/inertia)

## 5. Set collision and appearance

Visual geometry is for display; Collision geometry is for contact. Prefer collision geometry that is simple while preserving important contact features. On the Appearance page, you can set RGBA values or color Links automatically by hierarchy.

[View Visual and Collision](/en/features/collision) · [View Appearance](/en/features/appearance)

## 6. Select output

New export configurations select all four targets by default: ROS 1, ROS 2, OpenUSD, and MuJoCo MJCF. Existing explicit selections and the URDF-only legacy path retain their choices. Keep at least one target selected; clearing formats you do not need reduces export time and keeps the output directory uncluttered.

[View the Model and Export page](/en/features/export-page)

## 7. Review the reports

Start with the completion summary. ROS packages contain `config/export_report.md`; OpenUSD and MJCF contain `export_report.json` inside their target directories. Review the corresponding name maps as well. Error messages identify the affected location and suggest the next step.

Multi-target export shows success or failure for each format. A failed format does not discard other successful outputs. Any retained old output for a failed target is marked as not updated in this run; do not count it as a new successful result. If recovery is required, inspect the directories as directed by the error details rather than assuming the old output is intact.

After a partial failure, the export form stays open so you can select only failed formats and retry. Assembly-reading or common model-validation failures still stop the whole export; cancellation or serious errors may also interrupt remaining targets. For inertia validation failures, use the path in the error message to locate the retained diagnostic CSV.

## 8. Verify in the target tool

Check appearance, collision geometry, inertia, axis directions, and motion ranges in the actual ROS, Isaac Sim, USD Viewer, or MuJoCo environment. A successful export means the files passed the plugin checks; it does not mean that controller and task parameters are already tuned.
