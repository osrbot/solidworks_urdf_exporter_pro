# Inertia

The Inertia page shows the current Link's mass, center of mass, and inertia matrix in the selected coordinate system.

## What should you check?

- **Link coordinate system**: the frame in which inertia and center of mass are expressed.
- **Origin**: the center of mass position and orientation relative to the Link frame.
- **Mass**: measured in kg and must be greater than 0.
- **Inertia matrix**: measured in kg·m² and containing `ixx`, `iyy`, `izz`, `ixy`, `ixz`, and `iyz`.

Symmetric matrix terms are shown explicitly as `iyx = ixy`, `izx = ixz`, and `izy = iyz` to make clear that they are not separate inputs.

## Show the equivalent inertia body

Click **Show Equivalent Inertia Box** to inspect its position and orientation in SolidWorks. This preview helps reveal obvious coordinate or unit errors; it does not replace the original geometry.

## Common problems

- Mass is 0: check part materials, density, and excluded components.
- Center of mass is far outside the model: check the Link coordinate system and component transforms.
- Inertia validation fails: check units, coordinate directions, and whether mass properties came from the correct configuration.
- Preview fails while values look correct: inspect the error details first. Preview display and numeric export are separate validation paths.

Do not use Collision geometry as a substitute for mass properties. Collision geometry handles contact; inertia handles dynamics.
