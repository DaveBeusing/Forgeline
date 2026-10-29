# Generated battlefield maps

The generated map path is explicitly selected. Authored Central Divide and Split Basin still use ScenarioRegistry's pinned registrations and BattlefieldMapArtifact format 4 with its unchanged 4 MiB limit. Generated packages are not registered by searching directories or reading save-supplied paths.

## Pipeline and supported recipes

GeneratedMapCompiler.Compile takes a GeneratedMapRecipe and the host's expected SHA-256 asset identity. Version 1 runs landforms, temperate biome sampling, qualified strategic placement, explicit site cover, complete geography/strategy validation, and streamed environment scatter in that order. Rejection produces a concrete qualification error; no replacement seed or authored map is substituted. Existing strategic search remains bounded to eight attempts. Cancellation is checked between stages/chunks and within placement/audit.

The zero-origin rectangle has 8..16 chunks per axis, 256 m chunks, 65 height samples per side and eight chunks per region. Landform parameters retain their existing height/gradient bounds. Terrain, climate, geography, rules, tactical, scatter and material profiles are application-owned versioned IDs; arbitrary profiles and executable rules are rejected. Version 1 uses terrain.landforms.v1, climate.temperate.v1, geography.strategic.two-player.v1, rules.directorate.strategic.v1, tactical.site-cover.v1, scatter.temperate.v1 and terrain.biome.v1. The material profile retains the existing eight-material/four-layer contract. Tactical site cover is explicit infantry protection (.75 multiplier) beside each site; plants and props never confer protection.

## Canonical package

| File | Contract and limit |
| --- | --- |
| map.flgmap.json | Format 1 canonical UTF-8 JSON, at most 64 KiB. Recipe, asset/content identities, gameplay/visual/biome/decoration hashes, scatter count, sidecar byte lengths and SHA-256 values. |
| geography.json | Canonical format-1 generated geography, at most 4 MiB. Metadata, starts, resources, sites, road topology, crossings, objectives, static navigation obstacles and tactical areas. No decoration WorldObjects or authored terrain recipe. |
| terrain.bin | FGT1 little-endian header/count, sorted Z/X chunk coordinates and row-major 65x65 IEEE single-precision heights. At most 4,328,456 bytes for 16x16 chunks. Shared boundary samples are retained exactly. |

Geography fields use the existing typed game data contracts; vectors include their public X/Y/Z fields. The generated geography is a separate format, not an authored format migration. Terrain and geography are integrity sidecars. The loader checks them but reconstructs runtime objects from compiled generators, never from untrusted sidecar arrays.

Gameplay identity binds the generator, full terrain recipe, tactical profile, content catalog, terrain/geography bytes and strategic fingerprint. Visual identity additionally binds gameplay, biome/decoration hashes, material/scatter profile parameters and asset content signature. Scatter or asset changes leave gameplay identity unchanged. Seed/terrain/geography changes alter it. The manifest SHA-256 binds the complete package, including visuals.

Decoration is regenerated one bounded chunk at a time with the strategic clearance mask. The package stores only a sorted aggregate of chunk hashes and the instance count, never a giant JSON object list. GenerateDecoration exposes the same per-chunk result to a future client caller. CreateTerrain and Definition return caller-owned containers; immutable heightfields and static biome queries can be shared.

Export uses create-only sidecars and writes the manifest last. Existing package files are never overwritten. Partial exports fail loading. Only fixed filenames are accepted; no manifest paths or types are interpreted. Inputs use opened-file length limits and exact reads; links/reparse points anywhere in the path are rejected. Header parsing rejects unknown members, unsupported versions, duplicate/noncanonical fields, conflicting assets and over-budget recipes before regeneration. A complete regeneration must reproduce the canonical header, which also detects sidecars whose attacker updated their hashes. SHA-256 establishes content identity, not publisher authentication: an optional trusted manifest hash pins a particular package.

## Compiler and headless invocation

Use a compiled runtime catalog's validated ContentSignature as the host asset identity. The compiler accepts --asset-root and validates its integrity metadata, or --asset-sha256 for a host-provided signature. Headless requires the expected signature explicitly; it never trusts the package's own asset field as the host expectation. Synthetic fixture signatures do not qualify production assets.

~~~powershell
$asset = '<64 lowercase hex characters from the host catalog ContentSignature>'
dotnet run --project tools/ForgeLine.MapCompiler -c Release -- --generate-map --seed 17 --width-chunks 12 --height-chunks 12 --output artifacts/maps/seed17 --asset-sha256 $asset --preview-output artifacts/maps/seed17/preview.svg --qualification-output artifacts/maps/seed17/qualification.json
dotnet run --project tools/ForgeLine.MapCompiler -c Release -- --load-generated-map artifacts/maps/seed17 --asset-sha256 $asset
dotnet run --project src/ForgeLine.Headless -c Release -- --generated-map artifacts/maps/seed17 --generated-asset-sha256 $asset --profile validation --ticks 20 --save-output artifacts/seed17.save.json --replay-output artifacts/seed17.replay.json
dotnet run --project src/ForgeLine.Headless -c Release -- --generated-map artifacts/maps/seed17 --generated-asset-sha256 $asset --load-input artifacts/seed17.save.json
dotnet run --project src/ForgeLine.Headless -c Release -- --generated-map artifacts/maps/seed17 --generated-asset-sha256 $asset --replay-input artifacts/seed17.replay.json
~~~

--recipe reads a bounded GeneratedMapRecipe JSON document and cannot be combined with scalar recipe overrides. Scalar overrides include seed, width/height, scatter-density, scatter-limit, scatter-profile and material-profile. Unknown, missing, conflicting and duplicate options fail. Diagnostic paths cannot overwrite package files or recipe input. Qualification contains the canonical manifest/hash, complete operational geography and strategic audit; the SVG preview is diagnostic, not graphical acceptance.

Headless supports --generated-manifest-sha256 to pin a package. The compiler equivalent is --expected-manifest-sha256 when loading. A simulation --seed remains separate from the map recipe's seed. Generated packages conflict with authored --scenario/--scenario-root and developer preset selection; the authored registry's allowlist remains unchanged.

## Match identity and migration

CreateComposition returns generated.skirmish.<gameplay SHA-256> with the normal Directorate starting-base and objective rules. ScenarioIdentity adds an optional Generated record containing generator version, map seed, gameplay/visual hashes, manifest hash and asset identity. Authored identities omit it, preserving their serialization. Existing envelope/payload/authoritative schemas are retained because authoritative state and command formats are unchanged.

Save and replay captures retain this identity automatically. Reconstruction requires an explicit host resolver supplying the verified package's composition and compares the complete identity before terrain/runtime allocation. Missing generated identity, unknown versions and any different package identity fail. Default ScenarioRegistry refuses generated keys; saves never cause paths to be opened or generators to be selected implicitly. A client may call GeneratedMapPackage.Load followed by CreateComposition and pass that resolver through existing persistence APIs. No seed-entry UI or renderer integration is added.

## Validation

build/Test-GeneratedMaps.ps1 checks independent canonical exports, qualification/preview, loader verification, a headless tick-20 match, save/replay recovery and controlled invalid-input/asset-conflict failures. GeneratedMapTests cover repeated hashes, visual-only changes, gameplay changes, tampering, missing/oversized inputs, hostile headers, caller ownership and exact persistence identity. CI retains generated-map diagnostics alongside existing gates; the normal match-terminal gate remains required.

The seed-17 12x12 fixture produced 79,564 instances, 142,276 geography bytes and 2,434,760 terrain bytes. Its headless strategic audit had zero errors and warnings. These measurements use a synthetic host asset signature and establish deterministic data behavior only. Physical GPU timing, production asset qualification and visual acceptance remain separate.
