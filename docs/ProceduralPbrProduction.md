# Procedural atlas production

The CPU Cycles recipe producer extends the existing pinned Blender launcher. It
creates immutable source generations beneath `artifacts/`, with the existing
compiler as the sole source-to-runtime boundary. No game shader, material record,
mesh, socket, collision, simulation or installed-player dependency changes.

## Ownership and compatibility

`build/Author-ProductionSurfaces.py` owns the curated sixteen-cell atlas and seven
terrain families. Its existing 4096-square sources remain the production default.
`tools/blender/fixtures/pbr.json` owns eight bounded procedural alternatives: armor,
steel, rubber, canvas, glass, concrete, heat-treated steel and grime. The default
pilot replaces only cell 0 (painted armor) in an isolated copy of the tank dependency
closure. All other cells are copied at their exact pixel values. No atlas repacking
or automatic overwrite of `assets/source` is available. Promoting a generated set
requires reviewing its three source-image changes together through the ordinary PR.

The checked-in pilot below `materials/directorate/authoring/procedural` is an editable
candidate, outside asset discovery. It is not silently bound to all existing units.
The `.blend` holds procedural nodes and packed masters. Regeneration uses recipes;
manual graph edits are editable authoring experiments, not silently exported inputs.

The atlas remains 4x4; stable texture/material IDs, ordered material slots and
one-material sections survive unchanged at every LOD. Existing component islands
intentionally stack inside their assigned cell. Geometry is copied byte-for-byte,
validated with existing source/UV/normal/material/LOD checks and never exported by
this producer. This is preservation of the accepted overlap policy, not a proof
that arbitrary new overlapping UVs are safe. New layouts require separate approval.
One metre remains one engine unit; no transforms are applied to the pilot mesh.

## Commands

Run from repository root with the pinned Blender 5.2.2 build. The launcher probes
version, architecture and executable hash; it runs offline with factory defaults,
disabled autoexecution, a locked output root and a timeout. No network texture inputs.

```powershell
./tools/blender/Invoke-ProceduralPbr.ps1 -Check -OutputRoot artifacts/pbr-inspect
./tools/blender/Invoke-ProceduralPbr.ps1 -OutputRoot artifacts/pbr-pilot -RenderPreviews
./tools/blender/Invoke-ProceduralPbr.ps1 -Families glass,canvas -OutputRoot artifacts/pbr-selected
./tools/blender/Test-ProceduralPbr.ps1 -OutputRoot artifacts/pbr-clean -RenderPreviews
python -m unittest discover -s tools/blender/tests -p 'test_*contract.py' -v
```

`-Profile` selects an inspectable JSON recipe; unknown fields, changed cell identities,
nonfinite/out-of-range properties and runtime caps above the approved ceiling fail. Lower 1K/512 profiles preserve the 4K master and derive mip counts down to the same 128-pixel terminal level. `-Check` validates
inputs/dependencies and writes inspection only; it does not bake. Selective runs
always start from the authoritative atlas, never accumulate prior alternatives.
The key includes source closure hashes, recipes, selected families, tool build and
producer dependencies. Cached output is reused only after checking every file hash.
A damaged generation is retained and rejected; choose a fresh output root to recover.
Interrupted staging remains for diagnosis and is never accepted as a complete run.
Generation directories publish with an atomic rename. Launcher exit 23 denotes a
Python validation/bake failure; adjacent stdout/stderr logs and failure JSON identify
it. Nothing is promoted if a bake, image check or publication fails.

## Map semantics and bake policy

Each cell is natively baked at 1024 square and assembled into a true 4096-square atlas,
not enlarged from a runtime image. CPU Cycles uses one sample, one thread, fixed seed,
explicit selected metre swatch, UV0, tangent basis and deterministic factory state.
The two emission bakes contain only reflectance and packed material properties.
Roughness and metallic are explicit physical recipe inputs, independent of color.
Paint/rubber/canvas/glass/concrete/grime are dielectric; exposed steel is metallic.
Glass is opaque sensor glass under the current renderer, without a new transmission
or alpha convention. AO is neutral 1 on flat swatches: object cavities are not baked
into a shared repeating atlas. Normals come from the actual procedural bump shader,
with metre-scale height, tangent-space +X/+Y/+Z baking; no base-color grayscale normals.

Base Color is sRGB RGB with opaque alpha; Normal and ORM are linear. ORM is exactly
R=AO, G=roughness, B=metallic, checked against known recipe samples after the bake.
Flat glass is checked against (128,128,255), normals must be unit length within 3%
and face positive Z. There is no emissive output in this pilot. PNG CRC, dimensions,
channel count, decoded byte extent, alpha and normal orientation are checked. Pixel
roundtrips are lossless. Base Color has no lights, shadows or ambient occlusion.

Each cell extends its interior by 64 master pixels at its border. The established
0.018 full-atlas UV inset is 73.728 master pixels. Retained terminal mip is 128 square,
32 pixels/cell, inset 2.304 texels. This keeps neighboring cell data outside the
sampled islands through the existing mip chain; it does not promise arbitrary deep
mips or free camera-motion shimmer acceptance. Density is inherited per component,
not claimed uniformly across the tank. Importer-generated tangent frames and V/sign
conversion remain authoritative; the swatch is a +Y tangent-space authoring control.

## Runtime costs and acceptance

Existing definitions keep BC7 `balanced` (or existing supported `best`), semantic
mipmaps and 2048/1024/512 caps for Base/Normal/ORM. The existing 4K canonical masters
stay editable and never force 4K residency. Five/four/three retained mip levels cost
5,586,944 + 1,392,640 + 344,064 = 7,323,648 bytes. No format, section or instance layout
changes. BC7 file bytes can change; allocated mip payload cannot grow in this pilot.
The existing compiler hashes source/settings and validates cached containers; there
is no additional runtime texture container or PNG decoder. The source-only PNG
reader belongs exclusively to authoring validation.

The qualifier compares source images and copied definitions from two clean CPU
runs, verifies the cache, compiles the clone, records compile duration and verifies
runtime assets with an absent source root. `.blend` container bytes and render noise
are not byte-determinism claims; source PNG/definition bytes are. CPU tool-build
changes invalidate the generation; GPU baking is not offered as a deterministic
alternative. Ordinary CI checks recipes and source metadata without Blender. The
opt-in pinned Blender worker runs the complete bake/compile qualifier.

Blender preview light/camera and game PBR lighting differ; their screenshots are
inspection evidence, not exact shader parity. Native close/normal/strategic views
must retain recognition and silhouette. Human art approval, moving-camera seams and
shimmer and hardware-qualified frame-time acceptance remain separate gates. See
[qualification](ProceduralPbrQualification.md) for actual execution and limitations.

Native comparison uses the existing capture entrypoint with one committed binary
and independently sealed runtime roots. Predeclared plan: A/B/B/A, High 1920x1080,
8 warmups, 32 samples, two deterministic readbacks at each of the actual 120/420/1000m
bookmarks. Use fresh output roots for each letter. The unchanged 15% performance
comparison is diagnostic until the cohort and hardware state are qualified.

```powershell
./build/Invoke-RtsReferenceQualification.ps1 -Capture -ClientPath artifacts/pbr-build/bin/ForgeLine.Client/release_win-x64/ForgeLine.Client.exe -RuntimeAssetRoot assets/runtime -Scenes MainBattleTankProduction -Profiles High -Zooms CloseTactical,NormalGameplay,Strategic -Displays 1920x1080 -Warmups 8 -Samples 32 -RepeatCaptures 2 -OutputRoot artifacts/pbr-native-a
./build/Invoke-RtsReferenceQualification.ps1 -Capture -ClientPath artifacts/pbr-build/bin/ForgeLine.Client/release_win-x64/ForgeLine.Client.exe -RuntimeAssetRoot artifacts/pbr-full/runtime -Scenes MainBattleTankProduction -Profiles High -Zooms CloseTactical,NormalGameplay,Strategic -Displays 1920x1080 -Warmups 8 -Samples 32 -RepeatCaptures 2 -OutputRoot artifacts/pbr-native-b
```

Blender views are relative authoring inspection frames with neutral studio lighting;
native views use the game camera and lighting. They are not matched-lighting parity
certification. A separate 30m native close-up may aid material inspection and is
explicitly labelled; it must not replace the actual three-distance capture matrix.
