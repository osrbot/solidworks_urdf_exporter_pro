"""Audit offline reassembly and benchmark warm file reading/array preparation.

This is not a renderer, GPU, cold-disk or complete robot-viewer benchmark.
"""

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import statistics
import time

import numpy as np

from inspect_stl_reduction_corpus import render
from prototype_partition_stl import parse


def sha(data):
    return hashlib.sha256(data).hexdigest()


def prepare(path):
    data = path.read_bytes()
    records = parse(data)
    # Materialize vertex/normal arrays, analogous to unindexed STL geometry buffers.
    vertices = np.ascontiguousarray(records['v']).reshape(-1, 3)
    normals = np.repeat(records['normal'], 3, axis=0)
    return vertices, normals


def assess(report_path):
    report = json.loads(report_path.read_text(encoding='utf-8'))
    source_path, result_path = Path(report['source']), Path(report['output'])
    source_bytes, result_bytes = source_path.read_bytes(), result_path.read_bytes()
    source, result = parse(source_bytes), parse(result_bytes)
    assert sha(source_bytes) == report['source_hash']
    assert source_bytes[:80] == result_bytes[:80]
    order = np.load(report_path.parent / 'source-face-order.npy', allow_pickle=False)
    np.testing.assert_array_equal(np.sort(order), np.arange(len(source)))
    assert len(result) == report['final_triangles']
    source_offset = result_offset = reduced = raw_count = 0
    for chunk in report['chunks']:
        count, final = chunk['original_triangles'], chunk['final_triangles']
        original = source[order[source_offset:source_offset + count]]
        actual = (result[order[source_offset:source_offset + count]]
            if report.get('output_face_order') == 'source'
            else result[result_offset:result_offset + final])
        assert sha(original.tobytes()) == chunk['source_record_hash']
        assert sha(actual.tobytes()) == chunk['output_record_hash']
        assert final > 0
        if chunk['retained_exactly']:
            assert original.tobytes() == actual.tobytes()
            raw_count += final
        else:
            assert chunk['status'] == 'reduced' and final < count
            vertices = actual['v'].astype(float)
            cross = np.cross(vertices[:, 1] - vertices[:, 0], vertices[:, 2] - vertices[:, 0])
            assert np.all(np.linalg.norm(cross, axis=1) > 0)
            assert np.all(np.einsum('ij,ij->i', cross, actual['normal']) > 0)
            reduced += 1
        source_offset += count
        result_offset += final
    assert source_offset == len(source) and result_offset == len(result)
    assert reduced == report['reduced_regions']

    def degenerates(records):
        vertices = records['v'].astype(float)
        return int(np.count_nonzero(np.linalg.norm(np.cross(
            vertices[:, 1] - vertices[:, 0], vertices[:, 2] - vertices[:, 0]), axis=1) == 0))

    assert degenerates(source) == degenerates(result)
    timings = {'source': [], 'reduced': []}
    paths = {'source': source_path, 'reduced': result_path}
    for path in paths.values():
        prepare(path)
    for iteration in range(15):
        for key in (['source', 'reduced'] if iteration % 2 == 0 else ['reduced', 'source']):
            started = time.perf_counter()
            buffers = prepare(paths[key])
            timings[key].append((time.perf_counter() - started) * 1000)
            del buffers
    comparison = report_path.parent / 'comparison.png'
    render(source['v'].astype(float), result['v'].astype(float), comparison, source_path.stem + ': complete file')
    outcome = {
        'source': str(source_path), 'output': str(result_path),
        'source_sha256': sha(source_bytes), 'output_sha256': sha(result_bytes),
        'source_face_ownership': 'exactly once', 'raw_records_byte_identical': raw_count,
        'reduced_regions': reduced, 'degenerate_records_preserved': degenerates(source),
        'original_triangles': len(source), 'final_triangles': len(result),
        'source_bytes': len(source_bytes), 'output_bytes': len(result_bytes),
        'benchmark': 'Warm file read + binary validation + contiguous float32 position/normal arrays; not viewer loading',
        'median_ms': {key: statistics.median(values) for key, values in timings.items()},
        'samples_ms': timings, 'comparison': str(comparison), 'checks': 'PASS',
    }
    (report_path.parent / 'assessment.json').write_text(json.dumps(outcome, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in outcome.items() if k != 'samples_ms'}, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reports', nargs='+', type=Path)
    args = parser.parse_args()
    for path in args.reports:
        assess(path)
