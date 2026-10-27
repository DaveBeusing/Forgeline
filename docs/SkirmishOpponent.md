# Skirmish Opponent

## Purpose

The vertical-slice skirmish opponent is a deterministic-friendly strategic controller for the Directorate on the Central Divide battlefield. Its purpose is to exercise the complete Build-Supply-Conquer loop through the same authoritative simulation rules and command paths available to a player.

It is intentionally pragmatic rather than optimal. The controller prioritizes coherent full-match behavior, integration coverage, and recoverability over perfect build orders or tactical prediction.

## Authority and Knowledge Boundary

The opponent does not own alternate economy, movement, combat, or supply state.

- Construction is requested through the normal building command path and consumes real inventory resources.
- Production uses normal production and unit-production requests; priority changes use an ownership-validated unit-production command.
- Automatic resupply configuration uses the same ownership-validated player logistics command as the interactive player path.
- Power consumers and generators use the shared power-network simulation.
- Regional resource movement uses stock policies, logistics routing, Cargo Trucks, and physical inventories.
- Units move through normal navigation, formation, and ground-movement systems.
- Combat intent uses the normal Attack, AttackMove, Retreat, and fire-mission command paths.
- Fuel and Ammunition are real inventory-backed constraints; recovery uses normal battlefield supply.
- Direct entity attacks are allowed only when the faction currently identifies the target through battlefield intelligence.
- Detected contacts may supply a legitimate last-known coordinate but not hidden entity state.
- Without current hostile identification, strategic movement uses public static battlefield sites and map-defined start positions. A map start is not permission to inspect a hidden live Command Core entity or its transform.
- No resource multiplier, free construction, free production, teleportation, hidden target transform, infinite ammunition, infinite fuel, or supply bypass is provided.

Static map geometry, public strategic sites and starts, and the shared ruleset are allowed knowledge. Enemy runtime state must enter decision-making through the faction intelligence snapshot.

## Decision Layers

The full-match opponent preserves four responsibilities without creating parallel gameplay authority:

- **Strategic** chooses the current long-horizon goal: stabilize economy, establish infrastructure, expand, scout, defend, recover, prepare an offensive, or pressure the match objective.
- **Operational** translates that goal into a public-map objective and force-level task such as an expansion site, defensive response, recovery location, reconnaissance route, or offensive pressure point.
- **Tactical** uses normal combat groups, formations, Attack/AttackMove/Retreat, artillery, and supply-support commands against only legitimate intelligence.
- **Unit behavior** remains bounded per-unit tactical decision logic, but Attack, AttackMove, Retreat, and Hold requests are emitted through the same ownership-validated tactical simulation commands as higher-level control rather than writing combat intent directly.

The layer boundary is diagnostic and organizational. Economy, construction, production, logistics, movement, intelligence, combat, supply, and match lifecycle remain owned by their existing simulation systems.

Operational offensive movement is intentionally stable across decision cadences. Units already following the current Attack or AttackMove objective keep their existing combat/movement group; only newly eligible or previously diverted units receive reinforcement commands. Unit behavior may replace that objective to engage a currently identified target inside its configured engagement leash, but a distant detected/identified contact cannot replace an active Strategic/Operational AttackMove or Retreat objective. This prevents readiness/resupply churn from repeatedly rebuilding long-range formation routes while preserving legitimate local tactical reactions.

Forward logistics preserves the same responsibility boundary. A reconnaissance mission starts only when a field-ready Supply Truck is available. The Scout receives its normal AttackMove combat intent, then Scout and escort are submitted together through the existing MoveEntitiesCommand with combat intent preserved, producing one shared formation/navigation route whose pace naturally respects the slower support vehicle. The escort remains reserved while that reconnaissance movement group is active. Offensive admission must therefore find another field-ready Supply Truck, and offensive support selection cannot retarget the reconnaissance escort. Fuel consumption and replenishment remain owned by the normal movement and Battlefield Supply systems; no range or resource shortcut is introduced.

## Strategic State

Each controller stores simulation-owned strategic state:

- Bootstrap
- Recovering
- Expanding
- Mobilizing
- Defending
- Attacking
- Resupplying

The active strategic goal records the current intent, including power establishment, resource security, industrial bootstrap, production capability, expansion, scouting, defense, offensive preparation, attack pressure, and economy/supply recovery.

Strategic decisions run at a configurable low-frequency cadence. All timing uses simulation ticks. The controller's current decision order is critical economy recovery, bootstrap construction, local defense, expansion, force recovery, scouting, and offense. A successful earlier branch prevents a later branch from issuing a strategic objective in that decision. Economy stock and production policies and supply-truck loading are maintained before this branch selection.

Owned-state capture remains active on every opponent tick because critical logistics recovery can legitimately need current production-facility, supply-depot, unit, and pending-request state between strategic decisions. The collections backing that capture are reusable per-controller scratch storage: contents are cleared before refill, stable entity iteration remains authoritative, excessive retained capacity is trimmed, and the scratch state is released when its controller disappears. Intelligence capture plus economy and force assessment are decision-cadence work when development debug capture is disabled. When debug capture is enabled they continue on non-decision ticks so the published debug snapshot remains current rather than silently stale.

## Opening and Economy

The opening plan establishes the minimum industrial chain with real construction costs:

1. Power generation.
2. Ferrous Ore, Volatiles, and Silicates extraction from eligible deposits.
3. Storage.
4. Steel, Fuel, Electronics, and Ammunition processing capability.
5. Logistics capability.
6. Barracks and Vehicle Factory production capability.
7. Battlefield supply and radar capability.

Economy evaluation observes real inventory quantities, power generation/demand, offline consumers, active construction, and production capability. A power shortage raises power construction priority. Recovery is based on actual generating capacity rather than treating four completed plants as a permanent ceiling: when demand still exceeds generation or consumers remain offline, the controller may add bounded reserve capacity up to six plants through normal construction. Raw-resource shortages prioritize missing extractors before discretionary expansion.

Production facilities receive desired-stock programs through the existing production system. Logistics stock targets are applied through the existing stock-policy command so physical distribution remains responsible for moving material. Unit-production refill minima account for the active unit's material costs; stock above a generic minimum is not necessarily sufficient to start that unit. When a logistics-capable factory is below its essential live Cargo Truck fleet target, its Fuel policy reserves two Cargo Truck fuel costs while transports still exist. This prevents the physical logistics fleet from consuming its last recovery path without granting material or bypassing Cargo Truck delivery. The Command Core keeps a protected Steel minimum for construction, but its desired retained target remains below the full strategic reserve so Critical Vehicle Factory demand can draw real surplus when an objective-pressure unit is blocked on Steel.

Faction-wide inventory totals are an assessment, not a substitute for the consuming facility's inputs. Diagnosing NoInput requires comparing each recipe or unit cost with available stock in that facility. A full extractor output and nonempty deposit alongside an empty refinery input indicate a material-flow problem, not proof of deposit exhaustion. Power state is independently authoritative: recorded input shortages do not supersede an active NoPower block reason.

## Expansion

Expansion is considered after bootstrap when the configured economy readiness threshold is met or the economy is raw-resource constrained, provided the force and pending-construction checks permit it.

After the live core assault force is established, the controller evaluates known static expansion sites, requests normal Logistics Hub construction, and can add a nearby contested-resource extractor when construction and placement rules allow it. Unsupported remote hubs are candidates for a nearby Supply Depot. Placement is validated by the same building-placement service used for player construction.

Economic buildings are connected to the canonical prototype road corridor through the shared road-access and logistics-registration systems. No remote resource transfer is introduced by the opponent.

## Production and Force Composition

Unit production uses the existing Directorate unit catalog and normal unit-production queues. The current vertical-slice composition can request infantry, Combat Engineers, Scout Vehicles, Main Battle Tanks, Mobile Artillery, Cargo Trucks, and Supply Trucks.

Desired counts are intentionally simple. Configuration limits queue depth so the controller cannot monopolize a production facility with an unbounded plan. Critical logistics recovery owns the next vehicle-production slot only until the configured minimum Cargo Truck and Supply Truck fleet is restored. Once that floor is satisfied, the vehicle plan first establishes one reconnaissance Scout, then the configured minimum objective-pressure Main Battle Tanks, then one Mobile Artillery unit before optional roster growth. Mature mobile Supply Truck capacity follows physical Supply Depot growth up to three providers after the reconnaissance Scout, configured objective-pressure Main Battle Tanks, and first Mobile Artillery requirement are satisfied. This mobile-support growth is selected before optional Cargo Truck expansion so a mature frontline can recover multiple stranded combat units without changing the critical early Cargo/Supply fleet floors. If later network growth or vehicle loss leaves that mature support target unmet, recovery may reclaim a material-blocked Normal-priority optional vehicle slot through the same unit-production cancellation, priority, and queue commands; the core assault prerequisites and critical Cargo Truck floor still remain ahead of this recovery. Optional Cargo Truck capacity still scales from the physical Supply Depot count afterward. Only factories serving an essential live Cargo deficit retain the additional ordinary input stock for one Cargo Truck replacement before routine non-logistics production is admitted, preventing loss of the final transport fleet from stranding replacement production without granting materials or bypassing transport. Both rules add real produced vehicles only; they do not create transport capacity or Fuel implicitly. If the last reconnaissance Scout is destroyed, reconnaissance reconstitution may cancel a material-blocked Normal-priority vehicle request and enqueue the replacement Scout at High priority through the normal unit-production command path; an already critical logistics recovery still outranks that replacement. Once the configured objective-pressure Main Battle Tank floor is already present, the replacement Scout's physical factory-material stock policies are promoted to Critical until the reconnaissance unit can be produced, preventing a mature force from remaining blind behind routine material demand. When a Main Battle Tank is the active or planned vehicle, its unit-production material policies use Critical logistics priority; routine unit-production refill remains High. This preserves both reconnaissance continuity and the structure-capable assault path under cargo contention without granting resources or bypassing normal production.

Before the core assault roster is live, optional growth is deferred: one Scout,
the configured minimum objective-pressure Tanks and one Artillery are established
before requesting a second Scout/Artillery or optional Tanks. The initial infantry
target covers the configured main-force complement and enabled raid/home-defender
requirements; mature infantry growth follows once that core roster exists. An idle
infantry-only facility with no active or planned request disables routine input
refill through the ordinary stock-policy command. An explicit queued request still
receives its paid material policy, with the original active refill amounts. These
are planning and demand changes; production prices, transport and inventory
ownership remain authoritative.

A completed facility or a large army does not establish that its next unit has locally available production inputs or that enough units are currently eligible to attack.

## Reconnaissance and Intelligence

Idle Scout Vehicles prioritize an AttackMove reconnaissance route to a stand-off approach point toward the public opposing Command Core objective. The scout still has to cross the battlefield, enter real sensor range, and obtain identification through the battlefield intelligence system; no contact is synthesized from map knowledge. Because a Scout Vehicle cannot cross all of Central Divide on one tank of Fuel, the reconnaissance advance reuses the same physical forward-support routine as an offensive group: an available loaded Supply Truck follows the scout through normal movement and Battlefield Supply rules. Public expansion, forward-operating, and mining sites remain the fallback scouting route when no opposing objective is configured. Scouts are excluded from the strategic attack candidate set.

Strategic threat and opportunity evaluation consumes FactionIntelligenceSnapshot. Identified contacts may resolve to an entity for a direct Attack command. Detected contacts remain coordinate-level information. Artillery missions use current detected or identified contact keys and are validated again by the authoritative artillery system.

## Combat Groups and Formations

The strategic layer selects eligible owned combat units and issues normal combat commands. Those commands create the existing simulation-owned combat groups and, for movement-oriented orders, normal movement/formation groups.

The opponent reuses combat-group intent and readiness, Line/Column/Compact formation semantics, shared-route formation movement, tactical target coordination, pursuit leashes, and authoritative navigation and locomotion. It does not maintain a parallel combat-group implementation. Matching existing attack orders are retained rather than being recreated unconditionally at every decision.

## Defense, Offense, Resupply, and Retreat

Defense responds to current hostile intelligence inside the configured defensive radius and selects local defenders. Identified threats receive direct Attack intent; other legitimate contacts receive coordinate-based AttackMove intent.

Offensive admission is per candidate, not a comparison against the entire army's average:

- One owned Scout Vehicle is retained as the reconnaissance reserve; additional armed Scout Vehicles may reinforce the offensive group.
- Candidates with readiness data must meet the configured offensive readiness threshold.
- Fuel must meet the higher configured offensive reserve threshold; Ammunition must meet the general resupply threshold.
- At least one owned Supply Truck must be field-ready: not self-resupplying or rescue-assigned, carrying at least half of its Fuel cargo target and one quarter of its Ammunition target, with at least 35% propulsion Fuel when propulsion state is present.
- The remaining candidate set, bounded by MaximumAttackUnits, must contain at least MinimumAttackUnits.
- The eligible attack set must also contain at least MinimumObjectivePressureUnits Main Battle Tanks, ensuring the committed force can actually damage the Command Core instead of relying on infantry/Scout weapons that cannot engage structures.

The stronger Fuel reserve exists because a fraction that is sufficient to trigger normal resupply is not necessarily enough to commit a heavy vehicle across Central Divide and still retain recovery options. Fresh offensive admission therefore still requires the full offensive Fuel reserve, and combat-unit automatic resupply begins at that same threshold. Once a unit already carries an Attack or AttackMove order, a bounded continuation hysteresis permits it to remain part of the operational force down to the higher of the general resupply threshold or 15 percentage points below the initial offensive reserve. Resupply can therefore proceed in parallel without repeatedly dissolving and rebuilding the attack group at the admission boundary; units below the continuation threshold still recover through the normal supply/retreat path. The current implementation allows a candidate without a readiness component through the readiness filter; diagnostics explicitly records HasReadiness so absence cannot be mistaken for a measured full-readiness value. Normal runtime readiness is derived in SnapshotEvents.

When direct hostile identification exists, the opponent may attack that identified entity, prioritizing an identified Command Core. Otherwise it advances only toward public strategic sites on the opponent-facing half of the map rather than reading hidden live enemy state. Reaching that forward waypoint commits the Operational layer to the deeper public opposing start position; that commitment is retained across later readiness/resupply membership changes instead of being recomputed from a changing eligible-force centroid. This brings the force into normal reconnaissance range so the battlefield-intelligence system can identify the Command Core and hand control to the direct Attack path.

Individual low-readiness or low-supply units receive normal retreat/recovery behavior. Existing real ResupplyOrders are not replaced with a new strategic retreat. Before the Operational layer has committed to the deep opposing objective, strategic retreat retains the stable home-near recovery behavior used during force buildup. After `DeepOffensiveCommitted`, units that need a strategic retreat reuse the shared `RetreatRecoveryPlanner`, so the recovery destination is selected from owned repair/supply providers relative to the affected force instead of forcing a return to the rear. The resulting movement still uses the normal Retreat command, formation routing, Fuel consumption, and provider rules. Force-wide Resupplying is narrower than the presence of any degraded unit: it requires an established attack force, an active resupply order, and the configured aggregate/all-units recovery condition. Consequently, PrepareOffensive with no active resupply order must not be interpreted as proof of either adequate supply or a particular supply-system defect.

Automatic resupply policy remains attached to units. Only BattlefieldSupplySystem transfers Fuel or Ammunition. A recipient unable to afford the trip to a provider can instead receive a Supply Truck rescue, subject to separate propulsion/cargo checks and actual route-budget validation. Cargo Trucks use a conservative 55% self-refuel threshold because regional delivery is their primary task; Supply Trucks use a lower 35% propulsion threshold so a field-support provider does not repeatedly make itself unavailable while a supported force still has a viable rescue need. Supply-cargo staging is evaluated separately: a free Supply Truck below the same 50% Fuel-cargo reserve required for offensive admission returns to a legitimate owned loading source before the force commits, avoiding a PrepareOffensive deadlock between loading and admission thresholds. Loading-source selection compares operational Supply Depots with the owned Command Core and uses the nearest source that currently holds a needed resource; an existing loading route is retained only while it still targets that selected source, so a depleted truck does not stay committed to a farther depot after a closer stocked source becomes preferable. Rescue cancellation and supply-loading movement stops use the ownership-validated StopMovementCommand rather than direct movement-state mutation.

An available loaded Supply Truck follows the current offensive centroid as mobile support. Offense does not begin without such a field-ready provider, so strategic preparation cannot knowingly launch a force into a deep objective with no physical Fuel/Ammunition support path. See [Battlefield Supply](BattlefieldSupply.md).

Mobile Artillery receives fire missions only from current detected or identified contacts and respects a configurable firing-decision cadence.

## Configuration

SkirmishOpponentConfiguration exposes behavior tuning without direct simulation advantages:

- reaction cadence and aggression;
- expansion, offensive readiness, offensive Fuel reserve, retreat, and resupply thresholds;
- minimum and maximum attack-group size plus the minimum structure-pressure component;
- maximum queued units per production facility;
- defensive radius and objective-pressure pursuit leash;
- artillery decision cadence.

Configuration changes decision frequency and thresholds only. It does not modify resource income, construction cost, production speed, unit statistics, sensing, weapon performance, Fuel, Ammunition, or supply rules.

## Diagnostics

SkirmishOpponentDebugReadModel exposes observation-only development data: strategic state, active goal, economy health, power generation/demand, force composition, average readiness, current/known hostile contacts, selected public strategic objective, operational objective, active combat-group objective, force supply requirement, retreat reason, and decision tick/count.

The group projection is copied from the authoritative CombatGroupIntent and surviving CombatGroupMember state. Supply and retreat diagnostics are derived from real unit readiness, resupply, and repair/recovery state rather than from a second planning model.

The F2 world-debug view draws opponent home/objective markers, active group destination, and compact strategic/operational/supply/retreat labels. Visualization is presentation-only and never feeds decisions back into simulation.

Headless runs with a diagnostics output additionally attach SkirmishProgressionDiagnostics at the end of AiDecisions. Its snapshots record the selected strategic branch, eligible attacker count and exclusions, unit readiness and current order state, consuming-facility material shortages, provider cargo versus propulsion Fuel, industrial processing/extraction state, and bounded transport inventories, reservations, and movement intent.

The observer retains 128 history entries: the first 32 and a rolling 96-entry tail. First loss of the minimum eligible attacker count is retained separately as a before/after pair for each player, along with the latest decision. Each detailed category has a fixed bound and omission counts. Transition history is sampled at decision boundaries, not emitted every simulation tick. See [Diagnostics and Performance](DiagnosticsAndPerformance.md) for phase timing, sidecar names, retention, and interpretation.

A first eligibility loss identifies the immediate admission mechanism, not necessarily the underlying bug. Follow the resource's extraction, processing, transport, provider assignment, and physical transfer path before changing behavior. Cumulative successful cargo deliveries do not prove that currently assigned trucks are making progress.

## Headless Validation

MatchRuntime is the reusable game composition for the Central Divide simulation stack without graphics. SkirmishScenarioHarness is a thin test wrapper rather than a duplicate composition.

Deterministic scenarios cover symmetric authoritative starts, power and raw-resource recovery through normal construction, intelligence authorization for direct combat targets, same-seed strategic progression, and bounded Build-Supply-Conquer progression through bootstrap, expansion, reconnaissance, logistics movement, and combat-group formation.

Focused deterministic scenarios and bounded strategic progression belong in the normal test suite. CI additionally runs the explicit **Two-opponent full-match validation**: one fresh validation-profile match, two computer-controlled participants, seed 2026, an 80,000-tick ceiling, and `--require-terminal`. The gate fails unless normal Command Core gameplay produces a match result and the ordinary lifecycle reaches Completed. Its diagnostics are written to `artifacts/opponent-full-match.json`.

Forced-objective lifecycle tests verify objective handling only; they are not evidence of a naturally completed match.

Repeated multi-match soak uses the same runtime through build/Run-MatchSoak.ps1 or the manually dispatched soak workflow and remains separate from hardware-sensitive PR timing gates. Inspect every requested match, retain failing outcomes, and record the seed sequence. A successful seed or a passing unit suite alone does not establish general gameplay balance or universal termination.

The normal gameplay profile keeps the product-facing starting stock, default strategic-controller settings, and interactive navigation resolution. The validation profile intentionally uses accelerated resources, asymmetric attacker/defender pacing, and a coarser navigation grid for bounded coverage. Those values are not gameplay balance values. Failed progression must not be hidden by reducing attack thresholds, removing resource costs, forcing a match result, or weakening the terminal gate.

## Current Limitations

The first opponent deliberately does not include build-order search or economic optimization, opponent modeling, personalities, diplomacy, doctrine variants, learned behavior, dynamic difficulty, deception planning, advanced multi-front force allocation, persistent named task forces across strategic replans, predictive artillery against stale contacts, or campaign scripting.

These are future strategy-layer capabilities and must preserve the same knowledge and authority boundaries if added.


## Bounded logistics raids

A separate raid intent runs on the existing strategic reaction cadence after
necessary economy, bootstrap, base-defense and force-recovery decisions, before
free units are assigned to reconnaissance or the main offensive. Each controller
can own one raid of two or three units. Ready idle home defenders are retained
inside a configurable 260 m home radius (two by default). When an idle force can
already meet the existing main-offensive size and structure-pressure floor, that
force is reserved before lending surplus units. The five-unit offensive minimum,
structure-capable minimum and forward-supply admission remain unchanged. Scouts,
Cargo Trucks and Supply Trucks are excluded from raid membership.

Selection consumes the faction's copied knowledge rather than searching hostile
world state. A persisted cursor rotates through at most 32 candidate contacts per
decision. Only current identified contacts can resolve an entity: full entity
index and generation must still match. Exposed Cargo/Supply Trucks rank first,
then Storage/Supply Depots, Power Plants and processing, ammunition and unit-production plants.
Equal scores use distance from home, then full stable identity. Enemy inventories,
queued recipes and economic contents are never inspected. Known nearby defense
is assessed from all current identified contacts already in that same knowledge
snapshot, so a guard outside the candidate window is not silently ignored.
A known tank/artillery weighs three, another armed unit one; the initial admitted
threat ceiling is one within 160 m.

Admission checks real navigation outward and back for each unit's movement
capabilities, available unreserved fuel, weapon effectiveness against the target
class, ammunition and readiness. The initial fuel requirement is route length
times normal consumption times 1.25, plus 15% of fuel capacity; six shots and
0.65 readiness are required. A missing route or reserve postpones the raid.
Rifles may raid light vehicles but cannot qualify as structure attackers.
No inventory, fuel, ammunition, readiness or damage is created by this planner.

Approach uses ordinary AttackMove, an identified in-range target uses Attack,
and loss of identification permits only an AttackMove to the stored last known
point for up to 80 ticks. Hidden transforms cannot update that point. A 120 m
target-anchor leash bounds pursuit. Stronger known defense, lost routes, too few
survivors, ammunition/readiness loss or inadequate return fuel cause normal
Retreat or HoldPosition plus the existing nearest-provider resupply path.
A raid never clears a replacement command whose combat-group identity has
changed. Base defense can stop the orders still owned by the raid and take over.

The initial intent budget is 1,200 ticks, including search/return/resupply.
An expiry-only tick check enforces the deadline between strategic decisions
without capturing faction knowledge or selecting targets. Expansion decisions
also advance raids.
Expiry releases the strategic intent with a normal retreat/hold order still
valid; the existing tactical lifecycle owns completion of that return.
Completion starts a stable-tick cooldown of 600 ticks. These values are
configurable gameplay starting points, not guarantees about when a raid wins or
when a natural match first exposes a target. Repeated decisions retain a valid
group and only replace its command for a meaningful phase/destination change.

Local tactical behavior on computer-controlled units defers identified logistics
targets and unidentified contacts outside the configured home-defense perimeter
to strategic planning. Ordinary combat with known armed contacts and immediate
weapon acquisition remain available. Active raid members temporarily leave the
local decision behavior so it cannot replace their group each tick; automatic
supply policies and ordinary combat execution remain active. Disabling raids or
using the legacy constructor without navigation/weapons preserves the previous
local policy.

The existing strategic debug read model includes the raid phase, target/contact,
members/group, admission reason, stored/ordered positions, stable timestamps and
candidate count. Authoritative state schema **7** includes raid intent/cursor and
the tactical delegation fields. Envelope 1 and payload 6 remain unchanged.
Older authoritative checkpoints (including schema 6) are explicitly rejected;
there is no silent reinterpretation. Candidate lists and debug caches are derived.
Save/replay reconstructs ordinary commands and the original opponent configuration.

### Executed scenario and acceptance limits

The headless raid scene uses Gameplay/Standard stock, seed 2026, normal catalogs,
construction, production, logistics, sensors, movement, ammunition, damage and
destruction. It waits for three Rifle Squads to be produced through the normal
opponent/economy pipeline. The authored scene places the player's two existing
Cargo Trucks 600 m east of the opposing core and loads 20 Fuel each by transfer
from that player's finite starting inventory. It adds no vehicles or resources.
Recorded tactical commands move the existing observer, stop it, and expose the
trucks. Its defense radius is explicitly set to 180 m to separate a short-range
raid from base defense; the gameplay default remains 520 m. Main-offensive
requirements, costs, weapons, supply rates and raid defaults are unchanged.

In the unprotected run the selected loaded truck is destroyed through normal
combat and its cargo inventory is removed. In the same-seed counterplay run,
a recorded player movement reroutes that target; vehicle and all 20 Fuel survive.
Both runs advance 1,200 ticks after admission and leave the match running.
This demonstrates bounded logistics loss and counterplay, not a claimed
production-throughput measurement or guaranteed timing in the default deployment.

An active raid also reconstructs through serialized Save and Replay with equal
complete authoritative hashes and continued states at increments 0, 1, 7 and 24
ticks. Focused cases cover missing/detected-only intelligence, incompatible
weapons, return fuel, empty ammunition, known guards, pursuit, target-generation
loss, route changes, command replacement, cooldown and stable same-seed intent.
The required CI job runs the raid group independently before the complete suite.
A separate timer regression uses a 40-tick reaction cadence and a three-tick
intent budget to prove expiry before the next strategy decision; these validation
parameters do not replace the normal gameplay defaults.

Full final-head CI and natural terminal-match results are recorded in the PR.
Native raid visibility, group readability, pursuit/return feel and player
counterplay still require human Windows/D3D12 acceptance. Physical conveyor
qualification remains tracked in #310; the unchanged gameplay texture-budget
blocker is #344. WARP, physical GPU performance and human inspection are separate
evidence classes; the headless scene does not replace them.

## Core progression and production recovery

The progression correction starts from master `89086e68` (0.1.487) after
the earlier research, logistics and full-match work was integrated. It preserves
the existing controller, physical production, movement and supply systems.

Critical Cargo recovery uses `ResolveEssentialCargoFleetTarget`, the existing
configured/depot hauling floor. Processing-industry growth and expansion
headroom are optional fleet goals after the live Scout, configured tank minimum
and first artillery exist. A healthy essential fleet no longer adds an imaginary
Cargo replacement to every factory refill minimum. Actual Cargo losses restore
the reserve and use the normal High-priority recovery path. A missing Supply
Truck may release an unpaid High Cargo request once the essential live hauling
floor is healthy. Running/paid work continues.

The former Scout recovery path also restores missing core tanks and the first
unlocked artillery. An existing matching request is promoted, not duplicated.
An unpaid Normal request may be cancelled through the existing owner-validated
command to free the active slot; an obsolete High core-role request may yield
only after its live role is satisfied. The ordinary goal calculation retries
cancelled optional work when the core force has recovered. Optional Combat
Engineer production also waits for that core force. Supply deficits and
losses keep the existing critical recovery precedence.

Optional hub/depot expansion waits for that same live core assault force, so
its construction cannot repeatedly spend partial deliveries before necessary
research and units exist. Power, missing raw extraction and bootstrap industry
remain on the existing higher-priority paths. Attack admission, retreat/recovery,
reconnaissance escort handoff and deep-offensive commitment are unchanged;
restored units re-enter those existing planners on the next normal decision.

Research and construction share the definition of active unpaid factory inputs.
InventoryStore availability already excludes paid reservations. Such reservations
are not charged again by the budget calculation. New research retains its normal
Cargo/Supply replacement reserve and must be payable from one real owned source.
Needed Mechanized Systems precedes optional Sensor Fusion.

### Reading a blocked decision

The copied research diagnostic records source, resource, research cost, available
quantity, paid reserved quantity, active unpaid unit input, replacement reserve
and aggregate available budget after unpaid inputs. `ProductionInputs` means
the selected source would consume a live unit's unpaid inputs; `ProductionReserve`
means the remaining player budget is insufficient after those commitments.
`Requirements/MissingMaterials` remains an actual source shortage. Active research
continues to report its real research-system block even during other priorities.

The existing `--diagnostics-output` progression sidecar includes `Research`
alongside bounded production, industry, provider and transport details.
Research reason/technology/block transitions participate in its existing
128-entry bounded history; source details retain scalar values rather than
unbounded per-tick arrays. Debug observations never become inventory authority.

### Delivery and remaining manual work

Authoritative schema **10** explicitly rejects older deterministic ordering.
Envelope 1 and payload 8 are retained. No authoritative component or command
codec is added; research diagnostics and progression histories are derived.
See [Technology progression](TechnologyProgression.md) and
[recovery compatibility](SaveLoadReplayAndRecovery.md#opponent-progression-compatibility).

No tests were created, modified or executed, and no smoke, benchmark, headless,
native qualification, CI dispatch or rerun was started for this correction.
Product compilation is still outstanding: the connected content interface has no
compiler and the available local checkout lacks the current master object.
No build or behavioral success is claimed.

Manual review must follow the normal-profile research payment into Mechanized
Systems and physical artillery, then destroy core/logistics units and inspect
real reconstitution, restored supply and renewed attack commands. Separately
inspect an unmodified seed-2026 validation-profile match through the existing
80,000-tick terminal bound. Costs, initial resources, deadlines and victory rules
have not changed.

Baseline CI run 40407552138 fails the existing normal-research assertion at
TechnologyNormalMatchTests line 49. Its reported stock/force totals stop changing
around tick 30,000. That establishes stagnation, not the truck's physical cause.
If it persists, the concrete follow-up is to identify the first stationary paid
cargo job in the transport history and distinguish drive Fuel, docking/queue
ownership, navigation failure, source shortage and destination capacity. No
cargo-motion change or guaranteed natural victory is claimed by this scheduling
correction. User acceptance remains pending.
