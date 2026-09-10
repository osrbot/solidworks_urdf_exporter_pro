"""Offline alternate QEM experiment; never used by the production exporter.

Uses the existing face-ownership manifest and falls back to the earlier validated
result. MeshLab is isolated in a workspace dependency directory. STL has no units;
--max-error is expressed in input coordinates (NEO exports are in meters).
"""

import argparse
from collections import Counter
import copy
import json
from pathlib import Path
import sys
import time

import numpy as np

from prototype_partition_stl import candidate_check, digest, encode, parse, STL_DTYPE

ml = None


def indexed(vertices):
    points, inverse = np.unique(vertices.reshape(-1, 3), axis=0, return_inverse=True)
    return points, inverse.reshape(-1, 3).astype(np.int32)


def measures(vertices):
    points, faces = indexed(vertices)
    directed = np.concatenate([faces[:, [0, 1]], faces[:, [1, 2]], faces[:, [2, 0]]])
    edges, counts = np.unique(np.sort(directed, axis=1), axis=0, return_counts=True)
    return {
        'euler': len(points) - len(edges) + len(faces),
        'boundary_edges': int(np.count_nonzero(counts == 1)),
        'nonmanifold_edges': int(np.count_nonzero(counts > 2)),
        'duplicate_directed_edges': len(directed) - len(np.unique(directed, axis=0)),
    }


def samples(vertices):
    # Deterministic seven samples per triangle, not randomized Hausdorff sampling.
    return np.concatenate([vertices.reshape(-1, 3),
        (vertices[:, 0] + vertices[:, 1]) / 2,
        (vertices[:, 1] + vertices[:, 2]) / 2,
        (vertices[:, 2] + vertices[:, 0]) / 2, vertices.mean(axis=1)])


def directed_distance(points, target):
    meshset = ml.MeshSet()
    vertices, faces = indexed(target)
    meshset.add_mesh(ml.Mesh(vertex_matrix=vertices, face_matrix=faces), 'reference')
    # Sentinel catches queries for which no reference surface was found.
    meshset.add_mesh(ml.Mesh(vertex_matrix=points,
        v_scalar_array=np.full(len(points), 1e30)), 'samples')
    meshset.compute_scalar_by_distance_from_another_mesh_per_vertex(
        measuremesh=1, refmesh=0, signeddist=False, maxdist=ml.PercentageValue(100))
    values = meshset.mesh(1).vertex_scalar_array()
    if len(values) != len(points) or not np.isfinite(values).all() or np.any(values < 0):
        raise ValueError('Invalid surface distance result')
    return float(values.max())


def validate(original, candidate, required, interface, max_error):
    candidate_check(original, candidate, required, interface)
    a, b = original['v'].astype(float), candidate['v'].astype(float)
    before, after = measures(a), measures(b)
    if before['euler'] != after['euler'] or after['duplicate_directed_edges']:
        raise ValueError('Euler characteristic or consistent winding changed')
    tolerance = min(float(np.linalg.norm(np.ptp(a, axis=(0, 1)))) * 0.005, max_error)
    ab = directed_distance(samples(a), b)
    if ab > tolerance:
        raise ValueError(f'Source surface distance {ab:.9g} exceeds {tolerance:.9g}')
    ba = directed_distance(samples(b), a)
    if ba > tolerance:
        raise ValueError(f'Candidate surface distance {ba:.9g} exceeds {tolerance:.9g}')
    volume_change = None
    if before['boundary_edges'] == 0:
        def volume(v):
            centered = v - a.mean(axis=(0, 1))
            return np.einsum('ij,ij->i', centered[:, 0],
                np.cross(centered[:, 1], centered[:, 2])).sum() / 6
        va, vb = volume(a), volume(b)
        if abs(va) > np.linalg.norm(np.ptp(a, axis=(0, 1))) ** 3 * 1e-10:
            volume_change = abs(vb - va) / abs(va)
            if volume_change > 0.02:
                raise ValueError('Closed patch volume changed by more than 2 percent')
    return {'sample_max_source_to_output': ab, 'sample_max_output_to_source': ba,
        'tolerance': tolerance, 'relative_volume_change': volume_change, 'topology': after}


def reduce(original, keep, planar, frozen=None, optimal=True):
    vertices, faces = indexed(original['v'].astype(float))
    center = (vertices.min(axis=0) + vertices.max(axis=0)) / 2
    scale = float(np.linalg.norm(np.ptp(vertices, axis=0)))
    if not scale > 0:
        raise ValueError('Zero-size patch')
    meshset = ml.MeshSet()
    # QEM's numerical thresholds should not depend on CAD units or world offset.
    selectable = np.ones(len(faces)) if frozen is None else (~frozen).astype(float)
    meshset.add_mesh(ml.Mesh(vertex_matrix=(vertices-center)/scale, face_matrix=faces,
        f_scalar_array=selectable))
    partial = frozen is not None and np.any(frozen)
    if partial:
        meshset.compute_selection_by_condition_per_face(condselect='fq > 0.5')
    meshset.meshing_decimation_quadric_edge_collapse(
        targetfacenum=max(12, int(len(original) * keep)), targetperc=0.0, qualitythr=0.3,
        preserveboundary=True, preservenormal=True, preservetopology=True,
        optimalplacement=optimal, planarquadric=planar, planarweight=0.001,
        autoclean=False, selected=partial)
    mesh = meshset.current_mesh()
    result = np.zeros(mesh.face_number(), dtype=STL_DTYPE)
    result['v'] = mesh.vertex_matrix()[mesh.face_matrix()] * scale + center
    v = result['v'].astype(float)
    cross = np.cross(v[:, 1] - v[:, 0], v[:, 2] - v[:, 0])
    length = np.linalg.norm(cross, axis=1)
    if np.any(length == 0):
        raise ValueError('Serialized candidate contains a degenerate face')
    result['normal'] = cross / length[:, None]
    return result


def run(args):
    started = time.perf_counter()
    baseline = json.loads(args.baseline.read_text(encoding='utf-8'))
    if baseline.get('max_error', args.max_error) > args.max_error:
        raise ValueError('Cannot tighten a chained baseline without revalidating inherited candidates')
    source_path = Path(baseline['source'])
    data = source_path.read_bytes()
    assert digest(data) == baseline['source_hash']
    source = parse(data)
    previous = parse(Path(baseline['output']).read_bytes())
    order = np.load(args.baseline.parent / 'source-face-order.npy', allow_pickle=False)
    np.testing.assert_array_equal(np.sort(order), np.arange(len(source)))
    output = args.output.resolve()
    if output == source_path.parent or source_path.parent in output.parents:
        raise ValueError('Output must be outside the source directory')
    output.mkdir(parents=True, exist_ok=False)
    (output / 'patches').mkdir()
    np.save(output / 'source-face-order.npy', order)
    chunks = copy.deepcopy(baseline['chunks'])
    originals, bests, groups = {}, {}, {}
    source_offset = previous_offset = 0
    owners = np.zeros(len(source), dtype=np.int64)
    for i, chunk in enumerate(chunks):
        n, k = chunk['original_triangles'], chunk['final_triangles']
        ids = order[source_offset:source_offset + n]
        groups[chunk['id']] = ids
        owners[ids] = i
        originals[chunk['id']] = source[ids]
        bests[chunk['id']] = (previous[ids] if baseline.get('output_face_order') == 'source'
            else previous[previous_offset:previous_offset + k])
        assert digest(originals[chunk['id']].tobytes()) == chunk['source_record_hash']
        assert digest(bests[chunk['id']].tobytes()) == chunk['output_record_hash']
        source_offset += n
        previous_offset += k
    assert source_offset == len(source) and previous_offset == len(previous)
    points, faces = indexed(source['v'].astype(float))
    lowest, highest = np.full(len(points), len(chunks)), np.full(len(points), -1)
    np.minimum.at(lowest, faces.ravel(), np.repeat(owners, 3))
    np.maximum.at(highest, faces.ravel(), np.repeat(owners, 3))
    shared = lowest != highest
    jobs = sorted([c for c in chunks if (('input' in c or args.all_regions and c['original_triangles'] > 16 and c['id'] >= 0) if not args.protected_only
        else 'input' not in c and c['original_triangles'] > 16 and c['id'] >= 0)],
        key=lambda c: c['final_triangles'], reverse=True)
    if args.only_untried:
        jobs = [c for c in jobs if 'attempts' not in c]
    if args.limit:
        jobs = jobs[:args.limit]
    print(f'{source_path.name}: {len(jobs)} regions to try', flush=True)
    progress = output / 'progress.jsonl'
    with progress.open('w', encoding='utf-8') as log:
        for index, chunk in enumerate(jobs):
            original = originals[chunk['id']]
            ids = groups[chunk['id']]
            vertices = np.unique(faces[ids])
            required = {tuple(v) for v in points[vertices[shared[vertices]]]}
            interface = original['v'][np.any(shared[faces[ids]], axis=1)]
            frozen = np.any(shared[faces[ids]], axis=1)
            attempts = []
            for keep in args.keep:
                for planar in [False, True]:
                    optimal = not getattr(args, 'endpoint_placement', False)
                    attempt = {'keep': keep, 'planar': planar, 'optimal_placement': optimal}
                    try:
                        if np.any(original['attr']):
                            raise ValueError('Per-face attributes require original records')
                        candidate = reduce(original, keep, planar, frozen, optimal)
                        attempt['triangles'] = len(candidate)
                        if len(candidate) >= len(bests[chunk['id']]):
                            attempt['result'] = 'not_smaller'
                        else:
                            metrics = validate(original, candidate, required, interface, args.max_error)
                            bests[chunk['id']] = candidate
                            chunk['validation'] = metrics
                            chunk['method'] = 'meshlab_qem'
                            chunk['status'] = 'reduced'
                            chunk['warning'] = ''
                            chunk['retained_exactly'] = False
                            attempt['result'] = 'accepted'
                    except Exception as error:
                        attempt['result'], attempt['reason'] = 'retained', str(error)
                    attempts.append(attempt)
                if len(bests[chunk['id']]) <= max(12, int(len(original) * keep)) + 2:
                    break
                if all(a['result'] == 'not_smaller' for a in attempts[-2:]):
                    break
            chunk['attempts'] = attempts
            chosen = bests[chunk['id']]
            if chunk.get('method') == 'meshlab_qem':
                patch_path = output / 'patches' / f"{chunk['id']}.stl"
                patch_path.write_bytes(encode(chosen))
                chunk['output'] = str(patch_path)
            log.write(json.dumps({'id': chunk['id'], 'original': len(original),
                'final': len(chosen), 'attempts': attempts}) + '\n')
            log.flush()
            if index % 20 == 0 or index == len(jobs) - 1:
                print(f"{index+1}/{len(jobs)}: {sum(len(v) for v in bests.values())} faces, {time.perf_counter()-started:.1f}s", flush=True)
    count = sum(len(v) for v in bests.values())
    assert count <= len(previous)
    final = output / source_path.name
    pending = final.with_suffix('.pending')
    with pending.open('xb') as handle:
        import struct
        handle.write(data[:80] + struct.pack('<I', count))
        for chunk in chunks:
            chosen = bests[chunk['id']]
            chunk['final_triangles'] = len(chosen)
            chunk['output_record_hash'] = digest(chosen.tobytes())
            if chunk['status'] == 'reduced':
                patch_path = output / 'patches' / f"{chunk['id']}.stl"
                patch_path.write_bytes(encode(chosen))
                chunk['output'] = str(patch_path)
            handle.write(chosen.tobytes())
    assert len(parse(pending.read_bytes())) == count
    assert digest(source_path.read_bytes()) == baseline['source_hash']
    pending.rename(final)
    report = {k: v for k, v in baseline.items() if k != 'chunks'}
    report.update(output=str(final), final_triangles=count, final_bytes=final.stat().st_size,
        actual_removal=1-count/len(source), output_face_order='partition', chunks=chunks,
        reduced_regions=sum(c['status'] == 'reduced' for c in chunks),
        region_statuses=dict(Counter(c['status'] for c in chunks)),
        meshlab_regions=sum(c.get('method') == 'meshlab_qem' for c in chunks),
        max_error=args.max_error, keep_targets=args.keep, seconds=time.perf_counter()-started,
        inherited_validation_policy='Prior baseline guards; current error cap applies to newly accepted patches',
        baseline_report=str(args.baseline.resolve()), experiment='pymeshlab 2025.7.post1')
    (output / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'chunks'}, indent=2))


def refresh_patch_paths(report_path):
    """Materialize authoritative per-patch files from a completed experiment.

    Repairs provenance paths in early experimental reports, without changing
    their full STL, triangle counts or geometry verification results.
    """
    report_path = report_path.resolve()
    report = json.loads(report_path.read_text(encoding='utf-8'))
    records = parse(Path(report['output']).read_bytes())
    patch_dir = report_path.parent / 'patches'
    patch_dir.mkdir(exist_ok=True)
    offset = 0
    for chunk in report['chunks']:
        count = chunk['final_triangles']
        region = records[offset:offset + count]
        assert digest(region.tobytes()) == chunk['output_record_hash']
        if chunk['status'] == 'reduced':
            destination = patch_dir / f"{chunk['id']}.stl"
            destination.write_bytes(encode(region))
            chunk['output'] = str(destination)
            if chunk.get('method') == 'meshlab_qem':
                chunk['warning'] = ''
        offset += count
    assert offset == len(records)
    report['inherited_validation_policy'] = 'Prior baseline guards; current error cap applies to newly accepted patches'
    temporary = report_path.with_suffix('.json.pending')
    temporary.write_text(json.dumps(report, indent=2), encoding='utf-8')
    temporary.replace(report_path)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('baseline', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--dependencies', type=Path, default=Path('.codex-build/mesh-lab-deps'))
    parser.add_argument('--keep', nargs='+', type=float, default=[0.15, 0.25, 0.4, 0.6])
    parser.add_argument('--max-error', type=float, default=0.0002)
    parser.add_argument('--limit', type=int, default=0)
    parser.add_argument('--only-untried', action='store_true')
    parser.add_argument('--protected-only', action='store_true')
    parser.add_argument('--all-regions', action='store_true')
    parser.add_argument('--endpoint-placement', action='store_true')
    args = parser.parse_args()
    if not np.isfinite(args.max_error) or args.max_error <= 0 or any(not 0 < k < 1 for k in args.keep):
        parser.error('Positive finite max-error and keep fractions between 0 and 1 required')
    sys.path.insert(0, str(args.dependencies.resolve()))
    import pymeshlab as ml
    run(args)
