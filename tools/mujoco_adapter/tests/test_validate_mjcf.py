from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from validate_mjcf import OfficialRuntimeUnavailable, validate_models


class ValidateMjcfTests(unittest.TestCase):
    def test_reports_an_injected_compile_pass(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            model = Path(temporary) / "robot.xml"
            model.write_text("<mujoco/>", encoding="utf-8")

            report = validate_models(
                [model],
                lambda _: {
                    "muJoCoVersion": "test-version",
                    "bodies": 2,
                    "joints": 1,
                    "geoms": 3,
                },
            )

        self.assertEqual("passed", report["status"])
        self.assertEqual("test-version", report["muJoCoVersion"])
        self.assertEqual("passed", report["models"][0]["status"])

    def test_distinguishes_unavailable_runtime_from_compile_failure(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            model = Path(temporary) / "robot.xml"
            model.write_text("<mujoco/>", encoding="utf-8")

            def unavailable(_: Path) -> dict[str, object]:
                raise OfficialRuntimeUnavailable("not installed")

            unavailable_report = validate_models([model], unavailable)
            failed_report = validate_models(
                [model],
                lambda _: (_ for _ in ()).throw(ValueError("compiler diagnostic")),
            )

        self.assertEqual("unavailable", unavailable_report["status"])
        self.assertEqual("failed", failed_report["status"])
        self.assertIn("compiler diagnostic", failed_report["models"][0]["message"])


if __name__ == "__main__":
    unittest.main()
