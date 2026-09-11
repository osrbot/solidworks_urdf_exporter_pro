"""Recheck a completed STL against its original, including inherited reductions.

Checks deterministic sampled surface distance, not strict Hausdorff distance or
global intersections. Does not modify any STL or the export's physical properties.
"""

import argparse
import json
from pathlib import Path
import sys
import time

import numpy as np

import experiment_meshlab_partitions as experiment
from inspect_stl_reduction_corpus import render
from prototype_partition_stl import digest, parse


def audit(report_path, max_error, views):
    report = json.loads(report_path.read_text(encoding='utf-8'))
    data = Path(report['source']).read_bytes()
    result_data = Path(report['output']).read_bytes()
    assert digest(data) == report['source_hash']
    source, result = parse(data), parse(result_data)
    order = np.load(report_path.parent / 'source-face-order.npy', allow_pickle=False)
    np.testing.assert_array_equal(np.sort(order), np.arange(len(source)))
    owners = np.empty(len(source), dtype=np.int64)
    original_regions, result_regions, groups = {}, {}, {}
    source_offset = result_offset = 0
    for i, chunk in enumerate(report['chunks']):
        n, k = chunk['original_triangles'], chunk['final_triangles']
        ids = order[source_offset:source_offset + n]
        groups[chunk['id']] = ids
        owners[ids] = i
        original_regions[chunk['id']] = source[ids]
        result_regions[chunk['id']] = result[result_offset:result_offset + k]
        assert digest(source[ids].tobytes()) == chunk['source_record_hash']
        assert digest(result_regions[chunk['id']].tobytes()) == chunk['output_record_hash']
        source_offset += n
        result_offset += k
    assert source_offset == len(source) and result_offset == len(result)
    points, faces = experiment.indexed(source['v'].astype(float))
    lower, upper = np.full(len(points), len(report['chunks'])), np.full(len(points), -1)
    np.minimum.at(lower, faces.ravel(), np.repeat(owners, 3))
    np.maximum.at(upper, faces.ravel(), np.repeat(owners, 3))
    shared = lower != upper
    metrics, failures = [], []
    started = time.perf_counter()
    for chunk in report['chunks']:
        original, candidate = original_regions[chunk['id']], result_regions[chunk['id']]
        if chunk['retained_exactly']:
            assert original.tobytes() == candidate.tobytes()
            continue
        ids = groups[chunk['id']]
        vertex_ids = np.unique(faces[ids])
        required = {tuple(v) for v in points[vertex_ids[shared[vertex_ids]]]}
        interface = original['v'][np.any(shared[faces[ids]], axis=1)]
        try:
            values = experiment.validate(original, candidate, required, interface, max_error)
            metrics.append({'id': chunk['id'], **values})
        except Exception as error:
            failures.append({'id': chunk['id'], 'error': str(error)})
        if (len(metrics) + len(failures)) % 25 == 0:
            print(f'Quality audit: {len(metrics)} passed, {len(failures)} failed', flush=True)
    summary = {'checks': 'PASS' if not failures else 'FAIL', 'max_error': max_error,
        'source_sha256': digest(data), 'output_sha256': digest(result_data),
        'verified_reduced_regions': len(metrics), 'failures': failures,
        'maximum_sampled_error': max((max(m['sample_max_source_to_output'],
            m['sample_max_output_to_source']) for m in metrics), default=0),
        'seconds': time.perf_counter() - started, 'regions': metrics}
    (report_path.parent / 'quality-audit.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in summary.items() if k != 'regions'}, indent=2), flush=True)
    if views:
        a, b = source['v'].astype(float), result['v'].astype(float)
        for name, direction in [('top', (0.2, 0.2, 2)), ('rear', (-1.4, 1.8, 1.1))]:
            render(a, b, report_path.parent / f'comparison-{name}.png',
                Path(report['source']).stem + ': ' + name, direction=direction)
        if 10 in original_regions:
            render(original_regions[10]['v'].astype(float), result_regions[10]['v'].astype(float),
                report_path.parent / 'detail-region-10.png', 'Region 10: detail')
    assert digest(Path(report['source']).read_bytes()) == report['source_hash']
    return not failures


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('report', type=Path)
    parser.add_argument('--max-error', type=float, default=0.0005)
    parser.add_argument('--views', action='store_true')
    parser.add_argument('--dependencies', type=Path, default=Path('.codex-build/mesh-lab-deps'))
    args = parser.parse_args()
    sys.path.insert(0, str(args.dependencies.resolve()))
    import pymeshlab
    experiment.ml = pymeshlab
    sys.exit(0 if audit(args.report, args.max_error, args.views) else 1)
