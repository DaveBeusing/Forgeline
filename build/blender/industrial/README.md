# Industrial Blender masters: isolated preservation checkpoint

Scope: Extractor, Smelter and Electronics Plant. The current production kits,
three LODs, eight construction/state meshes, sockets, collision references,
material definitions and texture inputs remain authoritative. This checkpoint
creates independently editable Blender masters from that existing geometry.
It does not claim new artwork, newly baked PBR maps or production integration.

The saved sources are `masters/extractor.blend`, `masters/smelter.blend` and
`masters/electronics_plant.blend`. Each contains its eleven existing family
meshes plus the shared collision reference. The 33 family GLBs are under
`exports/`. `SourceBindings.json` maps every export to its original definition,
ordered material/LOD/socket metadata, input signature and binary SHA-256.
These files are a staged snapshot; canonical discovery remains unchanged.

## Audit and promotion boundary

Base: remote `master` `d3acf9b0fb94da75ecfd26173cc387398f317096`, version
`0.1.426-dev`, verified through GitHub and a fresh fetch. The default and
integration branch is `master`. Only `master` and
`testing/render-performance-qualification` existed remotely at precheck.

PR #182 is merged and already supplies the industrial geometry and live
presentation bindings. Its discussion has no comments. PR #269 is merged and
adds the inventory and staged preservation contract. Reuse these sources;
absence of a bound saved master is the demonstrated source-maturity gap.

PR #267 is merged, but PR #268 is still open at head
`1a0117802db3079fec315426d1b5486d7a680c34`. Its physical qualification jobs
were skipped, its report retains open product acceptance gates, and it has
not been integrated. Current-master CI run `39506391125` was in progress at
precheck. Neither a merge nor an authoring pass proves rendering acceptance.

The current `docs/rendering/parallelization/ParallelRenderOwnership.json`
retains the `assets/source/` and compiler ownership hold. All new authoring
work stays in this family-scoped build directory and fresh `artifacts/`
outputs. No canonical source definition, kit, shared atlas, compiler, renderer,
generated runtime asset, gameplay definition or budget is changed.

Consult `docs/asset-readiness/Contract.md`, `docs/IndustrialBuildingProduction.md`,
`docs/BlenderAssetRoundTrip.md`, `docs/VehicleDocking.md`, `docs/Development.md`
and the current ownership manifest before promoting these sources.

## Reproduction

Use the repository-pinned .NET SDK and Blender 5.2.2 x64:

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
pwsh ./tools/blender/Invoke-Blender.ps1 -Action Check -OutputRoot artifacts/industrial-check
pwsh ./build/blender/industrial/Qualify-Masters.ps1 -OutputRoot artifacts/industrial-masters -TimeoutSeconds 480
```

Output must be fresh. Each family gets separate `first`, `second`, `master`,
`reopened`, `native-contracts` and compiled runtime directories. `families.json`
includes the existing primary, LOD and all eight state IDs explicitly; the
existing catalog computes shared material/texture/collision closure.
For a scoped retry, call the script from PowerShell with, for example,
`-Family @('smelter','electronics_plant')` and a fresh output root. Subset reports
leave the combined typed comparison pending. Collect the completed family
directories under one artifacts root and run the three typed comparisons with
`FORGELINE_INDUSTRIAL_BLENDER_ROOT` pointing to that root before claiming all
families qualified. Preserve failed roots and original launcher/compiler logs.
Instance scales follow the game footprints in engine X/Y/Z order:
Extractor 12/8/12, Smelter 18/10/16, Electronics Plant 18/10/18.
The 37-degree instance and translation exercise the existing socket transform
validator; they do not substitute for native four-rotation or traffic review.

The qualifier:

- Saves two independent masters and compares repeated GLB/source/runtime bytes.
- Organizes one collection per asset, initially showing only the completed LOD0,
  while retaining every state, collision reference, socket and reduced mesh.
- Reopens that organized master and checks the strict existing exporter.
- Saves a separate qualification-only vertex edit, reopens it, verifies its
  exact predicted position and exports it through the existing GLB writer.
- Executes the existing 19 negative scene cases and two-material/tangent positive.
- Compiles baseline, both independent exports, reopened output and the edited
  test output, then validates each catalog with an absent source root.
- Checks that the complete production source tree is unchanged.
- Reuses the typed runtime comparison for all three families: geometry, generated
  tangents, material/dependency identity, sockets, states, LODs and batching.

`master/authoring.blend` is the preserved master. `master/edit-test.blend`
demonstrates edit persistence only; it is not production art. Existing source
normals, UVs, ordered material slots and engine-to-Blender mapping are retained.
No arbitrary material graph or animation support is added. The stock exporter
still rejects topology or shape changes; actual artistic modifications require
the existing bounded production workflow and explicit source review.

The output `qualification.json`, native launcher reports, `master-report.json`,
compiler reports and source-less reports are the evidence. Failed/canceled
attempts remain under their original output paths. Missing evidence remains
pending; do not infer success from the existence of a `.blend` or `.glb`.

## Remaining production acceptance

Release the source ownership hold and integrate/accept the rendering milestone
before production promotion. Then review any demonstrated visual quality gaps,
bind reviewed masters/exports to canonical IDs, and qualify current-source full
CI, native close/normal/strategic views and moving LODs, all real states,
bright/dark/wet, 1080p/4K and quality profiles, adjacent live trucks and four
rotations, 1/100/1,000-instance paired GPU/resource baselines, deterministic
simulation and a reproducible installed source-less package. Human visual
approval remains separate. No merge, golden approval, tag or release is made
by this checkpoint.
