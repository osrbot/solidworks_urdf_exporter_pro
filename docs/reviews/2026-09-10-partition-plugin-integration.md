# Partitioned STL Reduction Integration

## Scope

- The UI label remains "精简比例". The ratio is the fraction of triangles to remove, not
  a guaranteed file-size reduction. The UI shows
  target remaining STL size; unknown source sizes are not presented as MB estimates.
- Visual STL and SimplifiedMesh collision use the same ratio. AccurateMesh,
  primitive fitting and VisualMesh copying retain their existing meanings.
- An isolated, pinned CPython 3.11.9/PyMeshLab worker reduces connected regions,
  preserves shared interface records and verifies serialized geometry against
  source regions. The whole-file triangle budget can be shared across regions.
- The parent verifies result identity/counts/size before atomic replacement,
  limits its content cache to 64 MiB and rejects estimated working sets above
  2 GiB. The default helper hard limit is 300 seconds, with a 280-second soft
  budget. Soft expiry retains validated partial work; a hard kill keeps the
  complete original STL. This is not a SolidWorks-wide cancel feature.
- Result dialogs include measured per-Link original/final STL sizes, separately
  from warnings. Mass, COM and inertia are not recalculated from reduced meshes.

## Verification Before Installation

- 39 worker tests passed using the exact embedded Python 3.11.9 runtime with
  NumPy 2.2.6 and PyMeshLab 2025.7.post1. Includes binary/unicode protocol,
  shared interfaces, protected records, whole-file budgeting and bounded retries.
- 18 parent boundary tests passed: no-op, missing runtime, cache input identity,
  malformed/overflowed results, excess removal, timeout and cancellation.
- 126 mesh policy/UI/report tests and 13 export diagnostics tests passed.
- Independent review found malformed JSON conversion escaping fallback and a
  missing final cancellation check. Both were corrected before packaging.

## Earlier Example Assembly File-Level Measurement (67%)

This measurement used a large Link from an example assembly. Source identity and
hashes remain in the local audit, not in public documentation.

| Setting | Faces | Bytes | Time |
| --- | ---: | ---: | ---: |
| Source | 642074 | 32103784 | - |
| 67% target, 160s soft budget | 281110 | 14055584 | 163.594s |
| Same content/settings cache hit | 281110 | 14055584 | 0.638s |
| 67% target, 280s soft budget | 217960 | 10898084 | 282.069s |

The longer run retains about 34% of source bytes, close to but not exactly the
33% target. It is 10.90 decimal MB, not 10 MiB. Both runs reported soft-budget
and protected/rejected-region warnings. Original source hashes were unchanged.
The 0.638s cache number belongs to the 160-second configuration, not a measured
cache hit for the longer run. This is file-level reduction time, not total
SolidWorks export time or viewer loading time.

Other original example assembly STLs were processed through the parent bridge at 67% with
the earlier 160-second budget. All ten distinct visual files remained nonempty
and did not grow; original files were unchanged. Do not treat this as coverage
of every collision strategy or as a complete simulator acceptance test.

## User Export And Independent Audit (70%)

The user completed the example assembly export with commit `5656c5d77e18`, 70% requested
removal and coarse SolidWorks tessellation. The export report records four successful
targets (ROS 1, ROS 2, OpenUSD and MuJoCo), zero failed targets and 8m38s total.
This is user-performed plugin export validation, separate from the earlier file-level tests.
The independent audit inspected existing output; it did not re-export or modify the assembly.
Evidence is recorded in the local audit's `README.md` and `audit.json`; model names, local paths
and comparison images are not reproduced here.

| Visual STL | Original bytes | Final bytes | Decimal MB, original -> final | Size reduction |
| --- | ---: | ---: | --- | ---: |
| Large Link | 32,103,784 | 10,638,284 | 32.10 -> 10.64 | 66.86% |
| Complex smaller Link | 6,361,734 | 4,331,934 | 6.36 -> 4.33 | 31.91% |
| All visual STLs | 41,354,290 | 16,203,690 | 41.35 -> 16.20 | 60.82% |

Totals use this run's pre-reduction manifest statistics. Several smaller source Links differ
from older example assembly inputs, so an old-folder comparison would be misleading. Eight Links did not
reach 70% removal; only two smaller Links approximately reached it. The remaining visual size
is 39.18%, not 30%. This is partial reduction under safeguards, not a geometry export failure.
Mass, center of mass and inertia are not recalculated from reduced meshes.

Independent checks established:

- ROS 1 and ROS 2 URDFs are byte-identical, with 10 Links, 9 joints, 10 visuals and
  10 collisions each (six meshes and four native cylinders). Mesh references and checksums
  passed. Corresponding canonical STLs across all four targets have identical hashes.
- Installed official MuJoCo `testspeed` reloaded `robot.xml` and `scene.xml` and ran
  one zero-control step each, both exit 0. This is not long-horizon simulation or training validation.
- Installed OpenUSD structurally reopened the stage with 17 dependent layers, 16 nonempty
  meshes and no unresolved dependencies. Isaac Sim was not run.
- Fixed-camera comparisons of the large Link and complex smaller Link showed no obvious missing geometry. Their
  vertex-connected component counts remained 573/990; boundary-component, nonmanifold-edge,
  duplicate-face and degenerate-face counts were unchanged. Existing source defects remain.
  Maximum AABB-coordinate changes were 0.1044 mm and 0.000108 mm respectively. These are not
  strict Hausdorff-distance or global self-intersection checks.
- All inspected source export file hashes were unchanged.

The export log attributes 196.895s to inertia validation, 161.215s to large Link visual
reduction and 27.618s to complex smaller Link visual reduction. Collision reduction reused matching
results in 0.355s and 0.093s respectively. SolidWorks STL SaveAs calls still total 67.375s;
The large Link was tessellated twice. Reduction caching does not eliminate the second CAD save.

### Known Reporting Issues

- `mesh_manifest.csv` retains `collision_exists=true` and staging STL sizes for the four
  native cylinder wheels. The delivered package intentionally has no corresponding collision
  STLs; the readable export report correctly says false. This CSV/final-package mismatch is
  not a missing URDF wheel or a geometry failure.
- Top-level warnings repeat large Link and complex smaller Link messages without visual/collision labels
  and remain in English. Repeated messages should not be counted as separate geometry failures.

## Installation And Distribution

On 2026-09-11, read-only inspection of the installed `SW2URDF.dll` found file version
`1.6.0.0` and product version `5656c5d77e18`. The recorded installer is
`sw2urdfSetup_20260910_5656c5d.exe`; its provenance names commit
`5656c5d77e18faf56d76309af0e5fa83f707b125`.
The earlier agent-initiated GUI installation attempt was cancelled at UAC. The observed
installed version and subsequent user-performed export do not establish that this attempt
succeeded. Installation state, user export validation and independent output checks are
separate evidence.

This evidence concerns the tested local build; it does not itself establish publication or
acceptance of a later beta installer. Exact GPL Corresponding Source
availability and clean-Windows VC runtime provisioning remain public-release
prerequisites; see `THIRD_PARTY_LICENSES/MESH-REDUCTION-SOURCE-NOTICE.txt`.
