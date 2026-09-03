from __future__ import annotations

import importlib.util
import json
import math
import shutil
import tempfile
import unittest
from pathlib import Path

from pxr import Usd, UsdGeom, UsdPhysics


ADAPTER_PATH = Path(__file__).resolve().parents[1] / "osurdf_usd_adapter.py"
REPOSITORY_ROOT = Path(__file__).resolve().parents[3]
FIXTURE_PATH = REPOSITORY_ROOT / "tests" / "fixtures" / "usd_bundle"


def _load_adapter():
    spec = importlib.util.spec_from_file_location("osurdf_usd_simulation_test", ADAPTER_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load adapter: {ADAPTER_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _authored_api_schemas(prim: Usd.Prim) -> set[str]:
    value = prim.GetMetadata("apiSchemas")
    if value is None:
        return set()
    return {str(item) for item in value.GetAddedOrExplicitItems()}


adapter = _load_adapter()


class SimulationAssetTests(unittest.TestCase):
    def _copy_fixture(self, root: Path) -> Path:
        bundle = root / "bundle"
        shutil.copytree(FIXTURE_PATH, bundle)
        return bundle

    def test_safe_defaults_emit_a_referenceable_robot_asset_without_active_drives(self):
        with tempfile.TemporaryDirectory(prefix="osurdf-usd-safe-defaults-") as temp:
            root = Path(temp)
            output = root / "usd"
            adapter.export_bundle(FIXTURE_PATH, output, overwrite=True)

            entrypoint = output / "robot.usd"
            entrypoint_text = entrypoint.read_text(encoding="utf-8")
            self.assertTrue(entrypoint_text.startswith("#usda 1.0"))
            self.assertIn("@./geometry/", entrypoint_text)
            self.assertNotIn(str(output).replace("\\", "/"), entrypoint_text)
            stage = Usd.Stage.Open(str(output / "robot.usd"))
            self.assertIsNotNone(stage)
            prim_count = sum(1 for _ in stage.Traverse())
            robot = stage.GetDefaultPrim()
            self.assertEqual("/Robot", str(robot.GetPath()))
            self.assertIn("IsaacRobotAPI", _authored_api_schemas(robot))
            self.assertIn("PhysxArticulationAPI", _authored_api_schemas(robot))
            self.assertIn("NewtonArticulationRootAPI", _authored_api_schemas(robot))
            self.assertEqual("source", robot.GetAttribute("osurdf:baseMode").Get())
            self.assertEqual("Default", robot.GetAttribute("isaac:robotType").Get())
            self.assertFalse(robot.GetAttribute("physxArticulation:enabledSelfCollisions").Get())
            self.assertFalse(robot.GetAttribute("newton:selfCollisionEnabled").Get())
            self.assertFalse(robot.GetAttribute("osurdf:selfCollisionIntent").Get())
            self.assertFalse(stage.GetPrimAtPath("/Robot/Joints/fixed_base_joint"))

            relocated = root / "relocated"
            shutil.copytree(output, relocated)
            relocated_stage = Usd.Stage.Open(str(relocated / "robot.usd"))
            self.assertIsNotNone(relocated_stage)
            self.assertEqual(prim_count, sum(1 for _ in relocated_stage.Traverse()))

            report = json.loads((output / "export_report.json").read_text(encoding="utf-8"))
            self.assertEqual("UTF-8 USDA text", report["entrypointFormat"])
            name_map = json.loads((output / "name_map.json").read_text(encoding="utf-8"))
            arm_joint = stage.GetPrimAtPath(
                "/Robot/Joints/" + name_map["joints"]["arm joint"]
            )
            self.assertNotIn("PhysicsDriveAPI:angular", _authored_api_schemas(arm_joint))

    def test_explicit_settings_emit_robot_schema_collision_semantics_and_drive_data(self):
        with tempfile.TemporaryDirectory(prefix="osurdf-usd-explicit-settings-") as temp:
            root = Path(temp)
            bundle = self._copy_fixture(root)
            robot_json_path = bundle / "robot.json"
            document = json.loads(robot_json_path.read_text(encoding="utf-8"))
            document["schemaVersion"] = 3
            document["links"][0]["collisions"][0]["geometry"] = {
                "type": "mesh",
                "uri": "meshes/base.stl",
                "scale": {"x": 1.0, "y": 1.0, "z": 1.0},
            }
            document["links"] = [
                document["links"][1],
                document["links"][2],
                document["links"][0],
            ]
            document.setdefault("profiles", {})["usdSimulation"] = {
                "baseMode": "fixed",
                "robotType": "wheeled",
                "allowSelfCollision": True,
                "gainUnits": "SI",
                "jointDrives": [
                    {
                        "joint": "arm joint",
                        "mode": "position",
                        "stiffness": 120.0,
                        "damping": 8.0,
                    }
                ],
            }
            robot_json_path.write_text(
                json.dumps(document, ensure_ascii=False, indent=2) + "\n",
                encoding="utf-8",
            )

            output = root / "usd"
            adapter.export_bundle(bundle, output, overwrite=True)
            stage = Usd.Stage.Open(str(output / "robot.usd"))
            self.assertIsNotNone(stage)
            robot = stage.GetDefaultPrim()
            self.assertEqual("/Robot", str(robot.GetPath()))
            self.assertIn("IsaacRobotAPI", _authored_api_schemas(robot))
            self.assertIn("PhysxArticulationAPI", _authored_api_schemas(robot))
            self.assertIn("NewtonArticulationRootAPI", _authored_api_schemas(robot))
            self.assertEqual("fixed", robot.GetAttribute("osurdf:baseMode").Get())
            self.assertEqual("Wheeled", robot.GetAttribute("isaac:robotType").Get())
            self.assertTrue(robot.GetAttribute("physxArticulation:enabledSelfCollisions").Get())
            self.assertTrue(robot.GetAttribute("newton:selfCollisionEnabled").Get())
            self.assertTrue(robot.GetAttribute("osurdf:selfCollisionIntent").Get())

            link_targets = robot.GetRelationship("isaac:physics:robotLinks").GetTargets()
            joint_targets = robot.GetRelationship("isaac:physics:robotJoints").GetTargets()
            self.assertEqual(3, len(link_targets))
            self.assertEqual(3, len(joint_targets))
            name_map = json.loads((output / "name_map.json").read_text(encoding="utf-8"))
            self.assertEqual(
                "/Robot/Links/" + name_map["links"]["base link"],
                str(link_targets[0]),
            )
            for path in link_targets:
                self.assertIn("IsaacLinkAPI", _authored_api_schemas(stage.GetPrimAtPath(path)))
            for path in joint_targets:
                self.assertIn("IsaacJointAPI", _authored_api_schemas(stage.GetPrimAtPath(path)))
                self.assertTrue(stage.GetPrimAtPath(path).GetAttribute("isaac:NameOverride"))

            fixed_joint = stage.GetPrimAtPath("/Robot/Joints/fixed_base_joint")
            self.assertTrue(fixed_joint)
            self.assertTrue(fixed_joint.IsA(UsdPhysics.FixedJoint))
            self.assertEqual(1, len(UsdPhysics.Joint(fixed_joint).GetBody1Rel().GetTargets()))

            base_collision = stage.GetPrimAtPath(
                "/Robot/Links/"
                + name_map["links"]["base link"]
                + "/Collisions/collision_0"
            )
            self.assertEqual(UsdGeom.Tokens.guide, UsdGeom.Imageable(base_collision).GetPurposeAttr().Get())
            self.assertTrue(base_collision.HasAPI(UsdPhysics.CollisionAPI))
            self.assertTrue(base_collision.HasAPI(UsdPhysics.MeshCollisionAPI))
            self.assertEqual(
                UsdPhysics.Tokens.none,
                UsdPhysics.MeshCollisionAPI(base_collision).GetApproximationAttr().Get(),
            )
            self.assertTrue(base_collision.IsA(UsdGeom.Mesh))
            mesh = UsdGeom.Mesh(base_collision)
            self.assertGreater(len(mesh.GetPointsAttr().Get()), 0)
            self.assertGreater(len(mesh.GetFaceVertexIndicesAttr().Get()), 0)
            references = base_collision.GetMetadata("references").GetAddedOrExplicitItems()
            self.assertEqual(1, len(references))
            self.assertEqual("/Asset/Mesh", str(references[0].primPath))

            arm_joint = stage.GetPrimAtPath(
                "/Robot/Joints/" + name_map["joints"]["arm joint"]
            )
            drive = UsdPhysics.DriveAPI(arm_joint, UsdPhysics.Tokens.angular)
            self.assertTrue(arm_joint.HasAPI(UsdPhysics.DriveAPI, UsdPhysics.Tokens.angular))
            self.assertAlmostEqual(
                120.0 * math.pi / 180.0, drive.GetStiffnessAttr().Get(), places=6
            )
            self.assertAlmostEqual(
                8.0 * math.pi / 180.0, drive.GetDampingAttr().Get(), places=6
            )
            self.assertEqual(1.0, drive.GetMaxForceAttr().Get())
            self.assertEqual(0.0, drive.GetTargetPositionAttr().Get())
            self.assertAlmostEqual(
                math.degrees(1.0),
                arm_joint.GetAttribute("physxJoint:maxJointVelocity").Get(),
                places=5,
            )
            self.assertEqual("position", arm_joint.GetAttribute("osurdf:driveIntent").Get())

            report = json.loads((output / "export_report.json").read_text(encoding="utf-8"))
            self.assertEqual(
                1.0, report["simulationSettings"]["jointDrives"][0]["maxForce"]
            )
            self.assertEqual("fixed", report["simulationSettings"]["baseMode"])
            self.assertEqual("generated-world-fixed-joint", report["baseResolution"])
            self.assertEqual(1, report["validation"]["configuredJointIntents"])
            self.assertEqual(1, report["validation"]["configuredDrives"])
            self.assertEqual(0, report["validation"]["meshCollisionApproximations"])
            self.assertEqual(1, report["validation"]["preprocessedMeshCollisions"])
            self.assertEqual("SI", report["simulationSettings"]["gainUnits"])
            self.assertEqual(
                "authored, downstream validation not run",
                report["schemaAuthoring"]["status"],
            )
            self.assertFalse(report["schemaAuthoring"]["downstreamValidationRun"])

    def test_self_collision_requires_a_json_boolean(self):
        with tempfile.TemporaryDirectory(prefix="osurdf-usd-invalid-bool-") as temp:
            root = Path(temp)
            bundle = self._copy_fixture(root)
            robot_json_path = bundle / "robot.json"
            document = json.loads(robot_json_path.read_text(encoding="utf-8"))
            document.setdefault("profiles", {})["usdSimulation"] = {
                "allowSelfCollision": "false"
            }
            robot_json_path.write_text(
                json.dumps(document, ensure_ascii=False, indent=2) + "\n",
                encoding="utf-8",
            )

            with self.assertRaisesRegex(
                adapter.AdapterError,
                "allowSelfCollision must be a boolean",
            ):
                adapter.export_bundle(bundle, root / "usd", overwrite=True)

    def test_v3_requires_explicit_si_gain_units(self):
        with tempfile.TemporaryDirectory(prefix="osurdf-usd-v3-gain-units-") as temp:
            root = Path(temp)
            bundle = self._copy_fixture(root)
            robot_json_path = bundle / "robot.json"
            document = json.loads(robot_json_path.read_text(encoding="utf-8"))
            document["schemaVersion"] = 3
            document.setdefault("profiles", {})["usdSimulation"] = {
                "baseMode": "source",
                "robotType": "default",
                "allowSelfCollision": False,
                "jointDrives": [],
            }
            robot_json_path.write_text(
                json.dumps(document, ensure_ascii=False, indent=2) + "\n",
                encoding="utf-8",
            )

            with self.assertRaisesRegex(adapter.AdapterError, "requires.*gainUnits"):
                adapter.export_bundle(bundle, root / "missing", overwrite=True)

            document["profiles"]["usdSimulation"]["gainUnits"] = "degrees"
            robot_json_path.write_text(
                json.dumps(document, ensure_ascii=False, indent=2) + "\n",
                encoding="utf-8",
            )
            with self.assertRaisesRegex(adapter.AdapterError, "gainUnits must be SI"):
                adapter.export_bundle(bundle, root / "wrong", overwrite=True)

    def test_effort_is_intent_only_while_velocity_authors_an_active_drive(self):
        for mode, active in (("effort", False), ("velocity", True)):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory(
                prefix=f"osurdf-usd-{mode}-"
            ) as temp:
                root = Path(temp)
                bundle = self._copy_fixture(root)
                robot_json_path = bundle / "robot.json"
                document = json.loads(robot_json_path.read_text(encoding="utf-8"))
                document.setdefault("profiles", {})["usdSimulation"] = {
                    "baseMode": "floating",
                    "jointDrives": [
                        {
                            "joint": "arm joint",
                            "mode": mode,
                            **(
                                {"stiffness": 0.0, "damping": 2.0}
                                if mode == "velocity"
                                else {}
                            ),
                        }
                    ],
                }
                robot_json_path.write_text(
                    json.dumps(document, ensure_ascii=False, indent=2) + "\n",
                    encoding="utf-8",
                )

                output = root / "usd"
                adapter.export_bundle(bundle, output, overwrite=True)
                stage = Usd.Stage.Open(str(output / "robot.usd"))
                name_map = json.loads(
                    (output / "name_map.json").read_text(encoding="utf-8")
                )
                joint = stage.GetPrimAtPath(
                    "/Robot/Joints/" + name_map["joints"]["arm joint"]
                )
                self.assertEqual(mode, joint.GetAttribute("osurdf:driveIntent").Get())
                self.assertEqual(
                    active,
                    joint.HasAPI(UsdPhysics.DriveAPI, UsdPhysics.Tokens.angular),
                )
                if mode == "effort":
                    metadata = joint.GetCustomDataByKey("osurdf")
                    self.assertEqual("effort", metadata["driveIntent"])
                    self.assertEqual(1.0, metadata["effortLimit"])
                    self.assertEqual("N*m", metadata["effortLimitUnits"])
                else:
                    drive = UsdPhysics.DriveAPI(joint, UsdPhysics.Tokens.angular)
                    self.assertEqual(0.0, drive.GetStiffnessAttr().Get())
                    self.assertAlmostEqual(
                        2.0 * math.pi / 180.0, drive.GetDampingAttr().Get()
                    )
                    self.assertEqual(1.0, drive.GetMaxForceAttr().Get())
                report = json.loads(
                    (output / "export_report.json").read_text(encoding="utf-8")
                )
                self.assertEqual("mobile-no-world-joint", report["baseResolution"])
                self.assertEqual(1, report["validation"]["configuredJointIntents"])
                self.assertEqual(1 if active else 0, report["validation"]["activeDrives"])
                self.assertEqual(
                    1 if mode == "effort" else 0,
                    report["validation"]["effortLimitsPreserved"],
                )
                if mode == "effort":
                    configured = report["simulationSettings"]["jointDrives"][0]
                    self.assertEqual(1.0, configured["effortLimit"])
                    self.assertEqual("N*m", configured["effortLimitUnits"])

    def test_passive_and_effort_intents_reject_inactive_gains(self):
        for mode in ("passive", "effort"):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory(
                prefix=f"osurdf-usd-{mode}-gain-"
            ) as temp:
                root = Path(temp)
                bundle = self._copy_fixture(root)
                robot_json_path = bundle / "robot.json"
                document = json.loads(robot_json_path.read_text(encoding="utf-8"))
                document.setdefault("profiles", {})["usdSimulation"] = {
                    "jointDrives": [
                        {
                            "joint": "arm joint",
                            "mode": mode,
                            "stiffness": 10.0,
                        }
                    ]
                }
                robot_json_path.write_text(
                    json.dumps(document, ensure_ascii=False, indent=2) + "\n",
                    encoding="utf-8",
                )

                with self.assertRaisesRegex(
                    adapter.AdapterError,
                    "may use stiffness/damping only with position or velocity",
                ):
                    adapter.export_bundle(bundle, root / "usd", overwrite=True)

    def test_velocity_drive_rejects_nonzero_stiffness(self):
        with tempfile.TemporaryDirectory(prefix="osurdf-usd-velocity-stiffness-") as temp:
            root = Path(temp)
            bundle = self._copy_fixture(root)
            robot_json_path = bundle / "robot.json"
            document = json.loads(robot_json_path.read_text(encoding="utf-8"))
            document.setdefault("profiles", {})["usdSimulation"] = {
                "jointDrives": [
                    {
                        "joint": "arm joint",
                        "mode": "velocity",
                        "stiffness": 0.01,
                    }
                ]
            }
            robot_json_path.write_text(
                json.dumps(document, ensure_ascii=False, indent=2) + "\n",
                encoding="utf-8",
            )

            with self.assertRaisesRegex(adapter.AdapterError, "stiffness must be zero"):
                adapter.export_bundle(bundle, root / "usd", overwrite=True)

    def test_linear_drive_gains_remain_in_si_without_angular_conversion(self):
        with tempfile.TemporaryDirectory(prefix="osurdf-usd-linear-gains-") as temp:
            root = Path(temp)
            bundle = self._copy_fixture(root)
            robot_json_path = bundle / "robot.json"
            document = json.loads(robot_json_path.read_text(encoding="utf-8"))
            arm_joint = next(
                joint for joint in document["joints"] if joint["name"] == "arm joint"
            )
            arm_joint["type"] = "prismatic"
            document.setdefault("profiles", {})["usdSimulation"] = {
                "jointDrives": [
                    {
                        "joint": "arm joint",
                        "mode": "position",
                        "stiffness": 5.0,
                        "damping": 2.0,
                    }
                ]
            }
            robot_json_path.write_text(
                json.dumps(document, ensure_ascii=False, indent=2) + "\n",
                encoding="utf-8",
            )

            output = root / "usd"
            adapter.export_bundle(bundle, output, overwrite=True)
            stage = Usd.Stage.Open(str(output / "robot.usd"))
            name_map = json.loads((output / "name_map.json").read_text(encoding="utf-8"))
            joint = stage.GetPrimAtPath(
                "/Robot/Joints/" + name_map["joints"]["arm joint"]
            )
            drive = UsdPhysics.DriveAPI(joint, UsdPhysics.Tokens.linear)
            self.assertEqual(5.0, drive.GetStiffnessAttr().Get())
            self.assertEqual(2.0, drive.GetDampingAttr().Get())
            self.assertEqual(1.0, drive.GetMaxForceAttr().Get())

    def test_joint_drive_container_and_entries_fail_closed(self):
        invalid_containers = (False, 0, "drive", None)
        for value in invalid_containers:
            with self.subTest(container=value), tempfile.TemporaryDirectory(
                prefix="osurdf-usd-invalid-drive-container-"
            ) as temp:
                root = Path(temp)
                bundle = self._copy_fixture(root)
                robot_json_path = bundle / "robot.json"
                document = json.loads(robot_json_path.read_text(encoding="utf-8"))
                document.setdefault("profiles", {})["usdSimulation"] = {
                    "jointDrives": value
                }
                robot_json_path.write_text(
                    json.dumps(document, ensure_ascii=False, indent=2) + "\n",
                    encoding="utf-8",
                )
                with self.assertRaisesRegex(adapter.AdapterError, "must be an array"):
                    adapter.export_bundle(bundle, root / "usd", overwrite=True)

        for value in (False, 0, "drive", None):
            with self.subTest(entry=value), tempfile.TemporaryDirectory(
                prefix="osurdf-usd-invalid-drive-entry-"
            ) as temp:
                root = Path(temp)
                bundle = self._copy_fixture(root)
                robot_json_path = bundle / "robot.json"
                document = json.loads(robot_json_path.read_text(encoding="utf-8"))
                document.setdefault("profiles", {})["usdSimulation"] = {
                    "jointDrives": [value]
                }
                robot_json_path.write_text(
                    json.dumps(document, ensure_ascii=False, indent=2) + "\n",
                    encoding="utf-8",
                )
                with self.assertRaisesRegex(adapter.AdapterError, "must be an object"):
                    adapter.export_bundle(bundle, root / "usd", overwrite=True)


if __name__ == "__main__":
    unittest.main()
