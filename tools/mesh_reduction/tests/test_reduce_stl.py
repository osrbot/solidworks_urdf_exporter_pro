"""Synthetic protocol and geometry tests; no SolidWorks, installation or build."""

from collections import Counter
import importlib.util
import json
import math
import os
from pathlib import Path
import struct
import subprocess
import sys
import sysconfig
import tempfile
import time
import unittest
from unittest.mock import patch


sys.dont_write_bytecode = True
WORKER = Path(__file__).resolve().parents[1] / "reduce_stl.py"
REPO = WORKER.parents[2]
DEPS = REPO / ".codex-build" / "mesh-lab-deps"
if sys.version_info[:2] != (3, 12):
    DEPS = Path(sysconfig.get_paths()["purelib"])
if DEPS.is_dir():
    sys.path.insert(0, str(DEPS))
spec = importlib.util.spec_from_file_location("reduce_stl", WORKER)
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)
worker.load_numpy()
worker.load_meshlab()
np = worker.np


def plane(n=8, size=1.0, offset=(0, 0, 0)):
    triangles = []
    for x in range(n):
        for y in range(n):
            a, b = [x / n, y / n, 0], [(x + 1) / n, y / n, 0]
            c, d = [(x + 1) / n, (y + 1) / n, 0], [x / n, (y + 1) / n, 0]
            triangles.extend([[a, b, c], [a, c, d]])
    records = np.zeros(len(triangles), dtype=worker.STL_DTYPE)
    records["v"] = np.array(triangles) * size + np.array(offset)
    records["normal"] = [0, 0, 1]
    return records


def tetrahedron():
    records = np.zeros(4, dtype=worker.STL_DTYPE)
    points = np.array([[0, 0, 0], [1, 0, 0], [0, 1, 0], [0, 0, 1]], dtype=float)
    records["v"] = points[[[0, 2, 1], [0, 1, 3], [0, 3, 2], [1, 2, 3]]]
    v = records["v"].astype(float)
    normal = np.cross(v[:, 1] - v[:, 0], v[:, 2] - v[:, 0])
    records["normal"] = normal / np.linalg.norm(normal, axis=1)[:, None]
    return records


class WorkerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / "source.stl"
        self.output = self.root / "candidate.stl"
        self.request_path = self.root / "request.json"
        self.result_path = self.root / "result.json"

    def request(self, records=None, **overrides):
        if records is None:
            records = plane()
        self.source.write_bytes(worker.encode(records, bytes(range(80))))
        value = dict(schemaVersion=1, input=str(self.source), output=str(self.output),
                     ratio=0.75, timeoutSeconds=30, maxError=0.0005, relativeError=0.005)
        value.update(overrides)
        self.request_path.write_text(json.dumps(value), encoding="utf-8")
        return value

    def run_local(self):
        events = []
        code = worker.execute(self.request_path, self.result_path,
                              lambda s, c, t: events.append((s, c, t)))
        result = json.loads(self.result_path.read_text(encoding="utf-8"))
        return code, result, events

    def run_cli(self):
        bootstrap = ("import runpy,sys;sys.path.insert(0," + repr(str(DEPS)) + ");"
                     "sys.argv=sys.argv[1:];runpy.run_path(sys.argv[0],run_name='__main__')")
        return subprocess.run([sys.executable, "-B", "-c", bootstrap, str(WORKER),
                               "--request", str(self.request_path), "--result", str(self.result_path)],
                              capture_output=True, text=True, encoding="utf-8", timeout=40)

    def test_protocol_real_reduction_and_source_read_only(self):
        self.request()
        original = self.source.read_bytes()
        completed = self.run_cli()
        self.assertEqual(completed.returncode, 0, completed.stderr)
        result = json.loads(self.result_path.read_text())
        self.assertEqual(set(result), set(worker.empty_result()))
        self.assertEqual(result["status"], "reduced")
        self.assertEqual(result["sourceSha256"], worker.digest(original))
        self.assertEqual(result["ratio"], 0.75)
        self.assertEqual(result["originalTriangles"], 128)
        self.assertGreaterEqual(result["finalTriangles"], 32)
        self.assertLess(result["finalTriangles"], 128)
        self.assertEqual(result["finalBytes"], 84 + 50 * result["finalTriangles"])
        self.assertEqual(self.output.stat().st_size, result["finalBytes"])
        self.assertEqual(self.source.read_bytes(), original)
        self.assertEqual(self.output.read_bytes()[:80], original[:80])
        events = [json.loads(line) for line in completed.stdout.splitlines()]
        self.assertGreater(len(events), 3)
        for event in events:
            self.assertEqual(set(event), {"type", "stage", "completed", "total"})
            self.assertEqual(event["type"], "progress")
            self.assertLessEqual(event["completed"], event["total"])
        self.assertEqual(events[-1]["stage"], "done")
        self.assertFalse(list(self.root.glob("*.pending")))

    def test_zero_unicode_and_bom_byte_identical(self):
        self.source = self.root / "\u6a21\u578b \u6e90.stl"
        self.output = self.root / "\u5019\u9009 \u7f51\u683c.stl"
        request = self.request(ratio=0)
        self.request_path.write_text(json.dumps(request), encoding="utf-8-sig")
        completed = self.run_cli()
        self.assertEqual(completed.returncode, 0, completed.stderr)
        result = json.loads(self.result_path.read_text())
        self.assertEqual(result["status"], "unchanged")
        self.assertEqual(result["processedRegions"], 0)
        self.assertEqual(self.source.read_bytes(), self.output.read_bytes())

    def test_stdout_excludes_python_native_and_exit_chatter(self):
        self.request(ratio=0)
        bootstrap = (
            "import runpy,sys,os,atexit;sys.path.insert(0," + repr(str(DEPS)) + ");"
            "sys.argv=sys.argv[1:];"
            "scope=runpy.run_path(sys.argv[0]);"
            "state=scope['execute'].__globals__;real=state['load_numpy'];"
            "state['load_numpy']=lambda:(print('python chatter'),os.write(1,b'native chatter\\n'),real());"
            "atexit.register(lambda:os.write(1,b'exit chatter\\n'));"
            "sys.exit(scope['main']())"
        )
        completed = subprocess.run([sys.executable, "-B", "-c", bootstrap, str(WORKER),
                                    "--request", str(self.request_path), "--result", str(self.result_path)],
                                   capture_output=True, text=True, encoding="utf-8", timeout=40)
        self.assertEqual(completed.returncode, 0, completed.stderr)
        events = [json.loads(line) for line in completed.stdout.splitlines()]
        self.assertEqual(events[-1]["stage"], "done")
        for text in ("python chatter", "native chatter", "exit chatter"):
            self.assertIn(text, completed.stderr)
            self.assertNotIn(text, completed.stdout)

    def test_attributes_degenerate_and_tiny_complete_records(self):
        attributes = plane(offset=(2, 0, 0))
        attributes["attr"][0] = 0xFC10
        attributes["normal"][1] = [0.3, 0.4, 0.5]
        tiny = tetrahedron()
        tiny["v"] += [5, 0, 0]
        degenerate = np.zeros(2, dtype=worker.STL_DTYPE)
        degenerate["v"][0] = [[8, 0, 0], [9, 0, 0], [10, 0, 0]]
        degenerate["attr"] = [123, 456]
        degenerate["normal"] = [42, -0.0, 11]
        records = np.concatenate((plane(), attributes, tiny, degenerate))
        self.request(records, ratio=1)
        code, result, _ = self.run_local()
        self.assertEqual(code, 0)
        self.assertEqual(result["status"], "reduced")
        actual = Counter(r.tobytes() for r in worker.parse(self.output.read_bytes()))
        retained = Counter(r.tobytes() for r in np.concatenate((attributes, tiny, degenerate)))
        self.assertFalse(retained - actual)
        self.assertEqual(result["retainedRegions"], 3)

    def test_global_budget_compensates_retained_small_parts_with_real_qem(self):
        tiny = []
        for x in (2, 4, 6, 8):
            part = tetrahedron()
            part["v"] += [x, 0, 0]
            tiny.append(part)
        retained = np.concatenate(tiny)
        records = np.concatenate((plane(), retained))
        self.request(records, ratio=0.67)
        source = self.source.read_bytes()
        with patch.object(worker, "decimate", wraps=worker.decimate) as attempt:
            code, result, _ = self.run_local()
        self.assertEqual(code, 0)
        self.assertEqual(result["originalTriangles"], 144)
        self.assertEqual(result["finalTriangles"], 48)
        self.assertEqual(result["retainedRegions"], 4)
        self.assertEqual(attempt.call_count, 1)
        self.assertEqual(attempt.call_args.args[1], 32)
        self.assertLess(32, 128 - math.floor(128 * 0.67))
        actual = Counter(r.tobytes() for r in worker.parse(self.output.read_bytes()))
        self.assertFalse(Counter(r.tobytes() for r in retained) - actual)
        self.assertEqual(self.source.read_bytes(), source)
        self.assertLessEqual(len(records) - result["finalTriangles"], math.floor(len(records) * 0.67))

    def test_global_budget_rechecks_deferred_regions_after_other_savings(self):
        self.request(np.concatenate((plane(), plane(offset=(2, 0, 0)))), ratio=0.5)
        calls = []

        def candidate(original, target, frozen, planar):
            region = int(original["v"][:, :, 0].min())
            calls.append((region, target))
            if len(calls) == 1:
                raise ValueError("First region needs another target")
            return original[:64 if region == 2 else target].copy()

        with patch.object(worker, "decimate", side_effect=candidate), patch.object(worker, "validate"):
            code, result, _ = self.run_local()
        self.assertEqual(code, 0)
        self.assertEqual(calls, [(0, 20), (2, 20), (0, 64)])
        self.assertEqual(result["finalTriangles"], 128)
        self.assertEqual(result["retainedRegions"], 0)

    def test_native_undershoot_cannot_exceed_whole_file_budget(self):
        self.request(ratio=0.5)

        def undershoot(original, target, frozen, planar):
            return original[:63].copy()

        with patch.object(worker, "decimate", side_effect=undershoot) as attempt, patch.object(worker, "validate") as validate:
            code, result, _ = self.run_local()
        self.assertEqual(code, 0)
        self.assertEqual(result["status"], "unchanged")
        self.assertLessEqual(attempt.call_count, 5)
        validate.assert_not_called()
        self.assertEqual(self.output.read_bytes(), self.source.read_bytes())

    def test_easy_regions_finish_before_difficult_region_retries(self):
        self.request(np.concatenate((plane(), plane(offset=(2, 0, 0)))), ratio=1)
        visits = []

        def candidate(original, target, frozen, planar):
            region = int(original["v"][:, :, 0].min())
            visits.append(region)
            if region == 0 and target < 60:
                raise ValueError("Synthetic quality threshold")
            return original[:target].copy()

        with patch.object(worker, "decimate", side_effect=candidate), patch.object(worker, "validate"):
            code, result, events = self.run_local()
        self.assertEqual(code, 0)
        self.assertEqual(visits[:2], [0, 2])
        self.assertEqual(visits.count(2), 1)
        self.assertLessEqual(visits.count(0), 5)
        self.assertEqual(result["retainedRegions"], 0)
        progress = [completed for stage, completed, total in events if stage == "reduce"]
        self.assertEqual(progress[:3], [0, 0, 1])
        self.assertNotIn(2, progress[:-1])
        self.assertEqual(progress[-1], 2)

    def test_global_budget_exhaustion_does_not_start_more_regions(self):
        self.request(np.concatenate((plane(), plane(offset=(2, 0, 0)))), ratio=0.25)
        with patch.object(worker, "decimate", wraps=worker.decimate) as attempt:
            code, result, _ = self.run_local()
        self.assertEqual(code, 0)
        self.assertEqual(attempt.call_count, 1)
        self.assertEqual(result["finalTriangles"], 192)
        self.assertEqual(result["processedRegions"], 1)
        self.assertEqual(result["retainedRegions"], 1)

    def test_all_degenerate_and_tiny_are_noop(self):
        for records in (np.zeros(20, dtype=worker.STL_DTYPE), tetrahedron()):
            self.output.unlink(missing_ok=True)
            self.request(records, ratio=1)
            code, result, _ = self.run_local()
            self.assertEqual(code, 0)
            self.assertEqual(result["status"], "unchanged")
            self.assertEqual(self.source.read_bytes(), self.output.read_bytes())

    def test_invalid_requests_publish_failure(self):
        cases = [dict(ratio=-1), dict(ratio=1.01), dict(ratio=True), dict(ratio="0.5"),
                 dict(ratio=float("nan")), dict(schemaVersion=2), dict(schemaVersion=True),
                 dict(timeoutSeconds=-1), dict(timeoutSeconds=float("inf")),
                 dict(maxError=0), dict(relativeError=-0.1), dict(input="relative.stl")]
        for overrides in cases:
            with self.subTest(overrides=overrides):
                self.request(**overrides)
                code, result, _ = self.run_local()
                self.assertNotEqual(code, 0)
                self.assertEqual(result["status"], "failed")
                self.assertTrue(result["warning"])
                self.assertFalse(self.output.exists())

    def test_missing_input_nonzero_cli(self):
        self.request()
        self.source.unlink()
        completed = self.run_cli()
        self.assertNotEqual(completed.returncode, 0)
        self.assertEqual(json.loads(self.result_path.read_text())["status"], "failed")
        self.assertFalse(self.output.exists())

    def test_invalid_stl_and_invalid_json(self):
        self.request()
        invalid = [b"bad", b"solid ascii\nendsolid ascii", b"\0" * 84,
                   self.source.read_bytes() + b"extra"]
        bad = plane()
        bad["v"][0, 0, 0] = float("nan")
        invalid.append(worker.encode(bad))
        for data in invalid:
            self.source.write_bytes(data)
            code, result, _ = self.run_local()
            self.assertEqual(code, 1)
            self.assertEqual(result["status"], "failed")
            self.assertEqual(self.source.read_bytes(), data)
        self.request_path.write_text("{", encoding="utf-8")
        self.assertEqual(self.run_local()[0], 1)

    def test_existing_output_and_source_output_alias_rejected(self):
        self.request()
        self.output.write_bytes(b"do not overwrite")
        self.assertEqual(self.run_local()[0], 1)
        self.assertEqual(self.output.read_bytes(), b"do not overwrite")
        self.request(output=str(self.source))
        original = self.source.read_bytes()
        self.assertEqual(self.run_local()[0], 1)
        self.assertEqual(self.source.read_bytes(), original)

    def test_result_alias_never_overwrites_source_or_request(self):
        self.request()
        for protected in (self.source, self.request_path):
            before = protected.read_bytes()
            code = worker.execute(self.request_path, protected, lambda *args: None)
            self.assertEqual(code, 1)
            self.assertEqual(protected.read_bytes(), before)
        os.link(self.source, self.result_path)
        original = self.source.read_bytes()
        self.assertEqual(worker.execute(self.request_path, self.result_path, lambda *a: None), 1)
        self.assertEqual(self.source.read_bytes(), original)

    def test_zero_budget_retains_every_partition(self):
        self.request(np.concatenate((plane(), plane(offset=(2, 0, 0)))), timeoutSeconds=0)
        with patch.object(worker, "decimate", side_effect=AssertionError("Must not start QEM")):
            code, result, _ = self.run_local()
        self.assertEqual(code, 0)
        self.assertEqual(result["processedRegions"], 0)
        self.assertEqual(result["retainedRegions"], result["regionCount"])
        self.assertIn("budget", result["warning"])
        self.assertEqual(self.source.read_bytes(), self.output.read_bytes())

    def test_budget_expiry_mid_region_never_loses_faces(self):
        self.request(np.concatenate((plane(), plane(offset=(2, 0, 0)))))
        with patch.object(worker, "decimate", side_effect=worker.BudgetExpired):
            code, result, _ = self.run_local()
        self.assertEqual(code, 0)
        self.assertEqual(result["processedRegions"], 1)
        self.assertEqual(result["retainedRegions"], 2)
        self.assertEqual(self.source.read_bytes(), self.output.read_bytes())

    def test_budget_retains_validated_candidate_and_unprocessed_source(self):
        second = plane(offset=(2, 0, 0))
        self.request(np.concatenate((plane(), second)))
        real_decimate = worker.decimate
        calls = 0

        def expire_after_first(*args):
            nonlocal calls
            calls += 1
            if calls == 2:
                raise worker.BudgetExpired()
            return real_decimate(*args)

        with patch.object(worker, "decimate", side_effect=expire_after_first):
            code, result, _ = self.run_local()
        self.assertEqual(code, 0)
        self.assertEqual(result["status"], "reduced")
        self.assertEqual(result["processedRegions"], 2)
        self.assertEqual(result["retainedRegions"], 1)
        actual = Counter(r.tobytes() for r in worker.parse(self.output.read_bytes()))
        self.assertFalse(Counter(r.tobytes() for r in second) - actual)

    def test_soft_budget_expired_during_native_call_discards_candidate(self):
        self.request()
        real_decimate = worker.decimate
        now = time.monotonic()

        def overrun(*args):
            candidate = real_decimate(*args)
            worker.time.monotonic.return_value = now + 100
            return candidate

        with patch.object(worker.time, "monotonic", return_value=now), patch.object(worker, "decimate", side_effect=overrun):
            code, result, _ = self.run_local()
        self.assertEqual(code, 0)
        self.assertEqual(result["status"], "unchanged")
        self.assertIn("budget", result["warning"])
        self.assertEqual(self.source.read_bytes(), self.output.read_bytes())

    def test_ratio_attempts_are_bounded(self):
        self.request(ratio=1)
        with patch.object(worker, "decimate", side_effect=ValueError("Reject")) as attempt:
            code, result, _ = self.run_local()
        self.assertEqual(code, 0)
        self.assertLessEqual(attempt.call_count, 5)
        self.assertEqual(len({call.args[1] for call in attempt.call_args_list}), 5)
        self.assertEqual(result["status"], "unchanged")

    def test_small_ratio_is_maximum_removal_not_keep_fraction(self):
        self.request(ratio=0.013)
        code, result, _ = self.run_local()
        self.assertEqual(code, 0)
        self.assertLessEqual(128 - result["finalTriangles"], math.floor(128 * 0.013))

    def test_source_mutation_prevents_publication(self):
        self.request(ratio=0)
        real_stage = worker.stage_file

        def mutate(path, data):
            staged = real_stage(path, data)
            if path == self.output:
                self.source.write_bytes(self.source.read_bytes() + b"external modification")
            return staged

        with patch.object(worker, "stage_file", side_effect=mutate):
            code, result, _ = self.run_local()
        self.assertEqual(code, 1)
        self.assertIn("Source changed", result["warning"])
        self.assertFalse(self.output.exists())
        self.assertTrue(self.source.read_bytes().endswith(b"external modification"))

    def test_output_race_is_no_clobber(self):
        self.request(ratio=0)
        real_link = os.link

        def race(source, destination):
            self.output.write_bytes(b"concurrent owner")
            return real_link(source, destination)

        with patch.object(worker.os, "link", side_effect=race):
            code, result, _ = self.run_local()
        self.assertEqual(code, 1)
        self.assertEqual(result["status"], "failed")
        self.assertEqual(self.output.read_bytes(), b"concurrent owner")

    def test_result_publication_failure_rolls_back_own_candidate(self):
        self.request(ratio=0)
        real_replace = os.replace
        count = 0

        def fail_once(source, destination):
            nonlocal count
            count += 1
            if count == 1:
                raise OSError("Simulated result publication failure")
            return real_replace(source, destination)

        with patch.object(worker.os, "replace", side_effect=fail_once):
            code, result, _ = self.run_local()
        self.assertEqual(code, 1)
        self.assertEqual(result["status"], "failed")
        self.assertFalse(self.output.exists())

    def test_publication_detects_candidate_tampering(self):
        self.request(ratio=0)
        real_link = os.link

        def tamper(source, destination):
            real_link(source, destination)
            data = bytearray(self.output.read_bytes())
            data[-1] ^= 1
            self.output.write_bytes(data)

        with patch.object(worker.os, "link", side_effect=tamper):
            code, result, _ = self.run_local()
        self.assertEqual(code, 1)
        self.assertIn("candidate bytes", result["warning"])
        self.assertFalse(self.output.exists())

    def test_missing_dependency_reports_failure(self):
        self.request()
        with patch.object(worker, "load_numpy", side_effect=ImportError("NumPy missing")):
            code, result, _ = self.run_local()
        self.assertEqual(code, 1)
        self.assertIn("NumPy missing", result["warning"])
        self.assertFalse(self.output.exists())

    def test_native_meshlab_exception_is_local_fallback(self):
        self.request()
        with patch.object(worker, "decimate", side_effect=worker.ml.PyMeshLabException("Native rejection")) as attempt:
            code, result, _ = self.run_local()
        self.assertEqual(code, 0)
        self.assertEqual(result["status"], "unchanged")
        self.assertEqual(self.source.read_bytes(), self.output.read_bytes())
        self.assertEqual(attempt.call_count, 2)
        self.assertEqual([call.args[3] for call in attempt.call_args_list], [False, True])

    def test_native_stall_gets_only_one_alternate(self):
        self.request()
        with patch.object(worker, "decimate", side_effect=lambda original, *a: original.copy()) as attempt:
            code, result, _ = self.run_local()
        self.assertEqual(code, 0)
        self.assertEqual(result["status"], "unchanged")
        self.assertEqual(attempt.call_count, 2)
        self.assertEqual([call.args[3] for call in attempt.call_args_list], [False, True])

    def test_adaptive_search_refines_success_within_five_levels(self):
        self.request(ratio=1)
        targets = []

        def threshold(original, target, frozen, planar):
            targets.append(target)
            if target < 36:
                raise ValueError("Synthetic quality threshold")
            return original[:target].copy()

        with patch.object(worker, "decimate", side_effect=threshold), patch.object(worker, "validate"):
            code, result, _ = self.run_local()
        self.assertEqual(code, 0)
        self.assertEqual(result["finalTriangles"], 36)
        self.assertLessEqual(len(set(targets)), 5)
        self.assertLessEqual(len(targets), 5)


class GeometryTests(unittest.TestCase):
    def test_partition_only_exactly_two_opposite_edges(self):
        original = plane(n=1)
        groups, _, _ = worker.partition(original)
        self.assertEqual(len(groups), 1)
        reversed_face = original.copy()
        reversed_face["v"][1] = reversed_face["v"][1, ::-1]
        groups, _, _ = worker.partition(reversed_face)
        self.assertEqual(len(groups), 2)
        tripled = np.concatenate((original, original[:1]))
        groups, _, _ = worker.partition(tripled)
        self.assertEqual(len(groups), 3)
        worker.verify_coverage(groups, 3)
        with self.assertRaises(ValueError):
            worker.verify_coverage([np.array([0, 0, 2])], 3)

    def test_seven_samples_and_both_distances(self):
        original = plane()
        a = original["v"].astype(float)
        self.assertEqual(len(worker.samples(a)), len(a) * 7)
        self.assertAlmostEqual(worker.directed_distance(np.array([[0.5, 0.5, 0.001]]), a), 0.001, places=7)
        self.assertLess(worker.directed_distance(worker.samples(a), a), 1e-7)
        self.assertGreater(worker.directed_distance(np.array([[100, 100, 100]], dtype=float), a), 1)
        frozen = np.zeros(len(original), dtype=bool)
        candidate = worker.decimate(original, 60, frozen, False)
        with patch.object(worker, "directed_distance", side_effect=[0, 0.1]) as distance:
            with self.assertRaisesRegex(ValueError, "surface error"):
                worker.validate(original, candidate, frozen, 0.0005, 0.005)
            self.assertEqual(distance.call_count, 2)

    def test_normalized_qem_across_scales(self):
        counts = []
        for size, offset in ((1, (0, 0, 0)), (0.001, (0, 0, 0)), (1, (1000, 1000, 1000))):
            original = plane(size=size, offset=offset)
            frozen = np.zeros(len(original), dtype=bool)
            candidate = worker.decimate(original, 60, frozen, False)
            worker.validate(original, candidate, frozen, size * 0.0005, 0.005)
            counts.append(len(candidate))
        self.assertEqual(len(set(counts)), 1)

    def test_selected_interface_actual_reduction_and_raw_retention(self):
        original = plane()
        frozen = np.arange(len(original)) < 16
        candidate = worker.decimate(original, 60, frozen, False)
        worker.validate(original, candidate, frozen, 0.0005, 0.005)
        self.assertLess(len(candidate), len(original))
        self.assertFalse(Counter(r.tobytes() for r in original[frozen]) -
                         Counter(r.tobytes() for r in candidate))
        with self.assertRaisesRegex(ValueError, "interface"):
            worker.validate(original, candidate, np.ones(len(original), dtype=bool), 0.0005, 0.005)

    def test_ambiguous_neighbor_does_not_block_local_selected_reduction(self):
        original = plane()
        duplicate = original[:1].copy()
        records = np.concatenate((original, duplicate))
        groups, frozen, valid = worker.partition(records)
        big = max(groups, key=len)
        self.assertTrue(frozen[big].any())
        self.assertTrue(valid.all())
        candidate = worker.decimate(records[big], 70, frozen[big], False)
        worker.validate(records[big], candidate, frozen[big], 0.0005, 0.005)
        self.assertLess(len(candidate), len(big))

    def test_degenerate_shared_point_freezes_neighbors(self):
        original = plane()
        degenerate = np.zeros(1, dtype=worker.STL_DTYPE)
        groups, frozen, valid = worker.partition(np.concatenate((original, degenerate)))
        self.assertEqual(len(groups), 2)
        self.assertTrue(frozen[:2].all())
        self.assertFalse(valid[-1])

    def test_interface_roundtrip_at_nonbinary_scale(self):
        original = plane(n=10, size=0.123, offset=(0, -0.00471, 0))
        frozen = np.arange(len(original)) < 20
        candidate = worker.decimate(original, 110, frozen, False)
        worker.validate(original, candidate, frozen, 0.0005, 0.005)
        self.assertFalse(Counter(r.tobytes() for r in original[frozen]) -
                         Counter(r.tobytes() for r in candidate))

    def test_invalid_candidates_rejected(self):
        original = plane()
        frozen = np.zeros(len(original), dtype=bool)
        candidate = worker.decimate(original, 60, frozen, False)
        for fault in ("duplicate", "degenerate", "nonfinite", "normal", "winding"):
            bad = candidate.copy()
            if fault == "duplicate":
                bad[1] = bad[0]
            elif fault == "degenerate":
                bad["v"][0] = 0
            elif fault == "nonfinite":
                bad["v"][0, 0, 0] = float("inf")
            elif fault == "normal":
                bad["normal"][0] *= -1
            else:
                bad["v"][0] = bad["v"][0, ::-1]
                bad["normal"][0] *= -1
            with self.subTest(fault=fault), self.assertRaises(ValueError):
                worker.validate(original, bad, frozen, 0.0005, 0.005)

    def test_closed_volume_guard(self):
        candidate = tetrahedron()
        original = np.zeros(12, dtype=worker.STL_DTYPE)
        for i, triangle in enumerate(candidate["v"]):
            center = triangle.mean(axis=0)
            original["v"][3 * i:3 * i + 3] = [[triangle[0], triangle[1], center],
                                                            [triangle[1], triangle[2], center],
                                                            [triangle[2], triangle[0], center]]
            original["normal"][3 * i:3 * i + 3] = candidate["normal"][i]
        candidate["v"] *= 0.95
        with patch.object(worker, "directed_distance", return_value=0):
            with self.assertRaisesRegex(ValueError, "volume"):
                worker.validate(original, candidate, np.zeros(12, dtype=bool), 1, 1)

    def test_deadline_prevents_native_distance_call(self):
        with patch.object(worker.ml, "MeshSet", side_effect=AssertionError("Unexpected native call")):
            with self.assertRaises(worker.BudgetExpired):
                worker.directed_distance(np.zeros((1, 3)), plane()["v"], time.monotonic() - 1)


if __name__ == "__main__":
    unittest.main()
