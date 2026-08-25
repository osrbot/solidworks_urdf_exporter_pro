# SolidWorks to URDF Exporter

Authored and maintained by [Stephen Brawner](brawner@gmail.com). Past supporters include [PickNik Consulting](https://picknik.ai), Verb Surgical, Open Robotics, and Willow Garage. 

## OSRBot Maintained Fork

This fork is maintained at <https://github.com/osrbot/solidworks_urdf_exporter_pro>.

Current version maintainer: `kitso666 <kitso@osrbot.com>`.

The OSRBot-maintained build keeps the original SolidWorks add-in workflow and adds:

- ROS1 and ROS2 package output from the same export flow.
- SolidWorks mass-property based inertia export and per-link validation logs.
- Collision strategy selection for visual mesh, simplified mesh, accurate mesh, primitive boxes/cylinders/spheres, component boxes, and convex hulls.
- STL mesh reduction ratio control for lighter exported mesh packages.
- Automatic Link tree configuration loading from the SolidWorks assembly feature `URDF Export Configuration (v1.5)`.
- Transactional Link tree canvas for adding, renaming, moving, box-selecting, copying, and pasting Link groups before export.
- Automatic per-assembly recovery of edits when an exporter window is closed before the configuration is formally saved.
- Markdown-style Link tree outline editing, where heading depth (`#`, `##`, `###`) defines the Link hierarchy.
- Chinese UI localization and safer UTF-8 English export logs.
- Built-in usage guide with collision strategy recommendations, common URDF material names, project URL, and maintainer information.
- First-use, eight-step companion tutorial that follows the real assembly export workflow without modifying the SolidWorks model automatically.

## Latest Release

For the OSRBot fork, use the latest GitHub Release asset named:

```text
sw2urdfSetup_YYYYMMDD_<commit>.exe
```

Example:

```text
INSTALL/OUTPUT/sw2urdfSetup_20260629_598c7dd.exe
```

Release tags use the installer date: `vYYYYMMDD`. Daily releases are immutable: a second installer for the same date is rejected instead of moving a public tag or replacing an already published binary. The installer filename commit suffix and provenance identify the source commit declared by the local maintainer build.

**SolidWorks 2021**

https://github.com/ros/solidworks_urdf_exporter/releases/tag/1.6.1

**SolidWorks 2020**

https://github.com/ros/solidworks_urdf_exporter/releases/tag/1.6.0

**SolidWorks 2019 on 2018 SP 5**

https://github.com/ros/solidworks_urdf_exporter/releases/tag/1.5.1

## SolidWorks Version Requirements

1. The minimum required version of SolidWorks for use with this add-in is 2018 Service Pack 5. SolidWorks 2017 or earlier may work. See [this issue](https://github.com/ros/solidworks_urdf_exporter/issues/73).

## Usage

See the [ROS Wiki](http://wiki.ros.org/sw_urdf_exporter) and associated [tutorials](http://wiki.ros.org/sw_urdf_exporter/Tutorials).

### First-Use Export Tutorial

The first time `Tools > Export as URDF` is invoked for an assembly, the exporter offers three explicit choices:

- `Start tutorial`: opens the companion checklist and continues into the real exporter.
- `Skip once`: continues the export and asks again on a later assembly export.
- `Do not remind`: suppresses future automatic prompts.

The tutorial can always be reopened from `Tools > URDF Export Tutorial`. Progress is stored per Windows user in `%LOCALAPPDATA%\OSRBot\SW2URDF\urdf-export-tutorial-v1.state`; it does not use or modify SolidWorks registry keys. Closing an in-progress tutorial preserves its current step. Completing or permanently dismissing it suppresses automatic prompts, but the Tools menu entry remains available.

The eight tutorial steps follow the actual export order:

1. Prepare an assembly copy, resolve components, assign material density, rebuild, save, and verify SolidWorks mass properties.
2. Create `Origin_global`, per-Joint coordinate systems, and motion axes using a consistent right-handed convention.
3. Build and bind the Link tree, including free-canvas editing and Markdown outline editing with `#`, `##`, and `###` headings.
4. Configure Joint names, types, parent/child relationships, origins, axes, limits, dynamics, and optional Mimic relationships.
5. Validate mass, center of mass, the COM inertia tensor, rigid-body inequalities, and the inertia ellipsoid.
6. Select visual/collision geometry, collision strategy, and STL reduction while checking simulator cost and geometric coverage.
7. Export matching ROS1 and ROS2 packages with complete `urdf` and `meshes` directories.
8. Review `export_report.md`, `inertial_validation.csv`, and `mesh_manifest.csv`, then inspect Visual, Collision, Inertia, COM, axes, and Joint motion in a URDF viewer.

The companion window is intentionally instructional: it does not automate clicks, save files, alter CAD bindings, or mutate the model. This keeps the tutorial version-independent and lets the user verify each engineering decision in the real exporter.

### User-Friendly Workflow Features

The exporter stores the Link tree configuration inside the SolidWorks assembly as `URDF Export Configuration (v1.5)`. Existing v1.4 and older configurations are loaded and upgraded when saved. When the same assembly is opened again, the plugin loads the saved Link/Joint tree, names, parent-child structure, and saved link properties automatically. This is the normal path for iterative robot modeling: configure once, reopen, adjust, and export again.

When a saved tree is loaded, SolidWorks component references are reconnected from stored component PIDs. If a part was deleted, replaced, or saved as a new file and can no longer be resolved, the exporter warns which links need inspection before export.

Use `Edit Link Tree...` to open the free canvas. The canvas edits a working copy: `Cancel` discards all structural changes, while `Apply` validates and commits them as one transaction. Topology, URDF configuration, and SolidWorks CAD bindings are stored separately and are combined only when the PropertyManager or exporter requests a projection.

For a large hierarchy, click `Outline Edit` in the canvas and edit one Link per line. Markdown heading depth defines the parent-child relationship:

```text
# base_link
## camera_link
## left_steering_link
### left_front_wheel_link
```

Both `#base_link` and `# base_link` are accepted. Existing Links matched by name keep their Joint configuration, reusable URDF values, and SolidWorks CAD bindings. A plain-text rename in the same sibling position also keeps node identity and bindings. New headings create new Links with generated `fixed` Joints; `camera_link` becomes `camera_joint`, while a name without the `_link` suffix receives `_joint`. Joint names and types can then be changed on the canvas. Removing a heading removes that Link from the candidate tree. Invalid ROS names, duplicate names, multiple roots, and skipped heading levels are reported without replacing the canvas document. Apply the outline to update the canvas, then apply the canvas to commit the complete Link tree transaction.

Renaming a Joint updates existing mimic references. Deleting a Joint that is still referenced by a mimic relation is rejected. Reparenting a Link keeps its CAD component assignment but forces Joint kinematics and limits to be recomputed before export, so values calculated for the old relationship cannot be exported silently.

The recompute requirements are saved with the assembly. Closing the PropertyManager or SolidWorks after a topology or Joint-type change does not lose them: the next export automatically enables the required computations and clears each marker only after the computed configuration has been saved and accepted.

The right-side `Branch operations` group keeps `Copy branch`, `Paste branch`, and `Delete branch` in one place. Selecting one Link copies or deletes that Link together with every descendant. A box selection copies the union of all selected branches; overlapping parent/child selections are merged without duplicate nodes. `Ctrl+C`, `Ctrl+V`, and `Delete` use the same branch semantics.

Copy/paste duplicates the selected topology and reusable URDF configuration, including Joint settings, inertia, visual, collision, and mesh options. CAD component bindings are intentionally cleared on pasted Links because assigning the same SolidWorks body to two Links would create an invalid robot model. Pasted Links are marked incomplete until their mirrored or replacement components are assigned in the PropertyManager.

Each paste operation is treated as an independent group. Repeatedly pasting the same symmetric Link group keeps Mimic references inside that paste batch instead of pointing later copies back to the first pasted group. Link and Joint names are generated uniquely and can then be edited normally.

If the PropertyManager or the Joint/Link export window is closed before the current edits are formally saved, the exporter writes a recovery draft under `%LOCALAPPDATA%\OSRBot\SW2URDF\export-drafts`. Drafts are keyed by the complete saved assembly path, so two assemblies with the same filename in different folders do not share state. The next invocation for that assembly restores the draft automatically and reports that recovery occurred. A successful configuration save or completed export deletes the draft so committed assembly data remains authoritative. Unsaved assemblies do not have a stable path and therefore cannot use this recovery mechanism.

Recovery drafts reuse the same v1.5 Link/Joint configuration serializer as the assembly feature and also retain the ROS package name and last export directory. They are local per-user files: this feature does not add or remove SolidWorks registry entries and does not silently write the draft into the assembly.

Joint type validation uses the six standard URDF values: `fixed`, `revolute`, `continuous`, `prismatic`, `floating`, and `planar`. The editor also preserves the exporter-only `Automatically Detect` configuration state; it must resolve to one of the standard values during computation before URDF output. Type changes normalize incompatible saved fields. In particular, fixed and floating Joints remove stale motion data; continuous Joints keep effort, velocity, dynamics, and Mimic settings while removing lower and upper position bounds.

For fast review after export, check:

- `config/export_report.md`: human-readable export summary, fallback warnings, and effective strategies.
- `config/inertial_validation.csv`: per-link SolidWorks vs URDF mass, COM, inertia tensor, and error values.
- `config/mesh_manifest.csv`: per-link mesh strategy, estimated mesh size, and generated mesh records.

### Collision Strategy Quick Guide

Use `ComponentBoxes` as the default collision strategy for robotics simulation. It is usually much lighter and more stable than detailed mesh collision.

Use `BoxPrimitive` for box-like chassis, batteries, plates, and brackets. Use `CylinderPrimitive` for wheels, tubes, shafts, and lidar barrels. Use `SpherePrimitive` for spherical sensors or markers.

Use `ConvexHull` when the link is too complex for one primitive but still needs a single simple collision approximation.

Use `SimplifiedMesh` when primitives do not fit and the collision STL still needs to be smaller. Use `AccurateMesh` only when full collision detail matters more than simulator performance. Use `VisualMesh` mostly for viewer compatibility or when collision accuracy is not important.

Common material names available in the exporter include `black`, `white`, `gray`, `dark_gray`, `red`, `green`, `blue`, `yellow`, `orange`, `silver`, `aluminum`, `steel`, `plastic_black`, `rubber_black`, and `transparent_blue`.

## Development

1. Install Visual Studio 2017
1. Install .NET desktop development
    1. From Visual Studio: `Tools > Get Tools and Features...`
    1. Check `.NET desktop development` package
    1. Select `Modify`
1. Install the [SolidWorks API tools](https://help.solidworks.com/2019/english/api/sldworksapiprogguide/GettingStarted/SolidWorks_API_Getting_Started_Overview.htm)
1. Launch Visual Studio with admin privileges. Right click and select `Run as Administrator`
1. Open `sw2urdf/SW2URDF.sln`  
1. Enable Debugging
    1. Right click `SW2URDF` in the Solution Explorer
    1. Click the `Debug` Tab
    1. Ensure `Configuration:` is set to `Debug`
    1. Ensure `Start external program:` is pointing to the SolidWorks executable. For example `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe`

### Build Installer

From the repository root:

```powershell
.\scripts\BuildInstaller.ps1
```

The installer is written to `INSTALL\OUTPUT` and should be named `sw2urdfSetup_YYYYMMDD_<commit>.exe`. Packaging also writes `<installer>.sha256` and `<installer>.provenance.json`; the latter records the full source commit and tree, installer digest, build mode, pinned NuGet inputs, build-tool hashes, the exact staged SolidWorks API inputs, and a SHA256 manifest of every installed DLL and toolbar image.

Packaging requires Inno Setup 6.3 or newer and accepts only a clean `Release|x64` source tree. It creates a detached temporary Git worktree at the recorded source commit, copies the four SolidWorks API build inputs into that worktree, downloads a SHA256-pinned NuGet CLI, restores only `SW2URDF\packages.release.config` through the repository `NuGet.Config`, and verifies every `.nupkg` against `SW2URDF\packages.release.lock.json`. `INSTALL\OUTPUT` artifact changes are excluded from the cleanliness check. Release intermediates start empty, the production DLL uses deterministic compiler settings and commit-derived metadata, and only the completed installer is promoted back to `INSTALL\OUTPUT`. The installer includes runtime DLLs plus the SolidWorks toolbar image assets; test code, test runners, analyzers, XML documentation, PDB files, and the host-provided `solidworkstools.dll` are not shipped.

### Link Tree Architecture

The maintained Link tree implementation lives in `SW2URDF/UI/LinkTreeCanvas`. `LinkTreeSession` owns atomic topology transactions, while `LinkConfigurationStore` and `CadBindingStore` separately own reusable URDF values and SolidWorks object/PID bindings. The canvas depends only on `ILinkTreeCanvasHost` and never owns SolidWorks COM objects.

The former standalone prototype has been retired. Do not duplicate production tree, copy/paste, or validation behavior under `prototypes`; extend the production document/session boundaries and add focused tests instead.

### Tests

Build Debug first, then run the local test runner:

```powershell
TestRunner\bin\x64\Debug\net452\TestRunner.exe
```

To run a focused subset:

```powershell
TestRunner\bin\x64\Debug\net452\TestRunner.exe TestAssemblyExportLayout
```

### Release Automation

The workflow `.github/workflows/publish-installer-release.yml` publishes committed installer artifacts to the GitHub Releases page.

Trigger:

- Push to `master` or `main` with a changed `INSTALL/OUTPUT/sw2urdfSetup_*.exe`.
- Manual `workflow_dispatch`, optionally passing an installer path.

Behavior:

- Parses the date from `sw2urdfSetup_YYYYMMDD_<commit>.exe`.
- Ignores installer deletions and accepts added, modified, or Git-detected renamed artifacts only when exactly one current installer remains in the release commit.
- Selects the default manual-release artifact by Git commit time rather than checkout file timestamps.
- Requires the artifact commit to contain only one installer and its two sidecars, then verifies the checksum, source tree, build mode, pinned NuGet lock, and tool/input records. CI also extracts the Inno Setup package and compares every installed file and SHA256 value with the provenance payload manifest.
- Rejects an existing `vYYYYMMDD` tag or Release; published daily releases are immutable.
- Creates the Release as a draft with the installer, checksum, and provenance assets, then makes it public only after all uploads succeed. A retry removes only an incomplete draft for the same tag; a public daily Release remains immutable.

The current workflow promotes a locally built maintainer artifact; GitHub Actions does not rebuild the plugin because the proprietary SolidWorks API assemblies are not available on hosted runners. The provenance is therefore an auditable local-build attestation, not a CI-generated binary-source proof or an Authenticode signature.

## Converting mesh format from 3dxml to dae

Executing the following command will convert the format of the exported mesh from 3DXML to DAE, and rewrite the URDF, allowing you to display colored meshes in visualization tools like RViz:

```bash
pip3 install scikit-robot -U
convert-urdf-mesh <URDF_PATH> --output <OUTPUT_URDF_PATH>
```

### Trouble Shooting

1. `AxImp.exe` error - Check the installation of the .Net Tools. If there is no error, install the Windows 10 SDK.
1. `Resourse.resx` error - Check if `sw2urdf/SW2URDF/Resources.resx` exists and is empty. If empty, delete this file then right click the `SW2URDF` in the Solution Explorer and select `Properties`. Navigate to the Resources tab and click the button to create a new file.
