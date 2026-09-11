# STL Reducer Worker

Self-contained binary-STL worker for embedded Python 3.11.9, NumPy and
PyMeshLab 2025.7.post1. No imports from experimental scripts, Pillow, COM,
installation, or dependency discovery. The parent owns runtime packaging.

```text
python reduce_stl.py --request <request.json> --result <result.json>
```

## Protocol

UTF-8 request (a BOM is accepted):

```json
{
  "schemaVersion": 1,
  "input": "C:/private/source.stl",
  "output": "C:/private/candidate.stl",
  "ratio": 1.0,
  "timeoutSeconds": 90,
  "maxError": 0.0005,
  "relativeError": 0.005
}
```

Input/output must be absolute paths. Their parent directories and the result
parent directory must exist. Output must not exist, including a dangling symlink.
Input, output, request and result cannot alias protected files. Existing result
JSON may be atomically replaced. Source is only opened for reading.

`ratio` is the maximum fraction of source triangles to remove, not a keep
fraction. The budget applies to the whole file: reducible regions may compensate
for retained regions, without exceeding the total requested removal. Actual
removal can be smaller. Zero produces a byte-identical copy;
one never produces an empty region or deletes a source part. A region retains
at least 12 faces when reduced. The time/error defaults are those shown above;
zero seconds means no QEM attempts. Error limits must be positive and finite,
and use source coordinate units (STL itself does not specify units).

Result fields, all always present:

```json
{
  "schemaVersion": 1,
  "sourceSha256": "lowercase SHA-256 of the original file",
  "ratio": 1.0,
  "originalTriangles": 128,
  "finalTriangles": 32,
  "originalBytes": 6484,
  "finalBytes": 1684,
  "status": "reduced",
  "warning": "",
  "regionCount": 1,
  "processedRegions": 1,
  "retainedRegions": 0,
  "algorithmVersion": "partition-qem-1.1.0"
}
```

Exit 0 means `reduced` or `unchanged`; exit 1 means `failed`. Invalid CLI syntax
uses argparse exit 2. Failures have zero final counts/bytes, an English warning,
and any source metadata acquired before failure. A missing/unreadable request
or input still generates failure JSON when the result location is writable and
safe. An unsafe/unwritable result path cannot receive a result; check exit status
and stderr. Never infer success from an old result file after a nonzero exit.

`regionCount` includes tiny, attributed and degenerate retained regions; all
degenerate records form one raw group. `processedRegions` counts eligible
regions whose validation/reduction was started, including rejected/interrupted
ones. `retainedRegions` counts every region copied in full, including unprocessed
ones. It is not necessarily `regionCount - processedRegions`.

Stdout contains only JSONL progress records:

```json
{"type":"progress","stage":"reduce","completed":1,"total":12}
```

Stages are `read`, `partition`, `reduce`, `verify`, `done`; stages may be absent
on no-op/failure paths. Progress totals are stage-local. MeshLab Python/native
stdout is redirected to stderr, including dependency-import diagnostics.

## Safety and Quality

- Adjacency joins faces only across exactly two oppositely directed edges.
  Original degenerate faces are excluded from adjacency but participate in
  shared-point ownership. Every source face is accounted for exactly once.
- Degenerate groups, regions of at most 16 faces, and regions containing any
  nonzero attribute word retain all original 50-byte records. Unchanged output
  retains the complete original file, including source order and its header.
- Every face incident to a point shared between regions is frozen. Selected-only
  QEM can reduce the remaining interior even when the global STL is ambiguous.
  The candidate must independently pass local topology checks; there is no
  assumption that the complete STL is manifold.
- Normalized QEM uses quality 0.3, topology/boundary/normal protection and
  `autoclean=False`. At most five native calls per region, including alternate
  planar QEM and failed/passed target bisection. Round-robin retries prevent one
  difficult region from consuming all attempts before other regions are tried.
  Each candidate is measured against its original region and charged to the
  remaining whole-file removal budget. Small/frozen regions are not deleted.
- Validate serialized float32 vertices: finite/nondegenerate geometry, consistent
  winding, no duplicate faces or nonmanifold edges, unchanged Euler characteristic,
  connectivity, boundary-component count and closed/open state, bounded AABB
  changes, and exact oriented shared-interface triangles including multiplicity
  and full original records. Unchanged surviving triangles retain original records.
- All source and candidate triangles supply seven deterministic samples each:
  vertices, edge midpoints and centroid. Both point-to-triangle directions must
  meet `min(maxError, regionDiagonal * relativeError)`. Closed regions with
  meaningful signed volume must remain within 2% of their original volume.
- Coverage, original raw retention, output record counts, exact byte length and
  staged bytes are verified before publication. Source SHA-256 is rechecked
  before and immediately after candidate publication. No source parts are deleted.

## Parent Integration

There is no schema change from the requested version-1 contract. Two filesystem
entries cannot be published as one atomic transaction: candidate publication is
atomic and no-clobber, then result JSON is atomically replaced **last** as the
completion marker. Publication uses a same-directory temporary file and hard
link, requiring a filesystem supporting hard links (such as NTFS). Unsupported
filesystems fail conservatively; the worker never falls back to overwriting output.
On a handled publication failure it removes only its own candidate, not a file
created by another writer. Temporary files are removed on handled exit.

The parent must use unique private paths, prevent concurrent source writers, and
accept a candidate only after exit 0 plus matching successful result metadata.
After cancellation/hard timeout, discard that private job's candidates, results
and pending files. A process kill can occur between candidate and result publication.
There is no cross-process source lock; hash checks cannot prevent a write after
the final check. Source-to-export replacement remains the parent's responsibility.

`timeoutSeconds` is a soft budget covering setup and reduction. Partitioning and
final coverage/hash/publication work always finish to preserve completeness.
The worker checks time between candidate stages and native calls, retains all
unprocessed regions, and discards a candidate whose validation exceeded budget.
One native call can overrun or hang; the parent must enforce a hard timeout.

This is sampled error validation, not a strict Hausdorff or global self-intersection
proof. It does not establish collision/physics fidelity, compute inertia, or
guarantee the prior offline base-link size of approximately 10.8 MB within 90 seconds.
No real SolidWorks test or embedded CPython 3.11 packaging validation is claimed.

## Tests

Run with the existing bundled Python 3.12 development executable:

```text
<bundledpy312> -B tools/mesh_reduction/tests/test_reduce_stl.py -v
```

Only tests add the existing `.codex-build/mesh-lab-deps` to their import path.
They use actual PyMeshLab, synthetic meshes, and temporary directories, and do
not install packages or modify builds. Production uses the packaged environment's
normal NumPy/PyMeshLab imports, independently of the development runtime path.
