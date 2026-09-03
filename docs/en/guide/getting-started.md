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

[View the inertia page](/en/features/inertia)

## 5. Set collision and appearance

Visual geometry is for display; Collision geometry is for contact. Prefer collision geometry that is simple while preserving important contact features. On the Appearance page, you can set RGBA values or color Links automatically by hierarchy.

[View Visual and Collision](/en/features/collision) · [View Appearance](/en/features/appearance)

## 6. Select output

Select at least one target: ROS 1, ROS 2, OpenUSD, or MuJoCo MJCF. Selecting only the formats you need reduces export time and keeps the output directory uncluttered.

[View the Model and Export page](/en/features/export-page)

## 7. Review the reports

Start with `export_report.md` in the output root, then review the reports and name mappings inside the relevant target directory. Error messages identify the affected location and suggest the next step.

## 8. Verify in the target tool

Check appearance, collision geometry, inertia, axis directions, and motion ranges in the actual ROS, Isaac Sim, USD Viewer, or MuJoCo environment. A successful export means the files passed the plugin checks; it does not mean that controller and task parameters are already tuned.
