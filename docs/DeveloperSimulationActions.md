# Developer simulation actions

## Ownership and availability

`MatchRuntime` creates one Game-owned `DeveloperCommandGateway` in a Developer
build and registers it as the action dispatcher, parameter provider and operation
catalog in `MatchRuntimeServices`. Public composition has no gateway. The
dispatcher only accepts calls on the simulation owner thread; callers outside
that thread must use the session host's bounded request transport. The host binds
or revokes the live `DeveloperSessionAccess` through
`IDeveloperActionDispatcher.BindAccess` on that same owner boundary. Binding
capability is ephemeral and session-specific. Save data, replay data, diagnostics
and menu state cannot activate a handler.

Admission validates permission, session, current revision, supported operation,
target and current domain facts before queueing. `DeveloperRequestGate` and the
operation-specific checks run again when the normal `InputCommands` tick executes.
Requests are scheduled for the next tick, with at most 128 outstanding and 16
typed values per request. A paused simulation reports `Pending`; its command
remains queued until the host allows a normal tick. Revocation, stale revisions,
stale entity generations, invalid payloads and missing domain services are
reported as `Rejected` with a stable `DeveloperFailureReason`.

`IDeveloperActionDispatcher.CaptureResults()` returns a copied, bounded snapshot
of the latest 64 correlated execution results. `Accepted` and `Pending` are
receipts only. `Applied` includes the tick and actual affected values; a
rejection includes its reason. The operation's simulation revision advances only
when an action changes authoritative state. Duplicate correlation IDs return the
existing receipt or result when the typed request matches; a different request
with the same correlation is rejected.

## Operations

All numeric values must be finite. Resource quantities and damage are positive;
repair is positive and clamped to remaining health. A resource grant names both
the registered resource and one explicit inventory role (`BuildingStock` or
`Cargo`). The amount accepted by inventory capacity and reservations is reported;
no resource is removed from another inventory. Fuel and ammunition refill take
two finite nonnegative amounts, require a registered unit with the corresponding
inventory components, and respect both per-unit and aggregate inventory limits.

Repair and damage target a live, participant-owned, completed unit or building
with health. Construction sites, depleted entities and stale full entity IDs
(including generation) are rejected. Repair changes only health up to its
registered maximum. Damage names a registered weapon and finite incoming X/Z
direction, then enters the ordinary combat damage, destruction and match
resolution systems. It does not directly destroy an entity. Results report the
actual health restored or damage applied after combat resolution.

Spawn names one participant, one registered unit definition and a finite world
position. Terrain grounding, world bounds, traversability and overlap are checked
before `UnitFactory` creates exactly one fully registered unit. Rejected spawn
requests do not consume an entity ID or mutate the world. A successful spawn is
explicitly recorded as a developer intervention; it does not imply paid
production.

The registered action set is `GrantResource`, `RepairEntity`, `RefillUnit`,
`SpawnUnit` and `DamageEntity`. Reserved operation values such as `StartMatch`
and `ApplyPreset` have no handler here and remain unsupported. There is no
reflection editor, arbitrary component writer, unbounded grant or research
unlock.

## Recording, saves and replay

Replay payload schema 8 adds explicit command kind `DeveloperAction` with
versioned `DeveloperCommandPayload` schema 1. The payload stores session value,
correlation, expected revision, operation, target identity and up to 16 typed
finite values. No executable type name or persisted capability is accepted.
The recorder retains each submitted command in its original sequence, including
commands later rejected at execution; it captures the command's actual execution
activation state after the tick. Control commands retain their existing codec.

Developer restores require both the compiled Developer variant and an explicit
recovery entry point (`RestoreDeveloper` or `PlayDeveloperReplay`) supplied with
the current immutable capability. Public `Restore` and `PlayReplay` reject any
developer initial profile, audit, state marker or action command before running
a handler. A Developer recovery host binds a fresh permission to the newly
reconstructed session, decodes commands through the same registered handlers,
and still checks operation/domain validity at execution. Historical state hashes
are never rewritten.

The authoritative `developer.state.v1` contribution records the simulation
revision, intervention flag, ordered history sequence and
hash, initial overrides, effective/pending parameter state and conveyor tuning
state. The hash distinguishes initial values from later parameter edits. Ordered
parameter audits store tick, revision, correlation, reset state and before/after
values, and are compared with the reconstructed history. Resetting parameters
does not erase prior interventions. The audit is capped at 4096 entries; an
operation that would exceed the cap is rejected before mutation. Normal matches
without developer state keep the existing authoritative domain set and hash
behavior.

Schema 8 explicitly supports the new payload fields. Earlier payload versions
remain readable under their existing compatibility rules; they are not upgraded
to imply developer authority. Authoritative state remains schema 9. Size limits,
atomic file replacement, cancellation and transactional session recovery remain
the existing persistence service's responsibility.

## Integration handoff

The Game runtime owns registration, validation, actual effects, parameter audit
and replay reconstruction. The session host owns the client request queue and
must call `BindAccess` on the simulation owner when permission changes, then
submit typed requests on that owner boundary. The menu owns client input and
should read action descriptors, capture bounded correlated results, and keep
`Accepted`/`Pending` distinct from `Applied`. A headless developer host can use
the same `MatchRuntime.Services.DeveloperActions` contract on its simulation
owner thread. The concrete UI transport and native end-to-end operation remain
part of the combined developer-tools qualification; this package does not alter
`ClientApplication`, `ClientSimulationHost` or menu files owned by those layers.

Gameplay tuning is registered by the existing parameter provider. It uses the
shared dispatcher for `SetParameter`, `ResetParameter` and `ResetParameters`;
initial profile values and ordered changes are replayed by the same command and
save path. When composing a new provider or host adapter, bind it only after the
runtime's scenario and owner thread exist, and before accepting actions.

## Package acceptance

Implementation builds and package acceptance are separate. Acceptance for this
package uses the smallest existing Game cases that demonstrate real inventory
effects and capacity limits, stale session/entity rejection, capability gating,
and Save/Replay reconstruction with a pending action. The combined qualification
owns the full Public/Developer matrix, native menu interaction, presets and
end-to-end host transport. On this branch the Developer Game package selection
passed 4/4: capacity-limited resource grant, stale entity/session and inactive
access rejection, unit refill and one-unit spawn, and Save/Replay of parameter
set/reset, grant, actual combat damage, repair and a pending command. Public and
Developer Game product builds both passed with zero warnings and errors. Local
test dependency restore used the cached package set because NuGet's vulnerability
service was unreachable; the required PR CI restore remains responsible for the
normal security audit.
