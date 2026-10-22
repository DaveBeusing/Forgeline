# Raised Directorate Conveyor V1 (design sources)

This folder is an **editable preproduction kit contract**, not a playable
conveyor feature and not a compiled asset family.

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

## Next asset-production gate

Author corresponding Blender 5.2.2 source masters or glTF 2.0 GLBs for each
`slug`, including both curve hands and LOD0/1/2, the small state variants and
separate support/collision footprints. After visual review, create **real**
`*.asset.json` declarations with the existing compiler, qualifying sockets,
materials, boundaries, normal winding and content hashes. Do not rename this
preproduction kit JSON to `*.asset.json` and do not add an empty runtime asset
reference: the game currently has no conveyor simulation component.
