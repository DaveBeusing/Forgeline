# Battlefield impression masters

Eight original editable Blender 5.2.2 scenes own the existing tire, tracked,
road-wear, oil, blast, shell-impact, scorch and concrete-crack families. Each
contains a separate high-resolution impression mesh with editable Albedo, Orm
and Coverage point attributes, plus the existing unit-square carrier. Relief is
cosmetic source geometry; only the flat carrier enters the compiled catalog.
No external imagery or third-party material is used.

The source grid has 18,721 vertices / 18,432 quads. CPU Cycles selected-to-active
bakes produce unlit physical color, coverage, tangent normals and packed ORM.
Color is encoded once to sRGB; coverage remains straight alpha. Normal/ORM are
linear. The engine tangent basis uses the actual original UV derivatives;
Blender bakes NEG_Y and the saved preview graph reverses that green convention
for its own normal node. Twenty-five geometry/normal probes per family verify
the exported basis. The carrier retains the original vertices, normals, UVs,
indices, bounds and materialless placement contract, with explicit tangents.

All scenes pack the same three canonical atlas PNGs and retain canonical-relative
paths. The per-family GLBs are geometry-only review exports; `decal_quad.glb` is
the sole adopted shared carrier. Stable material IDs and cell order are retained.

```powershell
& ./tools/blender/Invoke-Blender.ps1 -Action Script -Script tools/blender/decal_bakes.py -InputRoot assets/source -OutputRoot artifacts/decal-rebuild -TimeoutSeconds 2400
& ./tools/blender/Test-BlenderDecals.ps1 -OutputRoot artifacts/decal-native-review
```

The generator stages new scenes, exports and atlas maps under a fresh artifacts
directory. To work from an edited saved scene, pass `--reopen <master> --rebake`
through the launcher's typed `ScriptArguments`. Qualification reopens every
master twice, checks packed map bytes/paths/color spaces, compares all 24 actual
rebaked cells to the canonical atlas, compiles a source-less scoped catalog and
proves a real relief edit changes the normal bake while retaining the flat carrier.

See [qualification and integration limits](../../../../../docs/BattlefieldDecalBakes.md).
