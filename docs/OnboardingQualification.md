# Logistics guidance qualification

The detailed opening guide extends the integrated collapsible interaction and
completed logistics controls. The current PR remains subject to the complete
required CI, the natural terminal-match gate and human visual/live-interaction
acceptance. No merge, tag, release or approved pixel golden is implied.

## Evidence contract

GameplayProgressJournal is owned by MatchRuntime. Actual Cargo load/unload and
Battlefield Supply transfers record positive quantities after successful
inventory operations. Actual unit production records Scout and Supply Truck
completion. The existing extraction sink supplies committed extraction results.
Conveyor evidence is recorded only after an actual positive transfer into an owned
building input port, not during construction or between belt buffers. Completed-tick
observation records committed processing output deltas (including Steel), accepted installed
Ore policies, a first observed blockage and blocked work returning to running work through the existing
work summary. The journal never reserves stock or changes world orders.

Each copied event carries session, player, monotonic event identity, completed
tick, full-generation actor/source/target identities, order/visit identity,
resource and actual amount. Configuration observations count accepted rules;
production observations carry committed output or unit counts. They are not
transport quantities. Ownership and live entity generations are checked
before recording. A completed mobile load requires Fuel and Ammunition from the
same source/truck/visit; two separate visits cannot be combined. Propulsion
refueling is not depot cargo loading. Field supply evidence requires an actual
mobile Supply Truck transfer to an owned recipient.

Research records the owned facility and technology only after paid research actually
completes. Starting stock, a request or consumed materials alone are not completion.

Retention is bounded: at most 16 player ledgers, 64 distinct kind/resource/
definition observations per player and 64 partial loading visits. Each player also
retains at most 64 first observations under the same keys for timing. This first
ledger is independent of later publication reads and latest-event replacement. Completed
observations are condensed independently of presentation reads. Current owned
production/policy bookkeeping is cleaned up when entities disappear. Repeated
snapshot consumption does not increment achievements. Immutable retained copies
remain unchanged when later transfers occur. Historic evidence proves its
recorded action and entity generations; it does not describe a replacement
entity's current stock. Session replacement rejects old event identities.

## Save/load boundary

Envelope 1, payload schema 6 and authoritative schema 7 are unchanged at this
source. The observational additions require no authoritative compatibility change. The existing
transactional recovery reconstructs the runtime from its complete supported
command/tick history. Real extraction/transfer/production observations are
rebuilt by those same operations and rebound to the new session. Stock snapshots
alone never reconstruct a delivery. Reading state is transient and starts Offered
in the restored session. Unsupported or absent history is not invented. Existing
persistence compatibility and checksum validation remain required.

## Executable opening journey

StandardOpeningRecordsRealProductionDepotLoadingAndMobileResupply starts with
the normal Gameplay stock, Core, Engineer and two Cargo Trucks; both participant
slots are human-controlled to isolate the opening mechanics. No needed building,
Scout, Supply Truck or input inventory is inserted beforehand. It builds power,
an Extractor on an existing valid Ore deposit, a Smelter, Vehicle Factory and
Supply Depot through the player gateway. Real policies and Cargo supply the
Smelter; actual Steel cycles supply vehicle production. A deliberately queued
empty factory observes NO INPUT, then returns to running work after real input
delivery. Scout and Supply Truck are produced through normal queues.

Policies for two planned vehicles use their active catalog costs. After the first
vehicle, the player lowers MIN to zero for the finite opening batch; this prevents
unnecessary subsequent factory replenishment while stocking the depot. Cargo
physically delivers depot Fuel/Ammunition. Normal movement and REQUEST SUPPLY
bring the produced Supply Truck to the depot, load both cargo resources during
one real visit and later physically refuel the owned Scout in the field. The
test checks the recorded sources, targets and positive transfer evidence.

ActualExtractionEvidenceSurvivesSkippedReadsAndSaveReplayWithNewSession checks
missed reads, immutable retained copies, faction isolation and replay-backed
recovery from real player construction/extraction commands. Separate identity
and visit tests reject foreign targets/actors, stale generations, uncompleted
publication and split-visit loading. Guidance tests keep reading, deferral and
world achievement separate and verify complete text through supported display,
DPI and UI-scale combinations, active rebindings and new sessions.

## Native and package qualification

`--onboarding-lessons-capture <empty-output> <runtime-root>` uses the production
HUD and font metrics to capture every page of SUPPLY YOUR SMELTER, UNDERSTAND YOUR
TWO TRUCK ROLES, LOAD AND DELIVER FIELD SUPPLY, CONNECT BASE PRODUCTION and
CHOOSE A PAID RESEARCH INVESTMENT at 1080p and compact 720p with
DPI 192/UI scale 2. Each page has two exact native readbacks, source identity,
full body text, font size, geometry, glyph/skin diagnostics and image hashes.
`--onboarding-capture` retains the production interaction sequence at
30/60/144-Hz injected sampling, normal/reduced motion and exact transition times.
These rates are test inputs, not measured performance or physical-input approval.

The final source's Release build, full tests, reproducible package, startup,
source/package native parity and hosted checks must be recorded in the PR and
local qualification report. Earlier targeted results are checkpoints, not final
acceptance. Full native matrix and human physical-display, live-motion,
keyboard/pointer and accessibility review remain distinct gates. Thresholds,
image tolerance, collision and terminal-match assertions are unchanged.

[Historical collapsible-interaction qualification](https://github.com/DaveBeusing/Forgeline/blob/eee4b603d9443b244ac7dc331577ea28b96440d0/docs/OnboardingQualification.md)
records the earlier source and attempts; its old PR-state statements do not
describe the current integration status.

## Normal Gameplay measurements and limits

[Machine-readable measurements](GameplayPacingMeasurements.json) retain source
identity, profile, roles, seed, tick budget, achieved and unreached outcomes, the
failed probe and separate native/package evidence. No Gameplay numeric value,
starting stock, cost, unit capability or opponent threshold is changed.

The command-driven Standard-stock opening uses seed 4133 with two human slots to
isolate mechanics. It produces and loads its vehicles through real commands and
inventories. The supported Scout and loaded Supply Truck share an ordinary
movement group toward the public opposing start. A repeated clean build at
13a9bd28 produces the same tick 14,013 and authoritative hash
44D270EE2D6C4364CB144598206884DC05DEB56D0A8C19499C6259CD598FC8A5.
This fixture contains no strategic opponent pressure and does not finish a match.

| First observed action | Logical tick |
| --- | ---: |
| Actual extraction | 101 |
| Owned blocked work | 209 |
| Blocked work returning to running | 3,036 |
| Committed processing output | 3,075 |
| Scout production | 7,319 |
| Supply Truck production | 7,438 |
| Real depot loading visit | 8,047 |
| Mobile field supply | 8,661 |
| Authorized enemy contact | 14,013 |

An earlier Scout-only movement probe did not reach contact within its 8,000-tick
movement budget. It remains a failed probe; its exact cause was not isolated.
The successful supported movement uses different ordinary commands, not a
balance change or free supplies. Belts, research, strategic expansion, damage and
regular victory are not claimed from this truck-based opening. Paid conveyor
handoff/production and normal-profile paid research Save/Replay have separate
executable regression cases. Guidance reading never participates in those Game
fixtures. Presentation tests separately cover disabled/suppressed help, deferral,
out-of-order completion, stale evidence and fresh-session reset.

Three computer-vs-computer Gameplay/Standard-stock seeds 17–19 at source 13a9bd28
remain active after 12,000 ticks without contact or damage. Their outcomes match;
this small seed set provides no win-rate or final balance claim. An extended
Gameplay seed-17 observation at 3e787953 remains active at 80,000 ticks with no
damage: player 2 first receives mobile field supply at 15,753 and authorized
contact at 40,589; player 1 reaches neither. Long transport and supply waits are
observed, but a numeric balance cause has not been isolated. Normal Gameplay
regular completion and the combined assessment after research extension
[PR #350](https://github.com/DaveBeusing/Forgeline/pull/350) remain open.

The unchanged Validation/seed-2026 terminal gate at 3e787953 passes naturally at
50,862 ticks via CommandCoreDestroyed, player 1, zero route failures. It uses
Validation stock/parameters and is separate evidence.

The reproducible local Windows package at 0349a64e passes its fresh-settings
D3D12 session smoke and two menu launches after warmup, including application
icons. Its five logistics/research lessons produce 29 page captures at 720p and
1080p with two exact repeats each, no missing/dropped content or submission
faults, on the recorded physical adapter. The first compact belt/research pages
were also inspected for wrapping and overlap. These authored fixtures do not
prove a complete live human journey or reference-hardware performance. Source
13a9bd28 separately passes the canonical WARP combat reference. Source identities
are preserved rather than relabeling earlier binaries as later-head results.

The unchanged window qualification guard at line 242 reproduces
[issue #344](https://github.com/DaveBeusing/Forgeline/issues/344): 68,761,923
gameplay texture bytes exceed the 58,720,256-byte limit. No assets or budget were
changed. Complete solution and actual final-head CI outcomes are recorded in
[PR #352](https://github.com/DaveBeusing/Forgeline/pull/352); compilation, focused
tests and captures do not substitute for those gates.

## Reproduction

Follow the complete canonical sequence in [Development](Development.md), including
runtime-cache compilation before asset/native checks. After the Release build:

~~~powershell
dotnet test --project tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Game.Tests.GameplayPacingProgressTests ForgeLine.Game.Tests.GameplayProgressJournalTests ForgeLine.Game.Tests.GameplayTelemetryTests ForgeLine.Game.Tests.PlayerConstructionProductionFlowTests ForgeLine.Game.Tests.ConveyorPersistenceTests --parallel none
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.EarlyGameGuidanceTests ForgeLine.Presentation.Tests.GuidanceLessonTests --parallel none
dotnet test --project tests/ForgeLine.Client.Tests/ForgeLine.Client.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Client.Tests.GameplayGuidanceInteractionTests --parallel none
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --scenario central-divide --profile gameplay --ticks 12000 --seed 17 --matches 3 --telemetry-output artifacts/gameplay-pacing.json
~~~

Set FORGELINE_GAMEPLAY_JOURNEY_REPORT to an existing artifact directory plus a new
JSON filename before the command-driven opening test to retain the actual source,
roles, tick/hash, first-action evidence and telemetry. No setting changes its
commands, assertions, costs or outcomes. The extended observation uses Gameplay,
seed 17, 80,000 ticks and both telemetry/diagnostics outputs. Its missing terminal
result stays visible.

## Manual Windows package journey — open

Use the qualified package with a new isolated settings directory. Record its
build-provenance identity, Gameplay profile, seed, roles and logical ticks.

1. Start Central Divide normally, minimize or disable guidance, and inspect the
   Core, actual local materials and the two starting Cargo Trucks.
2. Build powered extraction and processing in a different valid order. Connect
   compatible paid belt ports, keep loading approaches clear, and queue a recipe
   through PROCESS. Check real building delivery and committed output.
3. Use LOGISTICS / INPUTS / USE INPUTS / APPLY POLICY for a Scout and Supply Truck.
   Produce them from actual local stock; distinguish cargo Fuel from drive Fuel.
4. Build and physically replenish a depot. Load the Supply Truck through SUPPLY
   and REQUEST SUPPLY, then observe a real field transfer. Move the supported
   Scout through a known route and record the first authorized contact.
5. Inspect TECHNOLOGY costs, prerequisites and facility/power state. Choose an
   affordable investment while retaining transport and defense capacity; wait
   for real completion rather than accepting a queued request as success.
6. React to observed raids with escorts/rerouting. For a real interruption, use
   Operations, TRACE NEXT, FOCUS FACILITY and OPEN EXISTING CONTROLS to repair the
   observed cause. Record interruption/recovery independently of read cards.
7. Save during active logistics, load into a new session, and verify that already
   achieved gameplay does not need repeating. Reopen deferred cards in another
   order and verify that their reading state remains independent.
8. Expand with paid materials, preserve/rebuild lost transport capacity, sustain
   the front and finish through the real Command Core victory rule. Keep any
   unreached or failed case with its save/replay and tick budget.

This full live package journey, physical input/readability/accessibility review,
normal-profile regular completion and the combined research/raid assessment are
not completed by the automated evidence above.
