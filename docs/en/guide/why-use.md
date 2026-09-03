# Why Use SW2URDF

## What problem does it solve?

When a robot has already been designed in SolidWorks and the next step requires URDF, USD, or MJCF, manually rebuilding the Link hierarchy, coordinate frames, mesh paths, mass, and inertia is slow and error-prone. SW2URDF brings this work into a three-step wizard and performs basic checks before delivering the files.

## What does it do for you?

- Reads components, coordinate systems, reference axes, and mass properties from the assembly.
- Builds Links and Joints from your configuration.
- Generates visual meshes and collision geometry separately.
- Exports ROS 1, ROS 2, OpenUSD, or MuJoCo files together with validation reports.
- Validates new output before replacing existing files, preserving the previous result if export fails.

## What must you still decide?

- Which components belong to the same rigid body.
- The actual Joint type, direction of motion, and position limits.
- The force, torque, and speed allowed by each actuator.
- Which contact details must remain in the collision geometry.
- Controller, PID, friction, contact, and task parameters.

The plugin reduces repetitive data entry and format conversion, but it cannot infer control or safety parameters from CAD geometry alone.

## Who is it for?

- Developers maintaining ROS robot description packages from SolidWorks.
- Developers moving CAD robots into Isaac Sim or other USD tools.
- Developers preparing robot models for further control and task setup in MuJoCo.
- Robotics teams checking mass, inertia, collision geometry, and coordinate directions.
