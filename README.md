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
- Automatic Link tree configuration loading from the SolidWorks assembly feature `URDF Export Configuration (v1.4)`.
- Transactional Link tree canvas for adding, renaming, moving, box-selecting, copying, and pasting Link groups before export.
- CSV configuration loading and merge support through `Load Configuration...` for reusing old project values.
- Chinese UI localization and safer UTF-8 English export logs.
- Built-in usage guide with collision strategy recommendations, common URDF material names, project URL, and maintainer information.

## Latest Release

For the OSRBot fork, use the latest GitHub Release asset named:

```text
sw2urdfSetup_YYYYMMDD_<commit>.exe
```

Example:

```text
INSTALL/OUTPUT/sw2urdfSetup_20260629_598c7dd.exe
```

Release tags use the installer date: `vYYYYMMDD`. If multiple installers are published on the same day, the release tag stays the same and the installer filename commit suffix identifies the build.

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

### User-Friendly Workflow Features

The exporter stores the Link tree configuration inside the SolidWorks assembly as `URDF Export Configuration (v1.4)`. When the same assembly is opened again, the plugin loads the saved Link/Joint tree, names, parent-child structure, and saved link properties automatically. This is the normal path for iterative robot modeling: configure once, reopen, adjust, and export again.

When a saved tree is loaded, SolidWorks component references are reconnected from stored component PIDs. If a part was deleted, replaced, or saved as a new file and can no longer be resolved, the exporter warns which links need inspection before export.

Use `Edit Link Tree...` to open the free canvas. The canvas edits a working copy: `Cancel` discards all structural changes, while `Apply` validates and commits them as one transaction. Topology, URDF configuration, and SolidWorks CAD bindings are stored separately and are combined only when the PropertyManager or exporter requests a projection.

Renaming a Joint updates existing mimic references. Deleting a Joint that is still referenced by a mimic relation is rejected. Reparenting a Link keeps its CAD component assignment but forces Joint kinematics to be recomputed before export, so an Origin calculated for the old parent cannot be exported silently.

Copy/paste duplicates the selected topology and reusable URDF configuration, including Joint settings, inertia, visual, collision, and mesh options. CAD component bindings are intentionally cleared on pasted Links because assigning the same SolidWorks body to two Links would create an invalid robot model. Pasted Links are marked incomplete until their mirrored or replacement components are assigned in the PropertyManager.

`Load Configuration...` is a different feature. It imports values from a CSV export and opens a modal merge window, preventing edits made against a newer Link tree from being overwritten by an older merge snapshot. Use it when you want to reuse values from an old robot project, such as mass/inertia, visual/collision settings, joint kinematics, or other joint parameters. It is useful after a CAD redesign where the tree shape is similar but some values should come from a previous export.

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

The installer is written to `INSTALL\OUTPUT` and should be named `sw2urdfSetup_YYYYMMDD_<commit>.exe`.

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
- Push to `master` or `main` with a changed release workflow file, which publishes the newest committed installer.
- Manual `workflow_dispatch`, optionally passing an installer path.

Behavior:

- Parses the date from `sw2urdfSetup_YYYYMMDD_<commit>.exe`.
- Creates or updates release tag `vYYYYMMDD`.
- Uploads the installer as a release asset. Existing assets with the same name are replaced.

## Converting mesh format from 3dxml to dae

Executing the following command will convert the format of the exported mesh from 3DXML to DAE, and rewrite the URDF, allowing you to display colored meshes in visualization tools like RViz:

```bash
pip3 install scikit-robot -U
convert-urdf-mesh <URDF_PATH> --output <OUTPUT_URDF_PATH>
```

### Trouble Shooting

1. `AxImp.exe` error - Check the installation of the .Net Tools. If there is no error, install the Windows 10 SDK.
1. `Resourse.resx` error - Check if `sw2urdf/SW2URDF/Resources.resx` exists and is empty. If empty, delete this file then right click the `SW2URDF` in the Solution Explorer and select `Properties`. Navigate to the Resources tab and click the button to create a new file.
