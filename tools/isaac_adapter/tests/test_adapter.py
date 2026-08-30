import importlib.util
import json
import py_compile
import sys
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).parents[1] / "osurdf_isaac_adapter.py"
SPEC = importlib.util.spec_from_file_location("osurdf_isaac_adapter", MODULE_PATH)
assert SPEC and SPEC.loader
adapter = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = adapter
SPEC.loader.exec_module(adapter)


def isaac_profile(**overrides):
    value = {
        "schemaVersion": 1,
        "enabled": True,
        "isaacSimVersion": "6.0.0",
        "robotType": "Default",
        "baseType": "Source",
        "mergeMesh": True,
        "mergeFixedJoints": False,
        "allowSelfCollision": False,
        "collisionFromVisuals": False,
        "collisionType": "convex_hull",
        "debugMode": False,
        "runAssetTransformer": False,
        "runMultiPhysicsConversion": False,
        "packageMappings": {},
    }
    value.update(overrides)
    return value


def physics_profile():
    return {
        "enabledSelfCollisions": False,
        "solverPositionIterationCount": 8,
        "solverVelocityIterationCount": 2,
        "enableGyroscopicForces": True,
        "maxDepenetrationVelocity": 5.0,
    }


class AdapterTests(unittest.TestCase):
    def test_json_reader_rejects_duplicate_keys_and_nonfinite_numbers(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "fixture.json"
            for payload, message in (
                ('{"enabled": true, "enabled": false}', "Duplicate JSON property"),
                ('{"value": NaN}', "Non-finite JSON number"),
                ('{"value": Infinity}', "Non-finite JSON number"),
            ):
                with self.subTest(payload=payload):
                    path.write_text(payload, encoding="utf-8")
                    with self.assertRaisesRegex(adapter.AdapterError, message):
                        adapter._read_json(path)

    def test_bundle_profiles_require_exact_values_and_boolean_flags(self):
        adapter._require_matching_profile("isaac", {"enabled": False}, {"enabled": False})
        with self.assertRaisesRegex(adapter.AdapterError, "does not match"):
            adapter._require_matching_profile(
                "isaac",
                {"enabled": False},
                {"enabled": False, "optional": None},
            )
        self.assertFalse(adapter._profile_enabled({"enabled": False}, "ros1"))
        with self.assertRaisesRegex(adapter.AdapterError, "boolean enabled"):
            adapter._profile_enabled({"enabled": "false"}, "ros1")

    def test_importer_request_uses_directory_official_collision_names_and_package_mappings(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "packages" / "fixture").mkdir(parents=True)
            context = adapter.BundleContext(
                root=root,
                manifest={},
                robot={},
                isaac={
                    "baseType": "Fixed",
                    "collisionType": "convex_decomposition",
                    "packageMappings": {"fixture": "packages/fixture"},
                },
                isaac_lab={},
            )
            output = root / "converted"
            request = adapter._build_importer_request(context, root / "robot.urdf", output)
            self.assertEqual(str(output), request["usd_path"])
            self.assertEqual("Convex Decomposition", request["collision_type"])
            self.assertEqual(
                [{"name": "fixture", "path": str((root / "packages" / "fixture").resolve())}],
                request["ros_package_paths"],
            )
            self.assertIs(True, request["fix_base"])

    def test_isaac_version_tuple_is_exact_and_consistent(self):
        self.assertEqual("6.0.0", adapter._isaac_version_from_parts(("6.0.0", "", "6", "0", "0", "", "", "")))
        with self.assertRaisesRegex(adapter.AdapterError, "inconsistent"):
            adapter._isaac_version_from_parts(("6.0.0", "", "5", "1", "0", "", "", ""))
        with self.assertRaisesRegex(adapter.AdapterError, "unrecognized"):
            adapter._isaac_version_from_parts(("development",))

    def test_profile_validation_rejects_unknown_collision_token(self):
        context = adapter.BundleContext(
            root=Path("."),
            manifest={},
            robot={"metadata": {"modelLicense": "Apache-2.0"}, "links": [], "joints": []},
            isaac=isaac_profile(collisionType="guess"),
            isaac_lab={"enabled": False},
        )
        with self.assertRaisesRegex(adapter.AdapterError, "portable values"):
            adapter.validate_profiles(context)

    def test_profile_validation_rejects_malformed_robot_entries(self):
        context = adapter.BundleContext(
            root=Path("."),
            manifest={},
            robot={"metadata": {"modelLicense": "Apache-2.0"}, "links": [], "joints": [None]},
            isaac=isaac_profile(),
            isaac_lab={"enabled": False},
        )
        with self.assertRaisesRegex(adapter.AdapterError, r"joints\[0\] must be an object"):
            adapter.validate_profiles(context)

    def test_usd_name_mapping_is_stable_and_collision_safe(self):
        robot = {
            "links": [{"name": "1 base"}, {"name": "1-base"}, {"name": "_base"}],
            "joints": [{"name": "shoulder/joint"}],
        }
        mapping = adapter.create_name_map(robot)
        self.assertEqual("a1_base", mapping["links"]["1 base"])
        self.assertNotEqual(mapping["links"]["1 base"], mapping["links"]["1-base"])
        self.assertEqual("_base", mapping["links"]["_base"])
        self.assertEqual("shoulder_joint", mapping["joints"]["shoulder/joint"])

    def test_bundle_paths_are_canonical_and_reject_platform_aliases(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for relative in (r"profiles\\isaac.json", "C:profile.json", "../profile.json", "a//b"):
                with self.subTest(relative=relative):
                    with self.assertRaises(adapter.AdapterError):
                        adapter._safe_path(root, relative)

    def test_stored_validation_report_must_match_summary_and_findings(self):
        summary = {"valid": True, "errors": 0, "warnings": 1, "report": "reports/validation.json"}
        report = {
            "valid": True,
            "errors": 0,
            "warnings": 1,
            "findings": [{
                "severity": "warning",
                "code": "FIXTURE_WARNING",
                "path": "$.links[0]",
                "message": "Fixture warning.",
            }],
        }
        adapter._validate_stored_validation(summary, report)
        report["warnings"] = 0
        with self.assertRaisesRegex(adapter.AdapterError, "counts do not match"):
            adapter._validate_stored_validation(summary, report)

    def test_generated_outputs_require_safe_non_overlapping_destinations(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            bundle = root / "bundle"
            bundle.mkdir()
            with self.assertRaisesRegex(adapter.AdapterError, "must not contain"):
                adapter._publish_generated_output(
                    bundle,
                    root,
                    "Fixture",
                    overwrite=True,
                    writer=lambda _: None,
                )

            output = root / "generated"
            output.mkdir()
            (output / "owned.txt").write_text("existing", encoding="utf-8")
            with self.assertRaisesRegex(adapter.AdapterError, "--overwrite"):
                adapter._publish_generated_output(
                    bundle,
                    output,
                    "Fixture",
                    overwrite=False,
                    writer=lambda _: None,
                )
            adapter._publish_generated_output(
                bundle,
                output,
                "Fixture",
                overwrite=True,
                writer=lambda staging: (staging / "fresh.txt").write_text(
                    "fresh",
                    encoding="utf-8",
                ),
            )
            self.assertFalse((output / "owned.txt").exists())
            self.assertEqual("fresh", (output / "fresh.txt").read_text(encoding="utf-8"))

            def fail_after_partial_write(staging):
                (staging / "partial.txt").write_text("partial", encoding="utf-8")
                raise adapter.AdapterError("injected generation failure")

            with self.assertRaisesRegex(adapter.AdapterError, "injected generation failure"):
                adapter._publish_generated_output(
                    bundle,
                    output,
                    "Fixture",
                    overwrite=True,
                    writer=fail_after_partial_write,
                )
            self.assertEqual("fresh", (output / "fresh.txt").read_text(encoding="utf-8"))
            self.assertFalse((output / "partial.txt").exists())

    def test_generated_outputs_reject_symbolic_link_children(self):
        if sys.platform == "win32":
            self.skipTest("Creating symbolic links is not a stable unprivileged Windows CI operation.")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            bundle = root / "bundle"
            output = root / "generated"
            external = root / "external.json"
            bundle.mkdir()
            output.mkdir()
            external.write_text("external", encoding="utf-8")
            (output / "preflight.json").symlink_to(external)

            with self.assertRaisesRegex(adapter.AdapterError, "Symbolic links"):
                adapter._publish_generated_output(
                    bundle,
                    output,
                    "Fixture",
                    overwrite=True,
                    writer=lambda _: None,
                )
            self.assertEqual("external", external.read_text(encoding="utf-8"))

    def test_profile_validation_rejects_missing_actuator_coverage(self):
        context = adapter.BundleContext(
            root=Path("."),
            manifest={},
            robot={"metadata": {"modelLicense": "Apache-2.0"}, "links": [], "joints": [{"name": "joint", "type": "revolute"}]},
            isaac=isaac_profile(),
            isaac_lab={
                "schemaVersion": 1,
                "enabled": True,
                "isaacLabVersion": "2.3.2",
                "backend": "physx",
                "rootPosition": {"x": 0.0, "y": 0.0, "z": 1.0},
                "rootRotationWxyz": {"w": 1.0, "x": 0.0, "y": 0.0, "z": 0.0},
                "physics": physics_profile(),
                "smokeEnvironmentCount": 2,
                "smokeStepCount": 5,
                "jointPositions": {},
                "jointVelocities": {},
                "actuatorGroups": [],
            },
        )
        with self.assertRaisesRegex(adapter.AdapterError, "exactly one actuator"):
            adapter.validate_profiles(context, require_isaac_lab=True)

    def test_generated_configuration_is_valid_python(self):
        context = adapter.BundleContext(
            root=Path("."),
            manifest={},
            robot={"metadata": {"modelLicense": "Apache-2.0"}, "links": [], "joints": [{"name": "joint", "type": "revolute"}]},
            isaac=isaac_profile(),
            isaac_lab={
                "schemaVersion": 1,
                "enabled": True,
                "isaacLabVersion": "2.3.2",
                "backend": "physx",
                "primPath": "{ENV_REGEX_NS}/Robot",
                "rootPosition": {"x": 0.0, "y": 0.0, "z": 1.0},
                "rootRotationWxyz": {"w": 1.0, "x": 0.0, "y": 0.0, "z": 0.0},
                "jointPositions": {},
                "jointVelocities": {},
                "physics": physics_profile(),
                "smokeEnvironmentCount": 2,
                "smokeStepCount": 5,
                "actuatorGroups": [{
                    "name": "drive",
                    "controlMode": "position",
                    "joints": ["joint"],
                    "stiffness": 100.0,
                    "damping": 5.0,
                    "effortLimit": 20.0,
                    "velocityLimit": 3.0,
                }],
            },
        )
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            config = adapter.generate_isaaclab_config(context, output)
            py_compile.compile(str(config), doraise=True)
            py_compile.compile(str(output / "smoke_test.py"), doraise=True)
            groups = json.loads((output / "actuator_groups.json").read_text(encoding="utf-8"))
            self.assertEqual("drive", groups["groups"][0]["name"])

    def test_isaac_lab_rejects_multi_dof_joint_without_project_adapter(self):
        context = adapter.BundleContext(
            root=Path("."),
            manifest={},
            robot={
                "metadata": {"modelLicense": "Apache-2.0"},
                "links": [],
                "joints": [{"name": "planar_base", "type": "planar"}],
            },
            isaac=isaac_profile(),
            isaac_lab={
                "schemaVersion": 1,
                "enabled": True,
                "isaacLabVersion": "2.3.2",
                "backend": "physx",
            },
        )
        with self.assertRaisesRegex(adapter.AdapterError, "multi-DOF Joints"):
            adapter.validate_profiles(context, require_isaac_lab=True)


if __name__ == "__main__":
    unittest.main()
