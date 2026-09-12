"""Probe an installed release with its own Python; retain native module provenance."""
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import runpy
import struct
import subprocess
import sys


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def module_audit(bundle, destination):
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    psapi = ctypes.WinDLL("psapi", use_last_error=True)
    kernel.GetCurrentProcess.restype = wintypes.HANDLE
    psapi.EnumProcessModules.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.HMODULE), wintypes.DWORD, ctypes.POINTER(wintypes.DWORD)]
    psapi.GetModuleFileNameExW.argtypes = [wintypes.HANDLE, wintypes.HMODULE, wintypes.LPWSTR, wintypes.DWORD]
    process = kernel.GetCurrentProcess()
    handles = (wintypes.HMODULE * 4096)()
    needed = wintypes.DWORD()
    if not psapi.EnumProcessModules(process, handles, ctypes.sizeof(handles), ctypes.byref(needed)):
        raise ctypes.WinError(ctypes.get_last_error())
    assert needed.value <= ctypes.sizeof(handles), "Module buffer overflow"
    windows = Path(os.environ["SystemRoot"]).resolve()
    modules = []
    for handle in handles[:needed.value // ctypes.sizeof(wintypes.HMODULE)]:
        buffer = ctypes.create_unicode_buffer(32768)
        if not psapi.GetModuleFileNameExW(process, handle, buffer, len(buffer)):
            raise ctypes.WinError(ctypes.get_last_error())
        path = Path(buffer.value).resolve()
        origin = "payload" if path.is_relative_to(bundle) else "windows" if path.is_relative_to(windows) else "external"
        modules.append(dict(path=str(path), name=path.name, origin=origin, sha256=digest(path)))
    Path(destination).write_text(json.dumps(modules, indent=2), encoding="utf-8")
    unexpected = [row["path"] for row in modules if row["origin"] == "external"]
    assert not unexpected, f"Unexpected DLL search dependency: {unexpected}"
    return modules


def worker_mode(args):
    sys.argv = [str(args.bundle / "tools/mesh_reduction/reduce_stl.py"), "--request", str(args.request), "--result", str(args.result)]
    try:
        runpy.run_path(sys.argv[0], run_name="__main__")
    finally:
        module_audit(args.bundle, args.audit)


def probe(args):
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=True)
    runtime = args.bundle / "tools/openusd_runtime"
    assert Path(sys.executable).resolve() == (runtime / "python.exe").resolve()
    assert sys.version_info[:3] == (3, 11, 9)
    assert struct.calcsize("P") == 8
    assert all(Path(p).resolve().is_relative_to(runtime) for p in sys.path), sys.path
    import numpy as np
    import pymeshlab as ml
    from pxr import Usd, UsdGeom, UsdPhysics
    assert np.__version__ == "2.2.6"
    assert importlib.metadata.version("pymeshlab") == "2025.7.post1"
    assert Usd.GetVersion() == (0, 26, 8)
    assert "meshing_decimation_quadric_edge_collapse" in ml.filter_list()
    mesh = ml.MeshSet()
    mesh.create_sphere()
    sphere_before = mesh.current_mesh().face_number()
    mesh.meshing_decimation_quadric_edge_collapse(targetfacenum=80)
    sphere_after = mesh.current_mesh().face_number()
    assert 0 < sphere_after < sphere_before

    # A planar 8x8 grid exercises the exact shipped worker's real QEM path.
    records = []
    for x in range(8):
        for y in range(8):
            a, b = (x / 8, y / 8, 0), ((x + 1) / 8, y / 8, 0)
            c, d = ((x + 1) / 8, (y + 1) / 8, 0), (x / 8, (y + 1) / 8, 0)
            for tri in ((a, b, c), (a, c, d)):
                records.append(struct.pack("<12fH", 0, 0, 1, *tri[0], *tri[1], *tri[2], 0))
    source = out / "source.stl"
    source.write_bytes(bytes(range(80)) + struct.pack("<I", len(records)) + b"".join(records))
    source_hash = digest(source)
    results = []
    for name, ratio in (("unchanged", 0.0), ("reduced", 0.75)):
        target, request, result = [out / (name + suffix) for suffix in (".stl", ".request.json", ".result.json")]
        request.write_text(json.dumps(dict(schemaVersion=1, input=str(source), output=str(target), ratio=ratio, timeoutSeconds=30, maxError=0.0005, relativeError=0.005)), encoding="utf-8")
        completed = subprocess.run([sys.executable, "-I", "-B", str(Path(__file__).resolve()), "worker", "--bundle", str(args.bundle), "--request", str(request), "--result", str(result), "--audit", str(out / (name + ".modules.json"))], capture_output=True, text=True, encoding="utf-8", timeout=60)
        (out / (name + ".stdout.jsonl")).write_text(completed.stdout, encoding="utf-8")
        (out / (name + ".stderr.txt")).write_text(completed.stderr, encoding="utf-8")
        assert completed.returncode == 0, completed.stderr
        report = json.loads(result.read_text(encoding="utf-8"))
        assert report["schemaVersion"] == 1 and report["status"] == name, report
        assert report["sourceSha256"] == source_hash == digest(source)
        assert report["originalTriangles"] == 128
        assert report["finalBytes"] == target.stat().st_size == 84 + 50 * report["finalTriangles"]
        assert target.read_bytes()[:80] == source.read_bytes()[:80]
        if ratio == 0:
            assert digest(target) == source_hash and report["finalTriangles"] == 128
        else:
            assert 32 <= report["finalTriangles"] < 128
            assert report["processedRegions"] > 0
        events = [json.loads(line) for line in completed.stdout.splitlines()]
        assert len(events) > 3 and events[-1]["stage"] == "done"
        assert all(set(e) == {"type", "stage", "completed", "total"} and e["type"] == "progress" and e["completed"] <= e["total"] for e in events)
        results.append(report)
    module_audit(args.bundle, out / "imports.modules.json")
    report = dict(passed=True, executable=sys.executable, python=sys.version, sysPath=sys.path, numpy=np.__version__, pymeshlab=importlib.metadata.version("pymeshlab"), openusd=Usd.GetVersion(), sphereBefore=sphere_before, sphereAfter=sphere_after, workers=results)
    (out / "probe.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("PASS: packaged Python, native QEM, worker protocol, source preservation, module provenance")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("probe", "worker"))
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--request", type=Path)
    parser.add_argument("--result", type=Path)
    parser.add_argument("--audit", type=Path)
    args = parser.parse_args()
    args.bundle = args.bundle.resolve()
    worker_mode(args) if args.mode == "worker" else probe(args)
