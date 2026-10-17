# Bounded mechanical presentation motion

## Decision

Optional mesh metadata partitions the existing index buffer into at most 32
contiguous triangle ranges. Each range has a stable local name, a preceding
parent or root, a narrow motion kind and an authored socket. The compiler
validates names, topology, coverage, anchors and finite motion limits. Runtime
loading validates again before creating GPU resources. Missing metadata keeps
the whole static mesh path.

The presentation extractor copies cosmetic travel, active duration, aim and
visible shot time at the completed-tick boundary. Its two bounded history maps
retain only currently visible entities, using full entity generation identity.
They reset on session changes, tick gaps and backward time. No wall-clock
animation source, gameplay component, save schema or headless dependency is
introduced. Reconstructed saves start cosmetic phases from neutral; these
phases are deliberately not persisted as gameplay truth. Replaying identical
visible read-model sequences produces identical poses. Exact historical phase
restoration after a save requires replaying that extraction sequence and is
not claimed for a newly attached extractor.

RenderWorld interpolates copied pose values and shortest-path turret yaw.
Repeated rendering of a completed tick cannot advance cosmetic time beyond
that tick. Hidden targets cannot supply aim transforms. The render owner
composes rigid child transforms before parents into a fixed 2 KiB stack span.
Triangle ranges share the original GPU vertex/index resources; each range is
batched across entities. Existing frame uploads, fences and disposal own all
resources. No per-entity render controller or persistent transform cache exists.

Mechanical detail fades toward neutral before either edge of the existing
LOD0/LOD1 hysteresis band. Reduced meshes retain their established static
silhouettes and budgets. This is an explicit detail policy, not continuous
geometric LOD blending. Native transition inspection remains required.

## Alternatives and tradeoffs

A skeletal runtime would add authoring and resource machinery beyond the
required rigid parts. Separate mesh assets for every wheel would duplicate
stable asset identities and resource management. Per-frame vertex deformation
would require a new shader contract and animated tangent handling. Bounded
index ranges reuse existing rigid instancing, at the cost of extra batches at
close range. Range count, submission timing and upload payload must remain
observable; identical materials alone do not make these additional draws free.

## Exit criteria

Revisit the range approach when measured close-range batching cost becomes
material, or when reduced LODs need continuously articulated silhouettes.
Persistent cosmetic replay phases, tread-link circulation, independent wheel
steering and physically simulated suspension are separate future contracts.

## Infantry range extension

InfantryStride adds bounded hip rotation from copied travel, using the same
32-range validation, stack transform storage, resource sharing and LOD fade.
Sixteen groups per four-member family remain within that cap; existing Cannon
shoulder ranges supply elevation/recoil. Damage geometry remains bound in each
infantry LOD, while reduced live meshes remain static. No skeleton, foot planting,
retargeting, per-member simulation or animation-asset type is introduced. See
[production and measured near-view limits](../InfantryBattlefieldProduction.md).


## Vehicle metric rotation and mission-derived aim

Normalized mesh coordinates can have different authoritative scales on each axis.
Rigid motion therefore composes scale, rotation and inverse scale around the
normalized pivot before world placement. Quaternion rotation and normalized
recoil/door translation are faded separately. This preserves physical wheel radius
and cannon length while retaining the 32-part cap, shared resources, identity
fast path and warmed allocation contract. Articulated turret transforms and visual
muzzle extraction use the same convention. Infantry stride remains supported.

Owned active artillery FireMission coordinates provide cosmetic yaw and a bounded
elevation derived from the existing parabolic arc. Ownership is checked before
mission access; cancelled/completed and foreign missions supply no pose. Existing
visible shot history supplies recoil. No authoritative component, trajectory,
collision, skeleton or animation-resource contract changes. Clamped pitch may not
align with steep close-range trajectories. See
[vehicle geometry and measured limits](../CombatSupportUnitProduction.md).

## Blender rest-pose authoring

The [authoring adapter](../BlenderMechanicalAuthoring.md) reuses these typed ranges and fixed axes. Separate preview actions are inspection-only and cannot become runtime clips. Open doors retain completed-tick progress during power loss and production pause; powered idle resumes the bounded closing transition. Gameplay sockets, authoritative simulation and the existing static LOD fallback remain unchanged.
