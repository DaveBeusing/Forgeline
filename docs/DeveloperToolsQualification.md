# Developer tools qualification

The integration baseline is master `7c5061976166dceda52bbfcbf2f57b7975927e39`,
version 0.1.486. This change advances one PATCH to 0.1.487. The baseline includes
the foundation, tuning, menu, host controls, action persistence and preset packages.
Their focused acceptance records are retained in their existing documents; they
do not establish combined qualification.

The tested product source is `ad318ab3ad38e6feaaad92856a883c2c5f993bcb`,
tree `e66203eec545b2e4f67c34ba47db138c2f991f9c`. Focused integration and package
checks passed. Overall qualification remains blocked by required suite/terminal
failures and incomplete physical DPI/human approval. This is a reviewable
implementation, not a complete product acceptance or integration into master.

## Integration corrections before acceptance

Code inspection found that the native client still used missing providers by
default. The real host now binds the Game dispatcher on its owner thread and
transports typed action requests through its existing ordered bounded queue.
The menu composes the real host, parameter and preset adapters. Regular menu and
window pauses use independent owner handles, so an acknowledged developer menu
transfer permits explicit steps without removing window/focus blockers.

The menu retains a separate correlated session request while an action is waiting
for a simulation tick. Its pending feedback does not claim application. Parameter
basis/initial/pending observations are copied into the menu snapshot. All five
registered domain actions have explicit typed forms. Preset import, original
export, saving current values, original restart, selected restart and baseline
restart use the existing codec/store/transactional loader.

Place incoming JSON under `%LOCALAPPDATA%\FORGELINE\Developer\Presets\Import`
with a filename matching the bounded stable preset key. Select its `Import:`
entry and explicitly import it. Incoming files cannot select an execution path.
Invalid files leave existing presets and the active session unchanged. Valid
imports never overwrite an existing key. Both directories reject links.

## Acceptance matrix

Tests are added and run only after the product implementation/compiler boundary.
Use the SDK and Microsoft.Testing.Platform runner from `global.json`.

| Guarantee | Evidence owner | State |
| --- | --- | --- |
| Public Release/Debug capability and implementation exclusion | Foundation/public rejection cases, assembly/package inspection | Passed |
| Actual menu, host queue and dispatcher composition | Integrated client backend cases | Passed |
| Pause, 1/10/100 steps, five speeds and independent blockers | Owner-thread cases, native controls and transfer-race regression | Passed |
| Resource/repair/refill/spawn/damage bounds and stale identities | Existing domain cases plus real transport | Passed |
| Actual conveyor throughput/conservation and next-match edits | Existing tuning cases plus preset/host integration | Passed |
| Initial profile, ordered action/reset, Save/Replay/Headless hashes | Integrated recovery fixture and existing codecs | Passed |
| Public publisher refuses Developer artifacts; package provenance | Reproducible Public publish and isolated internal publish | Passed |
| Native menu input at compact/normal sizes and DPI | Packaged physical D3D12 interaction at 1280x720/1920x1080, 144 DPI | Partial; remaining DPI/manual cases open |
| Human live operation/visual approval | Human review | Not executed |
| Required build-test and unchanged auxiliary workflows | Product source above; details below | Blocked by research; auxiliary checks passed |

Use seed 742026 for controlled intervention/recovery and seed 2026 for the
unchanged natural terminal gate. Central Divide uses map identity
`prototype.vertical_slice` revision 1 and registered terrain version 1; map/content
SHA-256 values come from `ScenarioRegistry` and are recorded in reproduction
artifacts. Do not substitute a different map or update catalog hashes for a test.

## Required process

The canonical Public restore/reference/solution build, complete solution tests,
runtime asset checks, native smoke, reproducible public packaging and natural
80,000-tick terminal gate remain unchanged in `docs/Development.md` and
`.github/workflows/ci.yml`. Focused combined qualification supplements them.
Internal test artifacts must stay local; no upload or publication rule is added.

Executed commands, source trees, package identities, failures and hardware limits
are recorded below as acceptance proceeds. A plan, compiler success, synthetic
UI model or WARP readback is not a physical native product acceptance result.

## Results

The explicit combined acceptance boundary is product commit `88c4c3bd`.
Both affected Client Release variants compiled without warnings/errors before it.
Canonical solution restore with normal NuGet auditing and the 47-project reference
validator passed. The first runner attempt was blocked by the local named-pipe
permission boundary and ran no tests; the canonical runner then executed with
the required local permission.

The first two integrated cases produced one pass and one failure. The menu/step/
pending-profile/new-session case passed. The recovery case showed that Save/Load
preserved the initial override while Replay exported current RuntimeSettings and
lost the original profile. `MatchPersistenceService.CaptureReplay` now uses the
same existing `CaptureConfiguration` path as Save. Both integrated cases and
the later separate-process recovery checks passed after this correction. No
schema, assertion or golden changed. An in-progress Public solution build was cancelled for this
source correction and supplies no completed build evidence.

The initial hardware inventory query was denied access to the Windows CIM
resource; this provides no GPU or D3D12 capability evidence.

The replay correction passed both integrated cases (2/2, no skips). The Public
selection then passed 2/3 and reproduced acceptance of a bare DeveloperModified
marker. The shared persistence guard now includes that marker and explicit
initial-profile/audit presence in its Developer-data checks. A marker without
corresponding state is also rejected as corrupt in an approved Developer host.
The later Public rejection and Developer recovery runs cover this correction.
The first complete solution build stopped on missing cancellation tokens in the
new Public tests; these were corrected without changing assertions.

At `fe31c0c1`, the Public selections passed 19/19 Game and 3/3 Client cases.
All seven isolated build entries, including ordinary Debug without the Developer
property, passed. Developer Game passed 47/48; the remaining old tuning fixture
attempted to bind a second provider to a fully composed runtime. It now consumes
the runtime's existing registered provider. The single-owner production invariant
and every assertion remain intact. The first runner report is retained as
`artifacts/developer-tools/qualification-fe31-first.json`.

The integrated preset PR baseline at `49463f88` failed its required build-test
at `TechnologyNormalMatchTests.NormalOpponentPaysForResearchAndReconstructsWhileProductionContinues`,
line 49 (56/57 selected cases passed). The log reports MissingMaterials for the
third technology. Its four auxiliary workflows passed; native-reference was
skipped by workflow policy. This pre-existing research blocker is independent
of the developer composition and is not waived.

## Final focused evidence

The complete Public solution build at `b2d61561` passed with zero warnings/errors.
The final affected Public and Developer compiler paths also passed. The complete
combined runner at product source `ad318ab3` passed in CI: Public Game 19/19,
Public Client 3/3, Developer Game 48/48, Developer Client 65/65 and ordinary
Debug 19/19, with no skips in those selections. All seven isolated variant builds
and the four publisher capability contracts passed. The Debug build receives no
Developer property. Release output/intermediate paths stay isolated by variant.

After the final native correction, the affected owner-thread/menu selection
passed locally, 60/60. The Public host, menu/version/artwork, rejection and
capability selection passed 43/43. The smaller menu/artwork selection had already
passed 16/16 in each variant at `cbf8c5f0`. These selections overlap; their counts
must not be added as independent coverage. A documentation-only successor does
not change the tested product source above.

Executed entry points, from the repository root:

```powershell
dotnet restore ForgeLine.sln
pwsh ./build/Validate-ProjectReferences.ps1
dotnet build ForgeLine.sln --configuration Release --no-restore --maxcpucount:1
pwsh ./build/Test-DeveloperToolsQualification.ps1
dotnet run --project tools/ForgeLine.MapCompiler/ForgeLine.MapCompiler.csproj --configuration Release --no-build -- --output artifacts/maps/central-divide.flmap.json --qualification-output artifacts/map-qualification.json
pwsh ./build/Test-CompiledScenarioLoading.ps1
pwsh ./build/Build-RuntimeAssets.ps1 -Mode build -ForceRebuild -NoBuild
pwsh ./build/Test-RuntimeAssetPipeline.ps1
dotnet test --solution ForgeLine.sln --configuration Release --no-build --max-parallel-test-modules 1
pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible -RuntimeAssetRoot assets/runtime -OutputRoot artifacts/developer-tools/prealpha-final
pwsh ./build/Test-DeveloperToolsPackages.ps1 -PublicRoot artifacts/developer-tools/prealpha-final/win-x64/publish
```

The canonical map qualification, both registered scenarios' real CLI
save/replay/missing-content checks, force-rebuilt runtime assets and runtime
process contracts passed. The publisher passed its publish-tree reproducibility
comparison and scenario/font/UI package checks on a clean source tree. Actual
Public assemblies exclude the writable Developer implementations, and the Public
guard rejects the actual internal Developer output. Both packages identify
`ad318ab3`, version `0.1.487-dev`, SDK `10.0.402`, Release and their correct variant.
The internal output remains under `artifacts/developer-tools/packages/<SHA>/Developer`.
No internal upload rule, release or merge was performed.

### Recovery fixture and content identity

The final retained local fixture is `artifacts/developer-tools/fixture-final`;
`recovery-final.json` records its product source/tree and separate-process exits.
It uses Validation profile, seed 742026, initial conveyor multiplier 1.25,
an actual resource grant, multiplier 2, then reset to the captured initial value.
The ordered interventions occur through the real host/dispatcher while paused
and explicitly stepped. DeveloperModified and both parameter audit entries
survive reset and export. At tick 4, Save/Load, Replay and separately started
Developer Headless agree on:

`6A0D0FF40D2D4EEDD95CF9D83EB8856366AF96CA294F55616DC6A1222D5DCC4C`

The CI fixture at the same source independently matched its own recorded hash
`DE0DAF28B7B330C3FA06D6F15084701B0B7E87B6BDAC96460814C542D24B409B`.
Compare each recorded fixture, including its request correlations, to its own
expected hash; these are not seed-only cross-run goldens. Public rejects the
Developer save, replay and preset option; conflicting preset seed input is also
rejected. The final local Headless build and both recovery commands exited zero.

| Identity | Value |
| --- | --- |
| Central Divide map | `2f2b21f21697c665a8ab3ac607e7cebc6f82d1b9cf06f2838a02444f179f1f1f` |
| Split Basin map | `f82b732e563b611a9106279e260564ba91a77eb3a61e19dd1e439caf28d8c8b8` |
| Directorate content | `205d0887e816d8922c43996a6a8ad85d177cbbf168e551a87ce76d9cc81efd63` |
| Runtime manifest SHA-256 | `0ac369d83713fb0086c8592b2a330bbf25b6f140aad8166dec946ff9f015a6a2` |
| Runtime content signature | `f8416ea299da82358086ec219bedbe38fbb27d2a7680b6691d3f78da352b4c3b` |

The qualified manifest contains 2,049 assets and 94,731,442 runtime bytes.
Map/content identities and gameplay costs were not adjusted for acceptance.

## Failures found and bounded corrections

The tuning fixture now uses the runtime's registered provider rather than binding
a second owner. The preset compatibility case now exercises RestartSelected;
RestartSameSeed deliberately restarts the original profile. Both retain their
original assertions. Reports from the failed `fe31c0c1` and `6d938d71` attempts are
retained under `artifacts/developer-tools/qualification-*-first.json`.

The compiled-scenario script still expected schema 7 after persistence had moved
to schema 8. Its exact expectation now matches the integrated serializer; all
identity checks remain. Both scenario CLI round trips then passed.

The first reproducible publish compared equal trees but failed while removing
verification DLLs: the Public metadata guard retained Windows assembly file
locks while collectible unloading was deferred. The guard now inspects assembly
bytes through a disposed memory stream. Capability conditions remain unchanged.
The publisher contracts and complete actual package runs passed afterward; the
failed log is `public-publish-3413-cleanup-failure.log`.

Native interaction at `ea800812` exposed a real pause-transfer race: STEP 1 was
rejected as Cancelled at tick 397. A regular menu signal from an older frame
could reclaim the Menu reason after its acknowledged transfer. The host now
ignores regular Menu updates while the explicit Developer lease is held; explicit
restore returns it. Window/loading owners remain independent. The added integrated
case reproduces the stale frame, exact one-tick execution and restoration. Its
60-case affected selection and the final native 1/10/100-step repetition passed.

The full suite also exposed stale variant-display/control expectations and a
missing DeveloperTools semantic artwork binding. The tests now require the real
variant suffix and explicitly require/exclude all four Developer control labels.
The artwork entry uses the existing settings role. The exact semantic coverage
assertion remains; no visual golden or resource payload was replaced.

## Complete-suite and required-gate outcome

The first unbounded parallel solution attempt was interrupted when it made the
desktop unresponsive; `solution-tests-interrupted.log` is incomplete evidence.
The completed repetition used the same serial-module option as CI and the
Public binaries built at `b2d61561` (the intervening `3413ec6d` changed only a
script). It took 38m30s: 4,356 total, 4,274 passed, 10 failed, 72 skipped, exit 2.
The three Developer menu/artwork failures were subsequently closed by their
16/16 Public and Developer selections. The other seven findings remain open:

- `UiInteractionFeedbackTests.SettingsRoundTripPreservesCameraAndSupportsAdditiveDefaults`: access denied replacing the settings file.
- `ObjectActionRingTests.EveryGlobalCategoryKeyOpensAtCursorAndRequiresRealOwnedTargetsForActions`: P/Production/unavailable case expects Production, observes Closed.
- `SkirmishSupplyPolicyTests.ExpansionFundingKeepsPartialHubCostsAheadOfOptionalExtractors`.
- `SkirmishSupplyPolicyTests.SteelInputRecoveryDoesNotRetainConstructionStockWhenProductionIsBlocked(true)`.
- `SkirmishOpponentTests.DirectCombatTargetsRequireCurrentIdentifiedIntelligence`.
- `TechnologyNormalMatchTests.NormalOpponentPaysForResearchAndReconstructsWhileProductionContinues`.
- `OperationsChainDiagnosisTests.AmmunitionWaitsForElectronicsWhoseSilicateInputRecoversThroughAPaidConnection(97, false)`.

These tests and their relevant gameplay paths were not changed by this work.
Only the research failure has the separately inspected prior-baseline CI proof
recorded above; no new baseline run is claimed for the other findings. No broad
gameplay repair, assertion weakening, budget or golden change was made.

Both lightweight Headless diagnostics passed (64 ticks/1,000 entities, seed
12345; 16 ticks/10,000 entities, seed 67890). The unchanged natural terminal
command ran all 80,000 ticks with seed 2026, Validation profile and
`--require-terminal`, then exited 3: status Active, lifecycle Running, no winner.
It is a failed mandatory gate, not a terminal-match success. Evidence is
`artifacts/match.json` and `artifacts/developer-tools/natural-terminal.log`.

At the final product source, [build-test](https://github.com/DaveBeusing/Forgeline/actions/runs/40401006429/job/121846520972)
passed the complete 154-case Developer qualification, then failed the same
research case at line 49 (56/57 in that early gate). Source-integrity,
conveyor-flow, navigation-movement, cpu-scale and frame-contracts passed.
Native-reference was skipped by workflow policy. Later job steps therefore do
not supply CI evidence; the package and native results above were executed locally.
Required build-test remains red, so the PR remains unmerged and unapproved.

## Physical native scope and remaining approval

The packaged Public smoke (`--smoke-test --render-stress 1000`) exited zero and
presented 221/221 frames at 1280x720. Its source was `ea800812`; the final
`ad318ab3` Public package was also started interactively and its Pause menu lacked
Developer Tools before/after Shift+F2. This supplements the actual assembly and
direct-request rejection checks.

Physical runs used NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver
32.0.15.9653, D3D12 Level 12_1, software=False, debugLayer=False. Product logs
identify Windows 10.0.26300, .NET 10.0.13, 144 DPI and UI scale 1.0. Developer
Gameplay sessions used seed 17 at 1280x720 and 1920x1080. Separate settings roots
were supplied; no native preset import/export or save into the user's catalog
was performed.

At compact size, real parameter adjustment, queued Apply, resume/confirmed 1.25,
queued Reset and return to the shell with that request pending were observed on
`ea800812`. At final product source, compact Session/Parameters/Actions navigation,
scrolling, the Command Core inspector (entity 77, generation 1, player 1,
snapshot tick 1311, 100% health), Escape and return/resume were observed. Shift+Right
and Delete in the menu caused no world command or camera movement; after resume
the selected core was still present at 100% health.

At normal size on final source, native steps advanced 494 -> 495 -> 505 -> 605.
All five speed choices were confirmed at tick 605 while paused. Product captures
showed writes disabled on focus loss and restored on focus gain; actual Minimized
and Restored events were logged. A separate other-application activation attempt
timed out in helper approval, so no successful cross-application operation is
claimed. Own-window focus recovery and minimization were executed afterward.
Both final native sessions exited zero with unchanged camera pose and no world
command sequence. Results and images remain in `native-qualification.json`,
`native-public-final`, `native-normal-fixed` and `native-compact-fixed` under the
local qualification artifact root.

This is automated input/image inspection, not human approval. Physical 96/192-DPI
operation, exhaustive native entity-destruction/stale-target interactions and
human review of all sections/error states remain open. Existing real-domain and
UI-model lifecycle tests cover their contracts but do not replace those physical
checks. The menu currently uses bounded numeric/choice controls, not a free-text
editor. No complete native/product qualification is claimed.
