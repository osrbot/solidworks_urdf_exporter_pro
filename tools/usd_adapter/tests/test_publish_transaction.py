from __future__ import annotations

import importlib.util
import io
import json
import sys
import tempfile
import types
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest import mock


def _load_adapter():
    pxr = types.ModuleType("pxr")
    for name in ("Gf", "Sdf", "Usd", "UsdGeom", "UsdPhysics", "UsdShade"):
        setattr(pxr, name, types.ModuleType(f"pxr.{name}"))

    adapter_path = Path(__file__).resolve().parents[1] / "osurdf_usd_adapter.py"
    spec = importlib.util.spec_from_file_location("osurdf_usd_adapter_under_test", adapter_path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load adapter: {adapter_path}")
    module = importlib.util.module_from_spec(spec)
    with mock.patch.dict(sys.modules, {"pxr": pxr}):
        spec.loader.exec_module(module)
    return module


adapter = _load_adapter()


class PublishTransactionTests(unittest.TestCase):
    def test_cli_protocol_escapes_unicode_paths_as_ascii_json(self):
        result = {
            "ok": True,
            "usd": r"E:\桌面\机器人\robot.usd",
            "nameMap": r"E:\桌面\机器人\name_map.json",
            "report": r"E:\桌面\机器人\export_report.json",
        }
        output = io.StringIO()

        with mock.patch.object(adapter, "export_bundle", return_value=result), redirect_stdout(
            output
        ):
            exit_code = adapter.main(
                ["export", "--bundle", "bundle", "--output", "output"]
            )

        protocol_line = output.getvalue().strip()
        self.assertEqual(0, exit_code)
        self.assertTrue(protocol_line.isascii())
        self.assertEqual(result, json.loads(protocol_line))

    @staticmethod
    def _make_bundle(root: Path) -> Path:
        bundle = root / "bundle"
        bundle.mkdir()
        (bundle / "robot.json").write_text(
            json.dumps({"links": [], "joints": []}),
            encoding="utf-8",
        )
        return bundle

    @staticmethod
    def _build_stage(_bundle: Path, staging: Path, _robot):
        stage = staging / "robot.usd"
        stage.write_text("#usda 1.0", encoding="utf-8")
        (staging / "new.txt").write_text("new", encoding="utf-8")
        return {
            "stage": str(stage),
            "linkNames": {},
            "jointNames": {},
            "meshDependencies": [],
            "planarJointPaths": [],
            "unsupportedPhysicsJointTypes": [],
        }

    @staticmethod
    def _validation(*_args):
        return {
            "ok": True,
            "stageReopened": True,
            "openUsdVersion": "0.26.8",
        }

    def test_publish_and_restore_failure_preserves_only_previous_output(self):
        with tempfile.TemporaryDirectory(prefix="osurdf-usd-publish-test-") as temp:
            root = Path(temp)
            bundle = self._make_bundle(root)
            output = root / "usd"
            output.mkdir()
            (output / "old.txt").write_text("old", encoding="utf-8")

            original_rename = Path.rename

            def fail_publish_and_restore(path: Path, target: Path):
                source = Path(path)
                if source.name.startswith(".osurdf-usd-"):
                    raise OSError("injected publish failure")
                if source.name.startswith(output.name + ".previous-"):
                    raise OSError("injected restore failure")
                return original_rename(source, target)

            with mock.patch.object(adapter, "_copy_canonical_meshes"), mock.patch.object(
                adapter, "_build_stage", side_effect=self._build_stage
            ), mock.patch.object(
                adapter, "_validate_stage", side_effect=self._validation
            ), mock.patch.object(Path, "rename", new=fail_publish_and_restore):
                with self.assertRaises(adapter.AdapterError) as raised:
                    adapter.export_bundle(bundle, output, overwrite=True)

            previous = list(root.glob("usd.previous-*"))
            self.assertEqual(1, len(previous))
            self.assertFalse(output.exists())
            self.assertEqual("old", (previous[0] / "old.txt").read_text(encoding="utf-8"))
            self.assertIn(str(previous[0]), str(raised.exception))
            self.assertEqual([], list(root.glob(".osurdf-usd-*")))

    def test_cleanup_failure_keeps_success_and_reports_retained_previous(self):
        with tempfile.TemporaryDirectory(prefix="osurdf-usd-cleanup-test-") as temp:
            root = Path(temp)
            bundle = self._make_bundle(root)
            output = root / "usd"
            output.mkdir()
            (output / "old.txt").write_text("old", encoding="utf-8")

            original_rmtree = adapter.shutil.rmtree

            def fail_previous_cleanup(path: Path, *args, **kwargs):
                candidate = Path(path)
                if candidate.name.startswith(output.name + ".previous-"):
                    raise PermissionError("injected cleanup failure")
                return original_rmtree(candidate, *args, **kwargs)

            with mock.patch.object(adapter, "_copy_canonical_meshes"), mock.patch.object(
                adapter, "_build_stage", side_effect=self._build_stage
            ), mock.patch.object(
                adapter, "_validate_stage", side_effect=self._validation
            ), mock.patch.object(
                adapter.shutil, "rmtree", side_effect=fail_previous_cleanup
            ):
                result = adapter.export_bundle(bundle, output, overwrite=True)

            retained = Path(result["retainedPreviousDirectory"])
            self.assertTrue(result["ok"])
            self.assertIsNotNone(retained)
            self.assertEqual("new", (output / "new.txt").read_text(encoding="utf-8"))
            self.assertEqual("old", (retained / "old.txt").read_text(encoding="utf-8"))

            published_report = json.loads(
                (output / "export_report.json").read_text(encoding="utf-8")
            )
            self.assertEqual(str(retained), published_report["retainedPreviousDirectory"])


if __name__ == "__main__":
    unittest.main()
