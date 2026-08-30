#!/usr/bin/env python3
"""Verified OSURDF Robot Bundle adapter for Isaac Sim and Isaac Lab.

Bundle inspection and configuration generation use only the Python standard
library. USD conversion and validation intentionally import Isaac modules only
after SimulationApp has started, so this file can also run in ordinary CI.
"""

from __future__ import annotations

import argparse
import hashlib
import inspect
import json
import math
import os
import re
import shutil
import sys
import tempfile
import uuid
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any, Callable, Iterable


MANIFEST_SCHEMA_VERSION = 1
ROBOT_SCHEMA_VERSION = 2
MOVING_JOINT_TYPES = {"continuous", "revolute", "prismatic", "planar"}
ISAAC_PHYSICS_JOINT_TYPES = MOVING_JOINT_TYPES | {"floating"}
ISAACLAB_SINGLE_DOF_JOINT_TYPES = {"continuous", "revolute", "prismatic"}
VERSION_PATTERN = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$")
ISAAC_COLLISION_TYPES = {
    "convex_hull": "Convex Hull",
    "convex_decomposition": "Convex Decomposition",
    "bounding_sphere": "Bounding Sphere",
    "bounding_cube": "Bounding Cube",
}


class AdapterError(RuntimeError):
    """A user-actionable adapter failure."""


@dataclass(frozen=True)
class BundleContext:
    root: Path
    manifest: dict[str, Any]
    robot: dict[str, Any]
    isaac: dict[str, Any]
    isaac_lab: dict[str, Any]


def _read_json(path: Path) -> dict[str, Any]:
    def unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            if key in result:
                raise AdapterError(f"Duplicate JSON property {key!r} in {path}")
            result[key] = value
        return result

    def reject_nonfinite(value: str) -> Any:
        raise AdapterError(f"Non-finite JSON number {value!r} in {path}")

    try:
        value = json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=unique_object,
            parse_constant=reject_nonfinite,
        )
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise AdapterError(f"Cannot read JSON {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise AdapterError(f"Expected a JSON object: {path}")
    return value


def _write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, indent=2, ensure_ascii=False, sort_keys=True, allow_nan=False) + "\n",
        encoding="utf-8",
    )


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _safe_path(root: Path, relative: str) -> Path:
    if not isinstance(relative, str):
        raise AdapterError(f"Unsafe bundle path: {relative!r}")
    if "\\" in relative or re.match(r"^[A-Za-z]:", relative):
        raise AdapterError(f"Unsafe bundle path: {relative!r}")
    normalized = relative
    parts = normalized.split("/")
    def windows_reserved(part: str) -> bool:
        stem = part.split(".", 1)[0].casefold()
        return stem in {"con", "prn", "aux", "nul"} or (
            len(stem) == 4 and stem[:3] in {"com", "lpt"} and stem[3] in "123456789"
        )

    if (
        not normalized
        or normalized.startswith("/")
        or any(
            part in {"", ".", ".."}
            or part.endswith((" ", "."))
            or windows_reserved(part)
            or any(ord(character) < 32 or character in '<>:"|?*' for character in part)
            for part in parts
        )
    ):
        raise AdapterError(f"Unsafe bundle path: {relative!r}")
    candidate = (root / Path(*parts)).resolve()
    resolved_root = root.resolve()
    try:
        candidate.relative_to(resolved_root)
    except ValueError as exc:
        raise AdapterError(f"Bundle path escapes its root: {relative!r}") from exc
    return candidate


def _validate_stored_validation(summary: dict[str, Any], report: dict[str, Any]) -> None:
    required = {"valid", "errors", "warnings", "findings"}
    if set(report) != required:
        raise AdapterError("Stored validation report fields do not match the canonical format.")
    if not isinstance(report.get("valid"), bool):
        raise AdapterError("Stored validation report valid flag must be boolean.")
    for name in ("errors", "warnings"):
        if type(report.get(name)) is not int or report[name] < 0:
            raise AdapterError(f"Stored validation report {name} must be a non-negative integer.")
    findings = report.get("findings")
    if not isinstance(findings, list):
        raise AdapterError("Stored validation report findings must be an array.")
    counts = {"error": 0, "warning": 0}
    for index, finding in enumerate(findings):
        if not isinstance(finding, dict) or set(finding) != {"severity", "code", "path", "message"}:
            raise AdapterError(f"Stored validation finding {index} fields are invalid.")
        severity = finding.get("severity")
        if severity not in {"info", "warning", "error"}:
            raise AdapterError(f"Stored validation finding {index} severity is invalid.")
        if any(not isinstance(finding.get(name), str) or not finding[name].strip() for name in ("code", "path", "message")):
            raise AdapterError(f"Stored validation finding {index} text fields are invalid.")
        if severity in counts:
            counts[severity] += 1
    if report["errors"] != counts["error"] or report["warnings"] != counts["warning"]:
        raise AdapterError("Stored validation report counts do not match its findings.")
    if report["valid"] != (report["errors"] == 0):
        raise AdapterError("Stored validation report valid flag does not match its error count.")
    if (
        report["valid"] != summary.get("valid")
        or report["errors"] != summary.get("errors")
        or report["warnings"] != summary.get("warnings")
    ):
        raise AdapterError("Stored validation report does not match the manifest summary.")


def _paths_overlap(first: Path, second: Path) -> bool:
    resolved_first = first.resolve()
    resolved_second = second.resolve()
    return (
        resolved_first == resolved_second
        or resolved_first in resolved_second.parents
        or resolved_second in resolved_first.parents
    )


def _validate_output_destination(bundle_root: Path, output: Path, label: str) -> None:
    if output.is_symlink():
        raise AdapterError(f"{label} output must not be a symbolic link: {output}")
    if _paths_overlap(bundle_root, output):
        raise AdapterError(f"{label} output and the source Robot Bundle must not contain one another.")
    if output.exists() and not output.is_dir():
        raise AdapterError(f"{label} output must be a directory: {output}")
    if output.exists():
        for directory, directories, files in os.walk(output, followlinks=False):
            for name in [*directories, *files]:
                candidate = Path(directory) / name
                if candidate.is_symlink():
                    raise AdapterError(
                        f"Symbolic links are not allowed in {label} output: {candidate}"
                    )


def _publish_generated_output(
    bundle_root: Path,
    output: Path,
    label: str,
    overwrite: bool,
    writer: Callable[[Path], Any],
) -> None:
    _validate_output_destination(bundle_root, output, label)
    if output.exists() and any(output.iterdir()) and not overwrite:
        raise AdapterError(f"{label} output is not empty; pass --overwrite explicitly: {output}")
    output.parent.mkdir(parents=True, exist_ok=True)
    staging: Path | None = Path(
        tempfile.mkdtemp(prefix=".osurdf-generated-", dir=str(output.parent))
    )
    previous: Path | None = None
    operation_failure: BaseException | None = None
    try:
        writer(staging)
        _validate_output_destination(bundle_root, staging, f"{label} staging")
        if output.exists():
            previous = output.with_name(output.name + ".previous-" + uuid.uuid4().hex)
            output.rename(previous)
        staging.rename(output)
        staging = None
        if previous is not None:
            try:
                shutil.rmtree(previous)
            except OSError as exc:
                raise AdapterError(
                    f"{label} output was published, but the prior directory was retained at {previous}"
                ) from exc
            previous = None
    except BaseException as exc:
        operation_failure = exc
        if previous is not None and not output.exists() and previous.exists():
            try:
                previous.rename(output)
                previous = None
            except OSError as recovery_error:
                if hasattr(exc, "add_note"):
                    exc.add_note(
                        f"{label} output recovery failed from {previous}: {recovery_error}"
                    )
        raise
    finally:
        if staging is not None and staging.exists():
            try:
                shutil.rmtree(staging)
            except OSError as cleanup_error:
                if operation_failure is None:
                    raise AdapterError(
                        f"{label} staging cleanup failed at {staging}: {cleanup_error}"
                    ) from cleanup_error
                if hasattr(operation_failure, "add_note"):
                    operation_failure.add_note(
                        f"{label} staging cleanup failed at {staging}: {cleanup_error}"
                    )


def _require_matching_profile(name: str, standalone: dict[str, Any], embedded: Any) -> None:
    if standalone != embedded:
        raise AdapterError(f"profiles/{name} does not match the corresponding robot.json profile.")


def _profile_enabled(profile: Any, name: str) -> bool:
    if not isinstance(profile, dict) or type(profile.get("enabled")) is not bool:
        raise AdapterError(f"robot.json profile {name!r} must contain a boolean enabled flag.")
    return profile["enabled"]


def verify_bundle(bundle: Path) -> BundleContext:
    if bundle.is_symlink():
        raise AdapterError("The Robot Bundle root must not be a symbolic link.")
    root = bundle.resolve()
    if not root.is_dir():
        raise AdapterError(f"Bundle directory does not exist: {root}")
    for directory, directories, files in os.walk(root, followlinks=False):
        for name in [*directories, *files]:
            candidate = Path(directory) / name
            if candidate.is_symlink():
                raise AdapterError(
                    f"Symbolic links are not allowed in a Robot Bundle: {candidate.relative_to(root).as_posix()}"
                )

    manifest_path = root / "manifest.json"
    checksums_path = root / "checksums.sha256"
    if not manifest_path.is_file() or not checksums_path.is_file():
        raise AdapterError("Robot Bundle requires manifest.json and checksums.sha256.")
    manifest = _read_json(manifest_path)
    required_manifest_keys = {
        "schemaVersion", "bundleFormat", "robotSchemaVersion", "robotName", "createdUtc",
        "reproducibleTimestamp", "generator", "entrypoints", "profiles", "validation", "files",
    }
    if set(manifest) != required_manifest_keys:
        raise AdapterError(
            "Manifest fields do not match schema v1; "
            f"missing={sorted(required_manifest_keys - set(manifest))}, "
            f"extra={sorted(set(manifest) - required_manifest_keys)}"
        )
    if type(manifest.get("schemaVersion")) is not int or manifest["schemaVersion"] != MANIFEST_SCHEMA_VERSION:
        raise AdapterError(f"Unsupported manifest schema: {manifest.get('schemaVersion')!r}")
    if manifest.get("bundleFormat") != "osurdf-robot-bundle":
        raise AdapterError("Not an OSURDF Robot Bundle.")
    if type(manifest.get("robotSchemaVersion")) is not int or manifest["robotSchemaVersion"] != ROBOT_SCHEMA_VERSION:
        raise AdapterError("Manifest robot schema version does not match this adapter.")
    if not isinstance(manifest.get("robotName"), str) or not manifest["robotName"].strip():
        raise AdapterError("Manifest robotName is required.")
    if not isinstance(manifest.get("reproducibleTimestamp"), bool):
        raise AdapterError("Manifest reproducibleTimestamp must be boolean.")
    try:
        datetime.strptime(manifest.get("createdUtc", ""), "%Y-%m-%dT%H:%M:%SZ")
    except (TypeError, ValueError) as exc:
        raise AdapterError("Manifest createdUtc must be an exact UTC timestamp.") from exc
    generator = manifest.get("generator")
    if not isinstance(generator, dict) or set(generator) != {"name", "version", "commit"} or any(
        not isinstance(generator.get(key), str) or not generator[key].strip()
        for key in ("name", "version", "commit")
    ):
        raise AdapterError("Manifest generator identity must be explicit.")
    canonical_entrypoints = {
        "robotJson": "robot.json",
        "portableUrdf": "robot.urdf",
        "isaacProfile": "profiles/isaac.json",
        "isaacLabProfile": "profiles/isaaclab.json",
    }
    if manifest.get("entrypoints") != canonical_entrypoints:
        raise AdapterError("Manifest entrypoints do not match the canonical Robot Bundle layout.")
    validation = manifest.get("validation")
    if (
        not isinstance(validation, dict)
        or set(validation) != {"valid", "errors", "warnings", "report"}
        or validation.get("valid") is not True
        or type(validation.get("errors")) is not int
        or validation["errors"] != 0
        or not isinstance(validation.get("warnings"), int)
        or isinstance(validation.get("warnings"), bool)
        or validation["warnings"] < 0
        or validation.get("report") != "reports/validation.json"
    ):
        raise AdapterError("A distributable Bundle must record a passing canonical validation report.")
    stored_validation = _read_json(_safe_path(root, validation["report"]))
    _validate_stored_validation(validation, stored_validation)

    expected: dict[str, str] = {}
    for raw_line in checksums_path.read_text(encoding="utf-8").splitlines():
        if not raw_line:
            continue
        if len(raw_line) < 67 or raw_line[64:66] != "  ":
            raise AdapterError(f"Malformed checksum line: {raw_line!r}")
        digest, relative = raw_line[:64].lower(), raw_line[66:]
        if not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise AdapterError(f"Malformed SHA-256 digest: {digest!r}")
        _safe_path(root, relative)
        if relative in expected:
            raise AdapterError(f"Duplicate checksum path: {relative}")
        expected[relative] = digest

    actual = {
        path.relative_to(root).as_posix()
        for path in root.rglob("*")
        if path.is_file() and path != checksums_path
    }
    if len({relative.casefold() for relative in actual}) != len(actual):
        raise AdapterError("Bundle paths collide on case-insensitive filesystems.")
    if actual != set(expected):
        missing = sorted(set(expected) - actual)
        extra = sorted(actual - set(expected))
        raise AdapterError(f"Checksum inventory mismatch; missing={missing}, extra={extra}")
    for relative, digest in expected.items():
        path = _safe_path(root, relative)
        if _sha256(path) != digest:
            raise AdapterError(f"Checksum mismatch: {relative}")

    manifest_files = manifest.get("files")
    if not isinstance(manifest_files, list):
        raise AdapterError("Manifest files must be an array.")
    inventory: set[str] = set()
    portable_inventory: set[str] = set()
    for index, entry in enumerate(manifest_files):
        if not isinstance(entry, dict):
            raise AdapterError(f"Manifest files[{index}] must be an object.")
        allowed_entry_keys = {"path", "role", "sha256", "bytes", "sourceUri"}
        required_entry_keys = {"path", "role", "sha256", "bytes"}
        if not required_entry_keys.issubset(entry) or not set(entry).issubset(allowed_entry_keys):
            raise AdapterError(f"Manifest files[{index}] fields do not match schema v1.")
        relative = entry.get("path")
        if not isinstance(relative, str):
            raise AdapterError(f"Manifest files[{index}].path is required.")
        path = _safe_path(root, relative)
        portable = relative.casefold()
        if relative in inventory or portable in portable_inventory:
            raise AdapterError(f"Duplicate or non-portable manifest path: {relative}")
        inventory.add(relative)
        portable_inventory.add(portable)
        digest = entry.get("sha256")
        size = entry.get("bytes")
        role = entry.get("role")
        if not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise AdapterError(f"Manifest SHA-256 is invalid: {relative}")
        if not isinstance(size, int) or isinstance(size, bool) or size < 0:
            raise AdapterError(f"Manifest byte count is invalid: {relative}")
        if not isinstance(role, str) or not role.strip():
            raise AdapterError(f"Manifest role is invalid: {relative}")
        if "sourceUri" in entry and not isinstance(entry["sourceUri"], str):
            raise AdapterError(f"Manifest sourceUri is invalid: {relative}")
        if not path.is_file() or path.stat().st_size != size or _sha256(path) != digest:
            raise AdapterError(f"Manifest payload metadata mismatch: {relative}")
        if expected.get(relative) != digest:
            raise AdapterError(f"Manifest and checksum inventory disagree: {relative}")
    expected_inventory = actual - {"manifest.json"}
    if inventory != expected_inventory:
        raise AdapterError(
            "Manifest inventory mismatch; "
            f"missing={sorted(expected_inventory - inventory)}, extra={sorted(inventory - expected_inventory)}"
        )

    entrypoints = manifest["entrypoints"]
    robot_path = _safe_path(root, entrypoints["robotJson"])
    isaac_path = _safe_path(root, entrypoints["isaacProfile"])
    lab_path = _safe_path(root, entrypoints["isaacLabProfile"])
    robot = _read_json(robot_path)
    isaac = _read_json(isaac_path)
    isaac_lab = _read_json(lab_path)
    if robot.get("schemaVersion") != ROBOT_SCHEMA_VERSION:
        raise AdapterError(f"Unsupported robot schema: {robot.get('schemaVersion')!r}")
    if manifest.get("robotName") != robot.get("name"):
        raise AdapterError("Manifest robot identity does not match robot.json.")
    embedded_profiles = robot.get("profiles")
    if not isinstance(embedded_profiles, dict):
        raise AdapterError("robot.json profiles must be an object.")

    standalone_profiles = {
        "package": _read_json(root / "profiles/package.json"),
        "ros1": _read_json(root / "profiles/ros1.json"),
        "ros2": _read_json(root / "profiles/ros2.json"),
        "isaac": isaac,
        "isaacLab": isaac_lab,
    }
    for name, standalone in standalone_profiles.items():
        _require_matching_profile(name, standalone, embedded_profiles.get(name))
    profiles = manifest.get("profiles") or {}
    expected_profiles = {
        "ros1": _profile_enabled(embedded_profiles.get("ros1"), "ros1"),
        "ros2": _profile_enabled(embedded_profiles.get("ros2"), "ros2"),
        "isaac": _profile_enabled(isaac, "isaac"),
        "isaacLab": _profile_enabled(isaac_lab, "isaacLab"),
    }
    if profiles != expected_profiles:
        raise AdapterError("Manifest profile flags do not match robot.json profiles.")
    return BundleContext(root=root, manifest=manifest, robot=robot, isaac=isaac, isaac_lab=isaac_lab)


def _require_exact_version(value: Any, field: str) -> str:
    if not isinstance(value, str) or not VERSION_PATTERN.fullmatch(value):
        raise AdapterError(f"{field} must pin an exact semantic version, for example 6.0.0.")
    return value


def _finite_number(value: Any, field: str) -> float:
    if not isinstance(value, (int, float)) or isinstance(value, bool) or not math.isfinite(float(value)):
        raise AdapterError(f"{field} must be a finite number.")
    return float(value)


def validate_profiles(context: BundleContext, require_isaac_lab: bool = False) -> None:
    metadata = context.robot.get("metadata")
    if not isinstance(metadata, dict):
        raise AdapterError("robot.json metadata must be an object.")
    for collection in ("links", "joints"):
        items = context.robot.get(collection)
        if not isinstance(items, list):
            raise AdapterError(f"robot.json {collection} must be an array.")
        for index, item in enumerate(items):
            if not isinstance(item, dict):
                raise AdapterError(f"robot.json {collection}[{index}] must be an object.")
            if not isinstance(item.get("name"), str) or not item["name"].strip():
                raise AdapterError(f"robot.json {collection}[{index}].name is required.")
            if collection == "joints" and item.get("type") not in {
                "fixed", "continuous", "revolute", "prismatic", "floating", "planar",
            }:
                raise AdapterError(f"robot.json joints[{index}].type is invalid.")

    if context.isaac.get("enabled") is not True:
        raise AdapterError("The Bundle does not enable its Isaac Sim profile.")
    for field in (
        "mergeMesh",
        "mergeFixedJoints",
        "allowSelfCollision",
        "collisionFromVisuals",
        "debugMode",
        "runAssetTransformer",
        "runMultiPhysicsConversion",
    ):
        if not isinstance(context.isaac.get(field), bool):
            raise AdapterError(f"{field} must be boolean.")
    _require_exact_version(context.isaac.get("isaacSimVersion"), "isaacSimVersion")
    if context.isaac.get("baseType") not in {"Source", "Fixed", "Mobile"}:
        raise AdapterError("baseType must be Source, Fixed, or Mobile.")
    if context.isaac.get("robotType", "Default") not in {
        "Default",
        "End Effector",
        "Manipulator",
        "Humanoid",
        "Wheeled",
        "Holonomic",
        "Quadruped",
        "Mobile Manipulators",
        "Aerial",
    }:
        raise AdapterError("robotType is not supported by the pinned Isaac URDF importer.")
    if context.isaac.get("collisionType") not in ISAAC_COLLISION_TYPES:
        raise AdapterError(
            "collisionType must be one of the portable values: "
            + ", ".join(sorted(ISAAC_COLLISION_TYPES))
        )
    if context.isaac.get("schemaVersion") != 1:
        raise AdapterError("Unsupported Isaac profile schema.")
    if not isinstance(metadata.get("modelLicense"), str) or not metadata["modelLicense"].strip():
        raise AdapterError("An explicit model license is required for Isaac asset conversion.")
    package_mappings = context.isaac.get("packageMappings") or {}
    if not isinstance(package_mappings, dict):
        raise AdapterError("packageMappings must be an object.")
    for package, relative in package_mappings.items():
        if (
            not isinstance(package, str)
            or not re.fullmatch(r"[a-z][a-z0-9_]*", package)
            or not isinstance(relative, str)
        ):
            raise AdapterError("Isaac package mappings must use portable ROS package names and string paths.")
        mapped_path = _safe_path(context.root, relative)
        if not mapped_path.is_dir():
            raise AdapterError(
                f"Isaac package mapping {package!r} does not name a Bundle directory: {relative!r}"
            )
    unsafe_names = []
    for collection in ("links", "joints"):
        for item in context.robot.get(collection, []):
            name = item.get("name") if isinstance(item, dict) else None
            if not isinstance(name, str) or not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", name):
                unsafe_names.append(f"{collection}:{name!r}")
    if unsafe_names:
        raise AdapterError(
            "Isaac conversion requires explicit USD-safe Link and Joint names: " + ", ".join(unsafe_names)
        )

    if not require_isaac_lab and not context.isaac_lab.get("enabled", False):
        return
    if not context.isaac_lab.get("enabled", False):
        raise AdapterError("The Bundle does not enable its Isaac Lab profile.")
    _require_exact_version(context.isaac_lab.get("isaacLabVersion"), "isaacLabVersion")
    if context.isaac_lab.get("schemaVersion") != 1:
        raise AdapterError("Unsupported Isaac Lab profile schema.")
    if context.isaac_lab.get("backend") != "physx":
        raise AdapterError("The generated Isaac Lab articulation currently requires backend='physx'.")
    unsupported_multi_dof = [
        joint.get("name")
        for joint in context.robot.get("joints", [])
        if isinstance(joint, dict) and joint.get("type") in {"planar", "floating"}
    ]
    if unsupported_multi_dof:
        raise AdapterError(
            "Isaac Lab actuator generation currently supports only one-DOF revolute, continuous, and "
            f"prismatic Joints; multi-DOF Joints require a project adapter: {unsupported_multi_dof}"
        )

    root_position = context.isaac_lab.get("rootPosition")
    rotation = context.isaac_lab.get("rootRotationWxyz")
    if not isinstance(root_position, dict) or not isinstance(rotation, dict):
        raise AdapterError("Isaac Lab root pose must be explicit.")
    for key in ("x", "y", "z"):
        _finite_number(root_position.get(key), f"rootPosition.{key}")
    quaternion = [_finite_number(rotation.get(key), f"rootRotationWxyz.{key}") for key in ("w", "x", "y", "z")]
    if not math.isclose(math.sqrt(sum(value * value for value in quaternion)), 1.0, rel_tol=0.0, abs_tol=1e-6):
        raise AdapterError("rootRotationWxyz must be a unit quaternion.")
    physics = context.isaac_lab.get("physics")
    if not isinstance(physics, dict):
        raise AdapterError("Isaac Lab physics settings are required.")
    position_iterations = physics.get("solverPositionIterationCount")
    velocity_iterations = physics.get("solverVelocityIterationCount")
    if not isinstance(position_iterations, int) or isinstance(position_iterations, bool) or position_iterations < 1:
        raise AdapterError("physics.solverPositionIterationCount must be a positive integer.")
    if not isinstance(velocity_iterations, int) or isinstance(velocity_iterations, bool) or velocity_iterations < 0:
        raise AdapterError("physics.solverVelocityIterationCount must be a non-negative integer.")
    if _finite_number(physics.get("maxDepenetrationVelocity"), "physics.maxDepenetrationVelocity") <= 0.0:
        raise AdapterError("physics.maxDepenetrationVelocity must be positive.")
    for key in ("enabledSelfCollisions", "enableGyroscopicForces"):
        if not isinstance(physics.get(key), bool):
            raise AdapterError(f"physics.{key} must be boolean.")
    for key in ("smokeEnvironmentCount", "smokeStepCount"):
        value = context.isaac_lab.get(key)
        if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
            raise AdapterError(f"{key} must be a positive integer.")

    movable = {
        joint.get("name")
        for joint in context.robot.get("joints", [])
        if joint.get("type") in ISAACLAB_SINGLE_DOF_JOINT_TYPES
    }
    if None in movable:
        raise AdapterError("A movable Joint is missing its name.")
    coverage = {name: 0 for name in movable}
    groups = context.isaac_lab.get("actuatorGroups")
    if not isinstance(groups, list):
        raise AdapterError("actuatorGroups must be a list.")
    group_names: set[str] = set()
    for index, group in enumerate(groups):
        if not isinstance(group, dict):
            raise AdapterError(f"actuatorGroups[{index}] must be an object.")
        name = group.get("name")
        mode = group.get("controlMode")
        if not isinstance(name, str) or not name:
            raise AdapterError(f"actuatorGroups[{index}].name is required.")
        if name in group_names:
            raise AdapterError(f"Duplicate actuator group name: {name!r}")
        group_names.add(name)
        if mode not in {"position", "velocity", "effort", "passive"}:
            raise AdapterError(f"Unsupported controlMode for actuator group {name!r}.")
        joints = group.get("joints")
        if not isinstance(joints, list) or not joints:
            raise AdapterError(f"Actuator group {name!r} must select at least one Joint.")
        if any(not isinstance(joint, str) or not joint for joint in joints):
            raise AdapterError(f"Actuator group {name!r} contains a blank or non-string Joint name.")
        if len(set(joints)) != len(joints):
            raise AdapterError(f"Actuator group {name!r} selects a Joint more than once.")
        for joint in joints:
            if joint not in coverage:
                raise AdapterError(f"Actuator group {name!r} selects non-movable Joint {joint!r}.")
            coverage[joint] += 1
        if mode == "position":
            _finite_number(group.get("stiffness"), f"{name}.stiffness")
            _finite_number(group.get("damping"), f"{name}.damping")
        if mode == "velocity":
            _finite_number(group.get("damping"), f"{name}.damping")
        for key in ("stiffness", "damping", "armature", "friction"):
            if group.get(key) is not None and _finite_number(group[key], f"{name}.{key}") < 0.0:
                raise AdapterError(f"{name}.{key} must be non-negative.")
        for key in ("effortLimit", "velocityLimit"):
            if group.get(key) is not None and _finite_number(group[key], f"{name}.{key}") <= 0.0:
                raise AdapterError(f"{name}.{key} must be positive.")
    incomplete = {name: count for name, count in coverage.items() if count != 1}
    if incomplete:
        raise AdapterError(f"Every movable Joint must have exactly one actuator group: {incomplete}")
    for field in ("jointPositions", "jointVelocities"):
        initial = context.isaac_lab.get(field) or {}
        if not isinstance(initial, dict):
            raise AdapterError(f"{field} must be an object.")
        for joint, value in initial.items():
            if joint not in movable:
                raise AdapterError(f"{field} refers to non-movable Joint {joint!r}.")
            numeric = _finite_number(value, f"{field}.{joint}")
            if field == "jointPositions":
                model_joint = next(
                    item for item in context.robot.get("joints", [])
                    if isinstance(item, dict) and item.get("name") == joint
                )
                limit = model_joint.get("limit") or {}
                if not isinstance(limit, dict):
                    raise AdapterError(f"Joint {joint!r} has an invalid limit object.")
                if limit.get("lower") is not None and numeric < _finite_number(limit["lower"], f"{joint}.limit.lower"):
                    raise AdapterError(f"jointPositions.{joint} is below its lower limit.")
                if limit.get("upper") is not None and numeric > _finite_number(limit["upper"], f"{joint}.limit.upper"):
                    raise AdapterError(f"jointPositions.{joint} is above its upper limit.")


def usd_safe_name(name: str, used: set[str] | None = None) -> str:
    value = re.sub(r"[^A-Za-z0-9_]", "_", name)
    if not value or not (value[0].isalpha() or value[0] == "_"):
        value = "a" + value
    if used is None or value not in used:
        if used is not None:
            used.add(value)
        return value
    suffix = hashlib.sha256(name.encode("utf-8")).hexdigest()[:8]
    candidate = f"{value}_{suffix}"
    counter = 2
    while candidate in used:
        candidate = f"{value}_{suffix}_{counter}"
        counter += 1
    used.add(candidate)
    return candidate


def create_name_map(robot: dict[str, Any]) -> dict[str, dict[str, str]]:
    result: dict[str, dict[str, str]] = {"links": {}, "joints": {}}
    for key, source_key in (("links", "links"), ("joints", "joints")):
        used: set[str] = set()
        for item in robot.get(source_key, []):
            if not isinstance(item, dict):
                raise AdapterError(f"{source_key} contains a non-object entry.")
            original = item.get("name")
            if not isinstance(original, str) or not original:
                raise AdapterError(f"{source_key} contains a blank name.")
            if original in result[key]:
                raise AdapterError(f"{source_key} contains duplicate name {original!r}.")
            result[key][original] = usd_safe_name(original, used)
    return result


def preflight(
    bundle: Path,
    output: Path | None = None,
    require_isaac_lab: bool = False,
    overwrite: bool = False,
) -> BundleContext:
    context = verify_bundle(bundle)
    validate_profiles(context, require_isaac_lab=require_isaac_lab)
    report = {
        "ok": True,
        "bundle": str(context.root),
        "robot": context.robot.get("name"),
        "isaacSimVersion": context.isaac.get("isaacSimVersion"),
        "isaacLabVersion": context.isaac_lab.get("isaacLabVersion") if context.isaac_lab.get("enabled") else None,
        "links": len(context.robot.get("links", [])),
        "joints": len(context.robot.get("joints", [])),
        "nameMap": create_name_map(context.robot),
    }
    if output is not None:
        def write_preflight(staging: Path) -> None:
            _write_json(staging / "preflight.json", report)
            _write_json(staging / "name_map.json", report["nameMap"])

        _publish_generated_output(
            context.root,
            output,
            "Preflight",
            overwrite,
            write_preflight,
        )
    return context


def _python_literal(value: Any) -> str:
    return repr(value)


def _actuator_expression(group: dict[str, Any]) -> str:
    mode = group["controlMode"]
    stiffness = group.get("stiffness")
    damping = group.get("damping")
    if mode in {"effort", "passive"}:
        stiffness = 0.0 if stiffness is None else stiffness
        damping = 0.0 if damping is None else damping
    elif mode == "velocity":
        stiffness = 0.0 if stiffness is None else stiffness
    args = {
        "joint_names_expr": [re.escape(name) + "$" for name in group["joints"]],
        "stiffness": stiffness,
        "damping": damping,
        "effort_limit": group.get("effortLimit"),
        "velocity_limit": group.get("velocityLimit"),
        "armature": group.get("armature"),
        "friction": group.get("friction"),
    }
    return "_implicit_actuator(" + ", ".join(f"{key}={_python_literal(value)}" for key, value in args.items()) + ")"


def generate_isaaclab_config(
    context: BundleContext,
    output: Path,
    usd_relative_path: str = "robot.usd",
    overwrite: bool = False,
) -> Path:
    validate_profiles(context, require_isaac_lab=True)
    _safe_path(context.root, usd_relative_path)

    def write_isaaclab(staging: Path) -> None:
        _write_isaaclab_config(context, staging, usd_relative_path)

    _publish_generated_output(
        context.root,
        output,
        "Isaac Lab",
        overwrite,
        write_isaaclab,
    )
    return output / "robot_cfg.py"


def _write_isaaclab_config(
    context: BundleContext,
    output: Path,
    usd_relative_path: str,
) -> Path:
    profile = context.isaac_lab
    physics = profile.get("physics") or {}
    root_position = profile.get("rootPosition") or {"x": 0.0, "y": 0.0, "z": 1.0}
    rotation = profile.get("rootRotationWxyz") or {"w": 1.0, "x": 0.0, "y": 0.0, "z": 0.0}
    joint_positions = profile.get("jointPositions") or {".*": 0.0}
    joint_velocities = profile.get("jointVelocities") or {".*": 0.0}
    groups = profile.get("actuatorGroups") or []

    lines = [
        '"""Generated Isaac Lab articulation configuration. Do not hand-edit."""',
        "",
        "import inspect",
        "import os",
        "from pathlib import Path",
        "",
        "import isaaclab.sim as sim_utils",
        "from isaaclab.actuators import ImplicitActuatorCfg",
        "from isaaclab.assets import ArticulationCfg",
        "",
        f"EXPECTED_ISAAC_LAB_VERSION = {_python_literal(profile['isaacLabVersion'])}",
        f"USD_PATH = Path(os.environ.get('OSURDF_USD_PATH', str(Path(__file__).resolve().parent / {_python_literal(usd_relative_path)})))",
        "",
        "",
        "def _implicit_actuator(*, joint_names_expr, stiffness, damping, effort_limit, velocity_limit, armature, friction):",
        "    common = {",
        "        'joint_names_expr': joint_names_expr,",
        "        'stiffness': stiffness,",
        "        'damping': damping,",
        "        'armature': armature,",
        "        'friction': friction,",
        "    }",
        "    common = {key: value for key, value in common.items() if value is not None}",
        "    parameters = inspect.signature(ImplicitActuatorCfg).parameters",
        "    limits = dict(common)",
        "    if effort_limit is not None:",
        "        if 'joint_effort_limit' in parameters:",
        "            limits['joint_effort_limit'] = effort_limit",
        "        elif 'effort_limit_sim' in parameters:",
        "            limits['effort_limit_sim'] = effort_limit",
        "        else:",
        "            raise RuntimeError('Pinned Isaac Lab does not expose a supported solver effort-limit field')",
        "    if velocity_limit is not None:",
        "        if 'joint_velocity_limit' in parameters:",
        "            limits['joint_velocity_limit'] = velocity_limit",
        "        elif 'velocity_limit_sim' in parameters:",
        "            limits['velocity_limit_sim'] = velocity_limit",
        "        else:",
        "            raise RuntimeError('Pinned Isaac Lab does not expose a supported solver velocity-limit field')",
        "    return ImplicitActuatorCfg(**limits)",
        "",
        "",
        "ROBOT_CFG = ArticulationCfg(",
        f"    prim_path={_python_literal(profile.get('primPath', '{ENV_REGEX_NS}/Robot'))},",
        "    spawn=sim_utils.UsdFileCfg(",
        "        usd_path=str(USD_PATH),",
        "        rigid_props=sim_utils.RigidBodyPropertiesCfg(",
        f"            max_depenetration_velocity={_python_literal(physics.get('maxDepenetrationVelocity', 5.0))},",
        f"            enable_gyroscopic_forces={_python_literal(physics.get('enableGyroscopicForces', True))},",
        "        ),",
        "        articulation_props=sim_utils.ArticulationRootPropertiesCfg(",
        f"            enabled_self_collisions={_python_literal(physics.get('enabledSelfCollisions', False))},",
        f"            solver_position_iteration_count={_python_literal(physics.get('solverPositionIterationCount', 8))},",
        f"            solver_velocity_iteration_count={_python_literal(physics.get('solverVelocityIterationCount', 2))},",
        "        ),",
        "    ),",
        "    init_state=ArticulationCfg.InitialStateCfg(",
        f"        pos=({_finite_number(root_position.get('x', 0.0), 'rootPosition.x')}, {_finite_number(root_position.get('y', 0.0), 'rootPosition.y')}, {_finite_number(root_position.get('z', 1.0), 'rootPosition.z')}),",
        f"        rot=({_finite_number(rotation.get('w', 1.0), 'rootRotation.w')}, {_finite_number(rotation.get('x', 0.0), 'rootRotation.x')}, {_finite_number(rotation.get('y', 0.0), 'rootRotation.y')}, {_finite_number(rotation.get('z', 0.0), 'rootRotation.z')}),",
        f"        joint_pos={_python_literal(joint_positions)},",
        f"        joint_vel={_python_literal(joint_velocities)},",
        "    ),",
        "    actuators={",
    ]
    for group in groups:
        lines.append(f"        {_python_literal(group['name'])}: {_actuator_expression(group)},")
    lines.extend(["    },", ")", ""])
    config_path = output / "robot_cfg.py"
    config_path.write_text("\n".join(lines), encoding="utf-8")
    _write_json(output / "actuator_groups.json", {"schemaVersion": 1, "groups": groups})
    (output / "smoke_test.py").write_text(_smoke_test_source(profile), encoding="utf-8")
    return config_path


def _smoke_test_source(profile: dict[str, Any]) -> str:
    environments = int(profile.get("smokeEnvironmentCount", 64))
    steps = int(profile.get("smokeStepCount", 1000))
    expected = profile["isaacLabVersion"]
    return f'''#!/usr/bin/env python3
"""Generated Isaac Lab articulation smoke test."""

import argparse
import importlib.metadata
import json
from pathlib import Path

from isaaclab.app import AppLauncher

parser = argparse.ArgumentParser()
parser.add_argument("--report", default="isaaclab-smoke-report.json")
parser.add_argument("--environments", type=int, default={environments})
parser.add_argument("--steps", type=int, default={steps})
AppLauncher.add_app_launcher_args(parser)
args = parser.parse_args()
launcher = AppLauncher(args)
simulation_app = launcher.app

import torch
import isaaclab.sim as sim_utils
from isaaclab.scene import InteractiveScene, InteractiveSceneCfg
from isaaclab.utils import configclass
from isaaclab.assets import AssetBaseCfg
from robot_cfg import ROBOT_CFG


actual_version = importlib.metadata.version("isaaclab")
expected_version = {expected!r}
if actual_version != expected_version:
    raise RuntimeError(f"Isaac Lab version mismatch: expected {{expected_version}}, got {{actual_version}}")


@configclass
class SmokeSceneCfg(InteractiveSceneCfg):
    ground = AssetBaseCfg(prim_path="/World/defaultGroundPlane", spawn=sim_utils.GroundPlaneCfg())
    light = AssetBaseCfg(prim_path="/World/light", spawn=sim_utils.DomeLightCfg(intensity=2000.0))
    robot = ROBOT_CFG.replace(prim_path="{{ENV_REGEX_NS}}/Robot")


sim = sim_utils.SimulationContext(sim_utils.SimulationCfg(dt=1.0 / 120.0, device=args.device))
scene = InteractiveScene(SmokeSceneCfg(num_envs=args.environments, env_spacing=2.5))
sim.reset()
scene.reset()
robot = scene["robot"]
for step in range(args.steps):
    scene.write_data_to_sim()
    sim.step()
    scene.update(sim.get_physics_dt())
    if not torch.isfinite(robot.data.root_pos_w).all() or not torch.isfinite(robot.data.joint_pos).all():
        raise RuntimeError(f"Non-finite articulation state at step {{step}}")

report = {{
    "ok": True,
    "isaacLabVersion": actual_version,
    "environments": args.environments,
    "steps": args.steps,
    "instances": int(robot.num_instances),
    "joints": int(robot.num_joints),
}}
Path(args.report).write_text(json.dumps(report, indent=2, sort_keys=True) + "\\n", encoding="utf-8")
print(json.dumps(report, indent=2, sort_keys=True))
simulation_app.close()
'''


def _isaac_version_from_parts(parts: Any) -> str:
    if not isinstance(parts, (tuple, list)) or not parts:
        raise AdapterError(f"Isaac Sim returned an invalid version tuple: {parts!r}")
    core = str(parts[0]).strip()
    reconstructed = None
    if len(parts) >= 5 and all(str(part).isdigit() for part in parts[2:5]):
        reconstructed = ".".join(str(part) for part in parts[2:5])
    if VERSION_PATTERN.fullmatch(core):
        if reconstructed is not None and core.split("-", 1)[0].split("+", 1)[0] != reconstructed:
            raise AdapterError(f"Isaac Sim returned inconsistent version components: {parts!r}")
        return core
    if reconstructed is not None:
        return reconstructed
    raise AdapterError(f"Isaac Sim returned an unrecognized version tuple: {parts!r}")


def _running_isaac_version() -> str:
    from isaacsim.core.version import get_version  # type: ignore[import-not-found]

    return _isaac_version_from_parts(get_version())


def _validate_usd_stage(usd_path: Path, expected_joint_names: set[str]) -> dict[str, Any]:
    from pxr import Usd, UsdPhysics  # type: ignore[import-not-found]

    stage = Usd.Stage.Open(str(usd_path))
    if stage is None:
        raise AdapterError(f"USD stage could not be opened: {usd_path}")
    articulations = []
    joints = []
    joint_names = set()
    invalid_transforms = []
    for prim in stage.Traverse():
        if prim.HasAPI(UsdPhysics.ArticulationRootAPI):
            articulations.append(str(prim.GetPath()))
        if prim.IsA(UsdPhysics.Joint):
            joints.append(str(prim.GetPath()))
            joint_names.add(prim.GetName())
        transformable = prim.GetAttribute("xformOp:transform")
        if transformable and transformable.HasAuthoredValueOpinion():
            value = transformable.Get()
            if value is not None and any(not math.isfinite(float(component)) for row in value for component in row):
                invalid_transforms.append(str(prim.GetPath()))
    if not articulations:
        raise AdapterError("USD contains no ArticulationRootAPI.")
    if invalid_transforms:
        raise AdapterError(f"USD contains non-finite transforms: {invalid_transforms}")
    missing_joints = sorted(expected_joint_names - joint_names)
    if missing_joints:
        raise AdapterError(f"USD is missing expected physics Joint identities: {missing_joints}")
    return {
        "ok": True,
        "usd": str(usd_path),
        "articulationRoots": articulations,
        "physicsJoints": len(joints),
        "expectedJointNames": sorted(expected_joint_names),
    }


def _build_importer_request(context: BundleContext, portable_urdf: Path, usd_output_directory: Path) -> dict[str, Any]:
    collision_type = context.isaac.get("collisionType")
    if collision_type not in ISAAC_COLLISION_TYPES:
        raise AdapterError(f"Unsupported portable collision type: {collision_type!r}")
    mappings = context.isaac.get("packageMappings") or {}
    ros_package_paths = [
        {"name": package, "path": str(_safe_path(context.root, relative))}
        for package, relative in sorted(mappings.items())
    ]
    base_type = context.isaac.get("baseType", "Source")
    return {
        "urdf_path": str(portable_urdf),
        # Isaac Sim's API expects an output directory here, not a .usd file path.
        "usd_path": str(usd_output_directory),
        "merge_mesh": bool(context.isaac.get("mergeMesh", True)),
        "merge_fixed_joints": bool(context.isaac.get("mergeFixedJoints", False)),
        "debug_mode": bool(context.isaac.get("debugMode", False)),
        "collision_from_visuals": bool(context.isaac.get("collisionFromVisuals", False)),
        "collision_type": ISAAC_COLLISION_TYPES[collision_type],
        "allow_self_collision": bool(context.isaac.get("allowSelfCollision", False)),
        "ros_package_paths": ros_package_paths,
        "robot_type": str(context.isaac.get("robotType", "Default")),
        "fix_base": True if base_type == "Fixed" else False if base_type == "Mobile" else None,
        "run_asset_transformer": bool(context.isaac.get("runAssetTransformer", False)),
        "run_multi_physics_conversion": bool(context.isaac.get("runMultiPhysicsConversion", False)),
    }


def convert_bundle(bundle: Path, output: Path, overwrite: bool, headless: bool) -> dict[str, Any]:
    context = preflight(bundle, require_isaac_lab=False)
    _validate_output_destination(context.root, output, "Conversion")
    if output.exists() and not overwrite:
        raise AdapterError(f"Output exists; pass --overwrite explicitly: {output}")
    output.parent.mkdir(parents=True, exist_ok=True)
    staging: Path | None = Path(tempfile.mkdtemp(prefix=".osurdf-isaac-", dir=str(output.parent)))
    previous: Path | None = None
    simulation_app = None
    operation_failure: BaseException | None = None
    try:
        import isaacsim  # type: ignore[import-not-found]  # noqa: F401
        from isaacsim.simulation_app import SimulationApp  # type: ignore[import-not-found]

        simulation_app = SimulationApp({"headless": headless})
        actual_version = _running_isaac_version()
        expected_version = context.isaac["isaacSimVersion"]
        if actual_version != expected_version:
            raise AdapterError(f"Isaac Sim version mismatch: expected {expected_version}, got {actual_version}")

        from isaacsim.asset.importer.urdf import URDFImporter, URDFImporterConfig  # type: ignore[import-not-found]

        portable_urdf = _safe_path(
            context.root,
            (context.manifest.get("entrypoints") or {}).get("portableUrdf", "robot.urdf"),
        )
        requested = _build_importer_request(context, portable_urdf, staging)
        try:
            signature = inspect.signature(URDFImporterConfig)
        except (TypeError, ValueError) as exc:
            raise AdapterError("Pinned Isaac Sim does not expose an inspectable URDFImporterConfig API.") from exc
        supports_extra = any(parameter.kind == inspect.Parameter.VAR_KEYWORD for parameter in signature.parameters.values())
        supported = set(signature.parameters)
        required_options = {"urdf_path", "usd_path", "merge_mesh", "collision_from_visuals", "collision_type", "allow_self_collision"}
        missing_required = sorted(required_options - supported) if not supports_extra else []
        if missing_required:
            raise AdapterError(
                "Pinned Isaac Sim importer is missing required configuration fields: " + ", ".join(missing_required)
            )
        unsupported_requested = [
            name for name, value in requested.items()
            if name not in supported and not supports_extra and name not in required_options and value not in (None, False, [], "Default")
        ]
        if unsupported_requested:
            raise AdapterError(
                "Pinned Isaac Sim importer does not support requested options: " + ", ".join(sorted(unsupported_requested))
            )
        config_kwargs = {
            name: value for name, value in requested.items()
            if name in supported or supports_extra
        }
        config = URDFImporterConfig(**config_kwargs)
        imported_path = Path(URDFImporter(config).import_urdf()).resolve()
        simulation_app.update()
        if not imported_path.is_file():
            raise AdapterError(f"URDF importer did not produce a USD file: {imported_path}")
        try:
            usd_relative = imported_path.relative_to(staging.resolve()).as_posix()
        except ValueError as exc:
            raise AdapterError(f"Isaac importer wrote outside the requested output: {imported_path}") from exc

        moving_joint_names = {
            joint["name"] for joint in context.robot.get("joints", [])
            if joint.get("type") in ISAAC_PHYSICS_JOINT_TYPES
        }
        stage_report = _validate_usd_stage(imported_path, moving_joint_names)
        _write_json(staging / "name_map.json", create_name_map(context.robot))
        conversion_report = {
            **stage_report,
            "usd": str(output.resolve() / Path(*usd_relative.split("/"))),
            "bundle": str(context.root),
            "isaacSimVersion": actual_version,
            "expectedIsaacSimVersion": expected_version,
            "usdEntrypoint": usd_relative,
        }
        _write_json(staging / "conversion_report.json", conversion_report)
        if context.isaac_lab.get("enabled", False):
            _write_isaaclab_config(context, staging, usd_relative)

        _validate_output_destination(context.root, staging, "Conversion staging")

        if output.exists():
            previous = output.with_name(output.name + ".previous-" + uuid.uuid4().hex)
            output.rename(previous)
        staging.rename(output)
        staging = None
        if previous is not None:
            try:
                shutil.rmtree(previous)
            except OSError:
                conversion_report["retainedPreviousDirectory"] = str(previous)
                _write_json(output / "conversion_report.json", conversion_report)
            previous = None
        return conversion_report
    except BaseException as exc:
        operation_failure = exc
        raise
    finally:
        cleanup_errors: list[str] = []
        if simulation_app is not None:
            try:
                simulation_app.close()
            except Exception as exc:  # pragma: no cover - requires Isaac runtime failure
                cleanup_errors.append(f"Isaac SimulationApp close failed: {exc}")
        if staging is not None and staging.exists():
            try:
                shutil.rmtree(staging)
            except OSError as exc:
                cleanup_errors.append(f"staging cleanup failed at {staging}: {exc}")
        if previous is not None and previous.exists() and not output.exists():
            try:
                previous.rename(output)
            except OSError as exc:
                cleanup_errors.append(f"output recovery failed from {previous}: {exc}")
        if cleanup_errors:
            detail = "; ".join(cleanup_errors)
            if operation_failure is None:
                raise AdapterError(detail)
            if hasattr(operation_failure, "add_note"):
                operation_failure.add_note(detail)


def validate_usd(bundle: Path, usd: Path, headless: bool) -> dict[str, Any]:
    context = preflight(bundle, require_isaac_lab=False)
    simulation_app = None
    try:
        import isaacsim  # type: ignore[import-not-found]  # noqa: F401
        from isaacsim.simulation_app import SimulationApp  # type: ignore[import-not-found]

        simulation_app = SimulationApp({"headless": headless})
        actual_version = _running_isaac_version()
        expected = context.isaac["isaacSimVersion"]
        if actual_version != expected:
            raise AdapterError(f"Isaac Sim version mismatch: expected {expected}, got {actual_version}")
        expected_joint_names = {
            joint["name"] for joint in context.robot.get("joints", [])
            if joint.get("type") in ISAAC_PHYSICS_JOINT_TYPES
        }
        report = _validate_usd_stage(usd.resolve(), expected_joint_names)
        report["isaacSimVersion"] = actual_version
        return report
    finally:
        if simulation_app is not None:
            simulation_app.close()


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    preflight_parser = subparsers.add_parser("preflight", help="verify Bundle and pinned profiles without Isaac")
    preflight_parser.add_argument("--bundle", type=Path, required=True)
    preflight_parser.add_argument("--output", type=Path)
    preflight_parser.add_argument("--require-isaac-lab", action="store_true")
    preflight_parser.add_argument("--overwrite", action="store_true")

    generate_parser = subparsers.add_parser("generate-isaaclab", help="generate ArticulationCfg and smoke test")
    generate_parser.add_argument("--bundle", type=Path, required=True)
    generate_parser.add_argument("--output", type=Path, required=True)
    generate_parser.add_argument("--usd-relative-path", default="robot.usd")
    generate_parser.add_argument("--overwrite", action="store_true")

    convert_parser = subparsers.add_parser("convert", help="convert portable URDF to USD inside exact Isaac Sim")
    convert_parser.add_argument("--bundle", type=Path, required=True)
    convert_parser.add_argument("--output", type=Path, required=True)
    convert_parser.add_argument("--overwrite", action="store_true")
    convert_parser.add_argument("--headless", action=argparse.BooleanOptionalAction, default=True)

    validate_parser = subparsers.add_parser("validate-usd", help="validate USD structure inside exact Isaac Sim")
    validate_parser.add_argument("--bundle", type=Path, required=True)
    validate_parser.add_argument("--usd", type=Path, required=True)
    validate_parser.add_argument("--report", type=Path)
    validate_parser.add_argument("--headless", action=argparse.BooleanOptionalAction, default=True)
    return parser


def main(argv: Iterable[str] | None = None) -> int:
    args = _parser().parse_args(list(argv) if argv is not None else None)
    try:
        if args.command == "preflight":
            context = preflight(args.bundle, args.output, args.require_isaac_lab, args.overwrite)
            result = {
                "ok": True,
                "robot": context.robot.get("name"),
                "isaacSimVersion": context.isaac.get("isaacSimVersion"),
                "isaacLabVersion": context.isaac_lab.get("isaacLabVersion") if context.isaac_lab.get("enabled") else None,
            }
        elif args.command == "generate-isaaclab":
            context = preflight(args.bundle, require_isaac_lab=True)
            config = generate_isaaclab_config(
                context,
                args.output,
                args.usd_relative_path,
                overwrite=args.overwrite,
            )
            result = {"ok": True, "config": str(config), "smokeTest": str(args.output / "smoke_test.py")}
        elif args.command == "convert":
            result = convert_bundle(args.bundle, args.output, args.overwrite, args.headless)
        elif args.command == "validate-usd":
            result = validate_usd(args.bundle, args.usd, args.headless)
            if args.report:
                _write_json(args.report, result)
        else:
            raise AdapterError(f"Unsupported command: {args.command}")
        print(json.dumps(result, indent=2, ensure_ascii=False, sort_keys=True))
        return 0
    except (AdapterError, OSError, ValueError, TypeError, KeyError) as exc:
        print(json.dumps({"ok": False, "error": str(exc)}, indent=2, ensure_ascii=False), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
