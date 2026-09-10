"""Tests for the isolated MeshLab experiment, not the production plugin."""

from pathlib import Path
import contextlib
import io
import json
import sys
import tempfile
from types import SimpleNamespace
import unittest

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / '.codex-build/mesh-lab-deps'))
import pymeshlab
import experiment_meshlab_partitions as experiment
from prototype_partition_stl import STL_DTYPE, encode, run as partition_run

experiment.ml = pymeshlab


def plane(size=1.0):
    triangles = []
    for x in range(8):
        for y in range(8):
            a, b = [x/8*size, y/8*size, 0], [(x+1)/8*size, y/8*size, 0]
            c, d = [(x+1)/8*size, (y+1)/8*size, 0], [x/8*size, (y+1)/8*size, 0]
            triangles.extend([[a, b, c], [a, c, d]])
    result = np.zeros(len(triangles), dtype=STL_DTYPE)
    result['v'], result['normal'] = triangles, [0, 0, 1]
    return result


class MeshLabExperimentTests(unittest.TestCase):
    def test_distance_known_offset(self):
        target = plane()['v'].astype(float)
        points = np.array([[0.25, 0.25, 0.001], [0.75, 0.75, 0.001]])
        self.assertAlmostEqual(experiment.directed_distance(points, target), 0.001, places=7)

    def test_distance_identity(self):
        target = plane()['v'].astype(float)
        self.assertLess(experiment.directed_distance(experiment.samples(target), target), 1e-7)

    def test_distance_missing_surface_is_not_zero(self):
        target = plane()['v'].astype(float)
        points = np.array([[100, 100, 100], [101, 101, 101]], dtype=float)
        self.assertGreater(experiment.directed_distance(points, target), 1)

    def test_planar_reduction_and_meter_scaling(self):
        counts = []
        for size in [1, 0.001]:
            original = plane(size)
            candidate = experiment.reduce(original, 0.4, False)
            self.assertLess(len(candidate), len(original))
            metrics = experiment.validate(original, candidate, set(), None, size * 0.001)
            self.assertLess(metrics['sample_max_source_to_output'], size * 1e-5)
            counts.append(len(candidate))
        self.assertEqual(counts[0], counts[1])

    def test_interface_change_rejected(self):
        original = plane()
        candidate = experiment.reduce(original, 0.2, False)
        with self.assertRaises(ValueError):
            experiment.validate(original, candidate, set(), original['v'], 0.001)

    def test_selected_reduction_keeps_frozen_interface_triangles(self):
        original = plane()
        frozen = np.arange(len(original)) < 16
        candidate = experiment.reduce(original, 0.4, False, frozen)
        self.assertLess(len(candidate), len(original))
        experiment.validate(original, candidate, set(), original['v'][frozen], 0.001)

    def test_source_order_baseline_and_tighter_limit_rejection(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'input').mkdir()
            source = root / 'input' / 'fixture.stl'
            geometry = plane()[:3].copy()
            geometry['v'][-1] = 0
            source.write_bytes(encode(geometry))
            with contextlib.redirect_stdout(io.StringIO()):
                partition_run(source, root / 'baseline', source, 0.5)
                args = SimpleNamespace(baseline=root / 'baseline/report.json', output=root / 'candidate',
                    max_error=0.0002, keep=[0.2], limit=0, only_untried=False,
                    protected_only=False, all_regions=False)
                experiment.run(args)
            report = json.loads((root / 'candidate/report.json').read_text(encoding='utf-8'))
            self.assertEqual(report['final_triangles'], len(geometry))
            self.assertTrue(report['source_unchanged'])
            args.baseline, args.output, args.max_error = root / 'candidate/report.json', root / 'tighter', 0.0001
            with self.assertRaisesRegex(ValueError, 'tighten'):
                experiment.run(args)
            self.assertFalse(args.output.exists())

    def test_endpoint_mode_uses_original_vertex_positions(self):
        original = plane(0.001)
        candidate = experiment.reduce(original, 0.4, False, optimal=False)
        self.assertLess(len(candidate), len(original))
        original_points = {tuple(v) for v in original['v'].reshape(-1, 3)}
        self.assertTrue({tuple(v) for v in candidate['v'].reshape(-1, 3)}.issubset(original_points))
        experiment.validate(original, candidate, set(), None, 1e-6)


if __name__ == '__main__':
    unittest.main()
