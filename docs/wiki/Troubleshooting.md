# Troubleshooting

**English** | [简体中文](Troubleshooting-zh-CN)

## Exporter Menu Is Missing

- Confirm that the installer is x64 and was run as administrator.
- Exit and restart SolidWorks completely; hot reload is not supported.
- Check that SW2URDF appears and is enabled in SolidWorks Add-Ins.
- Inspect `%USERPROFILE%\sw2urdf_logs\sw2urdf.log`.

## Link Tree Edits Disappeared

- Saved assembly: check `%LOCALAPPDATA%\OSRBot\SW2URDF\export-drafts` for a recovery draft.
- Unsaved assembly: no stable full path exists, so isolated recovery is unavailable.
- Deleted/replaced/Save As component: the persistent-reference PID can be invalid; rebind manually.
- `Apply` and `Cancel` are transaction boundaries; canceling the canvas does not commit structure.

## Duplicate Joint Reported but Not Visible

- The root Link must not have a parent Joint; the load-repair path removes hidden legacy root Joints.
- Check Mimic references and names that collide after case/normalization rules.
- Use full canvas/Outline validation rather than only the currently expanded UI branch.

## Zero Inertia or Blocked Export

1. Confirm non-zero mass and valid material/density in SolidWorks Mass Properties.
2. Confirm that the Link binds the intended bodies, without parent/child duplicate selection.
3. Confirm that the selected Link-frame coordinate system exists and is correct.
4. Inspect mass, COM, tensor, principal moments, and errors in
   `config/inertial_validation.csv`.
5. If COM is outside component bounds, inspect component selection and transforms before manually
   editing URDF values.

SolidWorks COM/RPC failures can break Live API tests or previews. Restart SolidWorks and retry. A
graphics-layer failure neither proves nor disproves numerical validity.

## Inertia or Collision Preview Is Invisible

- Temporary bodies require a valid visible top-level Part display host; a top-level subassembly is
  not itself a valid host.
- Try wireframe, hidden-lines-visible, or shaded display mode.
- After switching Link, re-enable the preview so that old temporary bodies are not mistaken for the
  current Link.
- Record the SolidWorks `Display3` return code and log when reporting a failure.
- Continue checking formal reports, but do not release an unverified model.

## Collision Strategy Fell Back

Open:

- `config/mesh_manifest.csv`: requested/effective strategy and file record;
- `config/export_report.md`: fallback summary and reason.

Generation failure falls back to `VisualMesh`. Do not trust only the last strategy selected in UI.

## Missing Files or Incomplete ROS2 Meshes

- Read `export_report.md` first.
- Confirm that the output directory is writable and not locked.
- Check the completion summary's changed-file count.
- Verify URDF `package://` paths and ROS1/ROS2 package names.
- When reusing a directory, the summary counts files created or changed by this run, not unrelated
  old files.

## Test Failures

- Pure tests should not require SolidWorks.
- Live tests need a compatible local SolidWorks and working COM/RPC.
- `RPC server unavailable` usually means the process is unreachable or automation terminated it; it
  is not proof of a pure-algorithm failure.
- TestRunner uses a process-specific UTF-8 log in a temporary directory to avoid locking the normal
  plug-in log.

## Build Problems

- Point `SolidWorksInstallDir` to the actual installation containing matching Interop DLLs.
- Build x64 against .NET Framework 4.5.2.
- Install Visual Studio `.NET desktop development` and matching SolidWorks API Tools if resources or
  Interop tools are missing.
- Auditable installer builds currently require Inno Setup 6.3.0-6.3.3; newer versions are rejected
  by the packaging script.
