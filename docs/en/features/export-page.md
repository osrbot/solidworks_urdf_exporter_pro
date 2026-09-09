# Model and Export

The final page collects package information, selects output formats, and starts the export. These settings apply to the whole robot rather than an individual Link.

## Basic information

- Output name and package version.
- Package description.
- Maintainer name and email.
- Model license: choose a common identifier such as MIT, Apache-2.0, BSD, or CC, or type a custom name. Existing values are retained; `NOASSERTION` means the license has not been confirmed.
- Author or configurator.

Even when exporting only OpenUSD or MJCF, use a clear name and license so the output directory and reports can be identified correctly.

All four targets share the output name. For example, `osracer_description` produces
`ROS1/osracer_description`, `ROS2/osracer_description`, `USD/osracer_description`, and
`MuJoCo/osracer_description`. The preview shows normalized directory names. This does not rename the assembly or the model inside the exported files.

## Output options

The output name, version, description, maintainer, email, license, author, and target selections are saved with the export configuration.
Choose Save when closing to retain edits; choosing not to save does not commit them. Window-close recovery drafts also include these fields.
After writing the configuration to the assembly, save the SolidWorks document normally to retain it when reopening the file.

- ROS 1 package.
- ROS 2 package.
- OpenUSD robot asset.
- MuJoCo MJCF asset.

New export configurations select all four targets by default. Existing explicit selections and the URDF-only legacy path retain their choices. Select at least one; clearing unneeded formats reduces export time.

## Simulation settings

Common simulation settings select base behavior and passive, position, velocity, or effort intent for single-DOF joints. Separate OpenUSD and MuJoCo tabs hold target parameters: USD stiffness, damping, self-collision and robot type, or MJCF actuator gains and maximum force. Parameters are not borrowed across targets.

Defaults create no actuators or active drives. Without common settings, legacy configuration is preserved. A common `source` base preserves the target's previous behavior; explicit `fixed` / `floating` and common joint modes override legacy target choices. These settings configure a robot asset, not a training world, rewards, policy, or training project.

MJCF position control requires positive stiffness and explicitly entered nonnegative damping. Velocity control requires positive velocity gain (the damping column). Missing values are marked, but you can confirm saving an incomplete draft. Export checks them before mesh generation: return to edit by default, or explicitly skip MJCF and continue with other selected targets.

## Two export buttons

- **Export URDF without meshes**: faster and useful for checking structure and values only.
- **Export URDF and meshes**: generates the deliverable directory. OpenUSD and MJCF require this path.

Do not click repeatedly while export is running. When it finishes, read `export_report.md` first, then open the relevant target directory.

After a partial failure, successful outputs are retained and the export form stays open so you can retry only failed targets. Check the results window and error details for old output not updated this run or directories requiring recovery. See [Choose an Export Target](/en/exports/).
