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
Completed-tick observation records real Steel output deltas, accepted installed
Ore policies and blocked work returning to running work through the existing
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

Retention is bounded: at most 16 player ledgers, 64 distinct kind/resource/
definition observations per player and 64 partial loading visits. Completed
observations are condensed independently of presentation reads. Current owned
production/policy bookkeeping is cleaned up when entities disappear. Repeated
snapshot consumption does not increment achievements. Immutable retained copies
remain unchanged when later transfers occur. Historic evidence proves its
recorded action and entity generations; it does not describe a replacement
entity's current stock. Session replacement rejects old event identities.

## Save/load boundary

Save schema 4 and authoritative replay schema 3 are unchanged. The existing
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
TWO TRUCK ROLES and LOAD AND DELIVER FIELD SUPPLY at 1080p and compact 720p with
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
