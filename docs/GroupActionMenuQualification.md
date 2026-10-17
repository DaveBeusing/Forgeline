# Group action menu qualification

Qualified product/test source: `632ca88a4b3297b3f1bbd27eb8c1618fd1c16993`. Base master: `1ef23a9fc7ab9195d0d1bdadd97081016ea97861`. Branch: `ui/group-action-rings`. Version **0.1.388-dev**, exactly one PATCH. [PR 222](https://github.com/DaveBeusing/Forgeline/pull/222) remains **Draft**. This evidence/documentation checkpoint changes no product or test code; the package records the clean qualified source above.

## Implemented behavior and requirement status

| Requirement | Implemented contract / evidence |
|---|---|
| Exactly one group ring | Drag/Shift/type-filter selections and saved slot recall reuse the existing object ring. Recall explicitly invokes it even if membership is unchanged. Pure cases cover 0/1/2/10/50/100/1000 selected identities |
| Authorized bounded anchor | Selected-ID lookup only; one interpolated position per simulation entity. A visible subset supplies a marked anchor without filtering command members. Hidden owned members retain command membership without contributing positions. No visible anchor closes the menu and retains selection |
| Existing command parity | Tactical Attack, AttackMove, Stop, Hold, Retreat, artillery targeting/cancel and recovery reuse the existing whole-selection adapter. Ordinary Move remains the same right-click dispatcher/shared-route path. Stop compares the complete existing request and exact member IDs, with once-only consumption; split/coalesced outside right-clicks compare full membership and target |
| Selection tools and formation | All eight composition filters plus damaged/supply focus, formation cycling and group focus remain available in three pages. Their existing adapter is shared; filters never edit saved slots. Unsupported building-only unit focus stays disabled |
| Coherent capabilities | Immutable copied selection/member facts, captured tick and canonical identities gate presented controls. Mismatched render/input facts show Updating. Pressing a disabled item cannot become a command after capability refresh. Exact authoritative accepted/rejected counts remain in the informational inspector |
| Lifecycle | Generation/ownership pruning is independent of camera visibility. Selection/session/membership changes cancel pressed actions. Two to one switches to the single ring; zero closes. Saved groups remain organizational state, distinct from current selection and simulation movement groups |
| Shared HUD integration | Rectangular group action buttons are removed; five larger informational rows remain. Global mode buttons open this ring. One obstacle resolver serves gameplay, fixture preparation and native recording, including actual painted onboarding/tab, notifications and operations. Compact fallback retains a 96-pixel control band and 14-pixel type, with distinct tactical titles and full tooltips |
| Qualification still open | Default full-solution allocation failure, required texture/terminal gates, full native matrix and human live/physical/motion/golden acceptance below prevent full acceptance |

[Group interaction contracts and logical checkpoints](GroupActionMenus.md) describe supported actions and limits. No new formation, navigation, batching, Repair/Supply group mutation, simulation authority or UI framework is introduced. All authoritative Game/Simulation/Headless source is unchanged.

## Executed checks

| Check | Actual result |
|---|---|
| Restore / Release solution build | Passed; SDK 10.0.401, zero warnings/errors |
| Version / dependencies / canonical map | One PATCH; 43 projects, no cycles/boundary violations; 71 reachability pairs, two alternate navigation and two alternate road routes |
| Ring/reference focused tests | 49 passed on final logic; geometry/group checks include exact compact label bounds, shared obstacles, unique short tactical labels and allocation-free warm inspector rendering |
| **Final full solution** | **Unsuccessful: 2,396 total / 2,386 passed / one failed / nine existing opt-in skips**, 9m 25.958s. Unchanged `DirectorateUnitPresentationTests.ProductionDamageMeshesUseExistingStateAndResolveWithoutHotPathAllocations(supply_truck)` reports **8,144 vs zero bytes** |
| Complete Presentation serial diagnostic | Same final binaries, `--parallel none`: **1,179 total / 1,172 passed / zero failed / seven existing opt-in skips**, 1m 22.619s. This does **not** relabel the failed full invocation |
| Earlier full checkpoint | 1c0470a3: 2,391 total / 2,382 passed / zero failed / nine skips. Historical source, not the final full result |
| Runtime assets / process contracts | Forced rebuild at 65c35eee passes; later changes affect presentation/reference tooling only. Final-source validation passes with all **1,337 assets** and unchanged whole-tree fingerprint/signature |
| Headless smoke / stress | Final source: 64 ticks, seed 12345, 1,000 entities and 16 ticks, seed 67890, 10,000 entities; both exit zero |
| Save/load/replay | Final source: 256 ticks, seed 4242; creation, restore and replay all exit zero; replay hash `EDD800B633DD7D1AD2D882C6A01625654946955DE7EF486FBBF5C46747109390` |
| Source / packaged player startup | Both exit zero, application/session **Ready**, zero dropped events, empty stderr, 1,000 requested render-stress instances. Packaged player runs from an empty working directory and loads its own 1,337 assets |
| Personal settings | SHA-256 unchanged across source/package smoke; fresh isolated settings roots used. Contents are not archived |
| Reproducible package | Passed UI atlas/audio/source-exclusion/package contracts; 844 UI payloads verified. Local candidate, not a release |

Commands, complete successful and failed local logs, native manifests, independent audit, startup reports, CI summaries/excerpts and package provenance are in [records.zip](evidence/group-action-rings/records.zip) (137 records). Original save/replay payloads, other native PNGs and full raw CI logs remain local. The [index](evidence/group-action-rings/index.json) pins archive/image/package digests.

Runtime content signature: `5cb87ff663e609ff3c0842be2671e3eabc9a9389e3562a66e0fba46733700932`. Whole-tree fingerprint before/after forced rebuild: `ADE1441B64F93A5BD649686DA1AD9FD2B924CE7C01B6AC50FCCBBBBAD12DDA67`.

Package: `artifacts/group-rings-package-632ca88a/FORGELINE-0.1.388-dev-win-x64.zip`, **40,054,212 bytes**, SHA-256 **`88dfd28061d60eeabedf8de9fb741549cddce2544ffdedbd2ddf8460b3e7ed2d`**. Provenance confirms clean source `632ca88a4b3297b3f1bbd27eb8c1618fd1c16993`.

## Native frames, motion and resources

**27 final-source native candidates**, eight identical readbacks each, cover 720p Low / maximum density, 1080p Medium, 1440p, Ultrawide High / large UI, 4K High / large UI, and dark/haze/high-particle/half-world-resolution scenes. Group commands, tools, partial visibility and edges are included; 1080p also includes an actually selectable owned single building, single unit and targeting. Two packaged group frames match source **PNG and decoded RGBA hashes exactly**, with eight equal repeats each. PNG/RGBA/region hashes, native dimensions, text/skin diagnostics and submission health were independently checked. These are candidates, not approved goldens; raster DPI is not physical monitor acceptance.

The existing production motion owner provides **336 exact samples** for single/group opening/closing at injected 30/60/144 Hz with ordinary/reduced motion. Ordinary opacity follows 160 ms SmoothStep with intermediate samples; reduced motion settles immediately. Injected rates are not measured FPS.

After **128 open/close/context cycles**, resources remain **112 -> 112**, shared atlas buffers **52 -> 52**, eight UI textures and **28 MiB** shared pages. No missing/dropped glyphs or skin quads, extra retained textures or submission faults were observed.

| Same-selection clear/HUD/text sample | CPU submit p50 us | Full GPU frame p50 ms | Skin draws | Skin upload bytes | Total HUD vertices |
|---|---:|---:|---:|---:|---:|
| GroupClosed | 7,799.7 | 4.216800 | 17 | 83,520 | 60,498 |
| GroupRing | 8,434.9 | 4.514336 | 17 | 88,320 | 63,624 |
| GroupExpandedDockReference | 7,667.7 | 2.959904 | 17 | 136,512 | 64,758 |

These sequential same-device samples use 32 warmups / 128 samples, exclude world rendering, and include waits/Present in CPU time. `GroupExpandedDockReference` reconstructs the retained rectangular tactical dock using the **same current-source selection**, not a historical group-card binary. The ring is not claimed faster; timing variation and this narrow scope do not establish gameplay FPS or a general speedup.

| Authorized selected identities | Context capture mean us | Bytes per snapshot update | Anchor mean us | Anchor bytes/frame |
|---:|---:|---:|---:|---:|
| 0 | 0.138 | 152 | 0.020 | 0 |
| 1 | 0.259 | 256 | 0.371 | 0 |
| 2 | 0.352 | 272 | 0.509 | 0 |
| 10 | 1.163 | 400 | 3.399 | 0 |
| 50 | 8.518 | 1,040 | 12.798 | 0 |
| 100 | 17.605 | 1,840 | 25.789 | 0 |
| 1000 | 222.305 | 16,240 | 372.788 | 0 |

Each row uses 128 warmups / 128 samples over copied synthetic legal data. Context storage scales with selected identities and is rebuilt only for snapshot/selection changes; unchanged input context reuse is tested at zero allocation. Anchor work looks up selected IDs through the existing render index and interpolates only their positions. There is no per-frame world scan, new polling thread, per-member command dispatch or GPU resource creation.

Representative unchanged originals:

- [Compact commands](evidence/group-action-rings/compact-commands.png), [group commands](evidence/group-action-rings/group-commands.png), [group tools](evidence/group-action-rings/group-tools.png), [partial selection](evidence/group-action-rings/partial-selection.png).
- [Single building](evidence/group-action-rings/single-building.png), [single unit](evidence/group-action-rings/single-unit.png), [Ultrawide edge](evidence/group-action-rings/ultrawide-edge.png), [4K](evidence/group-action-rings/large-display.png), [dark / half-world](evidence/group-action-rings/dark-half-world.png).

Historical candidates are preserved separately: [65c35eee obstacle omission](evidence/group-action-rings/historical-obstacle-candidate.png) and [80a11c9e indistinct clipped titles](evidence/group-action-rings/historical-title-candidate.png). Their structural repeats passed but visual inspection found issues; neither is promoted to final acceptance. The fixes share the painted obstacle contract and use distinct compact titles; original pixels were not edited.

## CI and unresolved allocation measurements

Final source passes [Movement quality 39020872808](https://github.com/DaveBeusing/Forgeline/actions/runs/39020872808), [Scalability 39020872824](https://github.com/DaveBeusing/Forgeline/actions/runs/39020872824) and the unchanged retry of [Frame hot paths 39020872837](https://github.com/DaveBeusing/Forgeline/actions/runs/39020872837).

Frame attempt one ([job 117362243297](https://github.com/DaveBeusing/Forgeline/actions/runs/39020872837/job/117362243297)) reported **32 warm bytes** in the rebound gameplay HUD Pending case. The identical local current-source benchmark and a control using the integrated-master presentation assembly each pass all **120 cases**, `DOTNET_TieredCompilation=0`, zero allocations. Master presentation source matches e1bb7091 exactly; control assembly version/hash are archived. The one unchanged retry ([job 117365222340](https://github.com/DaveBeusing/Forgeline/actions/runs/39020872837/job/117365222340)) passes. The original failure is retained; its root cause is not established and no budget/assertion/warmup/timeout was weakened. Full original frame-job log SHA-256: `5990f8244580fb5ebedc6e5a463de837d6f5b3626ccee61b80726d48eedc4da7`.

Required [CI 39020872814](https://github.com/DaveBeusing/Forgeline/actions/runs/39020872814) is **failed** at Windows window/visual qualification: **99,608,050 gameplay texture bytes exceed 56 MiB**, unchanged content and budget. All preceding UI/group, atlas, pointer, native reference and package-integrity contracts execute successfully; downstream skipped steps are not passes. Full main-job log SHA-256: `68fb06fe9bdd0b1572b6a15fe13bd1a8c8fe5ecd462b70f951314709e7a2c2b8`.

The final local full-solution allocation assertion remains an unsuccessful invocation despite the complete serial Presentation pass. An earlier broad Presentation invocation also reported 6,040 vs zero for main_battle_tank; that class passed focused, and the later 1c0470a3 full solution passed. Failed runs are retained. No change touches the catalog method or its assertion. Isolation/measurement provenance supplements evidence; it does not establish a root cause or erase failures.

## Remaining acceptance

- Diagnose the default full-solution allocation failure without changing the zero-allocation assertion. The first hosted 32-byte anomaly also remains unexplained despite passing controls/retry.
- Resolve required CI's existing texture budget and the **natural match gate** separately: final source, seed 2026 / validation / 80,000 ticks stays **Active, winner 0, exit 3**. Tick limit, game rules and authoritative sources are unchanged; no forced result or resource grant is used.
- Complete the planned **86-state x 45 display/DPI/scale = 3,870-case** matrix where required. Its coverage contract passes synthetically; 27 source candidates are not the complete matrix.
- Human live in-game interaction, readability/contrast, physical DPI/focus/Alt-Tab, motion and final visual/golden acceptance remain open. Automated state/command cases, static native frames and injected motion do not replace that review.

Implementation is reviewable; full acceptance is **not complete**. Keep Draft. No merge, auto-merge, tag, protection change or release was performed.
