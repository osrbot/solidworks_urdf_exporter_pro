# Model and Export

The final page collects package information, selects output formats, and starts the export. These settings apply to the whole robot rather than an individual Link.

## Basic information

- ROS package name and version.
- Package description.
- Maintainer name and email.
- License.
- Author or configurator.

Even when exporting only OpenUSD or MJCF, use a clear name and license so the output directory and reports can be identified correctly.

## Output options

- ROS 1 package.
- ROS 2 package.
- OpenUSD robot asset.
- MuJoCo MJCF asset.

New export configurations select all four targets by default. Existing explicit selections and the URDF-only legacy path retain their choices. Select at least one; clearing unneeded formats reduces export time.

## OpenUSD settings

After selecting OpenUSD, a settings button appears at the bottom between **Previous** and the export buttons. Open it only when you need to specify a fixed or floating base, self-collision, or Joint drive intent. The default settings can be exported directly.

## Two export buttons

- **Export URDF without meshes**: faster and useful for checking structure and values only.
- **Export URDF and meshes**: generates the deliverable directory. OpenUSD and MJCF require this path.

Do not click repeatedly while export is running. When it finishes, read `export_report.md` first, then open the relevant target directory.

After a partial failure, successful outputs are retained and the export form stays open so you can retry only failed targets. Check the results window and error details for old output not updated this run or directories requiring recovery. See [Choose an Export Target](/en/exports/).
