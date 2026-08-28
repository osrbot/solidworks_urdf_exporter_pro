# Inertia

**English** | [简体中文](Inertia-zh-CN)

## URDF Quantities

Each Link `<inertial>` contains:

- `mass`: kilograms;
- `origin xyz`: COM position in the Link frame, meters;
- `origin rpy`: inertia-frame orientation relative to the Link;
- `ixx ixy ixz iyy iyz izz`: symmetric inertia tensor about COM, `kg*m^2`.

URDF stores the tensor about COM. A solver combines COM position, Link/Joint transforms, and the
parallel-axis theorem when another point is needed. Do not write a tensor about the Joint origin
directly into URDF.

## Link Frame

- The root Link uses `Origin_global` or another explicitly selected root frame.
- A non-root Link normally uses its child-Joint coordinate system as Link frame.
- Users must create engineering frames in SolidWorks; the exporter does not infer them from shape.
- The selector lists coordinate systems that actually exist in the active model.

Changing the Link frame recomputes and persists:

- COM coordinates in the new Link frame;
- the COM tensor rotated into the new frame orientation;
- adjacent Joint-origin relationships.

Mass and principal moments must remain invariant under a pure frame change.

## SolidWorks API Path

The maintained implementation enables system units and separates two reads:

1. one MassProperty object reads mass and COM;
2. an independent object reads the COM inertia tensor;
3. both results follow one document-to-Link-frame conversion route.

Separate objects work around a read-order failure observed during SolidWorks 2023 Live API testing,
where reading tensor and COM from one cached object can invalidate one result. This is an observed
engineering workaround, not a claim about every SolidWorks version's internals.

## Sign Convention

The SolidWorks Mass Properties dialog can display products of inertia in notation that suggests an
extra sign flip. The `GetMomentOfInertia` API used here already returns the physical symmetric tensor
in the requested frame. The exporter extracts `ixx ixy ixz iyy iyz izz` and preserves off-diagonal
signs. It then compares tensor eigenvalues with API principal moments; an accidental second sign
conversion fails this independent check.

Concept reference: Winter,
[掌握 URDF 中的惯性张量：从 SolidWorks 到强化学习机器人的关键一步](https://zhuanlan.zhihu.com/p/1887859297221845818).
The article explains COM-relative tensors, output frames, and notation traps. The actual API path,
implementation, and tests define this exporter's mapping.

## Frame Conversion

The COM tensor is rotated only:

```text
I_link = R * I_document * R^T
```

COM receives a rigid point transform. Because the tensor remains about COM, moving the Link-frame
origin alone does not apply a parallel-axis offset to `I_link`.

## Pre-export Validation

The exporter checks:

- finite values and positive mass;
- tensor symmetry;
- positive principal moments and rigid-body triangle inequalities;
- tensor eigenvalues matching API principal moments;
- mass and principal-moment invariance under frame conversion;
- COM inside the selected components' Link-local bounds.

The last check detects wrong component selection and frame transforms. A valid COM of concave or
hollow geometry can lie in an empty cavity; “inside bounds” does not mean “inside material.”

Any physical or numerical failure stops export before formal URDF/mesh output and identifies the
affected Link and failed check.

## Inertia Preview

The preview shows COM, an equivalent-inertia cuboid, and principal-axis directions. Temporary bodies
use SolidWorks Modeler/Display3 and require a valid visible top-level Part as display host.

A preview failure does not prove that mass properties are wrong; a visible preview also does not
prove that the tensor is physically valid. Use `inertial_validation.csv` and the final URDF.
