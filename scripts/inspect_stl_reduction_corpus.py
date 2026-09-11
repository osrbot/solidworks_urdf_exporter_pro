"""Independent binary-STL checks and fixed-camera before/after evidence.

Requires numpy and Pillow. Reads a Test-StlReductionCorpus.ps1 results.csv;
never writes into the source corpus or modifies an STL.
"""

import argparse
import csv
import hashlib
import json
from pathlib import Path
import struct

import numpy as np
from PIL import Image, ImageDraw, ImageFont


STL_DTYPE = np.dtype([("normal", "<f4", (3,)), ("v", "<f4", (3, 3)), ("attr", "<u2")])


def load(path):
    data = path.read_bytes()
    count = struct.unpack_from("<I", data, 80)[0]
    assert len(data) == 84 + 50 * count, f"Invalid binary STL: {path}"
    facets = np.frombuffer(data, dtype=STL_DTYPE, offset=84)
    vertices = facets["v"].astype(np.float64)
    assert np.isfinite(vertices).all(), f"Nonfinite vertices: {path}"
    area_vectors = np.cross(vertices[:, 1] - vertices[:, 0], vertices[:, 2] - vertices[:, 0])
    return vertices, {
        "hash": hashlib.sha256(data).hexdigest().upper(),
        "triangles": count,
        "bytes": len(data),
        "degenerate": int(np.count_nonzero(np.linalg.norm(area_vectors, axis=1) == 0)),
        "attributes": int(np.count_nonzero(facets["attr"])),
        "min": vertices.min(axis=(0, 1)).tolist(),
        "max": vertices.max(axis=(0, 1)).tolist(),
    }


def connected_count(pairs, vertex_count):
    parent = np.arange(vertex_count)

    def root(v):
        while parent[v] != v:
            parent[v] = parent[parent[v]]
            v = parent[v]
        return v

    for a, b in pairs:
        a, b = root(a), root(b)
        if a != b:
            parent[b] = a
    return len({root(v) for v in np.unique(pairs)})


def topology(vertices):
    points, indices = np.unique(vertices.reshape(-1, 3), axis=0, return_inverse=True)
    triangles = indices.reshape(-1, 3)
    edges = np.sort(np.concatenate([triangles[:, [0, 1]], triangles[:, [1, 2]], triangles[:, [2, 0]]]), axis=1)
    edges, counts = np.unique(edges, axis=0, return_counts=True)
    boundary = edges[counts == 1]
    return {
        "components": connected_count(edges, len(points)),
        "boundary_components": connected_count(boundary, len(points)) if len(boundary) else 0,
        "nonmanifold_edges": int(np.count_nonzero(counts > 2)),
        "duplicate_faces": len(triangles) - len(np.unique(np.sort(triangles, axis=1), axis=0)),
    }


def render(source, candidate, destination, title, direction=(1.4, -1.8, 1.1)):
    # Fixed orthographic camera and common scale; no smoothing or mesh resampling.
    image = Image.new("RGB", (1400, 740), "#f5f6f7")
    draw = ImageDraw.Draw(image)
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 22)
    except OSError:
        font = ImageFont.load_default()
    direction = np.array(direction, dtype=float)
    direction /= np.linalg.norm(direction)
    right = np.cross(direction, [0, 0, 1])
    right /= np.linalg.norm(right)
    up = np.cross(right, direction)
    rotation = np.stack([right, up, direction], axis=1)
    all_points = source.reshape(-1, 3) @ rotation
    lower, upper = all_points.min(axis=0), all_points.max(axis=0)
    center = (lower + upper) / 2
    scale = 560 / max(upper[0] - lower[0], upper[1] - lower[1], 1e-12)
    light = np.array([0.2, -0.5, 1.0])
    light /= np.linalg.norm(light)
    draw.text((24, 14), title, fill="#20252b", font=font)
    for panel, (label, mesh) in enumerate((("Source", source), ("Reduced", candidate))):
        transformed = mesh @ rotation
        normals = np.cross(mesh[:, 1] - mesh[:, 0], mesh[:, 2] - mesh[:, 0])
        lengths = np.linalg.norm(normals, axis=1)
        normals /= np.maximum(lengths[:, None], 1e-30)
        brightness = np.clip(0.35 + 0.65 * np.abs(normals @ light), 0, 1)
        projected = (transformed[:, :, :2] - center[:2]) * scale
        projected[:, :, 1] *= -1
        projected += [350 + panel * 700, 395]
        for index in np.argsort(transformed[:, :, 2].mean(axis=1)):
            if lengths[index] == 0:
                continue
            color = tuple(int(c * brightness[index]) for c in (116, 170, 191))
            draw.polygon([tuple(p) for p in projected[index]], fill=color)
        draw.text((24 + panel * 700, 60), f"{label}: {len(mesh):,} triangles", fill="#20252b", font=font)
    draw.line((700, 100, 700, 720), fill="#bec6ca")
    image.save(destination)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("results", type=Path)
    args = parser.parse_args()
    output = args.results.parent
    with args.results.open(encoding="utf-8-sig", newline="") as handle:
        rows = list(csv.DictReader(handle))
    cache = {}
    checks = []
    images = []
    for row in rows:
        source_path, result_path = Path(row["Source"]), Path(row["Output"])
        key = row["SourceHash"]
        if key not in cache:
            cache[key] = load(source_path)
        source, before = cache[key]
        candidate, after = load(result_path)
        assert before["hash"] == key
        assert after["triangles"] == int(row["FinalTriangles"])
        assert after["bytes"] == int(row["FinalBytes"])
        changed = after["hash"] != before["hash"]
        entry = {"source": str(source_path), "ratio": float(row["Ratio"]), "changed": changed}
        if changed:
            assert row["Status"] == "reduced"
            assert after["degenerate"] == 0 and after["attributes"] == 0
            original_topology, final_topology = topology(source), topology(candidate)
            assert original_topology["components"] == final_topology["components"]
            assert original_topology["boundary_components"] == final_topology["boundary_components"]
            assert final_topology["nonmanifold_edges"] == 0
            assert final_topology["duplicate_faces"] == 0
            tolerance = np.linalg.norm(np.subtract(before["max"], before["min"])) * 0.005
            bounds_error = max(np.max(np.abs(np.subtract(after[k], before[k]))) for k in ("min", "max"))
            assert bounds_error <= tolerance
            entry.update(before=original_topology, after=final_topology, bounds_error=float(bounds_error))
            if float(row["Ratio"]) == 1 and not source_path.name.startswith(("right_", "left_rear")):
                path = output / (source_path.stem + "-comparison.png")
                render(source, candidate, path, source_path.stem + " | maximum requested reduction")
                images.append(str(path))
        else:
            entry["byte_identical"] = True
        checks.append(entry)
    (output / "independent-checks.json").write_text(json.dumps({"checks": checks, "images": images}, indent=2), encoding="utf-8")
    print(f"Independent checks PASS: {len(checks)} cases; {len(images)} fixed-camera comparisons")


if __name__ == "__main__":
    main()
