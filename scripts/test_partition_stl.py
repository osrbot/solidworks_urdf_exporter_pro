"""Offline partition contract tests; no SolidWorks or reducer process required."""

from pathlib import Path
import tempfile
import unittest

import numpy as np

from prototype_partition_stl import candidate_check, encode, parse, partition, run, STL_DTYPE


def records(triangles):
    result = np.zeros(len(triangles), dtype=STL_DTYPE)
    result['v'] = triangles
    result['normal'] = [0, 0, 1]
    return result


class PartitionTests(unittest.TestCase):
    def setUp(self):
        self.square = records([
            [[0, 0, 0], [1, 0, 0], [1, 1, 0]],
            [[0, 0, 0], [1, 1, 0], [0, 1, 0]],
        ])

    def test_opposite_edge_joins_patch(self):
        labels, eligible, *_ = partition(self.square)
        self.assertEqual(labels.tolist(), [0, 0])
        self.assertEqual(eligible.tolist(), [True])

    def test_same_winding_edge_protects_both_patches(self):
        source = self.square.copy()
        source['v'][1] = source['v'][1][::-1]
        labels, eligible, *_ = partition(source)
        self.assertEqual(len(set(labels)), 2)
        self.assertFalse(eligible.any())

    def test_third_edge_incidence_protects_all_owners(self):
        source = np.concatenate([self.square, self.square[:1]])
        _, eligible, *_ = partition(source)
        self.assertFalse(eligible.any())

    def test_degenerate_neighbors_are_protected(self):
        degenerate = records([[[0, 0, 0], [1, 0, 0], [1, 0, 0]]])
        labels, eligible, *_ = partition(np.concatenate([self.square, degenerate]))
        self.assertEqual(labels[-1], -1)
        self.assertFalse(eligible.any())

    def test_point_contact_is_shared_without_joining_patches(self):
        other = records([[[0, 0, 0], [-1, 0, 0], [0, -1, 0]]])
        labels, _, points, _, shared = partition(np.concatenate([self.square, other]))
        self.assertNotEqual(labels[0], labels[-1])
        np.testing.assert_equal(points[shared], [[0, 0, 0]])

    def test_interface_face_change_rejected_even_if_vertex_survives(self):
        center = [0.5, 0.5, 0]
        refined = records([
            [[0, 0, 0], [1, 0, 0], center],
            [[1, 0, 0], [1, 1, 0], center],
            [[1, 1, 0], [0, 1, 0], center],
            [[0, 1, 0], [0, 0, 0], center],
        ])
        candidate_check(refined, self.square, set())
        with self.assertRaisesRegex(ValueError, 'interface'):
            candidate_check(refined, self.square, {(0, 0, 0)}, refined['v'][:1])

    def test_raw_records_keep_attributes_normals_and_duplicates(self):
        raw = np.concatenate([self.square, self.square[:1]])
        raw['attr'][0] = 27
        raw['normal'][1] = [2, 3, 4]
        encoded = encode(raw, b'COLOR=original header')
        self.assertEqual(parse(encoded).tobytes(), raw.tobytes())
        labels, *_ = partition(raw)
        order = np.argsort(labels, kind='stable')
        np.testing.assert_equal(np.sort(order), np.arange(len(raw)))

    def test_zero_ratio_is_byte_identical_and_source_untouched(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'input').mkdir()
            source = root / 'input' / 'fixture.stl'
            data = encode(self.square, b'original header')
            source.write_bytes(data)
            run(source, root / 'output', root / 'unused.dll', 0)
            self.assertEqual(source.read_bytes(), data)
            self.assertEqual((root / 'output' / source.name).read_bytes(), data)

    def test_truncated_input_rejected(self):
        with self.assertRaises(ValueError):
            parse(encode(self.square)[:-1])

    def test_all_raw_reassembly_is_byte_identical(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'input').mkdir()
            source = root / 'input' / 'fixture.stl'
            degenerate = records([[[0, 0, 0], [1, 0, 0], [1, 0, 0]]])
            original = np.concatenate([self.square, degenerate, self.square[:1]])
            original['attr'][-1] = 12
            data = encode(original, b'Original header')
            source.write_bytes(data)
            report = run(source, root / 'output', source, 0.5)
            self.assertEqual(report['output_face_order'], 'source')
            self.assertEqual(report['original_faces_accounted_once'], len(original))
            self.assertEqual((root / 'output' / source.name).read_bytes(), data)
            self.assertEqual(source.read_bytes(), data)

    def test_invalid_ratio_does_not_create_output(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for ratio in [-1, 1.1, float('nan')]:
                with self.assertRaises(ValueError):
                    run(root / 'source.stl', root / 'output', root / 'unused', ratio)
                self.assertFalse((root / 'output').exists())


if __name__ == '__main__':
    unittest.main()
