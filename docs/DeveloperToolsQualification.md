# Developer tools qualification

The integration baseline is master `7c5061976166dceda52bbfcbf2f57b7975927e39`,
version 0.1.486. This change advances one PATCH to 0.1.487. The baseline includes
the foundation, tuning, menu, host controls, action persistence and preset packages.
Their focused acceptance records are retained in their existing documents; they
do not establish combined qualification.

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
| Public Release/Debug capability and implementation exclusion | Foundation/public rejection cases, assembly/package inspection | Pending |
| Actual menu, host queue and dispatcher composition | Integrated client backend cases | Pending |
| Pause, 1/10/100 steps, five speeds and independent blockers | Existing owner-thread host cases plus menu transfer integration | Pending |
| Resource/repair/refill/spawn/damage bounds and stale identities | Existing domain cases plus real transport | Pending |
| Actual conveyor throughput/conservation and next-match edits | Existing tuning cases plus preset/host integration | Pending |
| Initial profile, ordered action/reset, Save/Replay/Headless hashes | Integrated recovery fixture and existing codecs | Pending |
| Public publisher refuses Developer artifacts; package provenance | Existing publisher and package validation plus isolated internal publish | Pending |
| Native menu input at compact/normal sizes and DPI | Packaged D3D12 application, automated interaction and visual inspection | Pending |
| Human live operation/visual approval | Human review | Not executed |
| Required build-test and unchanged auxiliary workflows | PR checks at the published head | Pending |

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
same existing `CaptureConfiguration` path as Save. This Game persistence correction
requires the integrated replay/hash case to be repeated; no schema, assertion or
golden is changed. An in-progress Public solution build was cancelled for this
source correction and supplies no completed build evidence.

The initial hardware inventory query was denied access to the Windows CIM
resource; this provides no GPU or D3D12 capability evidence.

The replay correction passed both integrated cases (2/2, no skips). The Public
selection then passed 2/3 and reproduced acceptance of a bare DeveloperModified
marker. The shared persistence guard now includes that marker and explicit
initial-profile/audit presence in its Developer-data checks. A marker without
corresponding state is also rejected as corrupt in an approved Developer host.
The affected Public rejection and Developer recovery cases must be repeated.
The first complete solution build stopped on missing cancellation tokens in the
new Public tests; these were corrected without changing assertions.

The integrated preset PR baseline at `49463f88` failed its required build-test
at `TechnologyNormalMatchTests.NormalOpponentPaysForResearchAndReconstructsWhileProductionContinues`,
line 49 (56/57 selected cases passed). The log reports MissingMaterials for the
third technology. Its four auxiliary workflows passed; native-reference was
skipped by workflow policy. This pre-existing research blocker is independent
of the developer composition and is not waived.
