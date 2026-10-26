# Developer tools foundation

## Scope and integration

The foundation starts at master20a6e0f6cbf4dbfadf5f2143a7a782b0db3b2c26
(0.1.478-dev), after research, runtime assets, navigation and diagnostic integration.
It adds build capability and contracts, not an operational menu, spawn handler,
parameter editor, preset importer or new persistence format. Public diagnostics
remain available. Official binaries are the access-control boundary: someone who
changes and rebuilds these public sources controls their own host.

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

## Assembly direction and composition

All neutral contracts live in ForgeLine.Game/DeveloperTools and use existing
Core identities, Game ownership and Simulation tick/session types. They add no UI,
rendering, platform or filesystem dependency to simulation. No project is added.
DeveloperToolsComposition.Create is the only initial composition hook and is
owned by ClientApplication through its DeveloperTools property. In a developer
build it enters CompiledDeveloperToolsHost; that type is absent publicly.

DeveloperToolsProviders contains IDeveloperMenuCatalog,
IDeveloperSessionController, IDeveloperActionDispatcher,
IDeveloperParameterProvider and IDeveloperPresetService. These are connection
points. All five currently report Unavailable/ProviderUnavailable. Parameter
metadata is visible but effective capture is explicitly unavailable; submitting a
request cannot pretend to apply it. Public composition rejects provider injection.
No central MatchRuntime, ClientSimulationHost, dispatcher or persistence ownership
is transferred to this foundation.

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
RequiresDeveloperTools declares a requirement and grants nothing. A future preset
provider must additionally validate against its resolved BattlefieldDefinition.

DeveloperParameterChange describes a real applied update/reset with correlation,
ordered sequence, actual tick, before/after values and advancing parameter revision.
DeveloperModificationState.AfterApplied retains a sticky DeveloperModified flag
once an actual authoritative value changed. A reset is another ordered event; it
cannot delete earlier changes or clear that flag. Presentation changes and host
speed do not mark authoritative state. Initial non-default authoritative values
must be recorded by the runtime owner as applied initialization before simulation.
The foundation provides data/projection contracts only; it does not write a live
history, replay record, authoritative hash or save document.

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

## Acceptance status

Implementation-time work uses affected-product compiler builds only. No new tests,
test runs, smoke runs or benchmarks occur before the explicitly opened acceptance.
The scoped acceptance results and exact final source are recorded in the PR.
Common end-to-end, full variant/package, Save/Replay and native interaction
qualification remain separate. A foundation compiler pass is not tool/UI delivery.
