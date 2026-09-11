# Original Arm v1.0 configuration

`original-arm-v1.0.xml` is the unmodified `data` string read through SolidWorks
from the `URDF Export Configuration` attribute (`exporterVersion = 1.0`) in
`examples/ORIGINAL_3_DOF_ARM/Arm.SLDASM` on 2026-09-11, while investigating SW2-2.
The assembly was already open in SolidWorks 2023. No CAD files were saved to
extract the fixture.

The fixture is stored as UTF-8 text, but intentionally retains the original
`encoding="utf-16"` declaration. Load it with a text reader, just as the COM
API returns decoded text. Re-encoding that string into UTF-8 bytes before XML
parsing reproduces the original `XML document (0, 0)` error.

It contains four links, seven named geometry references (including component
instance references), and the original component persistent IDs. The regression
test checks migration and v2 round-trip preservation of hierarchy, joint names,
joint types, and every component ID. The companion native probe verifies actual
reference resolution and save/close/reopen on a disposable assembly copy.
