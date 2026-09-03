# Appearance

The Appearance page controls display colors only. It does not affect Collision or Inertial calculations.

![Link Appearance page](/screenshots/link-appearance.png)

## Fields

- **URDF material ID**: generated consistently from RGBA values so identical colors share a material. It normally does not need manual editing.
- **Red, Green, Blue, Alpha**: each value ranges from 0 to 1.
- **Choose Color**: sets RGBA values with a color picker.
- **Auto Color**: generates distinct colors based on Link depth.

## Recommendations

- To preserve design colors, first check the RGBA values read by the plugin.
- To distinguish a kinematic chain quickly, use Auto Color.
- Alpha 1 is opaque. Before lowering it, confirm that the target viewer supports transparent materials.
- Check colors again in the target viewer after export because lighting and material rendering differ between viewers.
