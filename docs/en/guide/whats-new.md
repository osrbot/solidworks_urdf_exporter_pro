# What Has Changed from the Community Edition?

The current version keeps the familiar SolidWorks export workflow while addressing problems with complex assemblies, physical validation, modern output formats, and general usability.

| Use case | Community edition | Current version |
| --- | --- | --- |
| Coordinate systems and axes in nested components | Results can depend on names, hierarchy, and the active configuration | Locates the actual component instance and supports deeply nested, duplicate-name, and Chinese-named reference geometry |
| STEP or fixed assemblies | May treat mate state as Joint meaning | Uses the user's explicit choice; mate detection provides suggestions that must be confirmed |
| Mass and inertia | Primarily reads and writes values | Adds checks for units, center of mass, tensors, and principal moments |
| Collision | Fewer choices and limited inspection before export | Provides mesh, primitive, per-component bounding box, convex hull, simplification, and preview options |
| Appearance | Mixed with mesh settings | Separate Appearance page with RGBA, color picker, and automatic coloring |
| Output formats | Mainly traditional URDF | Adds ROS 2, OpenUSD, and MuJoCo MJCF |
| Error handling | Common failures can be difficult to locate | Copyable error details, direct log access, and export reports |
| Language and layout | Primarily the older English interface | Simplified Chinese interface, readable long names, and smoother page switching |

## Direct improvements in the interface

- Joint basic information, reference geometry, origin, and axis are shown in separate sections, so long paths remain readable.
- The Limits page supplies the smallest valid default for missing required positive values. A valid user value always takes precedence.
- The Link page is divided into Inertia, Visual/Collision, and Appearance tabs.
- At the retested window size, the Joint Basic and Limits and Safety pages keep complete input borders without extra scrollbars. This does not verify every DPI or multi-monitor combination.
- OpenUSD settings open only when needed and do not occupy space on the main page.

## Editing Links

- Adding or removing Links, changing direct child counts, and dragging to a new parent are validated before being applied. Cancellation or validation failure does not leave partial tree changes.
- Use multi-selection for bulk operations; select one node before editing individual Link or Joint properties. Applying a multi-selection does not clear Joint names with placeholder text.
- After a Joint rename passes validation, existing Mimic references are updated. Explicit user changes to references, multipliers, and offsets are retained.
- After changing a Link frame, manual COM and inertia values are transformed into the new frame during preview or export preparation. Repeated saves do not repeat the transformation. If the previous frame cannot be resolved, the draft is retained and an error is shown; resolve the reference before continuing.
- Switching Links clears inertia and collision previews while retaining the entered mass. Initial preview preparation can still take time; see [Quick Start](/en/guide/getting-started).

## New export targets

- **ROS 2 package**: for current ROS 2 robot descriptions and later control configuration.
- **OpenUSD**: produces a portable robot asset for further use in Isaac Sim or other USD tools.
- **MuJoCo MJCF**: produces a loadable robot model to which users can add scenes, actuators, and controls.

![OpenUSD settings](/screenshots/openusd-settings.png)

<p class="caption">OpenUSD settings cover base behavior, self-collision, and Joint drive intent. No Isaac version is required.</p>
