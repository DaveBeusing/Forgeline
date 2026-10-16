# Graphical tactical interface qualification

Base `a9e8f0b76b02ff9c1e85aba9b9d33952c357cc63` integrates production/operations PR #212. Production/test/native/package source is **`0cb9b8089f84abfdce0e754e9176be5c4628c16b`**, branch `ui/graphical-tactical-indicators`, [PR #213](https://github.com/DaveBeusing/Forgeline/pull/213), one PATCH to **0.1.379-dev**. Subsequent commits retain documentation/evidence. [Implementation and deliberate procedural components](GraphicalTacticalInterface.md).

The PR remains **draft**. Full regression and required CI are not green; physical-pointer, monitor, readability/accessibility and human art approval remain pending. No merge, tag, release, approval inheritance or AAA/60-FPS certification is performed.

## Checks and unresolved gates

| Check | Outcome |
| --- | --- |
| Release restore/build | Passed; zero warnings/errors |
| References/version | 43-project acyclic graph; one PATCH, grammar, idempotence and isolated allocation scripts passed |
| Runtime assets | Clean compile and final-source validate passed; 1,337 assets, 900 textures, 309 meshes, 127 materials |
| Focused RTS/HUD/geometry/privacy/bootstrap contracts | 94 passed, zero failed/skipped |
| Native cursor contracts | Five passed: dimensions, hotspots, focus restoration, session handle release and invalid payloads |
| Runtime cursor roles/DPI | Seven passed; all eleven roles at four sizes across two sessions |
| Final complete solution | **Failed: 2,090 passed, one failed, nine existing opt-in skips; 2,100 total** |
| Allocation follow-up | 39 isolated tests pass on current production; same 39 pass against the integrated-master production assembly; broad failure is unresolved |
| Final native source matrix | 48 complete cases, exact repeated PNG/RGBA hashes, no missing/dropped sprites/glyphs |
| Source-less reproducible package | Two publish trees match; 1,403 checksummed files / 1,337 runtime assets verified |
| Packaged captures/fresh defaults | Five exact source PNG/RGBA matches; smoke exit 0 and createdDefaults=True |
| Natural seed-2026 terminal match | Victory / CommandCoreDestroyed at tick 42,922, below the 80,000-tick limit; exit 0 |
| Source CI | Tactical step passes; main job fails existing compiled-texture budget; Frame hot paths and Scalability pass |
| Physical cursor/display and human graphical acceptance | Pending |

The final whole-solution run takes 8m 23s 775ms. Its sole failure is unchanged `DirectorateBuildingPresentationTests.RepeatedProductionStateResolutionDoesNotAllocateAssetNamesPerFrame(fuel_refinery)`: **1,984 allocated bytes, expected zero**, line 411. A preceding source cohort (`11aca562`) fails three unchanged allocation tests: SimpleInstanceRenderer warm submission (408 bytes), building-state Extractor (1,512), and Vehicle Factory (7,464). That preceding run is 2,099 total / 2,087 passed / three failed / nine skipped. Both original failed logs are retained. Neither becomes passed through a focused rerun.

The isolated control copies the test-module output into an independent artifact directory and replaces only Presentation with the published 0.1.378 production assembly. Its source `7e408b0357f437ef683fd4f7ecf6af83b09bebb5` is code-equivalent to base `a9e8f0b7` (empty Git diff over src/tests/tools/build). Both current and control pass the same 39 building/instance tests. This is **not** an entire master-suite run and does not reproduce or explain the broad failure. Root cause remains open; no assertion, warmup, compiler setting or allocation allowance is changed. The initial direct executable launch rejected the MTP filter; the successful control uses `dotnet test --test-modules ... -- --filter-class ...`. Its failed-launch record is retained separately.

[Source CI 38868574666](https://github.com/DaveBeusing/Forgeline/actions/runs/38868574666) fails step 36 at `build/Invoke-WindowModeQualification.ps1:232`: **99,608,050 compiled gameplay texture bytes exceed 58,720,256 (56 MiB)**. This is the same measured gate recorded by the integrated preceding UI work. Step 25, Tactical artwork privacy and native pointer ownership, passes. Later hosted full-test/publish/terminal steps are skipped; the local checks above are separate. [Frame hot paths 38868574683](https://github.com/DaveBeusing/Forgeline/actions/runs/38868574683) and [Scalability 38868574673](https://github.com/DaveBeusing/Forgeline/actions/runs/38868574673) pass at the production source. Budgets, reviewed goldens and the natural terminal gate remain unchanged.

## Native evidence

All final source cases use BattlefieldCombat / Medium, fixed camera, three warmups, eight samples and two exact readbacks. The named states are Contacts, Targeting, MinimapStrategic, MixedSelection and SupplyCritical. They are authored copied facts over the existing world fixture, not naturally played matches. The fixture's generic selection circles are historical reference decoration; production oriented-outline geometry has its own existing and extended tests.

| Root under artifacts | Cases | Request |
| --- | ---: | --- |
| tactical-native-final-normal | 5 | 1920×1080, DPI 96, scale 1, normal request; MinimapStrategic selects the real strategic camera |
| tactical-native-final-close | 5 | Same named normal request, supported 120 m inspection distance |
| tactical-native-final-world-zooms | 3 | World state, actual CloseTactical / NormalGameplay / Strategic bookmarks |
| tactical-native-final-compact | 15 | 1280×720, DPI 144, scales .75/1/2, world render scale .5 |
| tactical-native-final-wide | 10 | 2560×1440 and 3440×1440, DPI 144, UI scale 1, world render scale .5 |
| tactical-native-final-4k | 5 | 3840×2160, DPI 192, UI scale 2, world render scale .5 |
| tactical-native-final-dark | 5 | Named normal request at 120 m, daylight .2/cloud cover 1, 1080p/DPI96/UI1 |

The initial multi-zoom named-state request is rejected before native capture by the existing fixed-fixture guard. That failed request/log remains retained. The guard is unchanged. Close evidence uses the already supported inspection parameter; three World captures qualify the regular zoom bookmarks. Each manifest records the actual camera, raster/output and physical host independently. No rejected request is counted as a passed case or overwritten.

Requested native UI dimensions equal actual captured output in every completed case. The physical host is 1280×720 at DPI 144; requested 4K render output is not a physical 4K monitor review. The half-resolution world cases preserve full native UI/output dimensions. UI capture values FPS 60/TPS 20 are fixture labels, not measured performance.

Observed whole-HUD skin cost is **15–39 draws, 1,920–4,608 vertices and 61,440–147,456 uploaded bytes per frame**. Preparation is 13 batches / 52 buffers / 8,749,056 reserved upload bytes; shared atlas residency remains exactly **29,360,128 bytes (28 MiB)**. The real thousand-unit contract produces 2,000 map glyphs (unit plus selected-group glyph), at least 12,000 icon vertices, and repeats twelve frames without additional texture uploads, prepared-buffer growth or dropped quads. The separate world-outline test retains 24,000 lines for a thousand objects, within its existing bounded batch.

Whole-scene observations across these different camera/display/state workloads are CPU p50 5.2489–67.1457 ms and GPU p50 7.71584–65.53632 ms. They use only eight samples and are not a paired baseline, throughput improvement or stable 60-FPS result. Whole-scene resident texture payload is 77,188,276–89,891,412 bytes with 62–89 textures and 69–96 of 4,096 descriptors. These are distinct from shared HUD pages and the compiled-catalog CI gate; no whole-scene budget acceptance is claimed. All final cases record zero texture-binding failures and submission faults. Local D3D debug layer is unavailable; zero counters do not certify debug-layer cleanliness.

## Cursor and privacy evidence

Runtime metadata supplies hotspots (24: 3,2; 32: 4,3; 48: 6,4; 64: 8,6). The platform tests query the actual Win32 cursor dimensions and hotspots, distinguish cursor/icon handles, check RGBA-to-native conversion, focused client installation/system-arrow restoration, duplicate-image reuse and release across two sessions including native-window closure. Client tests construct all 44 role/size combinations twice. Standard input coordinates, gesture thresholds, modal capture, focus handling and exact map hit bounds remain covered by existing tests. Physical mouse/DPI transitions and precision at a real monitor still require human review.

Native world tests exclude hidden instances, foreign operational values, stale experience ticks, unavailable/nonfinite facts and group aggregates. Hover requires current session/tick and permitted extraction. A rotated 12×80 footprint test proves independent axes and corner orientation, including sloped terrain samples. Existing intelligence, targeting and minimap interaction tests preserve hidden/unidentified distinctions and coordinate authority. No new order, rail, air, patrol, health or intelligence authority is derived from artwork.

## Runtime and package identities

Artwork catalog SHA-256: `750c0cc724974a741c3b369f1c2177685f998500a44693df5773b29121c4d539`.
Atlas metadata SHA-256: `6587dc174a6ad4639fd8c9cc23c4d64a25b1ef3182fe0faf30017e6863ae503f`.
No reference/source artwork is modified. Compiler remains 1.10.0; runtime payload totals remain 113,308,083 bytes.

The clean master checkout materializes the pre-existing menu asset definition as CRLF. Clean recompilation changes exactly its source/build metadata and FLAS wrapper hash: content signature moves from `b6cbe90cbbd4dfc780abb28f5d42f7bc3b469ae0cf4e663f594975a2bfd219d9` to **`5cb87ff663e609ff3c0842be2671e3eabc9a9389e3562a66e0fba46733700932`**. Direct comparison verifies all **1,337 decoded binary payloads byte-identical** to the preceding package; the menu texture payload SHA is `c012796d8bd463987e1e125ab2d9b82803cd48fd8573ad366219e142f42be660`. No source is normalized or fingerprint forced to hide that difference. The initial five-image candidate at `4e25033e` uses the preceding runtime fingerprint; it is kept separately from final evidence.

Final development fingerprint: `ADE1441B64F93A5BD649686DA1AD9FD2B924CE7C01B6AC50FCCBBBBAD12DDA67`.
Packaged fingerprint: `B8ACA01894C15F98C09ACB5A1A80EA173B7FCD73C51D4C7E7BFF3701B61F523F`.
Their six expected playback sidecars are the four original UI WAV cues and studio intro MP4/WAV; all 1,338 shared runtime files (manifest plus assets) match byte-for-byte. Authoring/reference content is excluded. Cursor images are extracted from the packed runtime, with no source/catalog discovery in the player.

Archive: `artifacts/graphical-tactical-package/FORGELINE-0.1.379-dev-win-x64.zip`, **39,996,785 bytes**, SHA-256 **`c10f1a143dd9c5fa972b0fe65f604cd580ca74adf685116dee15c8ce7ebf2dc8`**. Its 1,403 checksummed files and every runtime size/hash pass; two publish trees reproduce exactly. Five packaged native images match source PNG and decoded RGBA hashes exactly. Fresh defaults use a separate artifact settings root and smoke exit 0.

The natural match uses seed 2026, one match and `--ticks 80000 --require-terminal`; elapsed overall time is 107,467.1641 ms. Victory / CommandCoreDestroyed occurs at tick 42,922 for player 1, with 268 completed cargo orders, 23,386.06629034142 cargo delivered, zero cargo failures and zero route failures. No forced terminal result or resources are added. This is a functional outcome, not a paired outcome/performance comparison.

## Retained review set and recovery

[Hashed evidence index](qualification/graphical-tactical/evidence.json) includes 53 original records in [qualification-records.zip](qualification/graphical-tactical/qualification-records.zip), all complete native manifests, original failed/passed logs, runtime comparison, control identity, CI readback/logs, package provenance, terminal output and exact reproduction scripts. Seven original PNGs retain their native bytes; none is resized, recolored or assembled for acceptance.

- [Contacts / minimap](qualification/graphical-tactical/contacts.png)
- [Targeting](qualification/graphical-tactical/targeting.png)
- [Close owned world status](qualification/graphical-tactical/world-supply-close.png)
- [Compact/DPI144/scale2](qualification/graphical-tactical/compact-contacts.png)
- [Strategic ultrawide](qualification/graphical-tactical/strategic-ultrawide.png)
- [4K/DPI192/mixed selection](qualification/graphical-tactical/selection-4k.png)
- [Dark world status](qualification/graphical-tactical/world-supply-dark.png)

Logical checkpoints: `75984820` minimap ordering/clipping, `6639713d` native cursor ownership, `130ffc10` footprint/status, `c948c924` privacy/camera/platform contracts, `731f6444` crowd fixture (initial unsigned-identity build failure, corrected by `4e25033e`), `11aca562` selector readability, `206e5f30` transient order glyphs, `48fc0b20` native dimensions/focus, and `0cb9b808` actual 2,000-glyph stress assertions. Preserve these commits; no squash or per-commit PATCH increment is performed. Required next acceptance work is the unresolved broad allocation gate, the unchanged texture budget and attributable physical/human review.
