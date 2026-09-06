# Link Tree

[简体中文](Link-Tree-zh-CN) | **English**

The Link tree defines the robot's rigid bodies and their parent-child hierarchy. A mistake here also
affects Joint, inertia, and mesh output.

## Available actions

- Add, rename, and remove Links.
- Drag Links to change the hierarchy.
- Assign SolidWorks components to a Link.
- Copy, paste, or delete a branch.
- Edit the hierarchy in outline form.
- Save the configuration and continue after reopening the assembly.

## Recommended order

1. Create one root Link.
2. Group components by rigid body, not simply by part count.
3. Build the hierarchy from the base to the end effectors.
4. Confirm that a component is not assigned to multiple Links.
5. Configure the connecting Joint for every non-root Link.

## Deep components and duplicate names

The maintained version resolves components, coordinate systems, and axes through their actual
assembly branch. Duplicate display names can be distinguished, but the user must still confirm the
selected branch. Recheck affected Links and Joints after changing the assembly hierarchy, replacing
components, or deleting reference geometry.

## Save and recovery

Applied configuration is saved with the assembly. Unsaved edits may be offered for recovery after an
unexpected exit. Review recovered content before saving, especially when the assembly structure has
changed.

## Common mistakes

Changing the direct child count affects only the next level. New Links may remain incomplete drafts;
set their Joint names and required references before export. Invalid edits leave the original tree
intact. Multi-selection does not write placeholder text into individual Joint names, and renaming a
Joint updates its existing Mimic references after validation.

- Splitting components that move as one rigid body into separate Links.
- Putting components from both sides of a Joint into one Link.
- Reusing old assignments after restructuring the assembly.
- Trusting a display name without checking the assembly branch.

The finished tree should match the robot's real kinematic chain.
