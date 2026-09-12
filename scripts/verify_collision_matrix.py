"""Verify native collision artifacts and hash persistent USD/MJCF dependencies.

Binary USD layers use the installed OpenUSD Python; set SW2URDF_OPENUSD_PYTHON
to override its location. No CAD/COM access or native export is performed.
"""
import argparse
import csv
import hashlib
import json
import math
import os
from pathlib import Path
import struct
import subprocess
import sys
import xml.etree.ElementTree as ET

STRATEGIES = (
    "VisualMesh", "SimplifiedMesh", "AccurateMesh", "BoxPrimitive",
    "CylinderPrimitive", "SpherePrimitive", "ComponentBoxes", "ConvexHull",
)


def referenced_file(directory, base, reference):
    assert isinstance(reference, str) and reference, (base, "empty asset reference")
    assert not Path(reference).is_absolute() and ":" not in reference, (base, reference)
    path = (base / reference).resolve()
    assert path.is_relative_to(directory.resolve()) and path.is_file(), (
        "missing or escaping referenced asset", path)
    return path


def artifact_hashes(directory, paths):
    return [{"path": p.relative_to(directory.resolve()).as_posix(),
             "sha256": hashlib.sha256(p.read_bytes()).hexdigest(),
             "bytes": p.stat().st_size}
            for p in sorted(set(paths))]


def verify_usd_artifacts(report_path, report):
    directory = report_path.parent.resolve()
    entrypoint = referenced_file(directory, directory, report["entrypoint"])
    declared = {referenced_file(directory, directory, ref)
                for ref in report.get("geometryDependencies", [])}
    assert declared, (report_path, "missing geometry dependency inventory")
    # Geometry layers are binary USDC. Inspect both text and binary references
    # with the installed official SDK, not regex or stale report declarations.
    runtime = Path(os.environ.get("SW2URDF_OPENUSD_PYTHON", str(
        Path(os.environ.get("ProgramFiles", "C:/Program Files")) /
        "SolidWorks Corp/SolidWorks/URDFExporter/tools/openusd_runtime/python.exe")))
    assert runtime.is_file(), ("OpenUSD Python missing; set SW2URDF_OPENUSD_PYTHON", runtime)
    code = '''
import json, sys
from pathlib import Path
from pxr import Sdf
root, entry = map(lambda p: Path(p).resolve(), sys.argv[1:])
visited = set()
def visit(path):
    if path in visited:
        return
    if not path.is_relative_to(root) or not path.is_file():
        raise RuntimeError("Missing or escaping USD asset: " + str(path))
    visited.add(path)
    if path.suffix.lower() in (".usd", ".usda", ".usdc"):
        layer = Sdf.Layer.FindOrOpen(str(path))
        if layer is None:
            raise RuntimeError("Cannot open USD layer: " + str(path))
        refs = set(layer.GetCompositionAssetDependencies()) | set(layer.GetExternalAssetDependencies())
        for ref in sorted(refs):
            if not ref or Path(ref).is_absolute() or ":" in ref:
                raise RuntimeError("Nonlocal USD asset reference: " + ref)
            visit((path.parent / ref).resolve())
visit(entry)
print(json.dumps([str(p) for p in sorted(visited)]))
'''
    completed = subprocess.run([str(runtime), "-I", "-c", code, str(directory), str(entrypoint)],
                               capture_output=True, text=True, encoding="utf-8", check=True)
    visited = {Path(p) for p in json.loads(completed.stdout)}
    assert declared.issubset(visited), (report_path, "declared geometry is not referenced", declared - visited)
    return artifact_hashes(directory, visited | {report_path.resolve()})


def verify_mjcf_artifacts(report_path, report):
    directory = report_path.parent.resolve()
    visited = set()

    def visit_xml(path):
        if path in visited:
            return
        visited.add(path)
        root = ET.parse(path).getroot()
        assert root.tag == "mujoco", (path, "unexpected exported MJCF root")
        # The installed exporter writes a flat robot.xml/scene.xml pair and
        # explicit relative asset paths. Do not guess compiler path overrides.
        for compiler in root.iter("compiler"):
            assert not any(compiler.get(key) for key in
                           ("assetdir", "meshdir", "texturedir", "strippath")), (
                               path, "unsupported compiler path override")
        for element in root.iter():
            if "file" not in element.attrib:
                continue
            dependency = referenced_file(directory, path.parent, element.attrib["file"])
            if element.tag == "include":
                visit_xml(dependency)
            else:
                visited.add(dependency)

    for name in ("robot.xml", "scene.xml"):
        visit_xml(referenced_file(directory, directory, name))
    # Check every declared persistent output. Directories are represented by
    # their referenced asset files, never logs or temporary validator outputs.
    for name in report.get("outputs", []):
        path = (directory / name).resolve()
        assert path.is_relative_to(directory) and path.exists(), (report_path, name)
        if path.is_file():
            visited.add(path)
    return artifact_hashes(directory, visited | {report_path.resolve()})


def verify(root):
    if sys.flags.optimize != 0:
        raise RuntimeError("Verification requires assertions; run without -O")
    results = []
    for strategy in STRATEGIES:
        directory = root / strategy
        urdfs = sorted(directory.rglob("*.urdf"))
        assert len(urdfs) == 2, (strategy, "ROS1/ROS2 files", urdfs)
        reports = []
        report_groups = []
        for path in sorted(directory.rglob("*.csv")):
            with path.open(encoding="utf-8-sig", newline="") as stream:
                reader = csv.DictReader(stream)
                if "collision_effective_strategy" in (reader.fieldnames or []):
                    rows = list(reader)
                    report_groups.append((path, rows))
                    reports.extend(rows)
        assert reports, (strategy, "missing actual-strategy report")
        assert all(r["collision_strategy"] == strategy and
                   r["collision_effective_strategy"] == strategy for r in reports), (strategy, reports)
        meshes = []
        expected_links = None
        for path in urdfs:
            robot = ET.parse(path).getroot()
            links = robot.findall("link")
            assert len(links) == 4 and len(robot.findall("joint")) == 3, path
            link_names = {link.attrib["name"] for link in links}
            assert len(link_names) == 4, path
            if expected_links is None:
                expected_links = link_names
            assert link_names == expected_links, (path, "ROS link identities differ")
            # Package root is found by package.xml rather than a assumed layout.
            package = next(p for p in path.parents if (p / "package.xml").is_file())
            package_name = ET.parse(package / "package.xml").getroot().findtext("name")
            assert package_name, (package, "missing package name")
            for link in links:
                collisions = link.findall("collision")
                assert len(collisions) == 1, (path, link.attrib)
                mesh = collisions[0].find("geometry/mesh")
                assert mesh is not None, (path, link.attrib)
                assert [child.tag for child in collisions[0].find("geometry")] == ["mesh"], (path, link.attrib)
                uri = mesh.attrib["filename"]
                assert uri.startswith("package://"), uri
                uri_package, relative = uri[len("package://"):].split("/", 1)
                assert uri_package == package_name, (path, "mesh URI names a different package", uri)
                stl = (package / relative).resolve()
                assert stl.is_relative_to(package.resolve()) and stl.is_file(), stl
                data = stl.read_bytes()
                assert len(data) >= 84, stl
                triangles = struct.unpack_from("<I", data, 80)[0]
                assert triangles > 0 and len(data) == 84 + 50 * triangles, stl
                for triangle in struct.iter_unpack("<12fH", data[84:]):
                    assert all(math.isfinite(value) for value in triangle[:12]), stl
                if strategy in ("BoxPrimitive", "CylinderPrimitive", "SpherePrimitive", "ComponentBoxes"):
                    origin = collisions[0].find("origin")
                    assert origin is not None
                    assert all(float(v) == 0 for key in ("xyz", "rpy") for v in origin.attrib[key].split()), (path, origin.attrib)
                meshes.append({"link": link.attrib["name"], "triangles": triangles,
                               "sha256": hashlib.sha256(data).hexdigest()})
        for report_path, rows in report_groups:
            assert len(rows) == len(expected_links) and {r["link"] for r in rows} == expected_links, (
                strategy, report_path, "missing or duplicate link report rows")
        by_link = {}
        for mesh in meshes:
            by_link.setdefault(mesh["link"], set()).add(mesh["sha256"])
        assert all(len(hashes) == 1 for hashes in by_link.values()), (strategy, "ROS assets differ")
        target_reports = [(p, json.loads(p.read_text(encoding="utf-8-sig")))
                          for p in sorted(directory.rglob("export_report.json"))]
        usd = [(p, r) for p, r in target_reports if "validation" in r and
               "stageReopened" in r["validation"]]
        mujoco = [(p, r) for p, r in target_reports if "officialCompilation" in r]
        assert len(usd) == 1 and len(mujoco) == 1, (strategy, "missing target validation reports")
        usd_path, usd_report = usd[0]
        mjcf_path, mjcf_report = mujoco[0]
        assert usd_report.get("ok") is True and usd_report["validation"].get("ok") is True
        assert usd_report["validation"].get("stageReopened") is True
        assert usd_report["validation"].get("openUsdVersion"), strategy
        compilation = mjcf_report["officialCompilation"]
        assert compilation["status"] == "passed", (strategy, compilation)
        assert compilation["validator"] == "bundled-official-mujoco-tools", compilation
        assert compilation.get("muJoCoVersion"), strategy
        results.append({"strategy": strategy, "effectiveStrategyMatches": True,
                        "fallback": False, "rosPackages": len(urdfs), "meshes": meshes,
                        "openUsdValidation": usd_report["validation"],
                        "openUsdArtifacts": verify_usd_artifacts(usd_path, usd_report),
                        "muJoCoCompilation": compilation,
                        "muJoCoArtifacts": verify_mjcf_artifacts(mjcf_path, mjcf_report),
                        "reductionStatuses": sorted({r["mesh_reduction_status"] for r in reports})})
    return {"passed": True, "strategies": results}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = verify(args.root.resolve())
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("PASS: eight strategies, ROS package/mesh references, finite STL geometry, and hashed USD/MJCF dependencies")
