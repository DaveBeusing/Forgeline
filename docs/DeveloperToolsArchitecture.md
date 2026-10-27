# Developer tools foundation

## Scope and integration

The foundation starts at master 20a6e0f6cbf4dbfadf5f2143a7a782b0db3b2c26
(0.1.478-dev), after research, runtime assets, navigation and diagnostic integration.
It is synchronized with master864b8977b4d9addd85d837b401188f7da89432e4,
including the subsequent conveyor-demolition integration.
The integrated product now includes the operational menu, Game-owned dispatcher,
session controls, parameter transport and preset file I/O through PR #379
(merge 89086e68cc72798544b8b75d81c7d36d6bd13811). ClientApplication composes
ClientDeveloperMenuBackend by default; ClientSimulationHost retains the sole
simulation owner and bounded transport. See [Developer tools menu](DeveloperToolsMenu.md)
and [Developer presets](DeveloperPresets.md) for operation.
Official binaries are the access-control boundary: someone who changes and
rebuilds these public sources controls their own host.

## Build and capability

ForgeLineEnableDeveloperTools defaults to false, independently of Debug/Release
and the version channel. Only true compiles FORGELINE_DEVELOPER_TOOLS. Invalid
values, contradictory variant metadata and an injected developer compilation
symbol with the property disabled fail the build. DeveloperTools/Implementation
sources are excluded from the public compilation in every project.

Public output remains bin/<configuration>/... and obj/... so existing CI and
qualification scripts retain their paths. Developer output is bin/Developer/
<configuration>/... and obj/Developer/... (including NuGet project assets).
Both roots are excluded from source globbing. Use the same property for restore,
build and consumers; do not supply shared OutDir, OutputPath or intermediate-path
overrides between variants. Custom rewritten build inputs are outside the
official-binary promise. BuildVariant and DeveloperToolsEnabled assembly metadata
identify every project; the client display and crash report include the variant.

Product compiler entry points:

~~~powershell
dotnet build src/ForgeLine.Client/ForgeLine.Client.csproj -c Release -p:ForgeLineEnableDeveloperTools=false
dotnet build src/ForgeLine.Client/ForgeLine.Client.csproj -c Release -p:ForgeLineEnableDeveloperTools=true
~~~

DeveloperToolsCapability.Current is immutable and derived from compilation, with
no public constructor. BindSession creates ephemeral DeveloperSessionAccess for
one specified SimulationSessionId. Activation cannot create absent capability.
New/restored sessions need fresh binding; settings, environment at runtime,
versions, presets, saves, replays, EnableDiagnostics and EnableDebugCapture cannot
mint this authority. No developer permission is added to persisted settings.

The public Publish-PreAlpha.ps1 rejects an enabled/contradictory developer property,
passes -p:ForgeLineEnableDeveloperTools=false explicitly and inspects actual
ForgeLine assembly metadata with Assert-PublicBuild.ps1 before packaging.
Provenance records buildVariant=Public and developerToolsEnabled=false. There is
no internal artifact publication entry point or automatic distribution here.
Full produced-package exclusion qualification belongs to the combined acceptance.

## Existing command-line entry points

Program's --validate-scenarios creates isolated scenario/recovery probes.
The reference, action-ring, onboarding, feedback, command-dock, HUD, cinematic,
frontend, skin, typography and navigation capture/timing entries own standalone
qualification scenes. Their fixture setup does not attach to an existing player
session. --author-menu-background constructs and exports presentation artwork,
not authoritative entities. Normal scenario/smoke/render-stress launch options
remain existing host/qualification entry points. None is a live-game developer
mutation endpoint. These entries remain public; their presence and the observational
--developer-navigation-diagnostics flag do not confer DeveloperSessionAccess.
Future live-game mutating entry points must use the same compiled capability and
execution-time request gate as menu actions.

## Assembly direction and composition

All neutral contracts live in ForgeLine.Game/DeveloperTools and use existing
Core identities, Game ownership and Simulation tick/session types. They add no UI,
rendering, platform or filesystem dependency to simulation. No project is added.
DeveloperToolsComposition.Create is the only initial composition hook and is
owned by ClientApplication through its DeveloperTools property. In a developer
build it enters CompiledDeveloperToolsHost; that type is absent publicly.

DeveloperToolsProviders contains IDeveloperMenuCatalog,
IDeveloperSessionController, IDeveloperActionDispatcher,
IDeveloperParameterProvider and IDeveloperPresetService. They remain connection
points for client composition. `MatchRuntimeServices` separately registers the
actual action gateway and gameplay parameter provider in Developer builds; the
session owner must bind/revoke its live access on the simulation thread and
transport client requests through that boundary. The gateway exposes correlated
results through `CaptureResults`, and its five domain actions use real runtime
effects. Public composition still rejects developer provider injection. ClientDeveloperMenuBackend connects those providers to the live menu and delegates
session controls to ClientSimulationHost and file operations to
ClientDeveloperPresetSessionAdapter. Runtime requests cross the host queue before
the simulation owner invokes the Game dispatcher. The original module ownership
remains in place. See [Developer simulation actions](DeveloperSimulationActions.md) for
operation contracts, persistence schema 8 and integration handoff.

## Requests, results and revision domains

DeveloperRequest is the one request envelope: current expected session, nonempty
Guid correlation, expected DeveloperStateRevision, DeveloperOperation,
DeveloperTarget, optional explicit parameter ID and at most16 copied typed values.
Targets are exactly Global, Player or full EntityId including generation. Arrays
are defensively copied. There is no arbitrary object path or executable payload.

DeveloperRequestGate.Check must run before enqueue and again at the actual command
boundary using newly captured DeveloperExecutionContext facts. It checks compiled
capability, activation, session, relevant revision, registered operation and live
target. Recycled entity indices fail full-identity liveness. A provider must also
check operation-specific ownership, domain invariants and cancellation before any
write. A queued admission result is never execution permission.

Parameter edits/resets use Parameters revisions; all parameter providers share
one monotonically advancing parameter revision. Presentation parameters, when
registered later, use Presentation revisions. Pause/resume/step/host-speed and
next-session/profile actions use Session revisions. Spawn/resource actions use
Simulation revisions. A domain dispatcher owns the relevant action revision and
must advance it after a successful domain write. An unrelated UI publication
revision is never substituted. Reserved operation enum members are not available
handlers; registering one requires its typed payload and owner validation.

DeveloperResult separates Accepted, Pending, Applied and Rejected. Only Applied
has AppliedTick and copied actual affected values. DeveloperAppliedValue identifies
parameter values, resource quantities, spawned full entity identities or session
outcomes with matching explicit value types. Rejected requires a stable
non-None DeveloperFailureReason; an unregistered operation returns
UnsupportedOperation and an unknown parameter returns UnsupportedParameter.
Applied carries the resulting revision, not merely the requested revision. No
consumer may infer success from enqueue, UI focus or an observed snapshot alone.

## Parameter registry

DeveloperValue supports finite bool/int/uint/double through explicit factories and
matching conversions only. Kind names Boolean/SignedNumber/UnsignedNumber/
RealNumber correspond to those CLR types. A default/unspecified value is invalid.
Descriptors contain stable ID/key, category, unit, default, inclusive minimum and
maximum, suggested UI step, allowed target and apply mode. Steps describe editor
increments, not a license to round values during application. IDs/keys are explicit
registrations; tick systems consume prepared fields, not string lookups per entity.

All opponent/raid rows are Player-targeted NextMatch. Distribution rows are
Global NextMatch. Only conveyor.throughput_multiplier is Global NextTick;
it defaults to1, allows0.25..4, step0.05. There are no NextCycle/NewEntity modes.
Registry defaults describe ordinary Gameplay policy; a captured scenario's actual
initial values take precedence over these defaults (Validation uses other stock
and distribution policy). NextMatch edits never mutate a running match.

| Stable key | CLR type | Default | Inclusive range / step |
| --- | --- | --- | --- |
| opponent.aggression | double |0.65|0..1 /0.01|
| opponent.expansion_readiness | double |0.55|0..1 /0.01|
| opponent.offensive_readiness | double |0.68|0..1 /0.01|
| opponent.retreat_threshold | double |0.28|0..1 /0.01|
| opponent.resupply_threshold | double |0.30|0..1 /0.01|
| opponent.offensive_fuel_threshold | double |0.55|0..1 /0.01|
| opponent.reaction_cadence_ticks | uint |20|1..uint.MaxValue /1|
| opponent.raids.enabled | bool |true|false..true|
| opponent.raids.minimum_units | int |2|2..3 /1|
| opponent.raids.maximum_units | int |3|2..3 /1|
| opponent.raids.home_defenders | int |2|1..int.MaxValue /1|
| opponent.raids.maximum_contacts | int |32|1..32 /1|
| opponent.raids.active_ticks | uint |1200|1..1200 /1|
| opponent.raids.cooldown_ticks | uint |600|1..uint.MaxValue /1|
| opponent.raids.search_ticks | uint |80|1..uint.MaxValue /1|
| opponent.raids.readiness_threshold | double |0.65|0..1 /0.01|
| opponent.raids.fuel_margin | double |1.25|1..double.MaxValue /0.05|
| opponent.raids.fuel_reserve_fraction | double |0.15|0..1 /0.01|
| opponent.raids.minimum_shots | int |6|3..int.MaxValue /1|
| opponent.raids.maximum_known_threat | int |1|0..int.MaxValue /1|
| opponent.raids.home_radius_metres | double |260|float.Epsilon..float.MaxValue /1|
| opponent.raids.maximum_distance_metres | double |1000|float.Epsilon..float.MaxValue /1|
| opponent.raids.pursuit_leash_metres | double |120|float.Epsilon..float.MaxValue /1|
| opponent.raids.threat_radius_metres | double |160|float.Epsilon..float.MaxValue /1|
| distribution.retry_delay_ticks | uint |20|1..uint.MaxValue /1|
| distribution.maximum_transport_attempts | uint |4|1..uint.MaxValue /1|
| distribution.fairness_aging_ticks | uint |200|1..uint.MaxValue /1|

Float-backed ranges convert explicitly once when preparing domain configuration;
the provider must reject non-finite or out-of-range converted values. The existing
ulong-backed distribution timing fields use the explicit uint editor subset and
widen losslessly. Paired fields retain full SkirmishOpponentConfiguration.Validate
and SkirmishRaidConfiguration.Validate constraints: for example retreat<offensive
readiness, offensive fuel>=resupply, and minimum raid units<=maximum raid units.
Descriptor membership/range checks never replace complete domain validation.
Reset means the captured initial profile value, not necessarily the descriptor's
ordinary Gameplay default; preset/controller owners must retain that profile.

## Initial state, history and presets

DeveloperInitialProfile stores at most128 copied unique (ID,target) initial values
at parameter revision0 and immutable DeveloperPresetMetadata. Schema1 permits
only the registered Central Divide/Split Basin canonical identities and Gameplay/
Validation profiles with exactly two distinct players and canonical starts0/1.
Seed, faction, player and computer-control fields remain typed data. No filename,
object path, type name, script or arbitrary command is executable from metadata.
RequiresDeveloperTools declares a requirement and grants nothing. The preset
codec resolves the registered scenario and validates its identity and parameter
profile against the resolved composition before building a start plan. See
[Developer presets and reproduction](DeveloperPresets.md) for file and session
integration boundaries.

DeveloperParameterChange describes a real applied update/reset with correlation,
ordered sequence, actual tick, before/after values and advancing parameter revision.
DeveloperModificationState.AfterApplied retains a sticky DeveloperModified flag
once an actual authoritative value changed. A reset is another ordered event; it
cannot delete earlier changes or clear that flag. Presentation changes and host
speed do not mark authoritative state. Initial non-default authoritative values
must be recorded by the runtime owner as applied initialization before simulation.
The neutral foundation defines data/projection contracts. The integrated Game
runtime supplies live ordered history, replay records, authoritative hash
contributions and save data through the existing persistence owner.

## File ownership handoff

| Owner | Owned integration |
| --- | --- |
| Build foundation | Directory.Build.props/targets, public publisher/metadata guard, these neutral contracts |
| Simulation actions | MatchRuntime composition, dispatcher registration, action revisions, replay/save contracts, authoritative history/hash |
| Session controls | ClientSimulationHost, activation lifecycle, pause/step/host pacing |
| Menu and diagnostics | ClientApplication/input routing, catalog projection, copied read models |
| Gameplay tuning | Explicit parameter providers, prepared typed fields, conveyor-throughput consumer |
| Presets | Schema parsing/validation and data service after parameter/persistence providers exist |

Change shared contracts in the integration branch before consumers adopt them.
Do not duplicate adapters, parsers or dispatcher authority in parallel consumers.

## Current integration and acceptance

The live provider paths are integrated. See [Developer tools qualification](DeveloperToolsQualification.md)
for the combined source-specific evidence and unresolved overall qualification.
Manual user approval remains outstanding. Documentation reconciliation changes
no commands, authoritative state, preset or Save/Replay schemas.

## Historical foundation acceptance

Implementation used affected-product compiler builds only, with no test files,
test/smoke runs or benchmarks before the explicit acceptance boundary.
Scoped acceptance completed and was repeated on the synchronized source after
correcting descriptor count units/categories:

- Canonical solution restore and unchanged project-reference validation succeeded.
- Complete Release solution build: zero warnings/errors.
- Public selection:29/29 Game cases (19 foundation,5 raid-configuration,5 newly
  integrated conveyor-demolition),1/1 newly integrated conveyor Save/Replay case
  and2/2 existing ClientBuildIdentity cases passed, no skips.
- Developer compilation:19/19 foundation cases passed from isolated Developer
  outputs, including active permission and stale execution-time rejection.
- Publisher contracts:4/4 passed (public assembly metadata, developer-output
  rejection, explicit-enabled rejection, contradictory-environment rejection).
  These metadata/early-rejection checks generated no package.
- Exactly one PATCH:0.1.478 to0.1.479. No existing test assertion, cost, deadline,
  version golden or required gate was changed.

Acceptance-only coverage and final documentation are committed separately from
the implementation. The PR records the exact published head and its required CI.
Common end-to-end, full variant/package, Save/Replay and native interaction
qualification remain separate. A foundation compiler pass is not tool/UI delivery.
