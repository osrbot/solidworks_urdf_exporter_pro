from __future__ import annotations

import copy
import json
import math
import shutil
import tempfile
import unittest
from pathlib import Path

from pxr import Usd, UsdPhysics

from test_simulation_asset import FIXTURE_PATH, adapter


class CommonSimulationTests(unittest.TestCase):
    def setUp(self):
        self.robot = json.loads((FIXTURE_PATH / "robot.json").read_text(encoding="utf-8"))
        self.robot["schemaVersion"] = 3
        self.robot["profiles"]["usdSimulation"] = {
            "baseMode": "fixed", "robotType": "wheeled",
            "allowSelfCollision": True, "gainUnits": "SI",
            "jointDrives": [{"joint": "arm joint", "mode": "position",
                             "stiffness": 120.0, "damping": 8.0}],
        }

    def resolve(self):
        return adapter._simulation_settings(self.robot, self.robot["joints"])

    def test_absent_and_null_preserve_legacy(self):
        expected = self.resolve()
        self.robot["profiles"]["simulation"] = None
        self.assertEqual(expected, self.resolve())

    def test_present_empty_common_clears_legacy_drives_but_preserves_source_base(self):
        expected = self.resolve()
        expected["jointDrives"] = []
        for common in ({}, {"jointDrives": []}, {"baseMode": "source", "jointDrives": []}):
            with self.subTest(common=common):
                self.robot["profiles"]["simulation"] = common
                self.assertEqual(expected, self.resolve())

    def test_common_list_does_not_retain_omitted_active_joint(self):
        joint = copy.deepcopy(next(j for j in self.robot["joints"] if j["name"] == "arm joint"))
        joint["name"] = "second joint"
        self.robot["joints"].append(joint)
        self.robot["profiles"]["simulation"] = {
            "jointDrives": [{"joint": "second joint", "mode": "effort"}]}
        self.assertEqual(["second joint"], [d["joint"] for d in self.resolve()["jointDrives"]])

    def test_active_common_mimic_is_rejected(self):
        joint = next(j for j in self.robot["joints"] if j["name"] == "arm joint")
        for mimic in ({}, {"joint": "other joint", "multiplier": 1.0, "offset": 0.0}):
            joint["mimic"] = mimic
            for mode in ("position", "velocity", "effort"):
                with self.subTest(mimic=mimic, mode=mode):
                    self.robot["profiles"]["simulation"] = {
                        "jointDrives": [{"joint": "arm joint", "mode": mode}]}
                    with self.assertRaisesRegex(adapter.AdapterError, "Mimic Joint"):
                        self.resolve()

    def test_overrides_preserve_target_options_and_do_not_mutate_input(self):
        for mode in ("passive", "position", "velocity", "effort"):
            with self.subTest(mode=mode):
                self.robot["profiles"]["simulation"] = {
                    "baseMode": "floating",
                    "jointDrives": [{"joint": "arm joint", "mode": mode}],
                    "mjcf": {"jointDrives": [{"joint": "arm joint", "kp": 999, "kv": 50}]},
                }
                before = copy.deepcopy(self.robot)
                settings = self.resolve()
                self.assertEqual(before, self.robot)
                self.assertEqual("floating", settings["baseMode"])
                self.assertEqual("wheeled", settings["robotType"])
                self.assertTrue(settings["allowSelfCollision"])
                if mode == "passive":
                    self.assertEqual([], settings["jointDrives"])
                    continue
                drive = settings["jointDrives"][0]
                self.assertEqual(mode, drive["mode"])
                if mode in ("position", "velocity"):
                    self.assertEqual(8.0, drive["damping"])
                    self.assertEqual(120.0 if mode == "position" else 0.0, drive["stiffness"])
                else:
                    self.assertNotIn("stiffness", drive)
                    self.assertNotIn("damping", drive)

    def test_common_can_add_drive_without_legacy_entry_in_v2(self):
        self.robot["schemaVersion"] = 2
        del self.robot["profiles"]["usdSimulation"]
        self.robot["profiles"]["simulation"] = {
            "baseMode": "fixed", "jointDrives": [{"joint": "arm joint", "mode": "effort"}]}
        settings = self.resolve()
        self.assertEqual("fixed", settings["baseMode"])
        self.assertEqual("effort", settings["jointDrives"][0]["mode"])
        self.assertEqual(1.0, settings["jointDrives"][0]["effortLimit"])

    def test_common_malformed_data_fails_closed(self):
        entries = [None, False, 1, "drive", {}, {"joint": "arm joint", "mode": []},
                   {"joint": [], "mode": "passive"}, {"joint": "missing", "mode": "passive"},
                   {"joint": "arm joint", "mode": "motor"}]
        invalid = [False, 0, "", [], {"baseMode": None}, {"baseMode": []},
                   {"baseMode": "invalid"}]
        invalid += [{"jointDrives": value} for value in (None, False, 0, "", {})]
        invalid += [{"jointDrives": [value]} for value in entries]
        entry = {"joint": "arm joint", "mode": "passive"}
        invalid.append({"jointDrives": [entry, entry]})
        for common in invalid:
            with self.subTest(common=common):
                self.robot["profiles"]["simulation"] = common
                with self.assertRaises(adapter.AdapterError):
                    self.resolve()

    def test_overrides_do_not_hide_malformed_legacy_gains(self):
        self.robot["profiles"]["simulation"] = {
            "jointDrives": [{"joint": "arm joint", "mode": "passive"}]}
        for value in (None, True, "1", -1, math.nan, math.inf):
            with self.subTest(value=value):
                self.robot["profiles"]["usdSimulation"]["jointDrives"][0]["damping"] = value
                with self.assertRaises(adapter.AdapterError):
                    self.resolve()

    def test_common_rejects_non_single_dof_and_requires_effort_for_active_modes(self):
        self.robot["profiles"]["usdSimulation"]["jointDrives"] = []
        joint = next(j for j in self.robot["joints"] if j["name"] == "arm joint")
        for joint_type in ("fixed", "floating", "planar"):
            with self.subTest(joint_type=joint_type):
                joint["type"] = joint_type
                self.robot["profiles"]["simulation"] = {
                    "jointDrives": [{"joint": "arm joint", "mode": "position"}]}
                with self.assertRaises(adapter.AdapterError):
                    self.resolve()
        joint["type"] = "revolute"
        for mode in ("position", "velocity", "effort"):
            for effort in (None, 0, -1, True, math.inf, math.nan):
                with self.subTest(mode=mode, effort=effort):
                    joint["limit"]["effort"] = effort
                    self.robot["profiles"]["simulation"] = {
                        "jointDrives": [{"joint": "arm joint", "mode": mode}]}
                    with self.assertRaises(adapter.AdapterError):
                        self.resolve()

    def test_export_common_modes_and_si_gains(self):
        for joint_type in ("revolute", "prismatic"):
            for mode in ("passive", "position", "velocity", "effort"):
                with self.subTest(joint_type=joint_type, mode=mode), tempfile.TemporaryDirectory() as temp:
                    root = Path(temp)
                    bundle = root / "bundle"
                    shutil.copytree(FIXTURE_PATH, bundle)
                    next(j for j in self.robot["joints"] if j["name"] == "arm joint")["type"] = joint_type
                    self.robot["profiles"]["simulation"] = {
                        "baseMode": "floating",
                        "jointDrives": [{"joint": "arm joint", "mode": mode}] + [
                            {"joint": j["name"], "mode": "passive"}
                            for j in self.robot["joints"] if j["type"] == "fixed"]}
                    (bundle / "robot.json").write_text(json.dumps(self.robot), encoding="utf-8")
                    output = root / "usd"
                    adapter.export_bundle(bundle, output, overwrite=True)
                    stage = Usd.Stage.Open(str(output / "robot.usd"))
                    names = json.loads((output / "name_map.json").read_text(encoding="utf-8"))
                    prim = stage.GetPrimAtPath("/Robot/Joints/" + names["joints"]["arm joint"])
                    dof = "angular" if joint_type == "revolute" else "linear"
                    self.assertEqual(mode in ("position", "velocity"), prim.HasAPI(UsdPhysics.DriveAPI, dof))
                    self.assertFalse(stage.GetPrimAtPath("/Robot/Joints/fixed_base_joint"))
                    if mode in ("position", "velocity"):
                        drive = UsdPhysics.DriveAPI(prim, dof)
                        scale = math.pi / 180 if dof == "angular" else 1
                        self.assertAlmostEqual(8 * scale, drive.GetDampingAttr().Get(), places=6)
                        self.assertAlmostEqual((120 if mode == "position" else 0) * scale,
                                               drive.GetStiffnessAttr().Get(), places=6)
                    report = json.loads((output / "export_report.json").read_text(encoding="utf-8"))
                    if mode == "passive":
                        self.assertEqual([], report["simulationSettings"]["jointDrives"])
                        self.assertFalse(prim.GetAttribute("osurdf:driveIntent"))
                    else:
                        self.assertEqual(mode, report["simulationSettings"]["jointDrives"][0]["mode"])
                    self.assertEqual(1 if mode == "effort" else 0,
                                     report["validation"]["effortLimitsPreserved"])

    def test_common_passive_accepts_existing_fixed_and_mimic_joints(self):
        self.robot["profiles"]["usdSimulation"]["jointDrives"] = []
        joint = next(j for j in self.robot["joints"] if j["name"] == "arm joint")
        for joint_type in ("fixed", "floating", "planar", "revolute"):
            with self.subTest(joint_type=joint_type):
                joint["type"] = joint_type
                if joint_type == "revolute":
                    joint["mimic"] = {"joint": "other joint", "multiplier": 1.0, "offset": 0.0}
                entry = {"joint": "arm joint", "mode": "passive"}
                self.robot["profiles"]["simulation"] = {"jointDrives": [entry]}
                self.assertEqual([], self.resolve()["jointDrives"])
                self.robot["profiles"]["simulation"]["jointDrives"].append(entry)
                with self.assertRaisesRegex(adapter.AdapterError, "duplicated"):
                    self.resolve()

    def test_common_passive_does_not_hide_invalid_legacy_fixed_drive(self):
        joint = next(j for j in self.robot["joints"] if j["name"] == "arm joint")
        joint["type"] = "fixed"
        entry = {"joint": "arm joint", "mode": "passive"}
        self.robot["profiles"]["usdSimulation"]["jointDrives"] = [entry]
        for common in (None, {"jointDrives": [entry]}):
            with self.subTest(common=common):
                self.robot["profiles"]["simulation"] = common
                with self.assertRaisesRegex(adapter.AdapterError, "USD joint drive.*non-one-DOF"):
                    self.resolve()


if __name__ == "__main__":
    unittest.main()
