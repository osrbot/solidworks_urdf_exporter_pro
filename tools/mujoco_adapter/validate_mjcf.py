#!/usr/bin/env python3
"""Development-only MuJoCo Python compatibility check for OSURDF MJCF assets."""

from __future__ import annotations

import argparse
import importlib.metadata
import json
from pathlib import Path
from typing import Any, Callable, Iterable


class OfficialRuntimeUnavailable(RuntimeError):
    """Raised when the official MuJoCo Python package is not installed."""


def _official_loader(path: Path) -> dict[str, Any]:
    try:
        import mujoco  # type: ignore[import-not-found]
    except ModuleNotFoundError as exc:
        raise OfficialRuntimeUnavailable(
            "The official 'mujoco' Python package is not installed in this verifier environment."
        ) from exc

    model = mujoco.MjModel.from_xml_path(str(path))
    return {
        "muJoCoVersion": importlib.metadata.version("mujoco"),
        "bodies": int(model.nbody),
        "joints": int(model.njnt),
        "geoms": int(model.ngeom),
    }


def validate_models(
    model_paths: Iterable[Path],
    loader: Callable[[Path], dict[str, Any]] = _official_loader,
) -> dict[str, Any]:
    paths = [path.resolve() for path in model_paths]
    if not paths:
        raise ValueError("At least one MJCF model path is required.")

    results: list[dict[str, Any]] = []
    versions: set[str] = set()
    overall_status = "passed"
    for path in paths:
        item: dict[str, Any] = {"model": path.name}
        if not path.is_file():
            item.update(status="failed", message=f"Model does not exist: {path}")
            overall_status = "failed"
            results.append(item)
            continue
        try:
            details = loader(path)
            version = details.get("muJoCoVersion")
            if isinstance(version, str) and version:
                versions.add(version)
            item.update(status="passed", details=details)
        except OfficialRuntimeUnavailable as exc:
            item.update(status="unavailable", message=str(exc))
            if overall_status != "failed":
                overall_status = "unavailable"
        except Exception as exc:  # MuJoCo surfaces compiler diagnostics through several exception types.
            item.update(status="failed", message=f"{type(exc).__name__}: {exc}")
            overall_status = "failed"
        results.append(item)

    return {
        "schemaVersion": 1,
        "format": "osurdf-official-mujoco-validation",
        "status": overall_status,
        "validator": "official-mujoco-python",
        "muJoCoVersion": next(iter(versions)) if len(versions) == 1 else None,
        "models": results,
    }


def _write_report(path: Path, report: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(report, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Development compatibility check using the official MuJoCo Python runtime."
    )
    parser.add_argument("--model", type=Path, action="append", required=True)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args(argv)

    report = validate_models(args.model)
    if args.report is not None:
        _write_report(args.report.resolve(), report)
    print(json.dumps(report, indent=2, sort_keys=True))
    if report["status"] == "passed":
        return 0
    if report["status"] == "unavailable":
        return 2
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
