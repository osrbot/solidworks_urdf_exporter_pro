from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from pxr import Usd, UsdPhysics


ADAPTER_PATH = Path(__file__).resolve().parents[1] / "osurdf_usd_adapter.py"
REPOSITORY_ROOT = Path(__file__).resolve().parents[3]
FIXTURE_PATH = REPOSITORY_ROOT / "tests" / "fixtures" / "usd_bundle"
LOCKED_DOFS = ("transZ", "rotX", "rotY")
FREE_DOFS = ("transX", "transY", "rotZ")


def _load_adapter():
    spec = importlib.util.spec_from_file_location("osurdf_usd_planar_test", ADAPTER_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load adapter: {ADAPTER_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


adapter = _load_adapter()


class PlanarJointTests(unittest.TestCase):
    def _export_fixture(self, root: Path) -> tuple[Path, str, dict]:
        output = root / "usd"
        adapter.export_bundle(FIXTURE_PATH, output, overwrite=True)
        name_map = json.loads((output / "name_map.json").read_text(encoding="utf-8"))
        joint_path = "/Robot/Joints/" + name_map["joints"]["planar joint"]
        report = json.loads((output / "export_report.json").read_text(encoding="utf-8"))
        return output, joint_path, report

    def test_planar_joint_locks_only_out_of_plane_degrees_of_freedom(self):
        with tempfile.TemporaryDirectory(prefix="osurdf-usd-planar-test-") as temp:
            output, joint_path, report = self._export_fixture(Path(temp))
            stage = Usd.Stage.Open(str(output / "robot.usd"))
            self.assertIsNotNone(stage)
            prim = stage.GetPrimAtPath(joint_path)

            self.assertEqual("PhysicsJoint", prim.GetTypeName())
            for dof in LOCKED_DOFS:
                token = getattr(UsdPhysics.Tokens, dof)
                self.assertTrue(prim.HasAPI(UsdPhysics.LimitAPI, token), dof)
                limit_api = UsdPhysics.LimitAPI(prim, token)
                self.assertTrue(limit_api.GetLowAttr().HasAuthoredValueOpinion(), dof)
                self.assertTrue(limit_api.GetHighAttr().HasAuthoredValueOpinion(), dof)
                self.assertGreater(
                    limit_api.GetLowAttr().Get(),
                    limit_api.GetHighAttr().Get(),
                    dof,
                )

            for dof in FREE_DOFS:
                token = getattr(UsdPhysics.Tokens, dof)
                self.assertFalse(prim.HasAPI(UsdPhysics.LimitAPI, token), dof)

            self.assertEqual(1, report["validation"]["planarJointsValidated"])
            self.assertEqual([], report["physicsMappingNotes"]["unsupportedJointTypes"])

    def test_reopen_validation_rejects_a_broken_planar_lock(self):
        with tempfile.TemporaryDirectory(prefix="osurdf-usd-planar-validation-test-") as temp:
            output, joint_path, _report = self._export_fixture(Path(temp))
            stage_path = output / "robot.usd"
            stage = Usd.Stage.Open(str(stage_path))
            prim = stage.GetPrimAtPath(joint_path)
            limit_api = UsdPhysics.LimitAPI(prim, UsdPhysics.Tokens.rotY)
            limit_api.GetHighAttr().Set(1.0)
            stage.GetRootLayer().Save()

            with self.assertRaisesRegex(adapter.AdapterError, "low > high"):
                adapter._validate_stage(stage_path, 3, 2, [joint_path])

    def test_missing_limit_api_does_not_replace_existing_output(self):
        with tempfile.TemporaryDirectory(prefix="osurdf-usd-planar-fail-closed-test-") as temp:
            root = Path(temp)
            output = root / "usd"
            output.mkdir()
            sentinel = output / "existing.txt"
            sentinel.write_text("existing", encoding="utf-8")

            with mock.patch.object(adapter.UsdPhysics, "LimitAPI", None):
                with self.assertRaisesRegex(
                    adapter.AdapterError,
                    "cannot represent planar joint limits",
                ):
                    adapter.export_bundle(FIXTURE_PATH, output, overwrite=True)

            self.assertEqual("existing", sentinel.read_text(encoding="utf-8"))
            self.assertFalse((output / "robot.usd").exists())
            self.assertEqual([], list(root.glob(".osurdf-usd-*")))


if __name__ == "__main__":
    unittest.main()
