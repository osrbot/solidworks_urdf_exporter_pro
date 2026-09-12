# Exact-candidate native release acceptance

Date: 2026-09-13. The installed candidate passed the checks below on
Windows 11 Home China build 26200 with SolidWorks 2023 SP1.0.

## Identity and source protection

- Installer source: `a0af2c5a1ea934fe027fb935df94516880c7a33f`.
- Installer SHA256: `bf7c25db6779b9eb1d6c4b1bfa1d369f4155e1c9a1799320283a9b2bc245eb0f`.
- Installed `SW2URDF.dll` SHA256: `1a5290ef2ecac276aa05729ccae5c1236e9bea712c3d80ae2b4436d04db821ec`.
- All 1,466 installed payload files matched the original provenance before
  native execution. The harness loaded the installed production assemblies.
- SolidWorks returned the loaded add-in object after the owned-instance
  `LoadAddIn` call; `native-addin-activation.json` records its actual result.
- All CAD work used disposable copies of the existing three-DOF Arm example.
  Eleven original example-file hashes remained unchanged. Existing user CAD
  changes were preserved. No host installation was performed in this run.

Installation, published-stable upgrade, overwrite, uninstall and reinstall
were tested on separate disposable Windows VMs; see
[runtime and lifecycle evidence](2026-09-13-clean-runtime.md).

## Migration and mass properties

The historical configuration produced four Links, three Joints and seven
explicit geometry references. Migration preserved structural identity,
component persistent references, topology and applicable joint settings;
derived pose/inertia/mesh data was reset and recomputed. Strict v2 XML
round-trip, migration-dialog rendering/cancellation, save to a new assembly,
close/reopen and restored binding resolution passed. The source's old
configuration and file bytes were preserved.

The test fixture explicitly reviewed the retained historical joint types,
axes, frames and position limits. Missing effort/velocity metadata used the
same defaults (1) as the production UI. This is fixture setup, not inferred
actuator design; the MJCF model has no actuators. Mass, COM, tensor and
principal-inertia validation passed for all four Links, including the
example's different component configurations.

## Native collision export matrix

Every strategy exported ROS1, ROS2, OpenUSD and MuJoCo successfully. Each ROS
package contains four Links, three Joints and exactly one collision mesh
reference per Link. Independent artifact checks verified package-relative
references, finite binary STL data, triangle counts, identical ROS1/ROS2
collision assets, and every actual-strategy CSV row. Generated primitive
origins are zero. No strategy fallback occurred in this fixture.

| Strategy | Collision triangles: base, link1, link2, link3 | Targets passed | Fallback |
| --- | --- | --- | --- |
| VisualMesh | 360, 2216, 3816, 3816 | 4 / 4 | None |
| SimplifiedMesh | 234, 1108, 1908, 1908 | 4 / 4 | None |
| AccurateMesh | 360, 2216, 3816, 3816 | 4 / 4 | None |
| BoxPrimitive | 12, 12, 12, 12 | 4 / 4 | None |
| CylinderPrimitive | 96, 96, 96, 96 | 4 / 4 | None |
| SpherePrimitive | 224, 224, 224, 224 | 4 / 4 | None |
| ComponentBoxes | 12, 48, 72, 72 | 4 / 4 | None |
| ConvexHull | 32, 38, 44, 44 | 4 / 4 | None |

All eight OpenUSD reports record successful stage reopening with OpenUSD
26.8. All eight MuJoCo reports record official 3.12.0 compilation to MJB,
independent XML/MJB reload and one zero-control step for robot.xml and
scene.xml. Temporary validation MJB files are not final package assets.

The VisualMesh result was preserved byte-for-byte from the first successful
short-path run (`r13`); the remaining seven ran in `r14`. The independent
`scripts/verify_collision_matrix.py` check covers the resulting complete
matrix and rejects missing reports, missing/duplicate Link rows or fallback.

## Live preview and export UI

The preview fixture added one disposable Arm wrapper while retaining the
original components and root frame. The selected hidden part was at actual
component depth **2**. Inertia preview displayed the
box and three principal axes; Show, Hide and Dispose passed. All eight
collision previews displayed finite bounds and passed Show/Hide, followed
by Dispose. The display target was a top-level Part component; the event
record confirms the existing Arm_base-1 was reused (UsesTemporaryHost=false).
SimplifiedMesh preview explicitly showed the original CAD shape as a
reference, not the final simplified mesh; the exported reduction is checked
separately in the matrix. Original
visibility, appearance, component inventory and CAD file hashes were
restored, with no extra host component or displayed preview left behind.

A separate real BoxPrimitive export used the installed progress session
and results dialog. The progress window was visible and TopMost while
processing real export events. Its UI thread completed without failure;
the final summary recorded four successes and zero failures. Captured
progress/results bitmaps were visually reviewed.

The final probe source additionally propagates UI subscriber failures to
the main acceptance path, because production export intentionally isolates
progress subscribers. This test-only hardening was compiled after the UI
run; the run's actual screenshots and four-success summary were independently
checked. The artifact verifier also hashes persistent USD/MJCF entrypoints
and dependencies; copy-only negative checks reject missing entrypoints,
wrong ROS package names, and detect changed dependency hashes.

## Target viewer

The official MuJoCo 3.12.0 simulate application loaded the unmodified
VisualMesh scene, rendered the model, paused/reset, changed camera view,
saved a screenshot, and exited normally. The downloaded official archive
matched SHA256 `ffe071c2747dd9513a1c59e7d2428bb678d887f9edec9eb9674b4288a248a8e9`.

A separate static geometry audit confirmed all three arm meshes exist in
distinct zero-pose locations. Per-Link URDF/MJCF mesh hashes agree, joint
positions agree, and RPY/quaternion rotation matrices differ by at most
2.22e-16. A view that appeared to show two segments did not establish a
missing segment; occlusion remains an inference. A same-camera CAD/render
fidelity comparison was not performed.

## Observed limits and retained evidence

- A first test setup lacked explicit joint-source confirmation and was
  correctly rejected before export. The reviewed fixture now uses the same
  confirmation policy as the UI; no production validation was bypassed.
- A deeply nested test destination exceeded a legacy Windows path limit
  during MuJoCo validation. The successful matrix used a shorter folder.
  Other successful targets were retained and the failed target was reported.
- A harness initially expected final MJB files, although the exporter only
  retains their validation report. It was corrected to inspect the actual
  official-compilation report, then independently checked all outputs.
- Early runs encountered SolidWorks low-memory prompts. The user authorized
  continuing on disposable test copies. No original CAD file was saved and
  no unrelated user process was terminated.
- The source Arm configuration includes two unresolved/suppressed brace
  references reported in the native log; all three full arm segments were
  exported. The audit describes this source state and does not silently add
  components or claim an engineering correction.
- Basic viewer loading and one-step validation do not establish controller
  quality, contact tuning, long simulations, ROS/Gazebo operation, Isaac
  Sim/Isaac Lab acceptance, or every SolidWorks version. The reported 2025
  issue #1 and ROS TF complaint are not declared fixed by these tests.

The release evidence archive retains migration outputs, all eight native
export packages/reports, independent checks, preview and UI images,
activation/geometry/viewer records, and original-file hash verification.
The production installer was not rebuilt or modified for these tests.
