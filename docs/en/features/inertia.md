# Inertia

The Inertia page shows the current Link's mass, center of mass, and inertia matrix in the selected coordinate system.

## What should you check?

- **Link coordinate system**: the frame in which inertia and center of mass are expressed.
- **Origin**: the center of mass position and orientation relative to the Link frame.
- **Mass**: measured in kg and must be greater than 0.
- **Inertia matrix**: measured in kg·m² and containing `ixx`, `iyy`, `izz`, `ixy`, `ixz`, and `iyz`.

Symmetric matrix terms are shown explicitly as `iyx = ixy`, `izx = ixz`, and `izy = iyz` to make clear that they are not separate inputs.

## Use measured mass

Set materials, density, or **Override Mass Properties** in SolidWorks first. The exporter reads
effective mass, COM, and inertia for the selected components. A subassembly with a whole-subassembly
override must belong to one Link; its aggregate override cannot be guessed for several Links.

You can also enter the weighed mass here. With **Calibrate inertia with measured mass** enabled,
the full matrix scales by `measured mass / source mass`. Changing 2 kg to 3 kg multiplies every
tensor entry by 1.5 while preserving COM, principal directions, and equivalent cuboid dimensions.

This assumes the source model's relative mass distribution is credible. Fix material distribution,
ballast, and component placement in SolidWorks when that assumption is wrong.

Explicitly entered tensors and SolidWorks inertia overrides are preserved, not automatically scaled.
**Restore SW values** rereads current properties and discards edits made in the exporter. Preview
and all export targets use the same final values. A measured mass differing from CAD is not itself
an error; non-positive mass and physically invalid inertia still block export.

Older configurations without source metadata keep their existing mass and tensor unchanged. Use
**Restore SW values**, then enter measured mass, to opt into calibration from SW mass distribution.

## Show the equivalent inertia body

Click **Show Equivalent Inertia Box** to inspect its position and orientation in SolidWorks. This preview helps reveal obvious coordinate or unit errors; it does not replace the original geometry.

## Common problems

- Mass is 0: check part materials, density, and excluded components.
- Center of mass is far outside the model: check the Link coordinate system and component transforms.
- Inertia validation fails: check units, coordinate directions, and whether mass properties came from the correct configuration.
- Preview fails while values look correct: inspect the error details first. Preview display and numeric export are separate validation paths.

Do not use Collision geometry as a substitute for mass properties. Collision geometry handles contact; inertia handles dynamics.
