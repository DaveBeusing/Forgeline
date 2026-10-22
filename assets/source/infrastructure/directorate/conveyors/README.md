# Raised Directorate Conveyor V1

The production model package is now in `authoring/`, `models/` and
`conveyor-production.v1.json`: fifteen editable Blender masters (including ground-mounted tier-1 supports), actual GLB
LODs/states, collision/selection meshes and existing-format rigid belt clips.
See [Models and animation handoff](../../../../../docs/ConveyorModelsAndAnimations.md)
for scale18, sockets, support constraints and manual review. No gameplay conveyor
or manual-test approval is implied. The original design baseline below remains
unchanged.

## Production material package

`materials/` contains the shared conveyor BaseColor, tangent Normal, ORM,
bounded emission and source-only RGB dirt/wear/heat mask, their runtime asset
definitions and deterministic recipe. All saved masters and render exports bind
`material.infrastructure.directorate.conveyor`. `conveyor-materials.v1.json`
records every module's valid state/material mapping and presentation precedence.
See [Material specification and manual acceptance](../../../../../docs/ConveyorMaterialsAndTextures.md).
No tests or gameplay/camera smokes were executed for this material delivery.

## Original design contract

`conveyor-kit.v1.json` is the **editable preproduction design contract**. It is
separate from the real model/asset definitions beside it and is not a playable
conveyor feature.

- `conveyor-kit.v1.json`: canonical engine-space dimensions, 12 module stable
  identities, typed input/output/closure ports, resource eligibility, shared
  Directorate materials and visual-state cues.
- [Dimensioned visual spec](../../../../../docs/ConveyorVisualLanguage.md):
  body proportions, silhouette, 5 m clearance, faction, state and LOD rules.
- [Greybox board](../../../../../docs/images/conveyor-greybox.svg):
  plan and elevation sketches for layout/design approval.
- [Existing logistics contract](../../../../../docs/BaseConveyorLogistics.md):
  conservation, throughput and regional Cargo Truck policy; visual assets
  do not implement those rules.
- [Existing port matrix](../../../../../docs/BaseConveyorPortMatrix.md):
  building interface eligibility; building socket positions remain to be
  authored and validated in their respective source assemblies.

## Portable authoring checks

From repository root (the PR CI automatically discovers the tests):

```sh
python tools/blender/conveyor_contract.py
python -m unittest discover -s tools/blender/tests -p 'test_conveyor_contract.py' -v
```

The checker uses only the Python standard library and validates that straight,
curve, ramp and junction sockets mate with aligned, opposite cardinal normals,
that resources remain fail-closed, and that the greybox board represents the
complete catalog. It does **not** open Blender or compile runtime assets.

## Remaining acceptance gate

Production masters, state/LOD exports, clips and material definitions are present.
Visual, vehicle/terrain clearance and runtime acceptance remain manual. Do not
rename the preproduction kit JSON to `*.asset.json`; the separate production
catalogs describe source assets without adding a conveyor simulation component.
