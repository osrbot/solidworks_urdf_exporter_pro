#!/usr/bin/env python3
"""Create a disposable nested SolidWorks assembly for Live API tests."""

import argparse
import gc
import glob
from pathlib import Path
import shutil
import sys
import tempfile
import time

try:
    import pythoncom
    import win32com.client
    from win32com.client import VARIANT
except ImportError as exc:
    raise RuntimeError(
        "This script requires pywin32. Install it with "
        "'python -m pip install pywin32'."
    ) from exc


UNICODE_FRAME_NAME = "\u5750\u6807\u7cfb1"
UNICODE_AXIS_NAME = "\u53c2\u8003\u8f741"
ASSEMBLY_TEMPLATE_PREFERENCE = 25
ASSEMBLY_DOCUMENT_TYPE = 2
SILENT_OPEN_OPTION = 1
SILENT_SAVE_OPTION = 1


def _get_com_member(obj, name, *args):
    member = getattr(obj, name)
    if args:
        return member(*args)
    try:
        return member() if callable(member) else member
    except Exception as exc:
        message = str(exc)
        if "-2147352573" in message or "Member not found" in message:
            return member
        raise


def _byref_int():
    return VARIANT(pythoncom.VT_BYREF | pythoncom.VT_I4, 0)


def _empty_dispatch():
    return VARIANT(pythoncom.VT_DISPATCH, None)


def _com_value(obj, name):
    value = getattr(obj, name)
    return value() if callable(value) else value


def _solidworks_process_ids():
    wmi = win32com.client.GetObject("winmgmts:")
    processes = wmi.ExecQuery(
        "SELECT ProcessId FROM Win32_Process WHERE Name = 'SLDWORKS.exe'"
    )
    return {int(_com_value(process, "ProcessId")) for process in processes}


def _ensure_no_running_solidworks():
    process_ids = _solidworks_process_ids()
    if not process_ids:
        return
    raise RuntimeError(
        "Close the running SolidWorks instance before creating the disposable "
        f"Live API fixture. Detected process IDs: {sorted(process_ids)}."
    )


def _wait_for_owned_process_ids(previous_process_ids, timeout_seconds=20.0):
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        process_ids = _solidworks_process_ids() - previous_process_ids
        if process_ids:
            return process_ids
        time.sleep(0.25)
    return set()


def _terminate_solidworks_process(process_id):
    wmi = win32com.client.GetObject("winmgmts:")
    matches = list(
        wmi.ExecQuery(
            "SELECT * FROM Win32_Process WHERE ProcessId = " + str(process_id)
        )
    )
    if not matches:
        return
    result = _com_value(matches[0], "Terminate")
    if result not in (None, 0):
        raise RuntimeError(
            f"Terminating disposable SolidWorks process {process_id} "
            f"failed with {result}."
        )


def _iter_top_level_features(model):
    feature = _get_com_member(model, "FirstFeature")
    while feature:
        yield feature
        feature = _get_com_member(feature, "GetNextFeature")


def _features_named(model, name):
    return [
        feature
        for feature in _iter_top_level_features(model)
        if _get_com_member(feature, "Name") == name
    ]


def _rename_feature(model, old_name, new_name):
    matches = _features_named(model, old_name)
    if len(matches) != 1:
        raise RuntimeError(
            f"Expected one feature named {old_name!r}, found {len(matches)}."
        )
    matches[0].Name = new_name
    if _get_com_member(matches[0], "Name") != new_name:
        raise RuntimeError(
            f"SolidWorks refused to rename {old_name!r} to {new_name!r}."
        )


def _assert_feature_exists(model, name):
    matches = _features_named(model, name)
    if len(matches) != 1:
        raise RuntimeError(
            f"Expected persisted feature {name!r}, found {len(matches)}."
        )


def _open_assembly(sw, file_path):
    errors = _byref_int()
    warnings = _byref_int()
    model = sw.OpenDoc6(
        str(file_path),
        ASSEMBLY_DOCUMENT_TYPE,
        SILENT_OPEN_OPTION,
        "",
        errors,
        warnings,
    )
    if model is None or errors.value != 0:
        raise RuntimeError(
            f"Opening {file_path} failed with errors={errors.value}, "
            f"warnings={warnings.value}."
        )
    return model


def _save_document(model, file_path=None):
    errors = _byref_int()
    warnings = _byref_int()
    if file_path is None:
        destination = Path(_get_com_member(model, "GetPathName")).resolve()
        success = model.Save3(SILENT_SAVE_OPTION, errors, warnings)
    else:
        destination = Path(file_path).resolve()
        destination.parent.mkdir(parents=True, exist_ok=True)
        success = model.Extension.SaveAs(
            str(destination),
            0,
            SILENT_SAVE_OPTION,
            _empty_dispatch(),
            errors,
            warnings,
        )
    if not success or errors.value != 0 or not destination.is_file():
        raise RuntimeError(
            f"Saving {destination} failed with success={bool(success)}, "
            f"errors={errors.value}, warnings={warnings.value}."
        )


def _template_candidates(sw, explicit_template=None):
    if explicit_template:
        yield Path(explicit_template).expanduser().resolve()

    configured = sw.GetUserPreferenceStringValue(
        ASSEMBLY_TEMPLATE_PREFERENCE
    )
    for configured_path in str(configured or "").split(";"):
        configured_path = configured_path.strip().strip('"')
        if not configured_path:
            continue
        candidate = Path(configured_path).expanduser()
        if candidate.is_file():
            yield candidate.resolve()
        elif candidate.is_dir():
            yield from sorted(candidate.glob("*.asmdot"))

    patterns = (
        r"C:\ProgramData\SolidWorks\SOLIDWORKS *\templates\*.asmdot",
        r"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\lang"
        r"\chinese-simplified\*.asmdot",
        r"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\lang"
        r"\english\*.asmdot",
    )
    for pattern in patterns:
        for candidate in sorted(glob.glob(pattern)):
            yield Path(candidate).resolve()


def _find_assembly_template(sw, explicit_template=None):
    seen = set()
    for candidate in _template_candidates(sw, explicit_template):
        key = str(candidate).casefold()
        if key in seen:
            continue
        seen.add(key)
        if candidate.is_file() and candidate.suffix.casefold() == ".asmdot":
            return candidate
    if explicit_template:
        raise FileNotFoundError(
            "The assembly template does not exist or is not an .asmdot "
            f"file: {explicit_template}"
        )
    raise FileNotFoundError(
        "No SolidWorks assembly template was found. Pass "
        "--assembly-template explicitly."
    )


def _new_assembly(sw, assembly_template):
    model = sw.NewDocument(str(assembly_template), 0, 0, 0)
    if model is not None:
        return model
    for _ in range(40):
        model = _get_com_member(sw, "ActiveDoc")
        if model is not None:
            return model
        time.sleep(0.25)
    raise RuntimeError("SolidWorks did not return a new assembly document.")


def _activate_document(sw, model):
    title = _get_com_member(model, "GetTitle")
    errors = _byref_int()
    try:
        return sw.ActivateDoc3(title, False, 0, errors) is not None
    except Exception:
        try:
            sw.ActivateDoc2(title, False, errors)
            return True
        except Exception:
            return False


def _try_add_component(assembly, component_path, x, y, z):
    errors = []
    add_component5 = getattr(assembly, "AddComponent5", None)
    if add_component5 is not None:
        try:
            component = add_component5(
                str(component_path),
                0,
                "",
                False,
                "",
                float(x),
                float(y),
                float(z),
            )
            if component is not None:
                return component, errors
            errors.append("AddComponent5 returned None")
        except Exception as exc:
            errors.append(f"AddComponent5: {exc}")

    try:
        component = assembly.AddComponent4(
            str(component_path),
            "",
            float(x),
            float(y),
            float(z),
        )
        if component is not None:
            return component, errors
        errors.append("AddComponent4 returned None")
    except Exception as exc:
        errors.append(f"AddComponent4: {exc}")
    return None, errors


def _same_path(left, right):
    return str(Path(left).resolve()).casefold() == str(
        Path(right).resolve()
    ).casefold()


def _validate_component(component, expected_path):
    actual_path = _get_com_member(component, "GetPathName")
    if not actual_path or not _same_path(actual_path, expected_path):
        raise RuntimeError(
            "SolidWorks inserted an unexpected component. "
            f"Expected {expected_path}, got {actual_path!r}."
        )


def _add_component(sw, assembly, component_path, x, y, z):
    component, errors = _try_add_component(
        assembly, component_path, x, y, z
    )
    if component is None:
        opened = _open_assembly(sw, component_path)
        if not _activate_document(sw, assembly):
            raise RuntimeError(
                "SolidWorks could not reactivate the wrapper assembly after "
                f"opening {component_path}."
            )
        component, retry_errors = _try_add_component(
            assembly, component_path, x, y, z
        )
        errors.extend(retry_errors)
        opened = None
    if component is None:
        raise RuntimeError(
            f"Adding component {component_path} failed: "
            f"{'; '.join(errors)}"
        )
    _validate_component(component, component_path)
    return component


def _close_document(sw, model):
    if model is None:
        return
    title = _get_com_member(model, "GetTitle")
    sw.CloseDoc(title)


def _close_all_documents(sw):
    try:
        sw.CloseAllDocuments(True)
    except Exception:
        pass
    for _ in range(128):
        active = _get_com_member(sw, "ActiveDoc")
        if active is None:
            return
        _close_document(sw, active)
    raise RuntimeError(
        "The disposable fixture left too many SolidWorks documents open."
    )


def _shutdown_owned_processes(sw, owned_process_ids):
    cleanup_error = None
    if sw is not None:
        try:
            _close_all_documents(sw)
        except Exception as exc:
            cleanup_error = exc
        try:
            sw.ExitApp()
        except Exception:
            pass

    sw = None
    gc.collect()
    deadline = time.monotonic() + 8.0
    while time.monotonic() < deadline:
        if not (_solidworks_process_ids() & owned_process_ids):
            return cleanup_error
        gc.collect()
        time.sleep(0.25)

    try:
        for process_id in _solidworks_process_ids() & owned_process_ids:
            _terminate_solidworks_process(process_id)
    except Exception as exc:
        cleanup_error = cleanup_error or exc

    deadline = time.monotonic() + 15.0
    while time.monotonic() < deadline:
        if not (_solidworks_process_ids() & owned_process_ids):
            return cleanup_error
        time.sleep(0.25)
    return cleanup_error or RuntimeError(
        "The disposable SolidWorks fixture process did not terminate cleanly."
    )


def _copy_source_tree(source_assembly, output_root):
    leaf_path = output_root / "unicode_leaf.SLDASM"
    shutil.copy2(source_assembly, leaf_path)
    for dependency in source_assembly.parent.iterdir():
        if (
            dependency.is_file()
            and dependency.suffix.casefold() in {".sldprt", ".sldasm"}
            and dependency.resolve() != source_assembly
        ):
            target = output_root / dependency.name
            if target.resolve() != leaf_path.resolve():
                shutil.copy2(dependency, target)
    return leaf_path


def create_fixture(
    source_assembly,
    output_directory=None,
    depth=5,
    visible=False,
    assembly_template=None,
):
    source_assembly = Path(source_assembly).resolve()
    if not source_assembly.is_file():
        raise FileNotFoundError(source_assembly)
    if source_assembly.suffix.casefold() != ".sldasm":
        raise ValueError(
            "source_assembly must be a SolidWorks .SLDASM file."
        )
    if depth < 5:
        raise ValueError(
            "The deep-reference fixture must contain at least five wrapper "
            "levels."
        )

    owns_output_root = output_directory is None
    output_root = (
        Path(output_directory).resolve()
        if output_directory
        else Path(tempfile.mkdtemp(prefix="sw2urdf-deep-reference-"))
    )
    output_root.mkdir(parents=True, exist_ok=True)
    leaf_path = _copy_source_tree(source_assembly, output_root)

    pythoncom.CoInitialize()
    sw = None
    owned_process_ids = set()
    completed = False
    leaf = None
    assembly = None
    component = None
    duplicate = None
    result_path = None
    try:
        _ensure_no_running_solidworks()
        previous_process_ids = _solidworks_process_ids()
        sw = win32com.client.DispatchEx("SldWorks.Application")
        sw.Visible = bool(visible)
        owned_process_ids = _wait_for_owned_process_ids(
            previous_process_ids
        )
        if len(owned_process_ids) != 1:
            raise RuntimeError(
                "Expected one disposable SolidWorks process, found "
                f"{sorted(owned_process_ids)}."
            )
        resolved_template = _find_assembly_template(
            sw, assembly_template
        )

        leaf = _open_assembly(sw, leaf_path)
        _rename_feature(leaf, "Origin_global", UNICODE_FRAME_NAME)
        _rename_feature(leaf, "Axis_prox_joint", UNICODE_AXIS_NAME)
        leaf.ForceRebuild3(False)
        _save_document(leaf)
        _close_document(sw, leaf)
        leaf = None

        leaf = _open_assembly(sw, leaf_path)
        _assert_feature_exists(leaf, UNICODE_FRAME_NAME)
        _assert_feature_exists(leaf, UNICODE_AXIS_NAME)
        _close_document(sw, leaf)
        leaf = None

        child_path = leaf_path
        for level in range(1, depth + 1):
            assembly = _new_assembly(sw, resolved_template)
            component = _add_component(
                sw,
                assembly,
                child_path,
                x=0.017 * level,
                y=-0.011 * level,
                z=0.013 * level,
            )
            if level == depth:
                duplicate = _add_component(
                    sw,
                    assembly,
                    leaf_path,
                    x=0.31,
                    y=0.07,
                    z=-0.09,
                )

            wrapper_path = output_root / f"level_{level}.SLDASM"
            assembly.ForceRebuild3(False)
            _save_document(assembly, wrapper_path)
            _close_document(sw, assembly)
            assembly = None
            component = None
            duplicate = None
            child_path = wrapper_path

        result_path = child_path
        completed = True
    finally:
        operation_failed = sys.exc_info()[0] is not None
        leaf = None
        assembly = None
        component = None
        duplicate = None
        gc.collect()
        cleanup_error = None
        try:
            cleanup_error = _shutdown_owned_processes(
                sw, owned_process_ids
            )
        finally:
            sw = None
            gc.collect()
            pythoncom.CoUninitialize()
        if owns_output_root and not completed:
            shutil.rmtree(output_root, ignore_errors=True)
        if cleanup_error is not None and not operation_failed:
            raise RuntimeError(
                "The disposable SolidWorks fixture could not close cleanly."
            ) from cleanup_error

    print(str(result_path))
    return result_path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source_assembly")
    parser.add_argument("--output-directory")
    parser.add_argument("--depth", type=int, default=5)
    parser.add_argument("--visible", action="store_true")
    parser.add_argument("--assembly-template")
    args = parser.parse_args()
    create_fixture(
        args.source_assembly,
        output_directory=args.output_directory,
        depth=args.depth,
        visible=args.visible,
        assembly_template=args.assembly_template,
    )


if __name__ == "__main__":
    main()
