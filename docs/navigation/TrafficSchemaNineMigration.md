# Authoritative traffic schema 9

Schema 8 did not describe traffic history, the original route-search start rotation or the now-retained source request on active routes. Schema 9 explicitly adds these authoritative facts and the sorted navigation.traffic domain. Runtime units also spawn at their authored suspension height, preventing an inconsistent terrain-height transition on their first precise movement. These are declared simulation-state changes, not a map-content migration.

The save/replay envelope version, map revision, map bytes and scenario descriptor identity remain unchanged. Old authoritative schema 8 must not be accepted as schema 9. Recovery continues through the existing command-history reconstruction and state-hash verification.

For the compiled central-divide validation scenario, seed 2026, independent runtime reconstruction produced identical initial and tick-20 hashes before replacing golden values. Serialized save/replay reconstruction of active traffic at tick 950 also produced the same hash as the original, followed by independent matching continuation after 1, 30 and 200 additional ticks. The geography test still requires the original descriptor identity, matching captured map artifact, 71 reachability checks, two alternate-navigation checks and two alternate-road checks.

| Checkpoint | Prior schema 8 | Schema 9 |
| --- | --- | --- |
| Initial | DD7193BEFC6B8357EFC22D156E57C3F53E9C94E297F200A75B71763082CFE4D0 | D17801D6414BD8CDD521DD640A95FA31BADBBBC14BD782B3926E13D8973089A1 |
| Tick 20 | B994E0FD0195766BB241EF64957B83B7F11352272D6913AA0D575E61DAD516A3 | BBE06AEF61EBDF3E6798DDDA9EF0EB19AEAEC227E83DBA943DDDAC2EC4C1F29C |

Transient captured body frames, fixed-pose occupancy checks, swept enclosures and diagnostic veto observations are rebuilt each tick. They do not introduce serialized wall-clock timings or presentation state. The authoritative traffic checkpoint validates its bounded full-identity records before replacing live state.

The combined lifecycle/traffic branch also independently compares seed 831 at tick 4 with and without a presentation observer. Its current hash is `954107A5D4F4648BF08110F48824AB7C702A42871863B2501852EC6417C58FF4`; the lifecycle-only checkpoint `26F32C324EAEF026D560BD7354789A53524D9ECE454AC368109111CAEC33EF15` is retained as a rejected historical value. The two independent current captures agree before the fixed checkpoint is checked.
