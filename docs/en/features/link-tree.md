# Link Tree

The Link tree defines the robot's rigid bodies and their parent-child relationships. Errors here also affect Joints, inertia, and meshes later in the workflow.

## What can you do on this page?

- Create, rename, and delete Links.
- Drag Links to change parent-child relationships.
- Assign SolidWorks components to a Link.
- Copy and paste branches.
- Organize the hierarchy in an outline editor.
- Save the configuration and restore it when the assembly is reopened.

## Recommended order

1. Create one root Link.
2. Group components by real rigid body, not mechanically by part count.
3. Build the parent-child hierarchy from the base toward the end effectors.
4. Confirm that no component is assigned to more than one Link.
5. Move to the Joint page and configure each connection.

New Links can remain incomplete while you organize the tree; required Joint fields are still checked
before export. Failed or cancelled edits leave the previous tree intact.

Applying a multi-selection does not clear Joint names. Select a single node before editing its Link or Joint properties. After a Joint rename passes validation, existing Mimic references are updated; explicit user changes to references, multipliers, and offsets are retained.

Keep the generated end-of-line marker when renaming or moving an existing Link in the outline.
It preserves that Link's parameters and component bindings. Add a heading line to create a new Link.

## Common mistakes

- Splitting parts that move rigidly together into separate Links.
- Putting components on opposite sides of a Joint into the same Link.
- Looking only at display names without checking the actual assembly branch.
- Reusing an old configuration after changing the assembly without reviewing component assignments.

When finished, the tree should match the robot's real kinematic chain.
