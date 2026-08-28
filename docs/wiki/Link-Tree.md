# Link Tree

**English** | [简体中文](Link-Tree-zh-CN)

## Data Ownership

The Link Tree separates three responsibilities:

- Topology: Link hierarchy and stable node identity;
- URDF configuration: reusable Link, Joint, Inertial, Visual, and Collision settings;
- CAD binding: SolidWorks objects and persistent-reference PIDs.

The canvas edits a working copy. `Cancel` discards structural changes; `Apply` commits atomically
only after complete validation. UI projections are not independent sources of truth. Persistence and
URDF export project from the committed session.

## Assembly Configuration

The formal configuration is stored in the assembly feature `URDF Export Configuration (v1.5)`.
Readable older formats load through compatibility code and upgrade only after a later formal save
succeeds. The exporter does not create or upgrade this feature while an unfinished PropertyManager
session is open.

On reopen, components reconnect through saved PIDs. Delete, replace, or Save As operations can
invalidate a PID. Rebind it manually; matching display names do not prove CAD identity.

## Canvas Operations

- add, rename, and drag to change parent;
- automatic layout and box selection;
- copy, paste, and delete complete branches;
- `Ctrl+C`, `Ctrl+V`, and `Delete` use the same branch semantics;
- overlapping parent/child selections collapse into a non-duplicated branch set.

Paste preserves topology and reusable URDF settings but deliberately clears CAD component bindings.
Assigning one SolidWorks body to two Links produces an invalid model, so pasted Links remain
incomplete until new components are bound.

Joint rename migrates Mimic references. Deleting a Joint still referenced by Mimic is rejected.
Reparenting marks Joint kinematics and limits from the old relationship for recomputation.

## Outline Editing

Use one Link per line; Markdown heading depth defines hierarchy:

```text
# base_link
## camera_link
## left_steering_hinge_link
### left_front_wheel_link
## right_steering_hinge_link
### right_front_wheel_link
```

Both `#base_link` and `# base_link` are accepted. The last valid document remains active when input
contains:

- an invalid ROS name;
- a duplicate Link;
- multiple roots;
- a skipped heading level;
- a structure that would leave a dangling Mimic reference.

New Links default to a `fixed` Joint. `camera_link` maps to `camera_joint`; names without `_link`
receive `_joint`. Existing nodes retain stable identity, settings, and CAD binding when an
unambiguous match or same-position rename exists.

## Automatic Link Colors

`Auto Links` applies one deterministic whole-tree color scheme to the current UI hierarchy:

- depth moves from cool to warm hues;
- corresponding names receive the same color after removing side tokens such as
  `left/right/lhs/rhs/port/starboard`;
- generated URDF material ID and RGBA persist through the normal configuration path;
- a user can still override an individual Link afterward.

Automatic coloring changes only Visual material ID/RGBA. It does not modify topology, CAD binding,
Collision, or Inertial.

## Recovery Drafts

When a window closes before formal save, the exporter can checkpoint the session under:

```text
%LOCALAPPDATA%\OSRBot\SW2URDF\export-drafts
```

Drafts are isolated by the saved assembly's full path and include Link/Joint settings, ROS package
name, and last output directory. A successful formal save or export removes the draft. Unsaved
assemblies have no stable path and cannot use this recovery mechanism.
