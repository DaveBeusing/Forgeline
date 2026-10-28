# Fair opponent difficulty profiles

Difficulty is a validated policy for the existing Directorate opponent.
Easy, Standard and Hard use the same starting stock, unit/building prices,
production times, weapon/sensor statistics, physical supply and game commands.
There is no resource multiplier, fog override or hidden-target permission.

## Selection and compatibility

Game hosts select policies through OpponentDifficultyProfiles.Apply:

```csharp
var settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, seed: 2026);
settings = settings with
{
    Scenario = OpponentDifficultyProfiles.Apply(settings.Scenario,
        new Dictionary<ulong, OpponentDifficultyId>
        {
            [1] = OpponentDifficultyId.Easy,
            [2] = OpponentDifficultyId.Hard
        })
};
using var match = MatchRuntime.Create(settings, cancellationToken);
```

Only configured player IDs and declared difficulty IDs are accepted. Apply copies
the configuration dictionary, validates the result and preserves caller settings,
starting stock, navigation, transport settings and scenario profile. The three
immutable descriptors have stable keys skirmish.easy.v1, skirmish.standard.v1
and skirmish.hard.v1. Their finite constants use existing configuration validation.

Standard is exactly new SkirmishOpponentConfiguration(), the current Gameplay
default for both starts. Existing client/headless defaults and the asymmetric
Validation preset are not retuned. Named selection is a Game configuration API;
this change adds no New Game difficulty widget or CLI parser flag and does not
take ownership of the shared pacing runner.

The existing save/replay configuration already stores every policy value.
Recovery needs no new profile discriminator or migration. Identify matches the
complete configuration, including nested raid policy; any override reports
custom rather than retaining a misleading named label. Existing persisted
numeric policies remain reconstructible independently of catalog naming.

## Policy differences

All durations below are simulation ticks. The first strategic decision occurs
at tick 1 for every profile; subsequent decisions use the configured cadence.

| Parameter | Easy | Standard | Hard |
|---|---:|---:|---:|
| Strategic cadence | 40 | 20 | 10 |
| Artillery planning cadence | 160 | 80 | 40 |
| Aggression | 0.55 | 0.65 | 0.75 |
| Expansion economy threshold | 0.65 | 0.55 | 0.50 |
| Maximum coordinated attack units | 6 | 12 | 16 |
| Maximum queued units per factory | 1 | 2 | 3 |
| Raid candidate window | 8 | 32 | 32 |
| Maximum raiders | 2 | 3 | 3 |
| Raid cooldown | 1200 | 600 | 400 |
| Lost-contact search duration | 160 | 80 | 40 |

At 20 Hz, strategic cadences correspond to 2, 1 and 0.5 seconds. They alter
strategic planning, not weapon simulation or local unit reflexes. Bounded attack
and queue limits allow more coordination/lookahead only when units can actually
be paid for, produced and supplied. Aggression chooses existing public-map
operational approaches; it never supplies a hidden entity or its live position.

Readiness, retreat/resupply/offensive Fuel reserves, essential Cargo/Supply
fleet floors, defender reservations, raid travel range, ammunition requirements,
return-Fuel margins and current-identification rules remain unchanged between
named levels. Hard does not ignore physical failures; Easy still scouts,
defends, researches and recovers through the existing systems. Candidate windows
rotate through existing legal faction contacts rather than permanently ignoring
later contacts. Hard remains bounded by 32 contacts and three raiders.

These parameters are ordered and legible, not a guarantee of monotonic win rate
on every map, seed or opponent. Aggression and queue depth can have tradeoffs.
See [executed cohort](OpponentDifficultyQualification.md) for observed outcomes,
including censored matches, rather than inferring wins from faster decisions.

## Information and counterplay

The preserved knowledge boundary is:
- owned economy, force, construction and physical supply are directly assessed;
- public authored map sites/starts can guide reconnaissance and AttackMove;
- detected contacts provide permitted last-known coordinates only;
- direct attacks/raid targets resolve only current faction-identified entities;
- lost contacts retain their legal last-known position and cannot track hidden
  movement or reused entity generations;
- known guards, retreat/resupply needs, blocked routes and insufficient return
  Fuel can deny or terminate a raid.

The existing strategic order remains critical economy, bootstrap, defense,
expansion, force recovery, reconnaissance and offense. Core logistics recovery
also runs between strategic decisions. Paid research and raid selection retain
their own admission conditions and commands. Factory replacement/blocked
production cannot obtain free stock; critical fleet recovery retains precedence
through the existing queue/cancel/priority and physical stock-policy owners.

There is no experimental omniscient handicap. The explicitly named Validation
scenario still has its pre-existing accelerated noncompetitive starting stock;
applying a difficulty policy does not convert that scenario into normal-stock
fairness evidence. Qualification of fair resources uses Gameplay stock.

## Qualification-only diagnostics

CaptureQualificationDecisions returns copied, read-only observations only when
the existing opponent debug capture is enabled, bounded to 16 controllers.
It contains profile key, player, observed/last/next decision tick, count,
strategic cause/goal, offensive admission, supply requirement, raid admission
and contact budget. It contains no hostile entity, position, raw hidden stock,
weapon target or selected-player-dependent disclosure.

Cause and goal names are human-readable in JSON. CadenceWait means no new
strategic evaluation at that observed tick; it does not claim critical logistics
or unit behavior did no work. Other causes identify the actual selected branch:
economy recovery, bootstrap, current threat, expansion, supply recovery,
reconnaissance, objective pressure or offensive admission.

The existing headless schema-3 diagnostic report has an optional
Matches[].OpponentDecisions projection when debug capture is available. No
GameplayTelemetry/pacing schema or shared runner/parser was changed. Existing
headless diagnostic output can qualify the default policy:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --scenario central-divide --profile gameplay --ticks 128 --seed 17 --diagnostics-output artifacts/opponent-diagnostics.json
```

This remains developer qualification data. No player UI, alert, spectator or
replay export consumes the projection. It is derived, not authoritative or
persisted state, and cannot issue commands or change a match.

## Qualification commands

After the canonical Release build:

```powershell
dotnet test --project tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Game.Tests.OpponentDifficultyProfileTests ForgeLine.Game.Tests.SkirmishRaidPlannerTests ForgeLine.Game.Tests.SkirmishProductionPolicyTests --parallel none
$env:FORGELINE_OPPONENT_COHORT='1'
$env:FORGELINE_OPPONENT_COHORT_OUTPUT=(Join-Path (Get-Location) 'artifacts/opponent-cohort')
dotnet test --project tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Game.Tests.OpponentDifficultyQualificationTests --parallel none
```

The opt-in fixture uses MatchRuntime directly, not a second game/benchmark runner.
It runs three seeds with both Easy/Hard start assignments, normal Gameplay stock,
a fixed 80000-tick ceiling and cooperative test cancellation. Each result records
completed/censored status, winner, full final state hash, observed wall time,
existing work/cargo counters and bounded decision metadata. Censoring is never
counted as a win or draw. Wall times are observational, not a controlled speed
comparison. The canonical terminal CI gate remains unchanged.

See [recorded qualification and limitations](OpponentDifficultyQualification.md).
