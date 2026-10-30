# Developer gameplay tuning

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

<details>
<summary>On this page</summary>

- [Scope and source](#scope-and-source)
- [Editor schema](#editor-schema)
- [State and execution handoff](#state-and-execution-handoff)
- [Physical conveyor hook](#physical-conveyor-hook)
- [Restart builder](#restart-builder)
- [Qualification boundary](#qualification-boundary)

</details>

## Scope and source

Starts from master 9dd75d9a4133f576e3fe1a2838584e3fa98654a1 with the integrated
developer foundation, research and logistics changes. It is synchronized with
master 715b513eab83374d98b5ebe8aa1971af4f5e7d48; the PR version is 0.1.481-dev. The editor subset implements
13 existing Foundation IDs; it neither expands the shared registry nor defines a
second request protocol. All implementation files reside below
DeveloperTools/Implementation/GameplayTuning and are excluded publicly.
The only existing gameplay implementation changed here is ConveyorSimulationSystem.
MatchRuntime, MatchRuntimeServices, persistence/replay/hash and main UI remain
owned by their integration owners and are unchanged by this work.

## Editor schema

DeveloperGameplayTuningSchema.Entries provides English labels and narrowed
DeveloperParameterDescriptor contracts. Use this executable subset rather than
assuming every shared Foundation metadata entry already has a handler. Descriptor
defaults describe ordinary policy; actual Basis/Initial values come from the selected
Gameplay or Validation records. Display those observations instead of substituting
metadata defaults. Fraction step0.01 is an editor increment; conveyor quarter steps
are mandatory. Unsigned fields require explicit uint values; no coercion from double.

| Key | Label | Type | Range / step | Scope | Mode |
| --- | --- | --- | --- | --- | --- |
| conveyor.throughput_multiplier | Conveyor throughput |double|0.25..4 /0.25|Global|NextTick|
| opponent.aggression | Aggression |double|0..1 /0.01|Computer Player|NextMatch|
| opponent.expansion_readiness | Expansion readiness |double|0..1 /0.01|Computer Player|NextMatch|
| opponent.offensive_readiness | Offensive readiness |double|0..1 /0.01|Computer Player|NextMatch|
| opponent.offensive_fuel_threshold | Offensive fuel reserve |double|0..1 /0.01|Computer Player|NextMatch|
| opponent.retreat_threshold | Retreat threshold |double|0..1 /0.01|Computer Player|NextMatch|
| opponent.resupply_threshold | Resupply threshold |double|0..1 /0.01|Computer Player|NextMatch|
| opponent.reaction_cadence_ticks | Reaction cadence |uint|1..1200 /1|Computer Player|NextMatch|
| opponent.raids.enabled | Raids enabled |bool|false/true|Computer Player|NextMatch|
| opponent.raids.cooldown_ticks | Raid cooldown |uint|1..12000 /1|Computer Player|NextMatch|
| distribution.retry_delay_ticks | Distribution retry delay |uint|1..1200 /1|Global|NextMatch|
| distribution.maximum_transport_attempts | Transport attempts |uint|1..32 /1|Global|NextMatch|
| distribution.fairness_aging_ticks | Distribution fairness aging |uint|1..12000 /1|Global|NextMatch|

Units remain the Foundation units (ratio, multiplier, ticks, boolean or attempts).
Entity scopes, human/nonconfigured players, unknown/unsupported IDs, nonfinite or
mismatched value types and non-quarter conveyor values reject. The copied complete
opponent/raid/domain configuration is validated after all profile fields have been
prepared: existing retreat<offensive-readiness and offensive-fuel>=resupply checks
remain. No new readiness/fuel/attack relationship is invented. Unchanged valid
policy fields are copied even when outside these narrower editor ranges; resetting
restores the captured initial value, not an ordinary metadata default. Native ulong
distribution durations widen edited uint values losslessly; untouched durations are
never clamped. The Foundation uint observation format cannot represent a native
duration above uint.MaxValue and fails explicitly rather than truncating it.

## State and execution handoff

DeveloperGameplayTuningProvider accepts a SimulationCoordinator, validated
DeveloperRestartPlan and optional IDeveloperActionDispatcher. For an existing
unmodified match, use DeveloperRestartBuilder.ForExisting(runtime.RuntimeSettings).
For a new modified start, first build a plan, construct a candidate runtime from
plan.Settings, then construct the provider from that actual runtime's Simulation
and the same plan before the first tick. Nonempty initial overrides cannot be
attached retroactively. The host owns validation that the candidate construction
succeeded and the transactional swap from the old session.

The central owner must perform these registrations; they are deliberately not
written into its runtime/services files here:

~~~csharp
var tuning = new DeveloperGameplayTuningProvider(runtime.Simulation, plan, dispatcher);
runtime.Services.ConveyorFlow.BindDeveloperTuning(tuning);
// Register tuning as the parameter-provider slot in DeveloperToolsProviders.
// Dispatch SetParameter/ResetParameter/ResetParameters to tuning.ApplyAtTick.
~~~

Submit uses the existing Foundation gate and routes to the supplied central
dispatcher. Without that integration, Status is Unavailable/ProviderUnavailable;
Submit cannot fake admission/application. Descriptors remain inspectable.
CaptureContribution supplies copied actual data for diagnostic/integration use;
Capture returns Foundation effective observations only when dispatcher integration
is available. Menu consumers need both the common provider and the typed copied
contribution to show Basis, Initial, Effective and Pending separately.

At actual central command execution, call:

~~~csharp
DeveloperTuningApplication outcome = tuning.ApplyAtTick(context, currentAccess, request);
// Publish outcome.Result. Append outcome.Changes in global command order.
// Encode/hash outcome.Contribution through the central state-contributor adapter.
// An outcome.IsReplay result has no new changes and must not create another event.
~~~

The handler recaptures its own session/revision, checks current access and the
actual registry, InputCommands phase and current fixed tick. The conveyor's last
observation fences control calls after that tick already completed. There must be
one bound conveyor owner; wrong-runtime reads fail before transfer. It does not
own a second queue, command type, serializer, replay stream or runtime factory.
All validation and complete candidate policy checks precede one state replacement.
Rejected requests retain the exact prior state/revision and return no changes.

SetParameter has one matching typed value. ResetParameter has none. ResetParameters
has no parameter ID or values and resets one Global or Computer Player scope; this
keeps the batch under the shared16-value bound. Reset restores captured Initial,
including a nondefault initial override, and clears corresponding pending restarts.
It is an explicit new ordered change, not deletion of earlier events.

A successful transaction advances the single Parameters revision. NextTick changes
carry AppliedTick and actual changed values. NextMatch values return Pending after
the staging transaction, retain Effective and affect no running opponent or
distribution system. Clearing pending values is an applied staging reset with no
claimed effective parameter change. A Global reset may clear pending distribution
while actually resetting the live conveyor field. The observation's Pending and
ApplyMode remain authoritative for this distinction.

The bounded128-entry correlation cache stores successful applications. Equivalent
retries return the original result without another mutation or revision advance;
different payloads under the same correlation reject. Replayed execution returns
current contribution, no new changes and IsReplay=true. Evicted old requests still
face stale-revision rejection. The central dispatcher must preserve this same
idempotence ordering before global history writes.

DeveloperTuningContribution keeps the immutable original override profile, copied
Basis/Initial/Effective/Pending rows, revision, recorded tick and prepared conveyor
state. It is bounded to the Foundation128-parameter limit. DeveloperModified is
sticky after a real live change or a modified successfully constructed initial
configuration; pending-only policy changes do not modify this active match.
The central owner combines this with other action modification markers, retains
all ordered events, derives save requirements from actual state/history rather
than only the initial metadata flag, and owns the authoritative hash/codec.

## Physical conveyor hook

BindDeveloperTuning and its source reference are compiled only with the developer
property. The system captures one prepared double multiplier per Execute. The same
effective nominal throughput (100 times multiplier) feeds module budgets, building
port budgets and the existing bounded round calculation. Power, health, broken
support, tick duration, inventory availability/capacity, held cargo, target limits,
positive fractions, stable cursor ordering and truck arbitration are unchanged.
The100000-round hard ceiling remains. Default1 uses the original calculation.
No descriptor construction, reflection or string lookup occurs per entity.

EffectiveThroughput is the effective nominal budget cap, not measured cargo moved.
TuningRevision/TuningAppliedTick identify its applied source. Existing per-node
flow observations retain actual transfer rates and power/damage/backpressure
causes. Normal public/unbound execution remains100, revision0, applied tick0.

## Restart builder

DeveloperRestartBuilder.TryBuild(basis, metadata, overrides, out plan, out failure)
accepts bounded typed data, validates every explicit override and then the complete
copied records atomically. Metadata must retain the selected canonical composition,
identity and profile; choose the appropriate Gameplay/Validation basis first.
Seed/participants come from the validated session/preset metadata. All other scenario
fields and catalog/map hashes remain unchanged. Participant, opponent and nested
raid collections are defensively copied. Duplicate keys or any invalid field
produce no plan and never partially update the input. No runtime is created.

TryCreateRestartPlan combines the provider's effective/pending intent relative to
its retained basis and delegates to this same builder. New player scopes must still
be valid in the requested metadata; no stale/human override is silently dropped.
A pending value becomes effective only after the session owner actually constructs
a new runtime and installs its initial profile. A failed build leaves the old
runtime untouched. Catalog, recipe, cost, output, research, unit-instance,
navigation, tick-duration and buffer editors remain separate future features.

## Qualification boundary

Implementation used only affected Game compiler paths, with no new tests, test
runs, smokes or benchmarks before the explicit scoped-acceptance boundary.
Production source8c995045 then passed the canonical restore/reference checks,
a complete Release solution build (zero warnings/errors,51.59s),4/4 unchanged
public flow cases and26/26 developer cases (21 tuning cases plus5 existing raid
configuration cases), no skips. The developer test build also has zero warnings/
errors. The4x fixture at10Hz requires40 transfer rounds; the old32-round bound
cannot satisfy it. Initial reset/retry, inactive/stale/invalid scope, atomic invalid
profiles and pending-only runtime isolation are included. No existing assertion,
price, buffer, golden, deadline or gate was weakened. These targeted cases do not
claim a full production-chain, save/replay, public-package or native campaign.
After synchronization with master 715b513e, the complete Release solution build
passed again (zero warnings/errors, 26.82s), along with the same 4/4 public
and 26/26 developer cases. A final observation correction reads provider
availability once and avoids a live ECS callback for unsupported entity scopes;
the affected developer build and 11/11 targeted cases passed afterward.
The PR records the exact published source and required CI. Central persistence/replay/hash integration,
menu-driven production chains, full public/developer package exclusion and native
interaction belong to the combined developer-tools qualification work.
