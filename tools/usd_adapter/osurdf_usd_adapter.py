#!/usr/bin/env python3
"""Build and validate a portable OpenUSD robot asset from an OSURDF bundle."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import shutil
import struct
import sys
import tempfile
import uuid
from pathlib import Path
from typing import Any, Iterable, Sequence

from pxr import Gf, Sdf, Usd, UsdGeom, UsdPhysics, UsdShade


class AdapterError(RuntimeError):
    pass


_PUBLISH_STAGED = "staged"
_PUBLISH_PREVIOUS_MOVED = "previous_moved"
_PUBLISH_PUBLISHED = "published"
_PUBLISH_RESTORED = "restored"
_PUBLISH_PREVIOUS_RETAINED = "previous_retained"
_PLANAR_LOCKED_DOFS = ("transZ", "rotX", "rotY")
_PLANAR_FREE_DOFS = ("transX", "transY", "rotZ")


def _read_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise AdapterError(f"Expected a JSON object: {path}")
    return value


def _write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def _publish_staging_directory(staging: Path, output: Path) -> Path | None:
    """Atomically publish staging while never discarding the only previous output."""
    state = _PUBLISH_STAGED
    previous: Path | None = None

    if output.exists():
        previous = output.with_name(output.name + ".previous-" + uuid.uuid4().hex)
        output.rename(previous)
        state = _PUBLISH_PREVIOUS_MOVED

    try:
        staging.rename(output)
        state = _PUBLISH_PUBLISHED
    except OSError as publish_error:
        if state == _PUBLISH_PREVIOUS_MOVED and previous is not None:
            try:
                previous.rename(output)
                state = _PUBLISH_RESTORED
            except OSError as restore_error:
                state = _PUBLISH_PREVIOUS_RETAINED
                raise AdapterError(
                    "USD publish failed and the previous output could not be restored. "
                    f"The only previous output was retained at: {previous}. "
                    f"Publish error: {publish_error}. Restore error: {restore_error}."
                ) from publish_error
        raise

    if state == _PUBLISH_PUBLISHED and previous is not None:
        try:
            shutil.rmtree(previous)
        except OSError:
            state = _PUBLISH_PREVIOUS_RETAINED

    if state == _PUBLISH_PREVIOUS_RETAINED and previous is not None and previous.exists():
        return previous
    return None


def _record_retained_previous(
    report_path: Path,
    report: dict[str, Any],
    retained_previous: Path | None,
) -> str | None:
    report["retainedPreviousDirectory"] = (
        str(retained_previous) if retained_previous is not None else None
    )
    if retained_previous is None:
        return None
    try:
        _write_json(report_path, report)
    except OSError as exc:
        # Publication already succeeded. Keep that result successful and return
        # the retained path through stdout even if the diagnostic rewrite fails.
        return f"Could not update export_report.json after retaining previous output: {exc}"
    return None


def _safe_path(root: Path, relative: str) -> Path:
    normalized = str(relative or "").replace("\\", "/")
    parts = normalized.split("/")
    if not normalized or normalized.startswith("/") or any(part in ("", ".", "..") for part in parts):
        raise AdapterError(f"Unsafe bundle-relative path: {relative!r}")
    candidate = (root / Path(*parts)).resolve()
    try:
        candidate.relative_to(root.resolve())
    except ValueError as exc:
        raise AdapterError(f"Bundle asset escapes its root: {relative!r}") from exc
    return candidate


def _validate_output_destination(bundle: Path, output: Path) -> None:
    bundle = bundle.resolve()
    output = output.resolve()
    if bundle == output:
        raise AdapterError("USD output cannot replace the source bundle.")
    try:
        output.relative_to(bundle)
    except ValueError:
        pass
    else:
        raise AdapterError("USD output cannot be created inside the source bundle.")
    try:
        bundle.relative_to(output)
    except ValueError:
        pass
    else:
        raise AdapterError("USD output cannot contain the source bundle.")


def _identifier(value: str, fallback: str) -> str:
    cleaned = re.sub(r"[^A-Za-z0-9_]", "_", value or "")
    cleaned = re.sub(r"_+", "_", cleaned).strip("_") or fallback
    if cleaned[0].isdigit():
        cleaned = "_" + cleaned
    return cleaned


def _unique_identifiers(items: Sequence[dict[str, Any]], fallback: str) -> dict[str, str]:
    result: dict[str, str] = {}
    used: set[str] = set()
    for index, item in enumerate(items):
        source = str(item.get("name") or f"{fallback}_{index}")
        base = _identifier(source, fallback)
        candidate = base
        suffix = 2
        while candidate in used:
            candidate = f"{base}_{suffix}"
            suffix += 1
        result[source] = candidate
        used.add(candidate)
    return result


def _vector3(value: Any, default: Sequence[float] = (0.0, 0.0, 0.0)) -> tuple[float, float, float]:
    if not isinstance(value, dict):
        return float(default[0]), float(default[1]), float(default[2])
    return float(value.get("x", default[0])), float(value.get("y", default[1])), float(value.get("z", default[2]))


def _pose(value: Any) -> tuple[tuple[float, float, float], tuple[float, float, float]]:
    if not isinstance(value, dict):
        return (0.0, 0.0, 0.0), (0.0, 0.0, 0.0)
    return _vector3(value.get("xyz")), _vector3(value.get("rpy"))


def _rpy_matrix(rpy: Sequence[float]) -> list[list[float]]:
    roll, pitch, yaw = rpy
    cr, sr = math.cos(roll), math.sin(roll)
    cp, sp = math.cos(pitch), math.sin(pitch)
    cy, sy = math.cos(yaw), math.sin(yaw)
    return [
        [cy * cp, cy * sp * sr - sy * cr, cy * sp * cr + sy * sr],
        [sy * cp, sy * sp * sr + cy * cr, sy * sp * cr - cy * sr],
        [-sp, cp * sr, cp * cr],
    ]


def _matrix_multiply(left: Sequence[Sequence[float]], right: Sequence[Sequence[float]]) -> list[list[float]]:
    return [
        [sum(left[row][inner] * right[inner][column] for inner in range(3)) for column in range(3)]
        for row in range(3)
    ]


def _matrix_to_quaternion(matrix: Sequence[Sequence[float]]) -> tuple[float, float, float, float]:
    trace = matrix[0][0] + matrix[1][1] + matrix[2][2]
    if trace > 0.0:
        scale = math.sqrt(trace + 1.0) * 2.0
        w = 0.25 * scale
        x = (matrix[2][1] - matrix[1][2]) / scale
        y = (matrix[0][2] - matrix[2][0]) / scale
        z = (matrix[1][0] - matrix[0][1]) / scale
    elif matrix[0][0] > matrix[1][1] and matrix[0][0] > matrix[2][2]:
        scale = math.sqrt(1.0 + matrix[0][0] - matrix[1][1] - matrix[2][2]) * 2.0
        w = (matrix[2][1] - matrix[1][2]) / scale
        x = 0.25 * scale
        y = (matrix[0][1] + matrix[1][0]) / scale
        z = (matrix[0][2] + matrix[2][0]) / scale
    elif matrix[1][1] > matrix[2][2]:
        scale = math.sqrt(1.0 + matrix[1][1] - matrix[0][0] - matrix[2][2]) * 2.0
        w = (matrix[0][2] - matrix[2][0]) / scale
        x = (matrix[0][1] + matrix[1][0]) / scale
        y = 0.25 * scale
        z = (matrix[1][2] + matrix[2][1]) / scale
    else:
        scale = math.sqrt(1.0 + matrix[2][2] - matrix[0][0] - matrix[1][1]) * 2.0
        w = (matrix[1][0] - matrix[0][1]) / scale
        x = (matrix[0][2] + matrix[2][0]) / scale
        y = (matrix[1][2] + matrix[2][1]) / scale
        z = 0.25 * scale
    magnitude = math.sqrt(w * w + x * x + y * y + z * z)
    if magnitude <= 1.0e-15:
        return 1.0, 0.0, 0.0, 0.0
    return w / magnitude, x / magnitude, y / magnitude, z / magnitude


def _quaternion(value: Sequence[float]) -> Gf.Quatf:
    return Gf.Quatf(float(value[0]), Gf.Vec3f(float(value[1]), float(value[2]), float(value[3])))


def _pose_matrix(xyz: Sequence[float], rpy: Sequence[float]) -> Gf.Matrix4d:
    rotation = _matrix_to_quaternion(_rpy_matrix(rpy))
    matrix = Gf.Matrix4d(1.0)
    matrix.SetRotate(Gf.Quatd(rotation[0], Gf.Vec3d(rotation[1], rotation[2], rotation[3])))
    matrix.SetTranslateOnly(Gf.Vec3d(*xyz))
    return matrix


def _apply_pose(prim: Usd.Prim, xyz: Sequence[float], rpy: Sequence[float]) -> None:
    xform = UsdGeom.Xformable(prim)
    xform.ClearXformOpOrder()
    xform.AddTransformOp(UsdGeom.XformOp.PrecisionDouble).Set(_pose_matrix(xyz, rpy))


def _apply_matrix(
    prim: Usd.Prim,
    rotation: Sequence[Sequence[float]],
    translation: Sequence[float],
) -> None:
    quaternion = _matrix_to_quaternion(rotation)
    matrix = Gf.Matrix4d(1.0)
    matrix.SetRotate(Gf.Quatd(quaternion[0], Gf.Vec3d(*quaternion[1:])))
    matrix.SetTranslateOnly(Gf.Vec3d(*translation))
    xform = UsdGeom.Xformable(prim)
    xform.ClearXformOpOrder()
    xform.AddTransformOp(UsdGeom.XformOp.PrecisionDouble).Set(matrix)


def _compose_pose(
    parent: tuple[list[list[float]], tuple[float, float, float]],
    child_xyz: Sequence[float],
    child_rpy: Sequence[float],
) -> tuple[list[list[float]], tuple[float, float, float]]:
    parent_rotation, parent_translation = parent
    child_rotation = _rpy_matrix(child_rpy)
    rotation = _matrix_multiply(parent_rotation, child_rotation)
    translated = tuple(
        parent_translation[row]
        + sum(parent_rotation[row][column] * child_xyz[column] for column in range(3))
        for row in range(3)
    )
    return rotation, translated


def _world_link_poses(
    links: Sequence[dict[str, Any]],
    joints: Sequence[dict[str, Any]],
) -> dict[str, tuple[list[list[float]], tuple[float, float, float]]]:
    identity = ([[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]], (0.0, 0.0, 0.0))
    link_names = {str(link.get("name") or "") for link in links}
    child_names = {str(joint.get("child") or "") for joint in joints}
    poses = {name: identity for name in sorted(link_names - child_names)}
    if not poses and links:
        raise AdapterError("Robot Link graph has no root Link.")
    pending = list(joints)
    while pending:
        next_pending: list[dict[str, Any]] = []
        progressed = False
        for joint in pending:
            parent = str(joint.get("parent") or "")
            child = str(joint.get("child") or "")
            if parent not in poses:
                next_pending.append(joint)
                continue
            xyz, rpy = _pose(joint.get("origin"))
            candidate = _compose_pose(poses[parent], xyz, rpy)
            if child in poses:
                raise AdapterError(f"Link graph assigns more than one parent to {child!r}.")
            poses[child] = candidate
            progressed = True
        if not progressed:
            unresolved = ", ".join(str(item.get("name") or "<unnamed>") for item in next_pending)
            raise AdapterError("Robot Link graph is cyclic or disconnected at Joints: " + unresolved)
        pending = next_pending
    if set(poses) != link_names:
        raise AdapterError("Robot Link graph does not cover every Link.")
    return poses


def _jacobi_eigen(matrix: Sequence[Sequence[float]]) -> tuple[list[float], list[list[float]]]:
    values = [[float(matrix[row][column]) for column in range(3)] for row in range(3)]
    vectors = [[1.0 if row == column else 0.0 for column in range(3)] for row in range(3)]
    for _ in range(32):
        p, q = max(((0, 1), (0, 2), (1, 2)), key=lambda pair: abs(values[pair[0]][pair[1]]))
        if abs(values[p][q]) < 1.0e-14:
            break
        angle = 0.5 * math.atan2(2.0 * values[p][q], values[q][q] - values[p][p])
        cosine, sine = math.cos(angle), math.sin(angle)
        for row in range(3):
            if row not in (p, q):
                left, right = values[row][p], values[row][q]
                values[row][p] = values[p][row] = cosine * left - sine * right
                values[row][q] = values[q][row] = sine * left + cosine * right
        app, aqq, apq = values[p][p], values[q][q], values[p][q]
        values[p][p] = cosine * cosine * app - 2.0 * sine * cosine * apq + sine * sine * aqq
        values[q][q] = sine * sine * app + 2.0 * sine * cosine * apq + cosine * cosine * aqq
        values[p][q] = values[q][p] = 0.0
        for row in range(3):
            left, right = vectors[row][p], vectors[row][q]
            vectors[row][p] = cosine * left - sine * right
            vectors[row][q] = sine * left + cosine * right
    ordered = sorted(range(3), key=lambda index: values[index][index])
    eigenvalues = [values[index][index] for index in ordered]
    eigenvectors = [[vectors[row][index] for index in ordered] for row in range(3)]
    determinant = (
        eigenvectors[0][0] * (eigenvectors[1][1] * eigenvectors[2][2] - eigenvectors[1][2] * eigenvectors[2][1])
        - eigenvectors[0][1] * (eigenvectors[1][0] * eigenvectors[2][2] - eigenvectors[1][2] * eigenvectors[2][0])
        + eigenvectors[0][2] * (eigenvectors[1][0] * eigenvectors[2][1] - eigenvectors[1][1] * eigenvectors[2][0])
    )
    if determinant < 0.0:
        for row in range(3):
            eigenvectors[row][2] *= -1.0
    return eigenvalues, eigenvectors


def _axis_alignment(axis: Sequence[float]) -> tuple[float, float, float, float]:
    x, y, z = axis
    magnitude = math.sqrt(x * x + y * y + z * z)
    if magnitude <= 1.0e-15:
        raise AdapterError("A moving joint has a zero-length axis.")
    x, y, z = x / magnitude, y / magnitude, z / magnitude
    dot = max(-1.0, min(1.0, x))
    if dot > 1.0 - 1.0e-12:
        return 1.0, 0.0, 0.0, 0.0
    if dot < -1.0 + 1.0e-12:
        return 0.0, 0.0, 0.0, 1.0
    cross = (0.0, -z, y)
    scale = math.sqrt(2.0 * (1.0 + dot))
    return scale * 0.5, cross[0] / scale, cross[1] / scale, cross[2] / scale


def _planar_axis_alignment(axis: Sequence[float]) -> tuple[float, float, float, float]:
    x, y, z = axis
    magnitude = math.sqrt(x * x + y * y + z * z)
    if magnitude <= 1.0e-15:
        raise AdapterError("A planar joint has a zero-length axis.")
    x, y, z = x / magnitude, y / magnitude, z / magnitude
    dot = max(-1.0, min(1.0, z))
    if dot > 1.0 - 1.0e-12:
        return 1.0, 0.0, 0.0, 0.0
    if dot < -1.0 + 1.0e-12:
        return 0.0, 1.0, 0.0, 0.0
    cross = (-y, x, 0.0)
    scale = math.sqrt(2.0 * (1.0 + dot))
    return scale * 0.5, cross[0] / scale, cross[1] / scale, cross[2] / scale


def _read_stl(path: Path) -> tuple[list[tuple[float, float, float]], list[int]]:
    data = path.read_bytes()
    points: list[tuple[float, float, float]] = []
    indices: list[int] = []
    lookup: dict[tuple[float, float, float], int] = {}

    def add(vertex: Sequence[float]) -> None:
        point = (float(vertex[0]), float(vertex[1]), float(vertex[2]))
        index = lookup.get(point)
        if index is None:
            index = len(points)
            points.append(point)
            lookup[point] = index
        indices.append(index)

    if len(data) >= 84:
        triangle_count = struct.unpack_from("<I", data, 80)[0]
        if 84 + triangle_count * 50 == len(data):
            offset = 84
            for _ in range(triangle_count):
                values = struct.unpack_from("<12fH", data, offset)
                add(values[3:6])
                add(values[6:9])
                add(values[9:12])
                offset += 50
            return points, indices
    text = data.decode("utf-8", errors="ignore")
    vertex_pattern = re.compile(
        r"\bvertex\s+([-+0-9.eE]+)\s+([-+0-9.eE]+)\s+([-+0-9.eE]+)",
        re.IGNORECASE,
    )
    for match in vertex_pattern.finditer(text):
        add((float(match.group(1)), float(match.group(2)), float(match.group(3))))
    if not indices or len(indices) % 3 != 0:
        raise AdapterError(f"Unsupported or malformed STL mesh: {path}")
    return points, indices


def _write_mesh_asset(source: Path, destination: Path) -> None:
    points, indices = _read_stl(source)
    destination.parent.mkdir(parents=True, exist_ok=True)
    stage = Usd.Stage.CreateNew(str(destination))
    asset = UsdGeom.Xform.Define(stage, "/Asset")
    stage.SetDefaultPrim(asset.GetPrim())
    mesh = UsdGeom.Mesh.Define(stage, "/Asset/Mesh")
    mesh.CreatePointsAttr([Gf.Vec3f(*point) for point in points])
    mesh.CreateFaceVertexCountsAttr([3] * (len(indices) // 3))
    mesh.CreateFaceVertexIndicesAttr(indices)
    mesh.CreateSubdivisionSchemeAttr(UsdGeom.Tokens.none)
    mesh.CreateDoubleSidedAttr(True)
    stage.GetRootLayer().Save()


def _copy_canonical_meshes(bundle: Path, output: Path) -> None:
    source = bundle / "meshes"
    if source.is_dir():
        shutil.copytree(source, output / "meshes", dirs_exist_ok=True)


def _define_material(stage: Usd.Stage, path: str, rgba: Sequence[float]) -> UsdShade.Material:
    material = UsdShade.Material.Define(stage, path)
    shader = UsdShade.Shader.Define(stage, path + "/PreviewSurface")
    shader.CreateIdAttr("UsdPreviewSurface")
    shader.CreateInput("diffuseColor", Sdf.ValueTypeNames.Color3f).Set(Gf.Vec3f(*rgba[:3]))
    shader.CreateInput("opacity", Sdf.ValueTypeNames.Float).Set(float(rgba[3]))
    material.CreateSurfaceOutput().ConnectToSource(shader.ConnectableAPI(), "surface")
    return material


def _geometry_prim(
    stage: Usd.Stage,
    bundle: Path,
    output: Path,
    cache: dict[str, str],
    path: str,
    geometry: dict[str, Any],
    collision: bool,
) -> Usd.Prim:
    geometry_type = str(geometry.get("type") or "").lower()
    if geometry_type == "mesh":
        uri = str(geometry.get("uri") or "")
        source = _safe_path(bundle, uri)
        if source.suffix.lower() != ".stl":
            raise AdapterError(
                f"USD assets require STL source meshes; unsupported geometry: {uri}. "
                "Select STL mesh format and export again."
            )
        if not source.is_file():
            raise AdapterError(f"Mesh asset is missing: {uri}")
        dependency = cache.get(uri)
        if dependency is None:
            digest = hashlib.sha256(uri.encode("utf-8")).hexdigest()[:16]
            dependency = f"geometry/{_identifier(source.stem, 'mesh')}_{digest}.usd"
            _write_mesh_asset(source, output / dependency)
            cache[uri] = dependency
        prim = UsdGeom.Xform.Define(stage, path).GetPrim()
        prim.GetReferences().AddReference("./" + dependency.replace("\\", "/"), "/Asset")
        scale = _vector3(geometry.get("scale"), (1.0, 1.0, 1.0))
        UsdGeom.Xformable(prim).AddScaleOp().Set(Gf.Vec3f(*scale))
    elif geometry_type == "box":
        size = _vector3(geometry.get("size"), (1.0, 1.0, 1.0))
        cube = UsdGeom.Cube.Define(stage, path)
        cube.CreateSizeAttr(1.0)
        cube.AddScaleOp().Set(Gf.Vec3f(*size))
        prim = cube.GetPrim()
    elif geometry_type == "sphere":
        sphere = UsdGeom.Sphere.Define(stage, path)
        sphere.CreateRadiusAttr(float(geometry.get("radius") or 0.0))
        prim = sphere.GetPrim()
    elif geometry_type == "cylinder":
        cylinder = UsdGeom.Cylinder.Define(stage, path)
        cylinder.CreateRadiusAttr(float(geometry.get("radius") or 0.0))
        cylinder.CreateHeightAttr(float(geometry.get("length") or 0.0))
        cylinder.CreateAxisAttr(UsdGeom.Tokens.z)
        prim = cylinder.GetPrim()
    else:
        raise AdapterError(f"Unsupported USD geometry type: {geometry_type!r}")
    if collision:
        if not UsdPhysics.CollisionAPI.Apply(prim):
            raise AdapterError(f"OpenUSD rejected CollisionAPI at {path}")
    return prim


def _apply_mass(link_prim: Usd.Prim, inertial: dict[str, Any]) -> None:
    mass = float(inertial.get("mass") or 0.0)
    if not math.isfinite(mass) or mass <= 0.0:
        raise AdapterError(f"Invalid mass for {link_prim.GetPath()}: {mass}")
    origin_xyz, origin_rpy = _pose(inertial.get("origin"))
    tensor = inertial.get("inertia") or {}
    matrix = [
        [float(tensor.get("ixx") or 0.0), float(tensor.get("ixy") or 0.0), float(tensor.get("ixz") or 0.0)],
        [float(tensor.get("ixy") or 0.0), float(tensor.get("iyy") or 0.0), float(tensor.get("iyz") or 0.0)],
        [float(tensor.get("ixz") or 0.0), float(tensor.get("iyz") or 0.0), float(tensor.get("izz") or 0.0)],
    ]
    eigenvalues, eigenvectors = _jacobi_eigen(matrix)
    if any(not math.isfinite(value) or value <= 0.0 for value in eigenvalues):
        raise AdapterError(f"Inertia tensor is not positive definite for {link_prim.GetPath()}")
    principal_rotation = _matrix_multiply(_rpy_matrix(origin_rpy), eigenvectors)
    mass_api = UsdPhysics.MassAPI.Apply(link_prim)
    mass_api.CreateMassAttr().Set(mass)
    mass_api.CreateCenterOfMassAttr().Set(Gf.Vec3f(*origin_xyz))
    mass_api.CreateDiagonalInertiaAttr().Set(Gf.Vec3f(*eigenvalues))
    mass_api.CreatePrincipalAxesAttr().Set(_quaternion(_matrix_to_quaternion(principal_rotation)))


def _joint_schema(stage: Usd.Stage, path: str, joint_type: str) -> UsdPhysics.Joint:
    if joint_type == "fixed":
        return UsdPhysics.FixedJoint.Define(stage, path)
    if joint_type in ("continuous", "revolute"):
        return UsdPhysics.RevoluteJoint.Define(stage, path)
    if joint_type == "prismatic":
        return UsdPhysics.PrismaticJoint.Define(stage, path)
    return UsdPhysics.Joint.Define(stage, path)


def _limit_api_type_and_token(dof: str) -> tuple[Any, Any]:
    limit_api_type = getattr(UsdPhysics, "LimitAPI", None)
    tokens = getattr(UsdPhysics, "Tokens", None)
    token = getattr(tokens, dof, None) if tokens is not None else None
    if limit_api_type is None or token is None:
        raise AdapterError(
            "The bundled OpenUSD runtime cannot represent planar joint limits; "
            f"missing LimitAPI support for {dof}."
        )
    return limit_api_type, token


def _apply_planar_limits(prim: Usd.Prim) -> None:
    for dof in _PLANAR_LOCKED_DOFS:
        limit_api_type, token = _limit_api_type_and_token(dof)
        try:
            limit_api = limit_api_type.Apply(prim, token)
            applied = bool(limit_api) and prim.HasAPI(limit_api_type, token)
            # OpenUSD LimitAPI defines low > high as a locked axis.
            low_set = applied and limit_api.CreateLowAttr().Set(1.0)
            high_set = applied and limit_api.CreateHighAttr().Set(-1.0)
        except Exception as exc:
            raise AdapterError(
                f"The bundled OpenUSD runtime could not lock planar DOF {dof} "
                f"at {prim.GetPath()}."
            ) from exc
        if not applied or not low_set or not high_set:
            raise AdapterError(
                f"The bundled OpenUSD runtime rejected the planar DOF lock {dof} "
                f"at {prim.GetPath()}."
            )


def _validate_planar_joint(prim: Usd.Prim) -> None:
    path = prim.GetPath()
    if not prim or not prim.IsA(UsdPhysics.Joint):
        raise AdapterError(f"OpenUSD planar joint is missing or invalid at {path}.")
    joint_type = prim.GetAttribute("osurdf:jointType")
    if not joint_type or joint_type.Get() != "planar":
        raise AdapterError(f"OpenUSD planar joint marker is missing at {path}.")

    for dof in _PLANAR_LOCKED_DOFS:
        limit_api_type, token = _limit_api_type_and_token(dof)
        try:
            if not prim.HasAPI(limit_api_type, token):
                raise AdapterError(f"OpenUSD planar joint {path} does not lock {dof}.")
            limit_api = limit_api_type(prim, token)
            low = limit_api.GetLowAttr()
            high = limit_api.GetHighAttr()
            if (
                not low.HasAuthoredValueOpinion()
                or not high.HasAuthoredValueOpinion()
                or float(low.Get()) <= float(high.Get())
            ):
                raise AdapterError(
                    f"OpenUSD planar joint {path} must lock {dof} with low > high."
                )
        except AdapterError:
            raise
        except Exception as exc:
            raise AdapterError(
                f"OpenUSD could not verify planar DOF lock {dof} at {path}."
            ) from exc

    for dof in _PLANAR_FREE_DOFS:
        limit_api_type, token = _limit_api_type_and_token(dof)
        try:
            constrained = prim.HasAPI(limit_api_type, token)
        except Exception as exc:
            raise AdapterError(
                f"OpenUSD could not verify planar free DOF {dof} at {path}."
            ) from exc
        if constrained:
            raise AdapterError(f"OpenUSD planar joint {path} unexpectedly constrains {dof}.")


def _build_stage(bundle: Path, output: Path, robot: dict[str, Any]) -> dict[str, Any]:
    links = [item for item in robot.get("links", []) if isinstance(item, dict)]
    joints = [item for item in robot.get("joints", []) if isinstance(item, dict)]
    link_names = _unique_identifiers(links, "link")
    joint_names = _unique_identifiers(joints, "joint")
    stage_path = output / "robot.usd"
    stage = Usd.Stage.CreateNew(str(stage_path))
    UsdGeom.SetStageUpAxis(stage, UsdGeom.Tokens.z)
    UsdGeom.SetStageMetersPerUnit(stage, 1.0)
    world = UsdGeom.Xform.Define(stage, "/World")
    stage.SetDefaultPrim(world.GetPrim())
    robot_prim = UsdGeom.Xform.Define(stage, "/World/Robot").GetPrim()
    UsdPhysics.ArticulationRootAPI.Apply(robot_prim)
    stage.DefinePrim("/World/Robot/Links", "Scope")
    stage.DefinePrim("/World/Robot/Joints", "Scope")
    stage.DefinePrim("/World/Looks", "Scope")

    material_cache: dict[tuple[float, float, float, float], UsdShade.Material] = {}
    mesh_cache: dict[str, str] = {}
    link_paths: dict[str, str] = {}
    world_poses = _world_link_poses(links, joints)
    for link_index, link in enumerate(links):
        name = str(link.get("name") or f"link_{link_index}")
        link_path = f"/World/Robot/Links/{link_names[name]}"
        link_paths[name] = link_path
        prim = UsdGeom.Xform.Define(stage, link_path).GetPrim()
        world_rotation, world_translation = world_poses[name]
        _apply_matrix(prim, world_rotation, world_translation)
        UsdPhysics.RigidBodyAPI.Apply(prim)
        inertial = link.get("inertial")
        if isinstance(inertial, dict):
            _apply_mass(prim, inertial)
        visual_scope = link_path + "/Visuals"
        collision_scope = link_path + "/Collisions"
        stage.DefinePrim(visual_scope, "Scope")
        stage.DefinePrim(collision_scope, "Scope")
        for visual_index, visual in enumerate(link.get("visuals") or []):
            if not isinstance(visual, dict) or not isinstance(visual.get("geometry"), dict):
                continue
            item_path = f"{visual_scope}/visual_{visual_index}"
            item_prim = _geometry_prim(
                stage, bundle, output, mesh_cache, item_path, visual["geometry"], False
            )
            xyz, rpy = _pose(visual.get("origin"))
            _apply_pose(item_prim, xyz, rpy)
            material_data = visual.get("material")
            if isinstance(material_data, dict):
                rgba_data = material_data.get("rgba")
                rgba = (*_vector3(rgba_data, (0.7, 0.7, 0.7)), float((rgba_data or {}).get("w", 1.0)))
                key = tuple(round(value, 9) for value in rgba)
                material = material_cache.get(key)
                if material is None:
                    material = _define_material(stage, f"/World/Looks/material_{len(material_cache)}", rgba)
                    material_cache[key] = material
                UsdShade.MaterialBindingAPI.Apply(item_prim).Bind(material)
        for collision_index, collision in enumerate(link.get("collisions") or []):
            if not isinstance(collision, dict) or not isinstance(collision.get("geometry"), dict):
                continue
            item_path = f"{collision_scope}/collision_{collision_index}"
            item_prim = _geometry_prim(
                stage, bundle, output, mesh_cache, item_path, collision["geometry"], True
            )
            xyz, rpy = _pose(collision.get("origin"))
            _apply_pose(item_prim, xyz, rpy)

    unsupported_joint_types: list[str] = []
    planar_joint_paths: list[str] = []
    for joint_index, joint_data in enumerate(joints):
        name = str(joint_data.get("name") or f"joint_{joint_index}")
        parent_name = str(joint_data.get("parent") or "")
        child_name = str(joint_data.get("child") or "")
        if parent_name not in link_paths or child_name not in link_paths:
            raise AdapterError(f"Joint {name!r} references an unknown Link.")
        joint_type = str(joint_data.get("type") or "fixed").lower()
        joint = _joint_schema(stage, f"/World/Robot/Joints/{joint_names[name]}", joint_type)
        joint.CreateBody0Rel().SetTargets([Sdf.Path(link_paths[parent_name])])
        joint.CreateBody1Rel().SetTargets([Sdf.Path(link_paths[child_name])])
        xyz, rpy = _pose(joint_data.get("origin"))
        axis = _vector3(joint_data.get("axis"), (1.0, 0.0, 0.0))
        if joint_type == "planar":
            alignment = _planar_axis_alignment(axis)
        elif joint_type not in ("fixed", "floating"):
            alignment = _axis_alignment(axis)
        else:
            alignment = (1.0, 0.0, 0.0, 0.0)
        parent_quaternion = _matrix_to_quaternion(_rpy_matrix(rpy))
        parent_quat = Gf.Quatd(parent_quaternion[0], Gf.Vec3d(*parent_quaternion[1:]))
        align_quat = Gf.Quatd(alignment[0], Gf.Vec3d(*alignment[1:]))
        combined = parent_quat * align_quat
        combined_imaginary = combined.GetImaginary()
        joint.CreateLocalPos0Attr().Set(Gf.Vec3f(*xyz))
        joint.CreateLocalRot0Attr().Set(
            _quaternion(
                (
                    combined.GetReal(),
                    combined_imaginary[0],
                    combined_imaginary[1],
                    combined_imaginary[2],
                )
            )
        )
        joint.CreateLocalPos1Attr().Set(Gf.Vec3f(0.0))
        joint.CreateLocalRot1Attr().Set(_quaternion(alignment))
        limit = joint_data.get("limit") or {}
        if joint_type in ("continuous", "revolute"):
            revolute = UsdPhysics.RevoluteJoint(joint.GetPrim())
            revolute.CreateAxisAttr(UsdPhysics.Tokens.x)
            if joint_type == "revolute":
                if limit.get("lower") is not None:
                    revolute.CreateLowerLimitAttr().Set(math.degrees(float(limit["lower"])))
                if limit.get("upper") is not None:
                    revolute.CreateUpperLimitAttr().Set(math.degrees(float(limit["upper"])))
        elif joint_type == "prismatic":
            prismatic = UsdPhysics.PrismaticJoint(joint.GetPrim())
            prismatic.CreateAxisAttr(UsdPhysics.Tokens.x)
            if limit.get("lower") is not None:
                prismatic.CreateLowerLimitAttr().Set(float(limit["lower"]))
            if limit.get("upper") is not None:
                prismatic.CreateUpperLimitAttr().Set(float(limit["upper"]))
        elif joint_type == "planar":
            _apply_planar_limits(joint.GetPrim())
            planar_joint_paths.append(str(joint.GetPath()))
        elif joint_type not in ("fixed",):
            unsupported_joint_types.append(name + ":" + joint_type)
        joint.GetPrim().CreateAttribute("osurdf:jointType", Sdf.ValueTypeNames.String).Set(joint_type)
        joint.GetPrim().CreateAttribute("osurdf:sourceName", Sdf.ValueTypeNames.String).Set(name)

    stage.GetRootLayer().Save()
    return {
        "stage": stage_path,
        "linkNames": link_names,
        "jointNames": joint_names,
        "meshDependencies": sorted(mesh_cache.values()),
        "planarJointPaths": planar_joint_paths,
        "unsupportedPhysicsJointTypes": unsupported_joint_types,
    }


def _validate_stage(
    stage_path: Path,
    expected_links: int,
    expected_joints: int,
    expected_planar_joint_paths: Sequence[str],
) -> dict[str, Any]:
    stage = Usd.Stage.Open(str(stage_path))
    if stage is None:
        raise AdapterError(f"OpenUSD could not reopen the generated stage: {stage_path}")
    links = 0
    joints = 0
    rigid_bodies = 0
    masses = 0
    collisions = 0
    unresolved_assets: list[str] = []
    actual_planar_joint_paths: list[str] = []
    for prim in stage.Traverse():
        path = str(prim.GetPath())
        if path.startswith("/World/Robot/Links/") and path.count("/") == 4:
            links += 1
        if prim.IsA(UsdPhysics.Joint):
            joints += 1
        if prim.HasAPI(UsdPhysics.RigidBodyAPI):
            rigid_bodies += 1
        if prim.HasAPI(UsdPhysics.MassAPI):
            masses += 1
        if prim.HasAPI(UsdPhysics.CollisionAPI):
            collisions += 1
        joint_type = prim.GetAttribute("osurdf:jointType")
        if joint_type and joint_type.Get() == "planar":
            actual_planar_joint_paths.append(path)
        for reference in prim.GetMetadata("references").GetAddedOrExplicitItems() if prim.HasAuthoredReferences() else []:
            asset_path = str(reference.assetPath)
            if asset_path and not (stage_path.parent / asset_path).resolve().is_file():
                unresolved_assets.append(asset_path)
    if links != expected_links:
        raise AdapterError(f"OpenUSD validation expected {expected_links} Links, found {links}.")
    if joints != expected_joints:
        raise AdapterError(f"OpenUSD validation expected {expected_joints} Joints, found {joints}.")
    if rigid_bodies != expected_links:
        raise AdapterError(f"OpenUSD validation expected {expected_links} rigid bodies, found {rigid_bodies}.")
    if sorted(actual_planar_joint_paths) != sorted(expected_planar_joint_paths):
        raise AdapterError(
            "OpenUSD validation found an unexpected set of planar Joints: "
            + ", ".join(sorted(actual_planar_joint_paths))
        )
    for path in expected_planar_joint_paths:
        _validate_planar_joint(stage.GetPrimAtPath(path))
    if unresolved_assets:
        raise AdapterError("OpenUSD stage has unresolved local assets: " + ", ".join(sorted(set(unresolved_assets))))
    return {
        "ok": True,
        "stageReopened": True,
        "links": links,
        "physicsJoints": joints,
        "rigidBodies": rigid_bodies,
        "massProperties": masses,
        "collisionShapes": collisions,
        "planarJointsValidated": len(expected_planar_joint_paths),
        "openUsdVersion": ".".join(str(value) for value in Usd.GetVersion()),
    }


def export_bundle(bundle: Path, output: Path, overwrite: bool) -> dict[str, Any]:
    bundle = bundle.resolve()
    output = output.resolve()
    _validate_output_destination(bundle, output)
    robot_path = bundle / "robot.json"
    if not robot_path.is_file():
        raise AdapterError(f"OSURDF bundle is missing robot.json: {bundle}")
    robot = _read_json(robot_path)
    if output.exists() and not overwrite:
        raise AdapterError(f"USD output exists; explicit overwrite is required: {output}")
    output.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=".osurdf-usd-", dir=str(output.parent)))
    try:
        _copy_canonical_meshes(bundle, staging)
        built = _build_stage(bundle, staging, robot)
        validation = _validate_stage(
            Path(built["stage"]),
            len(robot.get("links") or []),
            len(robot.get("joints") or []),
            built["planarJointPaths"],
        )
        name_map = {
            "schemaVersion": 1,
            "links": built["linkNames"],
            "joints": built["jointNames"],
        }
        report = {
            "schemaVersion": 1,
            "ok": True,
            "assetType": "OpenUSD robot asset",
            "entrypoint": "robot.usd",
            "geometryDependencies": built["meshDependencies"],
            "validation": validation,
            "validationScope": {
                "en": "Generated and reopened with the bundled OpenUSD runtime. Isaac Sim and Isaac Lab were not executed.",
                "zh-CN": "已使用安装包内置 OpenUSD 运行时生成并重新打开校验；未运行 Isaac Sim 或 Isaac Lab。",
            },
            "physicsMappingNotes": {
                "unsupportedJointTypes": built["unsupportedPhysicsJointTypes"],
                "en": "Planar joints use generic UsdPhysics joints with transZ, rotX, and rotY locked by LimitAPI (low > high), leaving local transX, transY, and rotZ free; floating joints remain generic and are not exact mappings.",
                "zh-CN": "planar 关节使用通用 UsdPhysics Joint，并以 LimitAPI（low > high）锁定 transZ、rotX 和 rotY，仅保留局部 transX、transY 和 rotZ；floating 仍为通用 Joint，不是精确映射。",
            },
        }
        report["retainedPreviousDirectory"] = None
        _write_json(staging / "name_map.json", name_map)
        _write_json(staging / "export_report.json", report)
        retained_previous = _publish_staging_directory(staging, output)
        report_warning = _record_retained_previous(
            output / "export_report.json",
            report,
            retained_previous,
        )
        result = {
            "ok": True,
            "outputDirectory": str(output),
            "usd": str(output / "robot.usd"),
            "nameMap": str(output / "name_map.json"),
            "report": str(output / "export_report.json"),
            "retainedPreviousDirectory": (
                str(retained_previous) if retained_previous is not None else None
            ),
        }
        if report_warning is not None:
            result["warning"] = report_warning
        return result
    finally:
        if staging.exists():
            shutil.rmtree(staging, ignore_errors=True)


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("export", nargs="?")
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--overwrite", action="store_true")
    return parser


def main(argv: Iterable[str] | None = None) -> int:
    args = _parser().parse_args(list(argv) if argv is not None else None)
    try:
        result = export_bundle(args.bundle, args.output, args.overwrite)
        print(json.dumps(result, ensure_ascii=False, sort_keys=True))
        return 0
    except (AdapterError, OSError, ValueError, TypeError, KeyError, json.JSONDecodeError) as exc:
        print(json.dumps({"ok": False, "error": str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
