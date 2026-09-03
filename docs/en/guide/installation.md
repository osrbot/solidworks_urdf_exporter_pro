# Installation

## Install

1. Download the published x64 installer from [GitHub Releases](https://github.com/osrbot/solidworks_urdf_exporter_pro/releases).
2. Close every SolidWorks process.
3. Run the installer as administrator and choose English or Simplified Chinese.
4. Restart SolidWorks.
5. Open the plugin from `Tools > Export as URDF`.

You do not need Isaac Sim or MuJoCo installed on the Windows computer to enable OpenUSD or MuJoCo export.

## Upgrade

- Exit SolidWorks completely before upgrading.
- Test a new release with a copy of your assembly first.
- Very old community-edition configurations may not identify deeply nested components reliably. Recreate the configuration and review each item.
- After upgrading, check the Link tree, coordinate systems, Joints, inertia, and collision preview before using a production model.

## Supported environment

| Item | Details |
| --- | --- |
| Operating system | Windows x64 |
| Runtime | .NET Framework 4.8 |
| Main hardware-tested version | SolidWorks 2023 |
| Historical minimum for the community edition | SolidWorks 2018 SP5 |

The historical minimum does not mean that every SolidWorks release and Service Pack in between has been tested.

## The menu is missing after installation

1. Confirm that you installed the x64 version.
2. Exit SolidWorks completely and restart it.
3. Confirm that SW2URDF is enabled in the SolidWorks Add-Ins list.
4. Check `%USERPROFILE%\sw2urdf_logs\sw2urdf.log`.
5. If the plugin still does not load, follow [How to ask for help](/en/support/help-and-contribute) and include the log and version information.
