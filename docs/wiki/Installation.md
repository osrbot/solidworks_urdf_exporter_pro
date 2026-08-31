# Installation

**English** | [简体中文](Installation-zh-CN)

## User Installation

1. Download a published `sw2urdfSetup_YYYYMMDD_<commit>.exe` from the maintained fork's
   [GitHub Releases](https://github.com/osrbot/solidworks_urdf_exporter_pro/releases).
2. Verify the supplied SHA-256 sidecar when available.
3. **Close every SolidWorks process.** The installer does not terminate SolidWorks and cannot
   hot-reload a new DLL into a running process.
4. Run the x64 installer as administrator and select English or Simplified Chinese.
5. Restart SolidWorks and open `Tools > Export as URDF`.

The default target is `SolidWorks Corp\SolidWorks\URDFExporter` under 64-bit Program Files. The
installer registers the add-in with 64-bit .NET `RegAsm.exe /codebase`. The uninstaller removes COM
registration only when the current `CodeBase` still belongs to that installation directory, so an
older uninstaller cannot unregister a newer installation.

## Upgrade

- Close SolidWorks before upgrading.
- The installer remembers a previously selected install directory.
- Current configurations use `URDF Export Configuration (v2)` with component-instance and feature
  persistent IDs. Name-based v1.x configurations are not migrated: delete the legacy
  feature, recreate the configuration, and review every CAD binding.
- Validate Link Tree, frames, inertia, and collision previews on a non-production assembly before
  upgrading production workflows.

## Support Range

| Item | Current evidence |
| --- | --- |
| OS and architecture | Windows x64 |
| Target framework | .NET Framework 4.8 |
| Historical minimum SolidWorks | 2018 SP5 |
| Current Live API verification focus | SolidWorks 2023 |

This does not imply support for every SolidWorks release or service pack. The upstream statement
that SolidWorks 2017 or earlier may work is retained as historical information, not a maintained
compatibility promise.

## Add-in Menu Is Missing

1. Confirm that the installer is x64 and was run as administrator.
2. Exit and restart SolidWorks completely.
3. Check that SW2URDF appears and is enabled in SolidWorks Add-Ins.
4. Inspect `%USERPROFILE%\sw2urdf_logs\sw2urdf.log`.
5. For a source build, confirm that the build used API assemblies from the active SolidWorks install.

## Historical Upstream Builds

For upstream behavior or older installers, use the
[ros/solidworks_urdf_exporter Releases](https://github.com/ros/solidworks_urdf_exporter/releases).
Do not assume that maintained-fork documentation describes those older binaries.
