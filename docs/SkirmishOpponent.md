# Skirmish Opponent

## Purpose

The vertical-slice skirmish opponent is a deterministic-friendly strategic controller for the Directorate on the Central Divide battlefield. Its purpose is to exercise the complete Build–Supply–Conquer loop through the same authoritative simulation rules and command paths available to a player.

It is intentionally pragmatic rather than optimal. The controller prioritizes coherent full-match behavior, integration coverage, and recoverability over perfect build orders or tactical prediction.

## Authority and Knowledge Boundary

The opponent does not own alternate economy, movement, combat, or supply state.

- Construction is requested through the normal building command path and consumes real inventory resources.
- Production uses normal production and unit-production requests.
- Power consumers and generators use the shared power-network simulation.
- Regional resource movement uses stock policies, logistics routing, Cargo Trucks, and physical inventories.
- Units move through normal navigation, formation, and ground-movement systems.
- Combat intent uses the normal Attack, AttackMove, Retreat, and fire-mission command paths.
- Fuel and Ammunition are real inventory-backed constraints; recovery uses normal battlefield supply.
- Direct entity attacks are allowed only when the faction currently identifies the target through battlefield intelligence.
- Detected contacts may supply a legitimate last-known coordinate but not hidden entity state.
- Without current hostile intelligence, strategic movement targets only static public battlefield knowledge such as expansion/FOB sites and the map center. Exact hostile Command Core coordinates are not used as hidden offensive knowledge.
- No resource multiplier, free construction, free production, teleportation, hidden target transform, infinite ammunition, infinite fuel, or supply bypass is provided.

Static map geometry, public strategic sites, the faction's own start area, and the shared ruleset are allowed knowledge. Enemy state must enter decision-making through the faction intelligence snapshot.

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

Strategic evaluation runs at a configurable low-frequency cadence rather than every tick. All timing uses simulation ticks.

## Opening and Economy

The opening plan establishes the minimum industrial chain with real construction costs:

1. Power generation.
2. Ferrous Ore, Volatiles, and Silicates extraction from eligible deposits.
3. Storage.
4. Steel, Fuel, Electronics, and Ammunition processing capability.
5. Logistics capability.
6. Barracks and Vehicle Factory production capability.
7. Battlefield supply and radar capability.

Economy evaluation observes real inventory quantities, power generation/demand, offline consumers, active construction, and production capability. A power shortage raises power construction priority. Raw-resource shortages prioritize missing extractors before discretionary expansion.

Production facilities receive desired-stock programs through the existing production system. Logistics stock targets are applied through the existing stock-policy command so physical distribution remains responsible for moving material.

## Expansion

Expansion is considered only after the opening economy is viable and the configured readiness threshold is met.

The controller evaluates known static expansion sites on its home side or near the center, requests normal Logistics Hub construction, and can add a nearby contested-resource extractor when construction and placement rules allow it. Placement is validated by the same building-placement service used for player construction.

Economic buildings are connected to the canonical prototype road corridor through the shared road-access and logistics-registration systems. No remote resource transfer is introduced by the opponent.

## Production and Force Composition

Unit production uses the existing Directorate unit catalog and normal unit-production queues. The current vertical-slice composition can request:

- infantry
- Scout Vehicles
- Main Battle Tanks
- Mobile Artillery
- Cargo Trucks
- Supply Trucks

Desired counts are intentionally simple. Configuration limits queue depth so the controller cannot monopolize a production facility with an unbounded plan.

## Reconnaissance and Intelligence

Idle Scout Vehicles are assigned AttackMove reconnaissance tasks toward public expansion and forward-operating sites while the faction lacks current hostile contacts.

Strategic threat and opportunity evaluation consumes FactionIntelligenceSnapshot only. Identified contacts may resolve to an entity for a direct Attack command. Detected contacts remain coordinate-level information. Artillery missions use current detected or identified contact keys and are validated again by the authoritative artillery system.

## Combat Groups and Formations

The strategic layer selects eligible owned combat units and issues normal combat commands. Those commands create the existing simulation-owned combat groups and, for movement-oriented orders, normal movement/formation groups.

The opponent therefore reuses:

- combat-group intent and readiness
- Line/Column/Compact formation semantics
- shared-route formation movement
- tactical target coordination
- pursuit leashes
- authoritative navigation and locomotion

The strategic controller does not maintain a parallel combat-group implementation.

## Defense, Offense, Resupply, and Retreat

Defense is triggered by current hostile intelligence inside the configured defensive radius. Identified threats receive direct Attack intent; other legitimate contacts receive coordinate-based AttackMove intent.

An offensive requires:

- the configured minimum number of combat units
- average readiness at or above the offensive threshold
- minimum force supply at or above the resupply threshold

When direct hostile identification exists, the opponent may attack that identified entity. Otherwise it advances toward public strategic map positions rather than hidden enemy state.

Low force readiness or low supply triggers a normal Retreat command toward a valid recovery point. Automatic resupply policy remains attached to combat units, and only the battlefield-supply system may transfer Fuel or Ammunition.

Mobile Artillery may receive fire missions only from current detected or identified contacts and respects a configurable firing-decision cadence.

## Configuration

SkirmishOpponentConfiguration exposes behavior tuning without direct simulation advantages:

- reaction cadence
- aggression
- expansion readiness threshold
- offensive readiness threshold
- retreat threshold
- resupply threshold
- minimum and maximum attack-group size
- maximum queued units per production facility
- defensive radius
- objective-pressure pursuit leash
- artillery decision cadence

Configuration changes decision frequency and thresholds only. It does not modify resource income, construction cost, production speed, unit statistics, sensing, weapon performance, Fuel, Ammunition, or supply rules.

## Diagnostics

SkirmishOpponentDebugReadModel exposes observation-only development data:

- strategic state
- active goal
- economy health
- power generation/demand
- force composition
- average readiness
- current/known hostile contacts
- selected public objective
- decision tick/count

The F2 world-debug view can draw opponent home/objective markers and compact status labels. The visualization is presentation-only and never feeds decisions back into simulation.

## Headless Validation

SkirmishScenarioHarness composes the normal Central Divide simulation stack without graphics. Both sides receive symmetric starting resources and units and are controlled by the same skirmish-opponent implementation.

Deterministic scenarios cover:

- symmetric authoritative starts
- power and raw-resource recovery through normal construction
- intelligence authorization for direct combat targets
- same-seed strategic progression
- bounded full-match execution toward a terminal match state

Short deterministic scenarios belong in normal CI. Longer soak runs may use the same harness outside the regular CI duration budget.

## Current Limitations

The first opponent deliberately does not include:

- build-order search or economic optimization
- opponent modeling or personality
- diplomacy
- doctrine variants
- learned behavior
- dynamic difficulty
- deception planning
- advanced multi-front force allocation
- persistent named task forces across strategic replans
- predictive artillery against stale contacts
- campaign scripting

These are future strategy-layer capabilities and must preserve the same knowledge and authority boundaries if added.
