# Changelog

All notable OSRBot-maintained changes to this fork are documented here.

## Unreleased / 未发布

### English

#### Added

- Added two first-class, user-selectable robot-asset targets alongside the existing ROS package
  targets:
  - OpenUSD writes `USD/<package>/robot.usd`, converted geometry dependencies, retained source mesh
    evidence, `name_map.json`, and `export_report.json`.
  - MuJoCo MJCF writes `MuJoCo/<robot>/robot.xml`, `scene.xml`, Visual/Collision assets,
    `name_map.json`, and `export_report.json`.
- Added pinned runtime validation for both asset targets. OpenUSD generation must reopen the stage
  with the bundled OpenUSD runtime. MJCF export must pass the bundled official MuJoCo tools by
  compiling and canonically saving both XML entry points, reloading them, and advancing one
  zero-control step.
- Added bilingual output documentation that separates generation capability, automated validation,
  and application-level runtime validation.

#### Changed

- Reduced the user-facing export choices to four concrete deliverables: ROS 1 package, ROS 2
  package, OpenUSD robot asset, and MuJoCo MJCF model. The canonical Robot Bundle is now a private,
  temporary staging representation and is neither a selectable target nor a delivered artifact.
- Removed the obsolete Isaac Sim version, Isaac Lab version, actuator-profile, and raw
  `ros2_control` file selectors from the main export workflow. OpenUSD export does not require or
  detect a local Isaac installation; MJCF export does not generate actuators, controllers, PID
  gains, tasks, or reinforcement-learning code.
- Made RGBA and the color picker the direct Link appearance controls. The stable URDF material ID
  is derived from that color instead of acting as a second color preset selector.
- On initial form load, defaulted missing moving-Joint `effort` and `velocity` fields to numeric
  value `1` in their URDF units. A value the user later clears is treated as invalid instead of
  being silently restored; position limits remain an engineering decision and are not guessed.
- Improved the themed WinForms layout, footer spacing, field borders, tab switching, bilingual
  diagnostics, and immediate Joint validation without changing canonical URDF Joint values.
- Validate inertia triangle inequalities in the principal frame using scale-aware eigenvalue
  checks, including rotated and very small tensors.
- Each export atomically replaces only the selected independent target directories. Existing
  unselected targets are retained, and the top-level report identifies the targets generated and
  validated by the current run.

#### Fixed

- Resolved deep or hidden Link preview display through a root-assembly-safe temporary-body host and
  the complete component transform, without adding persistent tree objects. Equivalent-inertia and
  Collision previews still require live validation in the maintainer's target SolidWorks versions.
- Kept private staging cleanup on both successful and failed export paths so internal Bundle files
  are not left in the user's delivery directory.
- Made the pinned MuJoCo integration test a hard-failing dedicated category. Hosted portable jobs
  exclude it explicitly; runtime CI and installer builds require a TRX proving exactly one executed,
  passed test, preventing missing-runtime skips or zero-test runs from appearing successful.
- Made selected-target publication fail closed: a blocking `FAIL` in the generated health report
  now rolls every selected target back. Directory replacement uses an output-root lock and a
  durable journal so the next export can finish an interrupted commit or reverse an uncommitted
  multi-target publication without mixing old and new target directories. The journal is retained
  when old-output cleanup is temporarily blocked, and recovery is idempotent both before the first
  directory move and after a partially completed rollback.

#### Validation Scope

- **Generation capability:** the implementation exposes four output routes and defines their file
  contracts; test results must be reported separately from this capability statement.
- **Automated runtime validation:** successful USD export requires generation and reopen with the
  pinned OpenUSD runtime; successful MJCF export requires compile, save, reload, and one
  zero-control step with pinned official MuJoCo tools. Reproducible installer packaging runs the
  deterministic plugin suite with `--exclude-live-solidworks`; native SolidWorks COM suites remain
  explicit Live API evidence and are recorded as not requested when they were not run.
- **Application runtime validation:** this development state does not claim that the new USD has
  been run in Isaac Sim or Isaac Lab, that ROS packages have been launched in a ROS installation,
  or that exported models have passed task-specific controller, contact, stability, performance,
  or reinforcement-learning validation. Deep SolidWorks preview behavior remains subject to live
  maintainer testing before a public release.

### 简体中文

#### 新增

- 在现有 ROS 功能包目标之外，新增两种一等、可由用户选择的机器人资产目标：
  - OpenUSD 写出 `USD/<package>/robot.usd`、转换后的几何依赖、保留的源网格证据、
    `name_map.json` 和 `export_report.json`。
  - MuJoCo MJCF 写出 `MuJoCo/<robot>/robot.xml`、`scene.xml`、Visual/Collision 资产、
    `name_map.json` 和 `export_report.json`。
- 为两种资产目标增加固定运行时验证。OpenUSD 必须使用安装包内置运行时重新打开生成的 stage；
  MJCF 必须使用内置 MuJoCo 官方工具对两个 XML 入口完成编译、规范保存、重载和一步零控制推进。
- 增加双语输出文档，分别说明生成能力、自动化验证和实际应用运行验证。

#### 变更

- 用户可见导出目标收敛为四种具体交付物：ROS 1 功能包、ROS 2 功能包、OpenUSD 机器人资产和
  MuJoCo MJCF 模型。规范 Robot Bundle 改为私有临时暂存表示，不再可选，也不作为产物交付。
- 从主导出流程删除旧 Isaac Sim/Isaac Lab 版本、actuator profile 和原始 `ros2_control` 文件选择。
  OpenUSD 不要求或检测本机 Isaac；MJCF 不生成 actuator、控制器、PID、任务或强化学习代码。
- RGBA 与选色器成为 Link 外观的直接输入；稳定 URDF material ID 由颜色派生，不再充当第二套
  颜色预设。
- 首次载入表单时，缺失的可动 Joint `effort` 和 `velocity` 按对应 URDF 单位默认填写数值 `1`。
  用户之后主动清空的值会被判定为无效，不会被静默补回；位置上下限仍由用户按工程语义填写，
  不做猜测。
- 改进 WinForms 主题布局、页脚间距、输入边框、Tab 切换、双语诊断和 Joint 即时校验，同时保持
  规范 URDF Joint 值不变。
- 使用尺度感知的特征值检查，在主轴系校验惯性三角不等式，包括旋转张量和极小张量。
- 每次导出只原子替换本次选中的独立目标目录；未选目标的既有目录会保留，顶层报告明确记录
  本次实际生成和验证的目标。

#### 修复

- 使用根装配安全的临时体宿主与完整组件变换，修复深层或隐藏 Link 的预览显示，且不增加持久化
  树节点。等效惯性与 Collision 预览仍须在维护者目标 SolidWorks 版本中完成 Live 验证。
- 成功和失败路径都会清理私有暂存，内部 Bundle 文件不会留在用户交付目录。
- 固定 MuJoCo 集成测试改为硬失败的专用类别。普通可移植作业显式排除；运行时 CI 与安装包构建
  必须由 TRX 证明恰好 1 项被执行并通过，禁止缺少运行时的跳过或零测试伪绿。
- 所选目标现在采用失败即关闭的发布语义：导出体检报告出现阻断级 `FAIL` 时回滚全部所选目标。
  目录替换使用输出根互斥锁与持久化 journal；若进程中断，下一次导出会完成已经确认的提交，或
  逆转尚未提交的多目标发布，避免新旧目标目录混杂。旧输出清理暂时受阻时会保留 journal；在
  第一次目录移动前中断以及部分回滚后再次中断时，恢复操作均保持幂等。

#### 验证范围

- **生成能力**：实现提供四条输出路线并定义文件合同；测试结果必须与能力声明分开报告。
- **自动化运行时验证**：USD 成功要求使用固定 OpenUSD 运行时生成并重开；MJCF 成功要求使用
  固定 MuJoCo 官方工具完成编译、保存、重载和一步零控制推进。可复现安装包构建使用
  `--exclude-live-solidworks` 运行确定性插件套件；原生 SolidWorks COM 套件仍是独立的 Live API
  证据，未运行时明确记录为未请求。
- **实际应用运行验证**：当前开发状态不声称新 USD 已在 Isaac Sim/Isaac Lab 中运行，不声称
  ROS 功能包已在 ROS 环境中启动，也不声称目标模型通过了控制器、接触、稳定性、性能、任务或
  强化学习验证。公开发布前仍须由维护者完成深层 SolidWorks 预览的 Live 测试。

## 2026-08-29

### Changed

- Reworked the assembly exporter around a themed, scrollable WinForms layout while preserving the
  existing Link/Joint workflow, current feature controls, and Simplified Chinese translations.
- Applied the same SolidWorks-compatible visual theme to the part exporter and made its content
  area scroll when localized or DPI-scaled controls exceed the available screen working area.

### Fixed

- Prevented repeated localization and theme passes from disposing a `Font` object that a WinForms
  control still references. This fixes exporter construction failures after equivalent fonts are
  applied more than once.
- Constrained DPI-scaled assembly exporter bounds to the active monitor working area so the footer
  actions remain reachable on 150% and 200% scaling.
- Reflowed the part export collision section and footer after font scaling, preventing the collision
  controls from covering the Finish button.
- Made the Link-page scroll reset regression independent of an interactive SolidWorks window while
  retaining hierarchy, ownership, and reset assertions.

### Documentation

- Added matching English and Simplified Chinese "Why This Fork Exists" summaries to the README and
  Wiki home page, tying the maintained branch to specific legacy production gaps and implemented
  responses while preserving upstream attribution.
- Added a complete Simplified Chinese README alongside the canonical English README, with reciprocal
  language navigation and the same support, inertia, collision, build, limitation, and credit
  boundaries.
- Published paired English and `-zh-CN` GitHub Wiki pages for every maintained topic and added one
  bilingual sidebar instead of mixing two languages inside each procedural paragraph.
- Made repository-maintained bilingual Release Notes the only candidate body source. CI now rejects
  a source commit that lacks either `## English` or `## 简体中文`, required artifact/source
  placeholders, or a fully resolved rendered body.
- Expanded the current draft-candidate notes in both languages. Existing public historical Releases
  remain immutable; the bilingual policy applies to the current Draft and future candidates.
- Rebuilt the repository README as a factual project entry point covering supported environments,
  installation boundaries, the eight-step export workflow, inertial and collision conventions,
  generated reports, testing, reproducible packaging, known limits, and the manual release gate.
- Added version-controlled GitHub Wiki sources under `docs/wiki` for installation, quick start,
  Link-tree editing, inertia, collision, troubleshooting, contribution, and release operations.
- Corrected two stale README claims: texture editing is no longer exposed by the maintained UI, and
  all user-facing collision strategies now have a SolidWorks temporary-geometry preview path.
- Added explicit credit to the upstream ROS project, Stephen Brawner, the historical supporters
  named by upstream, the recorded 3DXML contributors, and Winter's SolidWorks-to-URDF inertia
  convention article. These acknowledgements do not imply that the reference article supplied
  source code to this repository.
- Documented that the installer does not terminate or hot-reload a running SolidWorks process, that
  preview geometry is not promised to be byte-identical to final mesh tessellation, and that local
  provenance is not an Authenticode signature or a hosted-CI rebuild attestation.

## 2026-08-28

### Commit traceability

- `50b69a1` added deterministic whole-tree Link coloring and persistence.
- `425c47d` removed the maintained texture-editing controls while retaining legacy metadata reads.
- `5d48cb1` added Simplified Chinese Joint-type and direct-child-count explanations.
- `7f1349c` restricted temporary preview hosts to valid visible top-level Part instances.
- `3e172c9`, `0df4c94`, and `7c59918` completed temporary-body previews across collision strategies,
  including the sphere display fix.
- `8fbc64e` made equivalent-inertia principal axes visually distinguishable.
- `69d280c` added the equivalent-inertia cuboid preview.
- `7b397bb` corrected cylinder collision STL cap winding.

### Added

- Added explicit whole-tree automatic Link coloring. Link depth moves through a cool-to-warm
  palette, normalized left/right counterparts share stable colors, and every generated material
  ID plus RGBA value is persisted through the existing URDF configuration model. Individual Links
  can still be overridden with the existing material ID, RGBA fields, or color picker.
- Added complete temporary-body collision previews for every collision strategy. Box, cylinder,
  sphere, component-box, and convex-hull strategies now create visible SolidWorks BREP/sheet
  bodies instead of relying on document line style or external STL re-import.
- Added a faceted convex-hull preview built from the same in-memory Link-local vertices and
  triangles used by the convex-hull STL writer. SolidWorks sews the triangle sheets when possible
  and safely displays the sheets when sewing is unavailable.
- Added Live SolidWorks 2023 coverage for all eight collision strategies. The test verifies that
  every strategy creates bounded temporary geometry while the source component remains hidden,
  preserves component appearance, restores mixed component visibility, and cleans up every
  temporary body.

### Changed

- Removed texture-image editing from both assembly and part exporter pages. Existing serialized
  texture metadata remains readable and exportable for backward compatibility, but normal edits no
  longer overwrite it through a hidden field.
- Reframed the material-name field as the URDF material ID. Selecting a built-in ID now visibly
  drives the corresponding RGBA values in both assembly and part exporters; custom IDs keep the
  current RGBA.
- The Simplified Chinese PropertyManager now explains every URDF Joint type while preserving the
  canonical English value used for configuration persistence and export.
- The Link child-count field now explicitly identifies direct, next-level child Links and explains
  that deeper descendants are not included.
- Box and per-component box previews now use solid orange-red temporary bodies, matching the
  established cylinder and sphere preview behavior and remaining distinguishable in wireframe,
  hidden-line, and shaded views.
- VisualMesh and AccurateMesh previews use copied SolidWorks bodies as temporary display geometry.
  SimplifiedMesh uses the same non-destructive CAD-shape preview but now explicitly states that the
  final STL is generated with coarser tessellation tolerances and can contain fewer facets.
- Made the Live collision regression self-contained: it can start SolidWorks, open the four-wheel
  example, run the API checks, close the test document, and exit the test-owned process.
- Updated release workflow tests to enforce draft-only GitHub candidates. Online publication still
  requires the maintainer's manual SolidWorks validation and explicit approval.

### Fixed

- Prevented inertia and collision temporary-body previews from selecting a top-level subassembly
  as the SolidWorks `Display3` host. Preview host resolution now requires a visible top-level part
  instance, avoiding `Display3` error code 3 when Link components are nested in production
  subassemblies.
- Restored each Link component branch to its pre-export visible or hidden state after STL/3DXML
  generation instead of unconditionally hiding the exported components.
- Avoided direct per-component `Component2.Visible` writes during cleanup, which can block older or
  nested SolidWorks assemblies. Visibility restoration now uses the native batched Show/Hide path
  and always clears the temporary selection.
- Removed the obsolete collision-wireframe construction path so preview ownership, transforms,
  display, hiding, and COM release all follow one temporary-body lifecycle.
- Updated stale installer assertions for the pinned Inno Setup 6.3.0-6.3.3 toolchain and the
  draft-candidate release workflow.

## 2026-08-27

### Commit traceability

- `e8a1a41` added the solid cylinder collision preview; `6279afb` hardened temporary-preview
  lifecycle tests; `ab53da0` restored SolidWorks collision/inertia overlays.
- `c5e7a85`, `8f63475`, and `add310e` corrected SolidWorks frame conversion, fitted primitive
  collision geometry from selected bodies, and repaired the Link inertia layout.
- `937d8f0` added the live export validation workflow; `56dad5f` kept export progress above
  SolidWorks.
- `51638f1`, `47db07e`, `87ff0aa`, and `6bddcd9` made installer payloads inspectable and restored the
  rule that online publication requires explicit maintainer approval after manual validation.

### Added

- Added live SolidWorks collision overlays for Link-local box, cylinder, sphere, and per-component
  box strategies. Collision wireframes can remain visible with the COM/inertia preview, and an
  active overlay refreshes immediately when the user changes strategy.
- Added automatic SolidWorks component/document appearance loading, including valid texture image
  paths, while preserving explicit URDF material, RGBA, and texture overrides made by the user.
- Added a non-reentrant export progress window and a completion summary with changed file count,
  total size, elapsed time, and output root. File statistics use pre/post export snapshots instead
  of timestamp tolerances.

### Changed

- Replaced temporary solid collision-preview bodies with lightweight line and circle wire bodies,
  avoiding preview-side appearance mutations and reducing SolidWorks graphics side effects.
- Added an explicit COM marker to the inertia preview and kept the selected SolidWorks components at
  their original appearance so CAD, collision, COM, and inertia overlays can be compared directly.
- Tightened the Link page DPI layout so the Link-frame selector, inertia units, collision preview,
  and color controls remain separated at scaled desktop resolutions.

### Fixed

- Fixed collision and inertia previews that reported success but remained invisible in assembly
  documents. Temporary bodies now use a visible top-level component as their SolidWorks display
  context and are transformed from the Link frame into that component's local frame.
- Kept primitive collision previews visible when the Link's own component is hidden. The live
  SolidWorks regression now exercises box, cylinder, and sphere previews against a hidden Link
  component without changing the component's appearance.
- Rejected visible display anchors that do not expose a valid component transform instead of
  silently treating a missing transform as the assembly identity.
- Linked built-in material presets to their RGBA values and URDF material IDs. Explicitly choosing
  a color preset now clears an older texture that would otherwise override the selected color.
- Released temporary preview transforms and the inertia preview Modeler COM reference after use.
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
- Isolated export-progress observers from the core export transaction so a UI reporting failure
  cannot turn a completed package write into an export failure.

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
