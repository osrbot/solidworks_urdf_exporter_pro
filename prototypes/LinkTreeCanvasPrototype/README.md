# Link Tree Canvas Prototype

Standalone WPF prototype for validating the SW2URDF Link-tree canvas interaction.
It targets .NET Framework 4.5.2 and does not reference or modify the exporter project.

## Run

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe LinkTreeCanvasPrototype.csproj /t:Build /p:Configuration=Debug
.\bin\Debug\LinkTreeCanvasPrototype.exe
```

## Implemented prototype interactions

- Free node positioning with live directional connectors.
- Mouse-wheel zoom and right-button canvas panning.
- Add child Link from the toolbar, inspector, or node `+` button.
- Automatic fixed Joint creation for each new child.
- Drop a node onto another node to change its parent.
- Cycle prevention and immutable root-parent rules.
- Direct Link/Joint name and Joint type editing.
- Stable branch colors, automatic tree layout, subtree deletion, and sample reset.
- Apply through the `ILinkTreeCanvasHost` boundary without CAD dependencies.

## Integration boundary

The production exporter should provide an adapter implementing `ILinkTreeCanvasHost`:

- `LoadTree()` maps the current exporter `Link/LinkNode` tree into a `LinkTreeDocument`.
- `ApplyTree()` validates and maps the edited document back into the exporter model.
- `ValidateLinkName()` applies production ROS and project-specific naming rules.

The prototype intentionally does not include inertia, mesh, CSV, or SolidWorks COM logic.
