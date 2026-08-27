# Changelog

All notable OSRBot-maintained changes to this fork are documented here.

## 2026-08-27

### Fixed

- Fixed a SolidWorks 2023 `IMassProperty` read-order defect that could return a zero center of mass
  after reading the COM inertia tensor from the same COM object. The exporter now reads mass/COM
  and the COM tensor from independent mass-property objects, then explicitly converts both from
  the document frame to the selected Link frame.
- Unified assembly and part mass-property export on the same document-to-Link frame conversion
  route, with system units enabled for every SolidWorks mass-property query.
- Added a live SolidWorks integration test that verifies the converted COM and requires tensor
  eigenvalues to match the API principal moments for every wheel coordinate-system orientation in
  the four-wheel example.
- Stopped export before writing meshes or URDF when any inertial validation fails, including a
  calculated Link COM outside the selected SolidWorks component bounds; the error identifies the
  affected Link and failed check.

## 2026-08-26

### Fixed

- Fixed assembly Link COM coordinates being transformed twice after
  `IMassProperty.SetCoordinateSystem()`. Mass, COM, and the COM inertia tensor are now read
  directly in each selected Link coordinate system, matching the part-export route.
- Marshals selected SolidWorks component bodies through `DispatchWrapper[]`, matching the
  official `IMassProperty.AddBodies` C# contract so a Link cannot silently fall back to whole-model
  mass properties.
- Added a per-Link coordinate-system selector backed by the coordinate systems that exist in the
  active SolidWorks document. Changing it transactionally recomputes the Link COM inertia tensor,
  the parent Joint origin, and direct child Joint origins in the new Link frame.
- Kept URDF inertia at the center of mass during frame changes: translation changes the COM
  coordinates but does not apply a parallel-axis shift, while rotation applies `R * I * R^T` and
  preserves mass and principal moments.
- Shortened the ROS package path hint, added its full ROS1/ROS2 paths as a tooltip, displayed
  inertia-ellipsoid dimensions in millimeters, and fitted the new frame selector without adding a
  stale scrollbar.
- Repaired recovery drafts and legacy configurations that incorrectly retained a hidden parent
  Joint on the root Link, preventing false duplicate-Joint errors after reopening the exporter.
- Enforced the root-Link/no-parent-Joint invariant across configuration serialization, draft
  restoration, Link-tree session projections, robot imports, and final name validation while
  preserving the assembly-wide SolidWorks coordinate-system reference.
- Accepted SolidWorks-owned PropertyManager closes, including Component Preview transitions,
  instead of vetoing them with a COM exception that prevented `AfterClose`, draft persistence, and
  add-in owner release.
- Captured the current local Link-tree projection during `OnClose`, persisted configuration or a
  recovery draft only during `AfterClose`, and made close finalization and owner notification
  idempotent.
- Decoupled live Link/Joint field edits from SolidWorks component selection and PID refresh calls,
  with a committed-session fallback if the closing WinForms tree can no longer be cloned.
- Isolated TestRunner logs from a running SolidWorks process and made logger initialization
  thread-safe with immediate UTF-8 file flushing.

## 2026-08-25

### Added

- Added transactional Markdown-style Link tree outline editing inside the canvas, using `#`, `##`, and `###` headings for hierarchy depth.
- Added live outline validation for ROS names, duplicate Links, multiple roots, and skipped heading levels without mutating the current canvas tree.
- Unified automatic Joint naming across canvas and outline editing: a `_link` suffix is replaced with `_joint`, otherwise `_joint` is appended.
- Preserved node identity, reusable properties, and CAD bindings for unambiguous same-position Link renames in the outline editor.
- Added a first-use, eight-step companion tutorial for the complete assembly-to-URDF workflow: SolidWorks preparation, reference frames, Link tree, Joints, inertia validation, collision geometry, ROS1/ROS2 export, and report/viewer checks.
- Added `Tools > URDF Export Tutorial` so completed, skipped, or dismissed tutorials can always be reopened.
- Added per-assembly recovery drafts for unsaved PropertyManager, Joint, Link, ROS package, export-path, and mesh-option edits when an exporter window is closed unexpectedly.
- Added a dedicated Link-tree branch command group that keeps copy, paste, and delete actions together.

### Changed

- Removed the legacy `Load Configuration...` CSV merge button from the PropertyManager while retaining CSV serialization compatibility for existing exports.
- Stored tutorial progress only under the current user's `%LOCALAPPDATA%\OSRBot\SW2URDF` directory; the onboarding flow does not read or write SolidWorks registry keys and never modifies the active model automatically.
- Changed Link-tree copy semantics so a selected Link automatically includes every descendant; overlapping multi-selections are merged without duplicate nodes.
- Stored recovery drafts under `%LOCALAPPDATA%\OSRBot\SW2URDF\export-drafts`, isolated by the saved assembly's full path, and removed them after a successful formal configuration save or export.

### Fixed

- Kept the URDF PropertyManager alive for its full SolidWorks COM lifetime and isolated assembly
  display-state callbacks from invalid or unavailable COM objects.
- Deferred configuration persistence from `OnClose` to `AfterClose`, as required by the
  SolidWorks PropertyManager lifecycle, while preserving non-saved sessions as recovery drafts.
- Excluded the root Link from parent-Joint type validation so `base_link` no longer blocks preview/export with a false unsupported-Joint error.
- Changed successful legacy-configuration upgrade notices from a blocking English dialog to an English UTF-8 log entry so preview/export continues without an extra confirmation step.
- Prevented preview/export from creating or upgrading a SolidWorks configuration Feature while the PropertyManager is open; the computed Link tree is now protected by a local recovery draft and formally persisted from the following export window.

## 2026-08-24

### Fixed

- Included the required `solidworkstools.dll` runtime dependency in clean Release installers so first-time installation can register the SolidWorks add-in.
- Made release packaging fail when either `SW2URDF.dll` or `solidworkstools.dll` is absent from the installer payload.
- Prevented an older installer copy from unregistering a newer SW2URDF installation by verifying the active COM `CodeBase` before uninstall registration cleanup.
- Made SolidWorks add-in registry cleanup idempotent and limited it to the two SW2URDF GUID keys without recursive registry deletion.

## 2026-07-23

### Fixed

- Fixed Link tree editor startup in SolidWorks by removing a forward WPF `StaticResource` reference from the root window.

## 2026-07-22

### Changed

- Split Link tree topology, reusable URDF configuration, and SolidWorks CAD bindings into separate stores coordinated by a transactional session.
- Changed legacy TreeView and export models into generated projections so UI edits no longer mutate the committed Link tree state indirectly.
- Copy/paste now preserves reusable URDF configuration while intentionally clearing CAD component bindings on copied Links.
- Made standard Joint types use one canonical source shared by the PropertyManager, canvas, validation, and configuration policy.
- Retired the standalone Link tree prototype after production integration; the exporter implementation is now the only maintained behavior source.
- Changed CSV configuration merge to a modal operation so a stale merge snapshot cannot overwrite concurrent Link tree edits.
- Removed the legacy export side effect that detached the root node from the PropertyManager TreeView before creating the robot.

### Fixed

- Preserved canvas node identity when reopening the editor after editing Link properties or structure in the legacy PropertyManager tree.
- Prevented stale TreeView structure from diverging from the tree used for configuration serialization or URDF export.
- Migrated mimic references when a Joint is renamed and rejected deletion when surviving Joints still reference the removed name.
- Preserved Mimic relationships inside each repeated group-paste batch and during Joint-name swaps or case-only renames.
- Forced Joint kinematics recomputation after drag-to-reparent so an Origin calculated for the old parent cannot be exported.
- Forced Joint kinematics recomputation for newly added and copied Links after their CAD components are assigned.
- Persisted the kinematics-recompute marker and additional collision geometry through saved assembly configuration round trips.
- Persisted separate Joint-kinematics and Joint-limit recompute markers and invalidated both when topology or Joint type changes.
- Made legacy TreeView capture transactional and rejected dangling Mimic references without replacing the last valid session state.
- Prevented the exporter-only automatic Joint type from reaching URDF output when kinematics computation is disabled or fails to resolve it.
- Stopped export when assembly configuration serialization or SolidWorks attribute persistence fails.
- Made `LinkNode.IsIncomplete` the runtime source of truth and fixed incomplete-node traversal across sibling branches.
- Preserved non-ASCII coordinate-system and texture metadata by using UTF-8 for saved configuration XML string conversion.
- Normalized type-specific Joint data before projection: fixed/floating Joints discard stale motion fields, while continuous Joints retain effort, velocity, dynamics, and Mimic data but discard position bounds.
- Fixed duplicate SolidWorks PropertyManager control IDs for Link name, Joint name, axis, coordinate system, and Joint type controls.
- Prevented node copy/paste shortcuts from intercepting text editing, preserved manual Joint names when dropping onto the existing parent, and corrected deep-node focus positioning.
- Rejected duplicate internal node IDs and detached computed projections without partially committing session state.
- Preserved the exporter-only `Automatically Detect` Joint configuration in both the PropertyManager and canvas while keeping final URDF types canonical.
- Kept Mimic targets bound to stable node identity so deleting one Joint and reusing its name cannot silently retarget another Joint.
- Rejected stale canvas clipboard snapshots after their source branch is deleted instead of creating orphaned pasted nodes.
- Rejected unknown, conflicting, or multi-axis SolidWorks DOF results instead of silently exporting them as fixed Joints.
- Prevented fixed and floating Joints from creating or reading reference-axis geometry.
- Refused to overwrite assembly configurations written by a newer exporter serialization version.
- Corrected global-to-local inertia rotation to use the inverse Link-frame transform without reapplying SolidWorks product-of-inertia sign conversion.
- Prevented 3DXML mesh export from mutating the already computed URDF center of mass or inertia tensor.
- Kept configuration serialization detached from the live export projection so collision-name parsing cannot be undone before URDF generation.
- Exported mesh-bearing descendants below fixed-frame Links and restored per-Link component visibility after every success or failure path.
- Rejected changed clipboard sources, failed Joint recomputation state, ambiguous limit mates, and invalid limit bounds instead of exporting stale values.
- Preserved user-entered Mimic multiplier and offset values across repeated UI toggles and made clearing a Mimic target null-safe.
- Read the SolidWorks 2023 center-of-mass inertia tensor before the center of mass, preventing a valid mass property from being exported with an all-zero inertia tensor.
- Removed informational and failure message boxes from the package/export core; export now returns a failure status and detailed log path for the UI or automation caller to handle.
- Moved configuration-save confirmation, upgrade notices, and failure dialogs out of the persistence core and into a shared UI interaction boundary.
- Hardened SolidWorks feature and component enumeration against transient or unexpected COM proxy types instead of aborting the complete export.
- Removed WinForms event pumping from file retry loops so an in-progress export cannot re-enter through UI events.

### Development

- Made the SolidWorks test fixture lazy so pure unit tests no longer fail merely because the SolidWorks COM class is unavailable to the test process.
- Serialized SolidWorks test classes correctly, made fixture initialization thread-safe, and excluded tests and test frameworks from Release builds.
- Limited COM fixture cleanup to the SolidWorks process created by the test run, resolved lightweight components before export, and isolated generated ROS packages in disposable temporary roots.

### Packaging

- Upgraded saved assembly configuration to v1.5 while retaining v1.4 and older readers for automatic migration.
- Restricted installer builds to a clean `Release|x64` source tree, cleaned stale output before compilation, and packaged only runtime DLLs plus required SolidWorks toolbar image assets.
- Made installer release automation ignore deleted artifacts and identify releases by the source commit encoded in the installer filename.
- Updated installer publisher and support metadata to the maintained OSRBot fork.
- Built only the production project during packaging, removed the remaining Release dependency on xUnit build targets, and resolved toolbar images relative to the installed add-in DLL.
- Made installer publishing handle Git rename detection, choose manual artifacts by commit time, and refresh same-day release notes.
- Added a production-only NuGet restore manifest, pinned NuGet source/tool/package hashes, and clean isolated Release intermediates for auditable packaging.
- Preserved a user's selected install directory during upgrades.
- Made daily releases immutable and draft-first so failed uploads cannot move a public tag or leave a Release without its installer.
- Added SHA256 and provenance sidecars, validated them in CI, and moved manual workflow input through an environment variable before shell use.
- Built from a detached temporary worktree, staged the exact SolidWorks API inputs, and made embedded build metadata derive from the source commit time.
- Stopped redistributing SolidWorks' host-provided `solidworkstools.dll`; build provenance now records all SolidWorks API input versions and hashes.
- Fixed the assembly version, enabled deterministic Release DLL compilation, and rejected promotion if the source checkout changed during packaging.
- Required Inno Setup 6.3+ and made release retries replace only incomplete drafts while preserving public daily releases.
- Restricted artifact commits to the installer plus checksum/provenance sidecars and documented that hosted CI promotes a trusted maintainer build rather than rebuilding against proprietary SolidWorks assemblies.
- Added an exact installed-payload hash manifest and made release CI extract the Inno package before publication, rejecting missing, extra, or changed payload files.
- Rejected release artifact overwrites and checked Git command results independently before promoting a local build.
- Made Release version metadata fail closed when its source commit, worktree state, or commit time cannot be read from Git.
- Pinned the repository checkout Action to an immutable commit for release publication.

## 2026-07-17

### Added

- Added a transactional Link tree canvas to the SolidWorks PropertyManager workflow.
- Added free node placement, drag-to-reparent, automatic layout, box selection, and structure-only group copy/paste with automatic Link and Joint name deduplication.
- Added Link tree validation for a single root, unique ROS-compatible names, valid parents, and cycle prevention.

### Changed

- Existing Link configuration values and SolidWorks bindings remain intact when canvas structure changes are applied.
- New Links start empty; pasted Links copy reusable URDF settings but are marked incomplete and start without SolidWorks component bindings.

## 2026-06-29

### Added

- Added a built-in exporter guide window with collision strategy guidance, common material names, the project URL, and the current maintainer contact.
- Added common material name presets for exported URDF materials, including `aluminum`, `steel`, `rubber_black`, and `transparent_blue`.
- Added GitHub Actions release publishing for installer artifacts named `INSTALL/OUTPUT/sw2urdfSetup_YYYYMMDD_<commit>.exe`.
- Added user-facing documentation for automatic Link tree configuration loading, CSV configuration merge, and export report files.

### Changed

- Shortened the ROS package output hint in the Link page to `ROS1/<name> | ROS2/<name>`.
- Documented the maintained fork, installer naming convention, and release automation in `README.md`.

### Fixed

- Fixed Link page footer layout feedback where repeated layout passes could push export buttons downward and leave a stale vertical scrollbar.
- Fixed high-DPI Link and Joint page layout regressions around footer buttons, mimic-joint controls, and inertia matrix display.
- Hardened ROS2 package export so meshes are copied alongside the generated URDF.
- Improved UTF-8 English logging for export diagnostics to avoid mojibake in log files.

### Packaging

- Published installer: `INSTALL/OUTPUT/sw2urdfSetup_20260629_598c7dd.exe`.

## Earlier OSRBot Work

- Added native ROS1 and ROS2 package output support.
- Improved SolidWorks mass property and inertia tensor export, including per-link comparison reporting against SolidWorks values.
- Added collision mesh strategy support, mesh reduction controls, and mesh export manifest/report output.
- Added Chinese UI localization while preserving ROS-compatible package and link naming.
