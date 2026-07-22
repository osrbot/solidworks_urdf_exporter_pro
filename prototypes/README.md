# Retired prototypes

The standalone Link tree canvas prototype was retired after its behavior was
integrated into the exporter. The maintained implementation is now under:

- `SW2URDF/UI/LinkTreeCanvas`
- `SW2URDF/URDFExport/LinkTreeSession.cs`
- `SW2URDF/URDFExport/LinkTreeStores.cs`

Do not fork topology or copy/paste behavior back into a separate prototype.
Keep CAD integration behind `ILinkTreeCanvasHost`.
