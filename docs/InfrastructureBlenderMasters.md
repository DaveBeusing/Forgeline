# Road, bridge and crossing Blender sources

The existing Directorate infrastructure geometry is retained in three editable,
packed-PBR Blender masters: `roads.blend`, `bridge.blend` and `transitions.blend`
under `assets/source/infrastructure/directorate/authoring/`. The three families
cover nine road meshes, three bridge states, and High/Reduced bridge aprons,
roadside drains and loading aprons. The existing shared collision proxy remains
unchanged. Eighteen canonical GLBs are bound through the original stable asset
definitions; every source vertex, normal, UV, primitive, material slot and index
is preserved. The original glTF recipes remain available as reconstruction seeds.

The source audit used master `a524b0a8164390abe1e9cb38170e690f46147574`,
version 0.1.436. No related open PR or live remote branch existed. Integrated
infrastructure predecessors include PRs #49, #111, #181 and #185. Existing
rendering optimization and qualification PRs #258, #260, #263–#268 are integrated,
but their full physical, lifecycle and human acceptance gates remain open.
Source preservation does not establish production performance acceptance.

Each state occupies its own named collection at the canonical origin. Show the
desired collection to edit it. Engine coordinates are +Y up/+Z forward, Blender
coordinates +Z up/-Y forward, with meter units. Runtime instance scales remain
separate from the normalized source meshes. Packed preview materials use the
existing Base Color, tangent Normal and ORM maps. No material, texture, atlas,
sampler, compiler or runtime resource budget is increased.

Recreate a master from reviewed canonical sources with:

```powershell
./tools/blender/Invoke-Blender.ps1 -Action Check
$fixture = (Resolve-Path tools/blender/fixtures/infrastructure.json).Path
./tools/blender/Invoke-Blender.ps1 -Action Script -Script tools/blender/infrastructure.py -InputRoot assets/source -OutputRoot artifacts/infrastructure-author/roads -ScriptArguments @('--action','Author','--family','roads','--fixtures',$fixture)
./tools/blender/Test-BlenderInfrastructure.ps1 -OutputRoot artifacts/infrastructure-validation
python -m unittest discover -s tools/blender/tests -p 'test_infrastructure_contract.py' -v
```

Use `bridge` or `transitions` for the other masters. Export with `--action Export`
and `--blend-file` pointing to the saved master. Supported vertex edits stay
inside the original bounds and a normalized 0.01 displacement; collision,
topology, normals, UVs, materials, pivots and transforms remain locked. Physical
displacement depends on the documented runtime instance scale. Authoritative
surface or source changes require explicit master regeneration. Checkout CRLF/LF
differences are normalized only for identity comparison, never art changes.

The native qualifier reopens each master twice, compares source and compiled
bytes, validates without sources, rejects invalid edits, and proves a saved
supported edit changes exactly its intended compiled mesh. The opt-in
`Infrastructure Blender masters` workflow requires a provisioned pinned Blender
Windows runner; portable tests do not substitute for this native execution.

The known bridge/path orientation difference documented in Modular Environment
Kit is the targeted visual integration gap. Its correction, road approaches,
native image comparisons, headless parity, final full suite and installed
package evidence are recorded with subsequent integration validation. No merge,
tag, release or golden approval is part of this work. Human art/live motion and
controlled performance acceptance remain separate.
