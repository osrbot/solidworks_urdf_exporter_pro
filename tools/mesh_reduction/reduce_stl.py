"""Private, conservative binary-STL reduction worker (Python 3.11+)."""

import argparse
from collections import Counter, defaultdict, deque
from contextlib import contextmanager
import hashlib
import json
import math
import os
from pathlib import Path
import struct
import sys
import tempfile
import time


ALGORITHM_VERSION = "partition-qem-1.1.0"
np = None
ml = None
STL_DTYPE = None


def load_numpy():
    global np, STL_DTYPE
    import numpy as numpy
    np = numpy
    STL_DTYPE = np.dtype([("normal", "<f4", (3,)),
                          ("v", "<f4", (3, 3)), ("attr", "<u2")])


def load_meshlab():
    global ml
    import pymeshlab
    ml = pymeshlab


def digest(data):
    return hashlib.sha256(data).hexdigest()


def parse(data):
    if len(data) < 84:
        raise ValueError("Truncated binary STL")
    count = struct.unpack_from("<I", data, 80)[0]
    if not count or len(data) != 84 + count * 50:
        raise ValueError("Expected a nonempty binary STL with exact record length")
    records = np.frombuffer(data, dtype=STL_DTYPE, offset=84)
    if not np.isfinite(records["v"]).all():
        raise ValueError("Nonfinite source coordinates")
    return records


def encode(records, header=b"STL reduction worker"):
    return header[:80].ljust(80, b"\0") + struct.pack("<I", len(records)) + records.tobytes()


class BudgetExpired(Exception):
    pass


def check_budget(deadline):
    if time.monotonic() >= deadline:
        raise BudgetExpired("Soft time budget reached")


def component_labels(pairs, count):
    parent = np.arange(count)
    rank = np.zeros(count, dtype=np.int8)

    def root(v):
        while parent[v] != v:
            parent[v] = parent[parent[v]]
            v = parent[v]
        return v

    for a, b in pairs:
        a, b = root(a), root(b)
        if a == b:
            continue
        if rank[a] < rank[b]:
            a, b = b, a
        parent[b] = a
        if rank[a] == rank[b]:
            rank[a] += 1
    return np.unique([root(i) for i in range(count)], return_inverse=True)[1]


def indexed(triangles):
    points, inverse = np.unique(triangles.reshape(-1, 3), axis=0, return_inverse=True)
    return points, inverse.reshape(-1, 3).astype(np.int32)


def edge_table(faces):
    directed = np.concatenate([faces[:, [0, 1]], faces[:, [1, 2]], faces[:, [2, 0]]])
    edges, inverse, counts = np.unique(np.sort(directed, axis=1), axis=0,
                                       return_inverse=True, return_counts=True)
    return directed, edges, inverse, counts


def partition(records):
    triangles = records["v"].astype(float)
    points, faces = indexed(triangles)
    valid = np.any(np.cross(triangles[:, 1] - triangles[:, 0],
                            triangles[:, 2] - triangles[:, 0]) != 0, axis=1)
    labels = np.full(len(records), -1, dtype=np.int64)
    good = faces[valid]
    if len(good):
        directed, _, edge_ids, counts = edge_table(good)
        order = np.argsort(edge_ids, kind="stable")
        offsets = np.concatenate([[0], np.cumsum(counts)])
        two = np.flatnonzero(counts == 2)
        first, second = order[offsets[two]], order[offsets[two] + 1]
        opposite = np.all(directed[first] == directed[second, ::-1], axis=1)
        face_ids = np.tile(np.arange(len(good)), 3)
        pairs = np.column_stack((face_ids[first[opposite]], face_ids[second[opposite]]))
        labels[valid] = component_labels(pairs, len(good))
    order = np.argsort(labels, kind="stable")
    groups = np.split(order, np.flatnonzero(np.diff(labels[order])) + 1)
    verify_coverage(groups, len(records))
    # Include degenerate records in shared-vertex ownership, never in adjacency.
    low, high = np.full(len(points), len(groups)), np.full(len(points), -2)
    np.minimum.at(low, faces.ravel(), np.repeat(labels, 3))
    np.maximum.at(high, faces.ravel(), np.repeat(labels, 3))
    frozen = np.any((low != high)[faces], axis=1)
    return groups, frozen, valid


def verify_coverage(groups, count):
    order = np.concatenate(groups)
    if not np.array_equal(np.sort(order), np.arange(count)):
        raise ValueError("Source-face partition coverage is not exact")


def topology(triangles):
    points, faces = indexed(triangles)
    directed, edges, _, counts = edge_table(faces)
    boundary = edges[counts == 1]
    if len(boundary):
        used, inverse = np.unique(boundary, return_inverse=True)
        boundary_components = len(np.unique(component_labels(inverse.reshape(-1, 2), len(used))))
    else:
        boundary_components = 0
    return {
        "euler": len(points) - len(edges) + len(faces),
        "components": len(np.unique(component_labels(edges, len(points)))),
        "boundaryComponents": boundary_components,
        "closed": not len(boundary),
        "nonmanifold": bool(np.any(counts > 2)),
        "badWinding": len(directed) != len(np.unique(directed, axis=0)),
        "duplicates": len(faces) != len(np.unique(np.sort(faces, axis=1), axis=0)),
    }


def oriented_key(triangle):
    vertices = tuple(map(tuple, triangle))
    return min(vertices, vertices[1:] + vertices[:1], vertices[2:] + vertices[:2])


def restore_original_records(original, candidate):
    # Exact surviving triangles retain their complete normal/attribute/vertex bytes.
    originals = defaultdict(deque)
    for i, triangle in enumerate(original["v"]):
        originals[oriented_key(triangle)].append(i)
    for i, triangle in enumerate(candidate["v"]):
        matches = originals.get(oriented_key(triangle))
        if matches:
            candidate[i] = original[matches.popleft()]


def samples(triangles):
    return np.concatenate([triangles.reshape(-1, 3),
                           (triangles[:, 0] + triangles[:, 1]) / 2,
                           (triangles[:, 1] + triangles[:, 2]) / 2,
                           (triangles[:, 2] + triangles[:, 0]) / 2,
                           triangles.mean(axis=1)])


def directed_distance(points, target, deadline=math.inf):
    check_budget(deadline)
    meshset = ml.MeshSet()
    vertices, faces = indexed(target)
    meshset.add_mesh(ml.Mesh(vertex_matrix=vertices, face_matrix=faces), "reference")
    # Unresolved queries must not silently become zero distance.
    meshset.add_mesh(ml.Mesh(vertex_matrix=points,
                            v_scalar_array=np.full(len(points), 1e30)), "samples")
    meshset.compute_scalar_by_distance_from_another_mesh_per_vertex(
        measuremesh=1, refmesh=0, signeddist=False, maxdist=ml.PercentageValue(100))
    check_budget(deadline)
    values = meshset.mesh(1).vertex_scalar_array()
    if (len(values) != len(points) or not np.isfinite(values).all()
            or np.any(values < 0) or np.any(values >= 1e29)):
        raise ValueError("Invalid or unresolved surface distance")
    return float(values.max())


def validate(original, candidate, frozen, max_error, relative_error,
             deadline=math.inf, before=None):
    check_budget(deadline)
    if not 0 < len(candidate) < len(original) or np.any(candidate["attr"]):
        raise ValueError("Invalid candidate count or attributes")
    a, b = original["v"].astype(float), candidate["v"].astype(float)
    if not np.isfinite(b).all() or not np.isfinite(candidate["normal"]).all():
        raise ValueError("Nonfinite candidate geometry")
    cross = np.cross(b[:, 1] - b[:, 0], b[:, 2] - b[:, 0])
    if np.any(np.all(cross == 0, axis=1)):
        raise ValueError("New degenerate triangles")
    if np.any(np.einsum("ij,ij->i", cross, candidate["normal"]) <= 0):
        raise ValueError("Candidate normal disagrees with winding")
    if np.any(frozen):
        actual = Counter(oriented_key(t) for t in candidate["v"])
        required = Counter(oriented_key(t) for t in original["v"][frozen])
        if required - actual:
            raise ValueError("Oriented shared-interface triangles changed")
        actual_raw = Counter(r.tobytes() for r in candidate)
        if Counter(r.tobytes() for r in original[frozen]) - actual_raw:
            raise ValueError("Shared-interface records were not retained exactly")
    before = topology(a) if before is None else before
    after = topology(b)
    if any(after[k] for k in ("nonmanifold", "badWinding", "duplicates")):
        raise ValueError("Invalid candidate topology or winding")
    if any(before[k] != after[k] for k in ("euler", "components", "boundaryComponents", "closed")):
        raise ValueError("Region topology changed")
    lo, hi = a.min(axis=(0, 1)), a.max(axis=(0, 1))
    scale = float(np.linalg.norm(hi - lo))
    tolerance = min(scale * relative_error, max_error)
    bounds_error = max(np.max(np.abs(b.min(axis=(0, 1)) - lo)),
                       np.max(np.abs(b.max(axis=(0, 1)) - hi)))
    if bounds_error > tolerance:
        raise ValueError("Region bounds exceeded the error limit")
    center = (lo + hi) / 2
    # Normalize distance queries as well as QEM; verify serialized float32 geometry.
    an, bn = (a - center) / scale, (b - center) / scale
    for source, target in ((an, bn), (bn, an)):
        if directed_distance(samples(source), target, deadline) * scale > tolerance:
            raise ValueError("Bidirectional seven-point surface error exceeded the limit")
    if before["closed"]:
        def volume(v):
            return float(np.einsum("ij,ij->i", v[:, 0],
                                  np.cross(v[:, 1], v[:, 2])).sum() / 6)
        va, vb = volume(an), volume(bn)
        if abs(va) > 1e-10 and abs(vb - va) / abs(va) > 0.02:
            raise ValueError("Closed-region volume changed by more than 2 percent")
    check_budget(deadline)


def decimate(original, target, frozen, planar):
    points, faces = indexed(original["v"].astype(float))
    center = (points.min(axis=0) + points.max(axis=0)) / 2
    scale = float(np.linalg.norm(np.ptp(points, axis=0)))
    if not scale > 0:
        raise ValueError("Zero-size region")
    normalized = (points - center) / scale
    meshset = ml.MeshSet()
    meshset.add_mesh(ml.Mesh(vertex_matrix=normalized, face_matrix=faces,
                            f_scalar_array=(~frozen).astype(float)))
    partial = bool(np.any(frozen))
    if partial:
        meshset.compute_selection_by_condition_per_face(condselect="fq > 0.5")
    meshset.meshing_decimation_quadric_edge_collapse(
        targetfacenum=target, targetperc=0.0, qualitythr=0.3,
        preserveboundary=True, preservenormal=True, preservetopology=True,
        optimalplacement=True, planarquadric=planar, planarweight=0.001,
        autoclean=False, selected=partial)
    mesh = meshset.current_mesh()
    candidate = np.zeros(mesh.face_number(), dtype=STL_DTYPE)
    reduced_points = mesh.vertex_matrix()
    restored_points = reduced_points * scale + center
    # Invert unchanged vertices exactly, including zero coordinates: a floating
    # normalization round trip is not generally the identity after float32 I/O.
    exact_points = {tuple(point): i for i, point in enumerate(normalized)}
    for i, point in enumerate(reduced_points):
        original_index = exact_points.get(tuple(point))
        if original_index is not None:
            restored_points[i] = points[original_index]
    candidate["v"] = restored_points[mesh.face_matrix()]
    triangles = candidate["v"].astype(float)
    cross = np.cross(triangles[:, 1] - triangles[:, 0], triangles[:, 2] - triangles[:, 0])
    length = np.linalg.norm(cross, axis=1)
    if not np.isfinite(triangles).all() or np.any(length == 0):
        raise ValueError("Invalid serialized candidate triangles")
    candidate["normal"] = cross / length[:, None]
    restore_original_records(original, candidate)
    return candidate


def candidate_targets(count, minimum):
    minimum = max(12, minimum)
    # Escalation anchors; once one succeeds, remaining levels bisect the bracket.
    return sorted({max(minimum, math.ceil(count * keep))
                   for keep in (0.15, 0.3, 0.5, 0.7, 0.85)
                   if max(minimum, math.ceil(count * keep)) < count})


def reduce_regions(records, groups, frozen, valid, request, deadline, progress):
    chosen = {}
    removal_budget = math.floor(len(records) * request["ratio"])
    removed = 0
    jobs = [i for i, ids in enumerate(groups)
            if len(ids) > 16 and valid[ids].all() and not np.any(records["attr"][ids])
            and not frozen[ids].all()]
    jobs.sort(key=lambda i: (-len(groups[i]), i))
    states = {}
    pending = deque(jobs)
    finished = 0
    expired = False
    progress("reduce", 0, len(jobs))
    if jobs and removal_budget and time.monotonic() < deadline:
        load_meshlab()
    # One attempt per queue visit lets easy regions finish before hard regions
    # consume their retries. All modes together get at most five native calls.
    while pending and removed < removal_budget:
        try:
            check_budget(deadline)
        except BudgetExpired:
            expired = True
            break
        i = pending.popleft()
        ids = groups[i]
        original, locked = records[ids], frozen[ids]
        count = len(original)
        best_count = len(chosen[i]) if i in chosen else count
        floor = max(12, int(np.count_nonzero(locked)))
        # Other accepted regions consume the budget; this region's existing
        # saving is already in best_count and must not be charged a second time.
        minimum = max(floor, best_count - (removal_budget - removed))
        if minimum >= best_count:
            finished += 1
            progress("reduce", finished, len(jobs))
            continue
        state = states.get(i)
        requeued = False
        try:
            if state is None:
                state = dict(attempts=0, lower=floor - 1, upper=None,
                             retryPlanar=False, lastTarget=None, before=None)
                states[i] = state
                before = topology(original["v"].astype(float))
                # Ambiguity elsewhere is irrelevant; validate this region alone.
                if any(before[k] for k in ("nonmanifold", "badWinding", "duplicates")):
                    raise ValueError("Source region is not locally valid")
                state["before"] = before
            if state["retryPlanar"]:
                target = max(minimum, state["lastTarget"])
            elif state["upper"] is not None:
                lower = max(state["lower"], minimum - 1)
                upper = min(state["upper"], best_count)
                if upper - lower <= 1:
                    continue
                target = (lower + upper) // 2
            else:
                targets = [t for t in candidate_targets(count, minimum)
                           if state["lower"] < t < best_count]
                if not targets:
                    continue
                target = targets[0]
            if target >= best_count or state["attempts"] >= 5:
                continue
            # Prefer consistent plain-QEM brackets; reserve a final alternate
            # for regions with no accepted result, or one retry for native stalls.
            planar = state["retryPlanar"] or (state["attempts"] == 4 and i not in chosen)
            state["retryPlanar"] = False
            state["lastTarget"] = target
            check_budget(deadline)
            state["attempts"] += 1
            retry = False
            try:
                candidate = decimate(original, target, locked, planar)
                check_budget(deadline)
                if len(candidate) < minimum:
                    raise ValueError("Candidate exceeds the whole-file removal budget or region floor")
                if len(candidate) >= best_count:
                    # A stalled native simplification gets one alternate mode,
                    # not four increasingly conservative targets with no gain.
                    state["retryPlanar"] = not planar
                    retry = not planar
                else:
                    validate(original, candidate, locked, request["maxError"],
                             request["relativeError"], deadline, state["before"])
                    removed += best_count - len(candidate)
                    chosen[i] = candidate
                    state["upper"] = target
                    retry = state["attempts"] > 1
            except ml.PyMeshLabException:
                state["retryPlanar"] = not planar
                retry = not planar
            except (ValueError, RuntimeError):
                state["lower"] = max(state["lower"], target)
                retry = True
            if retry and state["attempts"] < 5:
                pending.append(i)
                requeued = True
        except BudgetExpired:
            expired = True
        except (ValueError, RuntimeError, ml.PyMeshLabException):
            pass
        finally:
            if not requeued:
                finished += 1
            progress("reduce", finished, len(jobs))
        if expired:
            break
    actual_removed = sum(len(groups[i]) - len(candidate) for i, candidate in chosen.items())
    if removed != actual_removed or removed > removal_budget:
        raise ValueError("Whole-file removal budget accounting failed")
    rejected = sum(i not in chosen for i in states)
    return chosen, len(states), rejected, expired


def assemble(data, records, groups, chosen):
    verify_coverage(groups, len(records))
    if not chosen:
        return data
    parts, checks = [], []
    for i, ids in enumerate(groups):
        raw = records[ids].tobytes()
        part = chosen[i].tobytes() if i in chosen else raw
        if not part or len(part) % 50 or len(part) > len(raw):
            raise ValueError("Invalid region record count during assembly")
        checks.append((len(part), digest(part), None if i in chosen else raw))
        parts.append(part)
    count = sum(len(p) // 50 for p in parts)
    combined = data[:80] + struct.pack("<I", count) + b"".join(parts)
    if len(parse(combined)) != count or not 0 < count <= len(records):
        raise ValueError("Invalid assembled STL count")
    offset = 84
    for size, expected_hash, raw in checks:
        actual = combined[offset:offset + size]
        if digest(actual) != expected_hash or (raw is not None and actual != raw):
            raise ValueError("Region bytes were not retained during assembly")
        offset += size
    if offset != len(combined):
        raise ValueError("Assembled STL length mismatch")
    return combined


def same_path(a, b):
    if a.resolve() == b.resolve():
        return True
    return a.exists() and b.exists() and os.path.samefile(a, b)


def number(request, name, default=None, zero=False, upper=None):
    value = request.get(name, default)
    if (isinstance(value, bool) or not isinstance(value, (int, float))
            or not math.isfinite(value) or value < 0 or (not zero and value == 0)
            or (upper is not None and value > upper)):
        raise ValueError("Invalid request field: " + name)
    return float(value)


def validate_request(request, request_path, result_path):
    if not isinstance(request, dict) or type(request.get("schemaVersion")) is not int or request["schemaVersion"] != 1:
        raise ValueError("Expected request schemaVersion 1")
    for name in ("input", "output"):
        if not isinstance(request.get(name), str) or not Path(request[name]).is_absolute():
            raise ValueError("Request " + name + " must be an absolute path")
    source, output = Path(request["input"]), Path(request["output"])
    if same_path(source, output):
        raise ValueError("Source and output paths must differ")
    if os.path.lexists(output):
        raise ValueError("Output already exists")
    if any(same_path(result_path, p) for p in (source, output, request_path)):
        raise ValueError("Result path aliases a protected path")
    if same_path(output, request_path):
        raise ValueError("Output path aliases the request")
    if not output.parent.is_dir() or not result_path.parent.is_dir():
        raise ValueError("Output and result parent directories must already exist")
    request = dict(request)
    request["ratio"] = number(request, "ratio", zero=True, upper=1)
    request["timeoutSeconds"] = number(request, "timeoutSeconds", 90, zero=True)
    request["maxError"] = number(request, "maxError", 0.0005)
    request["relativeError"] = number(request, "relativeError", 0.005)
    return request, source, output


def stage_file(path, data):
    fd, name = tempfile.mkstemp(prefix="." + path.name + ".", suffix=".pending", dir=path.parent)
    temporary = Path(name)
    try:
        with os.fdopen(fd, "wb") as handle:
            handle.write(data)
            handle.flush()
            os.fsync(handle.fileno())
        if temporary.read_bytes() != data:
            raise ValueError("Staged file bytes failed verification")
        return temporary
    except BaseException:
        temporary.unlink(missing_ok=True)
        raise


def result_bytes(result):
    return (json.dumps(result, ensure_ascii=True, allow_nan=False, indent=2) + "\n").encode("utf-8")


def publish(source, output, result_path, data, result):
    pending_output = pending_result = None
    published = False
    try:
        pending_output = stage_file(output, data)
        pending_result = stage_file(result_path, result_bytes(result))
        if digest(source.read_bytes()) != result["sourceSha256"]:
            raise ValueError("Source changed while the worker was running")
        # A hard link is an atomic, no-clobber publication on the same filesystem.
        # The result JSON is published last and is the parent's completion marker.
        os.link(pending_output, output)
        published = True
        if not os.path.samefile(output, pending_output) or output.read_bytes() != data:
            raise ValueError("Published candidate bytes failed verification")
        if digest(source.read_bytes()) != result["sourceSha256"]:
            raise ValueError("Source changed during publication")
        os.replace(pending_result, result_path)
    except BaseException:
        if published and output.exists() and os.path.samefile(output, pending_output):
            output.unlink()
        raise
    finally:
        for temporary in (pending_output, pending_result):
            if temporary is not None:
                temporary.unlink(missing_ok=True)


def empty_result():
    return dict(schemaVersion=1, sourceSha256="", ratio=0.0, originalTriangles=0,
                finalTriangles=0, originalBytes=0, finalBytes=0, status="failed",
                warning="", regionCount=0, processedRegions=0, retainedRegions=0,
                algorithmVersion=ALGORITHM_VERSION)


def execute(request_path, result_path, progress):
    started = time.monotonic()
    result = empty_result()
    request = None
    try:
        request = json.loads(request_path.read_text(encoding="utf-8-sig"))
        request, source, output = validate_request(request, request_path, result_path)
        result["ratio"] = request["ratio"]
        deadline = started + request["timeoutSeconds"]
        progress("read", 0, 1)
        data = source.read_bytes()
        result.update(sourceSha256=digest(data), originalBytes=len(data))
        load_numpy()
        records = parse(data)
        result["originalTriangles"] = len(records)
        progress("partition", 0, 1)
        groups, frozen, valid = partition(records)
        result["regionCount"] = len(groups)
        progress("partition", 1, 1)
        chosen, processed, rejected, expired = {}, 0, 0, False
        if request["ratio"] > 0:
            chosen, processed, rejected, expired = reduce_regions(
                records, groups, frozen, valid, request, deadline, progress)
        progress("verify", 0, 1)
        combined = assemble(data, records, groups, chosen)
        final_count = len(parse(combined))
        if len(records) - final_count > math.floor(len(records) * request["ratio"]):
            raise ValueError("Final STL exceeds the requested maximum removal")
        warning = []
        if expired:
            warning.append("Soft time budget reached; unprocessed regions were retained exactly.")
        if rejected:
            warning.append("Some regions failed reduction or quality checks and were retained exactly.")
        if request["ratio"] > 0 and final_count == len(records):
            warning.append("No safe reduction was accepted; the output is byte-identical to the source.")
        result.update(finalTriangles=final_count, finalBytes=len(combined),
                      status="reduced" if chosen else "unchanged", warning=" ".join(warning),
                      processedRegions=processed, retainedRegions=len(groups) - len(chosen))
        publish(source, output, result_path, combined, result)
        progress("done", 1, 1)
        return 0
    except Exception as error:
        result.update(status="failed", finalTriangles=0, finalBytes=0,
                      warning="STL reduction failed: " + str(error))
        # A malicious/mistaken result path must never overwrite source or request.
        protected = [request_path]
        if isinstance(request, dict):
            protected += [Path(request[k]) for k in ("input", "output")
                          if isinstance(request.get(k), str) and request[k]]
        try:
            if any(same_path(result_path, p) for p in protected):
                raise ValueError("Cannot write failure result to a protected path")
            pending = stage_file(result_path, result_bytes(result))
            try:
                os.replace(pending, result_path)
            finally:
                pending.unlink(missing_ok=True)
        except Exception as reporting_error:
            print("Could not publish failure result: " + str(reporting_error), file=sys.stderr)
        return 1


@contextmanager
def protocol_stdout():
    # Capture fd 1 too: native MeshLab plugins bypass contextlib.redirect_stdout.
    sys.stdout.flush()
    saved = os.dup(1)
    stream = os.fdopen(os.dup(saved), "w", encoding="utf-8", buffering=1)
    try:
        os.dup2(2, 1)
        yield lambda stage, completed, total: stream.write(json.dumps(
            dict(type="progress", stage=stage, completed=completed, total=total)) + "\n")
    finally:
        sys.stdout.flush()
        # CLI-only: leave fd 1 on stderr through process teardown. A native CRT
        # may flush buffered diagnostics at exit, after this context has ended.
        os.close(saved)
        stream.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--request", required=True, type=Path)
    parser.add_argument("--result", required=True, type=Path)
    args = parser.parse_args()
    with protocol_stdout() as progress:
        return execute(args.request.resolve(), args.result.absolute(), progress)


if __name__ == "__main__":
    sys.exit(main())
