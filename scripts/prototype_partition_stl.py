"""Offline-only conservative STL partition/reduction/reassembly experiment.

No production export entry point calls this script. Needs numpy, a Test-build
SW2URDF.dll and Windows PowerShell. Original files are read-only inputs.
"""

import argparse
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess
import time

import numpy as np

from inspect_stl_reduction_corpus import STL_DTYPE, topology


def digest(data):
    return hashlib.sha256(data).hexdigest()


def parse(data):
    if len(data) < 84:
        raise ValueError('Truncated binary STL')
    count = struct.unpack_from('<I', data, 80)[0]
    if not count or len(data) != 84 + count * 50:
        raise ValueError('Prototype accepts nonempty binary STL only')
    records = np.frombuffer(data, dtype=STL_DTYPE, offset=84)
    if not np.isfinite(records['v']).all():
        raise ValueError('Nonfinite input geometry')
    return records


def encode(records, header=b'Partition reduction offline prototype'):
    return header[:80].ljust(80, b'\0') + struct.pack('<I', len(records)) + records.tobytes()


def partition(records):
    xyz = records['v'].astype(float)
    valid = np.linalg.norm(np.cross(xyz[:, 1] - xyz[:, 0], xyz[:, 2] - xyz[:, 0]), axis=1) > 0
    points, indices = np.unique(xyz.reshape(-1, 3), axis=0, return_inverse=True)
    all_faces = indices.reshape(-1, 3)
    faces = all_faces[valid]
    count = len(faces)
    labels = np.full(len(records), -1, dtype=np.int64)
    if not count:
        return labels, np.zeros(0, dtype=bool), points, all_faces, np.zeros(len(points), dtype=bool)
    directed = np.concatenate([faces[:, [0, 1]], faces[:, [1, 2]], faces[:, [2, 0]]])
    face_ids = np.tile(np.arange(count), 3)
    _, edge_ids, multiplicity = np.unique(np.sort(directed, axis=1), axis=0, return_inverse=True, return_counts=True)
    order = np.argsort(edge_ids, kind='stable')
    offsets = np.concatenate([[0], np.cumsum(multiplicity)])
    two = np.flatnonzero(multiplicity == 2)
    first, second = order[offsets[two]], order[offsets[two] + 1]
    opposite = directed[first, 0] == directed[second, 1]
    parent = np.arange(count)

    def root(v):
        while parent[v] != v:
            parent[v] = parent[parent[v]]
            v = parent[v]
        return v

    # Only connect unambiguous, consistently wound neighboring triangles.
    for a, b in zip(face_ids[first[opposite]], face_ids[second[opposite]]):
        a, b = root(a), root(b)
        if a != b:
            parent[b] = a
    _, patch_ids = np.unique([root(i) for i in range(count)], return_inverse=True)
    labels[valid] = patch_ids
    ambiguous = multiplicity > 2
    ambiguous[two[~opposite]] = True
    touched = np.unique(patch_ids[face_ids[ambiguous[edge_ids]]])
    eligible = np.ones(patch_ids.max() + 1, dtype=bool)
    eligible[touched] = False
    if np.any(~valid):
        # Degenerate records can obscure original edge incidence; protect neighbors.
        degenerate_vertices = np.unique(all_faces[~valid])
        neighbors = np.any(np.isin(faces, degenerate_vertices), axis=1)
        eligible[np.unique(patch_ids[neighbors])] = False

    # Point contacts also matter: a movable vertex in one patch may touch another.
    lowest = np.full(len(points), len(eligible), dtype=np.int64)
    highest = np.full(len(points), -2, dtype=np.int64)
    np.minimum.at(lowest, all_faces.ravel(), np.repeat(labels, 3))
    np.maximum.at(highest, all_faces.ravel(), np.repeat(labels, 3))
    return labels, eligible, points, all_faces, lowest != highest


def candidate_check(original, candidate, required_points, interface_faces=None):
    a, b = original['v'].astype(float), candidate['v'].astype(float)
    if len(candidate) == 0 or len(candidate) >= len(original) or np.any(candidate['attr']):
        raise ValueError('Invalid reduced count or attributes')
    if not np.isfinite(candidate['normal']).all():
        raise ValueError('Nonfinite output normals')
    if np.any(np.linalg.norm(np.cross(b[:, 1] - b[:, 0], b[:, 2] - b[:, 0]), axis=1) == 0):
        raise ValueError('New degenerate triangles')
    if required_points:
        actual = {tuple(v) for v in b.reshape(-1, 3)}
        if not required_points.issubset(actual):
            raise ValueError('A point shared with another patch moved or disappeared')
    if interface_faces is not None and len(interface_faces):
        # Preserve oriented interface triangles, including their multiplicity.
        actual_faces = Counter(tuple(map(tuple, triangle)) for triangle in b)
        required_faces = Counter(tuple(map(tuple, triangle)) for triangle in interface_faces)
        if required_faces - actual_faces:
            raise ValueError('Triangles incident to a shared interface changed')
    before, after = topology(a), topology(b)
    if after['nonmanifold_edges'] or after['duplicate_faces']:
        raise ValueError('New invalid topology')
    if before['components'] != after['components'] or before['boundary_components'] != after['boundary_components']:
        raise ValueError('Patch connectivity changed')
    lo, hi = a.min(axis=(0, 1)), a.max(axis=(0, 1))
    tolerance = np.linalg.norm(hi - lo) * 0.005
    if max(np.max(np.abs(b.min(axis=(0, 1)) - lo)), np.max(np.abs(b.max(axis=(0, 1)) - hi))) > tolerance:
        raise ValueError('Bounds moved outside the reducer tolerance')
    return after


def run(source, output, assembly, ratio):
    if not np.isfinite(ratio) or not 0 <= ratio <= 1:
        raise ValueError('Ratio must be finite and between 0 and 1')
    started = time.perf_counter()
    source, output, assembly = source.resolve(), output.resolve(), assembly.resolve()
    if output == source.parent or source.parent in output.parents:
        raise ValueError('Output must be outside the source directory')
    output.mkdir(parents=True, exist_ok=False)
    data = source.read_bytes()
    source_hash = digest(data)
    records = parse(data)
    if ratio == 0:
        (output / source.name).write_bytes(data)
        return {'source': str(source), 'unchanged': True, 'triangles': len(records)}
    labels, eligible, points, faces, shared = partition(records)
    face_order = np.argsort(labels, kind='stable')
    assert np.array_equal(np.sort(face_order), np.arange(len(records)))
    np.save(output / 'source-face-order.npy', face_order)
    cut = np.flatnonzero(np.diff(labels[face_order])) + 1
    groups = np.split(face_order, cut)
    (output / 'input-patches').mkdir()
    (output / 'output-patches').mkdir()
    chunks, jobs = [], []
    for ids in groups:
        label = int(labels[ids[0]])
        raw = records[ids]
        reason = 'degenerate_retained' if label < 0 else 'ambiguous_retained' if not eligible[label] else 'tiny_retained' if len(ids) <= 16 else 'attributes_retained' if np.any(raw['attr']) else 'candidate'
        item = {'id': label, 'original_triangles': len(ids), 'source_record_hash': digest(raw.tobytes()), 'status': reason}
        if reason == 'candidate':
            item['input'] = str(output / 'input-patches' / f'{label}.stl')
            item['output'] = str(output / 'output-patches' / f'{label}.stl')
            Path(item['input']).write_bytes(encode(raw))
            jobs.append(item)
        chunks.append(item)
    manifest = output / 'manifest.json'
    manifest.write_text(json.dumps({'source': str(source), 'source_hash': source_hash, 'ratio': ratio, 'chunks': chunks, 'jobs': jobs}, indent=2), encoding='utf-8')
    print(f'{source.name}: {len(chunks)} regions; {len(jobs)} eligible for reduction', flush=True)
    results_path = output / 'batch-results.jsonl'
    host = Path(os.environ['SystemRoot']) / 'System32/WindowsPowerShell/v1.0/powershell.exe'
    worker = Path(__file__).with_name('Invoke-StlPartitionBatch.ps1').resolve()
    if jobs:
        subprocess.run([str(host), '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', str(worker),
            '-AssemblyPath', str(assembly), '-Manifest', str(manifest), '-Results', str(results_path), '-Ratio', str(ratio)],
            check=True, timeout=750, creationflags=subprocess.CREATE_NO_WINDOW)
    results = {row['id']: row for row in (json.loads(line) for line in results_path.read_text(encoding='utf-8-sig').splitlines())} if jobs else {}
    if len(results) != len(jobs) or set(results) != {job['id'] for job in jobs}:
        raise ValueError('Incomplete batch results')
    final_path = output / source.name
    staging_path = output / (source.name + '.pending')
    final_count, reduced_chunks, original_accounted = 0, 0, 0
    output_offsets = []
    with staging_path.open('xb') as handle:
        handle.write(data[:80] + b'\0' * 4)
        for item, ids in zip(chunks, groups):
            original = records[ids]
            original_accounted += len(ids)
            chosen = original
            if item['status'] == 'candidate':
                result = results[item['id']]
                item['status'], item['warning'] = result['status'], result['warning']
                if result['status'] == 'reduced':
                    try:
                        candidate = parse(Path(item['output']).read_bytes())
                        vertex_ids = np.unique(faces[ids])
                        required = {tuple(v) for v in points[vertex_ids[shared[vertex_ids]] ]}
                        interface = original['v'][np.any(shared[faces[ids]], axis=1)]
                        item['topology'] = candidate_check(original, candidate, required, interface)
                        chosen = candidate
                        reduced_chunks += 1
                    except (ValueError, AssertionError) as error:
                        item['status'], item['warning'] = 'postcheck_retained', str(error)
            item['final_triangles'] = len(chosen)
            item['output_record_hash'] = digest(chosen.tobytes())
            item['retained_exactly'] = chosen is original
            if chosen is original:
                assert item['source_record_hash'] == item['output_record_hash']
            output_offsets.append((final_count, len(chosen)))
            handle.write(chosen.tobytes())
            final_count += len(chosen)
        handle.seek(80)
        handle.write(struct.pack('<I', final_count))
    assert original_accounted == len(records)
    merged_data = staging_path.read_bytes()
    merged = parse(merged_data)
    assert len(merged) == final_count <= len(records)
    for item, (offset, count) in zip(chunks, output_offsets):
        assert digest(merged[offset:offset + count].tobytes()) == item['output_record_hash']
    if final_count == len(records):
        # A no-op must preserve the original file, not merely reordered triangles.
        staging_path.write_bytes(data)
    assert digest(source.read_bytes()) == source_hash
    staging_path.rename(final_path)
    report = {'source': str(source), 'output': str(final_path), 'source_hash': source_hash,
        'assembly_hash': digest(assembly.read_bytes()), 'source_unchanged': True,
        'ratio': ratio, 'original_triangles': len(records), 'final_triangles': final_count,
        'original_bytes': len(data), 'final_bytes': final_path.stat().st_size,
        'actual_removal': 1 - final_count / len(records), 'regions': len(chunks), 'eligible': len(jobs),
        'output_face_order': 'source' if final_count == len(records) else 'partition',
        'reduced_regions': reduced_chunks, 'region_statuses': dict(Counter(c['status'] for c in chunks)),
        'original_faces_accounted_once': original_accounted, 'seconds': time.perf_counter() - started,
        'chunks': chunks}
    (output / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'chunks'}, indent=2), flush=True)
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--assembly', type=Path, required=True)
    parser.add_argument('--ratio', type=float, default=0.5)
    args = parser.parse_args()
    if not np.isfinite(args.ratio) or not 0 <= args.ratio <= 1:
        parser.error('ratio must be finite and between 0 and 1')
    run(args.source, args.output, args.assembly, args.ratio)
