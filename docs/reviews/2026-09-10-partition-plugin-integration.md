# Partitioned STL Reduction Integration

## Scope

- The existing ratio remains the fraction of triangles to remove. The UI shows
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

## NEO Measurement

Source: `NEO/ROS2/osracer_description/meshes/visual/base_link.STL`.
SHA256: `be0f947057b60a85cd6aa0cb033d394d8c4eef76c75401aefed7bd0603ff6c51`.

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

Other original NEO STLs were processed through the parent bridge at 67% with
the earlier 160-second budget. All ten distinct visual files remained nonempty
and did not grow; original files were unchanged. Do not treat this as coverage
of every collision strategy or as a complete simulator acceptance test.

## Installation And Distribution

Installation and live SolidWorks acceptance are recorded after packaging; the
checks above alone do not establish that the installed plugin was exercised.
This is a local test build, not a public release. Exact GPL Corresponding Source
availability and clean-Windows VC runtime provisioning remain public-release
prerequisites; see `THIRD_PARTY_LICENSES/MESH-REDUCTION-SOURCE-NOTICE.txt`.
