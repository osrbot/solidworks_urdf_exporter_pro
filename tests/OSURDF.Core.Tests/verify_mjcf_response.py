"""Isolated MuJoCo 3.12 DLL smoke test; no Python mujoco package or CAD required."""

import argparse
import ctypes as c
import json
from pathlib import Path
import tempfile
import xml.etree.ElementTree as ET


def verify(runtime: Path) -> list[dict]:
    lib = c.CDLL(str(runtime / "mujoco.dll"))

    def bind(name, result, *args):
        fn = getattr(lib, name)
        fn.restype, fn.argtypes = result, args
        return fn

    ptr, num = c.c_void_p, c.POINTER(c.c_double)
    version = bind("mj_versionString", c.c_char_p)().decode()
    if version != "3.12.0":
        raise RuntimeError(f"This probe pins the 3.12.0 state flags, found {version}")
    load = bind("mj_loadXML", ptr, c.c_char_p, ptr, c.c_char_p, c.c_int)
    make = bind("mj_makeData", ptr, ptr)
    reset = bind("mj_resetData", None, ptr, ptr)
    forward = bind("mj_forward", None, ptr, ptr)
    step = bind("mj_step", None, ptr, ptr)
    size = bind("mj_stateSize", c.c_int, ptr, c.c_uint)
    get = bind("mj_getState", None, ptr, ptr, num, c.c_uint)
    put = bind("mj_setState", None, ptr, ptr, num, c.c_uint)
    dump = bind("mj_printFormattedData", None, ptr, ptr, c.c_char_p, c.c_char_p)
    delete_data = bind("mj_deleteData", None, ptr)
    delete_model = bind("mj_deleteModel", None, ptr)
    # MuJoCo 3.12.0 mjtype.h: QPOS=1<<1, QVEL=1<<2, CTRL=1<<6.
    qpos, qvel, ctrl = 2, 4, 64
    results = []
    with tempfile.TemporaryDirectory(prefix="osurdf-response-") as directory:
        work = Path(directory)
        for mode in ("position", "velocity", "motor"):
            xml = ET.Element("mujoco")
            ET.SubElement(xml, "compiler", angle="radian", autolimits="false", inertiafromgeom="false")
            ET.SubElement(xml, "option", gravity="0 0 0", timestep="0.001")
            base = ET.SubElement(ET.SubElement(xml, "worldbody"), "body", name="base")
            ET.SubElement(base, "freejoint", name="root_freejoint")
            ET.SubElement(base, "inertial", pos="0 0 0", mass="5", diaginertia="1 1 1")
            arm = ET.SubElement(base, "body", name="arm")
            ET.SubElement(arm, "inertial", pos="0 0 0", mass="1", diaginertia="0.1 0.1 0.1")
            ET.SubElement(arm, "joint", name="joint", type="hinge", axis="0 0 1", limited="false")
            actuator = ET.SubElement(ET.SubElement(xml, "actuator"), mode,
                                     joint="joint", gear="1", forcelimited="true", forcerange="-4.25 4.25")
            if mode == "position":
                actuator.set("kp", "12.5")
            if mode != "motor":
                actuator.set("kv", "0.75")
            model_path = work / "model.xml"
            ET.ElementTree(xml).write(model_path, encoding="utf-8")
            error = c.create_string_buffer(2048)
            model = load(str(model_path).encode(), None, error, len(error))
            if not model:
                raise AssertionError(error.value.decode())
            data = make(model)
            try:
                assert size(model, qpos) == 8 and size(model, qvel) == 7 and size(model, ctrl) == 1
                samples = []
                for command in (0.2, -0.2, 100.0, -100.0):
                    reset(model, data)
                    put(model, data, (c.c_double * 1)(command), ctrl)
                    forward(model, data)
                    printed = work / "data.txt"
                    dump(model, data, str(printed).encode(), b"%.17g")
                    lines = printed.read_text().splitlines()
                    start = lines.index("QFRC_ACTUATOR") + 1
                    force = [float(line.strip()) for line in lines[start:start + 7]][-1]
                    gain = 12.5 if mode == "position" else 0.75 if mode == "velocity" else 1.0
                    expected = max(-4.25, min(4.25, gain * command))
                    assert abs(force - expected) < 1e-8, (mode, command, force, expected)
                    for _ in range(10):
                        step(model, data)
                    velocity = (c.c_double * 7)()
                    get(model, data, velocity, qvel)
                    assert velocity[-1] * command > 0, (mode, command, list(velocity))
                    samples.append({"ctrl": command, "qfrc_actuator": force, "joint_velocity_after_10_steps": velocity[-1]})
                results.append({"version": version, "mode": mode, "nq": 8, "nv": 7, "samples": samples})
            finally:
                delete_data(data)
                delete_model(model)
    return results


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime", required=True, type=Path)
    print(json.dumps(verify(parser.parse_args().runtime), indent=2))
