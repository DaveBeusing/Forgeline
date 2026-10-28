# Strategic map audit

Offline geometry diagnostics; no win-rate, travel-time, physical vehicle or complete fairness guarantee. Null distances mean no measured route. Warning exceptions retain the original diagnostic.

## prototype.vertical_slice revision 2: ReviewRequired

Map SHA-256: 0f64c67943ddd0e399f7cc7579f65c43feded449e6bb43182de3bdd9ff4d0682; terrain SHA-256: d9ff8d68132b7c8a48936cf751552044ca6befe8b4359b1dd0b736de2bc67a33; content SHA-256: 205d0887e816d8922c43996a6a8ad85d177cbbf168e551a87ce76d9cc81efd63.
Authored deterministic terrain recipe; no RNG or match seed is consumed.
Grid 16 m / sectors 8 cells; local opening <= 450 m; asymmetry warning ratio > 1.25.

| Movement | Player | Resource | Local deposit | Route m | Quantity | Extraction/s |
|---|---:|---:|---|---:|---:|---:|
| Infantry | 1 | 1 | west.ferrous | 263.152 | 5500 | 7.5 |
| Infantry | 1 | 2 | west.volatiles | 205.35 | 3500 | 5 |
| Infantry | 1 | 3 | west.silicates | 351.241 | 4000 | 5.5 |
| Infantry | 2 | 1 | east.ferrous | 283.889 | 5500 | 7.5 |
| Infantry | 2 | 2 | east.volatiles | 212.151 | 3500 | 5 |
| Infantry | 2 | 3 | east.silicates | 347.239 | 4000 | 5.5 |
| Wheeled | 1 | 1 | west.ferrous | 263.152 | 5500 | 7.5 |
| Wheeled | 1 | 2 | west.volatiles | 205.35 | 3500 | 5 |
| Wheeled | 1 | 3 | west.silicates | 351.241 | 4000 | 5.5 |
| Wheeled | 2 | 1 | east.ferrous | 283.889 | 5500 | 7.5 |
| Wheeled | 2 | 2 | east.volatiles | 212.151 | 3500 | 5 |
| Wheeled | 2 | 3 | east.silicates | 347.239 | 4000 | 5.5 |
| Tracked | 1 | 1 | west.ferrous | 263.152 | 5500 | 7.5 |
| Tracked | 1 | 2 | west.volatiles | 205.35 | 3500 | 5 |
| Tracked | 1 | 3 | west.silicates | 351.241 | 4000 | 5.5 |
| Tracked | 2 | 1 | east.ferrous | 283.889 | 5500 | 7.5 |
| Tracked | 2 | 2 | east.volatiles | 212.151 | 3500 | 5 |
| Tracked | 2 | 3 | east.silicates | 347.239 | 4000 | 5.5 |

| Movement | Player | Reachable permitted yard samples m² |
|---|---:|---:|
| Infantry | 1 | 238080 |
| Infantry | 2 | 238080 |
| Wheeled | 1 | 238080 |
| Wheeled | 2 | 238080 |
| Tracked | 1 | 238080 |
| Tracked | 2 | 238080 |


| Movement | Player | Expansion resource | Reachable deposits | Nearest deposit | Route m |
|---|---:|---:|---:|---|---:|
| Infantry | 1 | 1 | 2 | north.contested.ferrous | 1348.041 |
| Infantry | 1 | 2 | 2 | south.contested.volatiles | 1333.476 |
| Infantry | 1 | 3 | 3 | center.contested.silicates | 1061.814 |
| Infantry | 1 | 8 | 2 | center.rare_elements | 1143.88 |
| Infantry | 2 | 1 | 2 | south.contested.ferrous | 1400.218 |
| Infantry | 2 | 2 | 2 | north.contested.volatiles | 1304.868 |
| Infantry | 2 | 3 | 3 | east.outpost.silicates | 1129.184 |
| Infantry | 2 | 8 | 2 | center.rare_elements | 1902.163 |
| Wheeled | 1 | 1 | 2 | north.contested.ferrous | 1348.041 |
| Wheeled | 1 | 2 | 2 | south.contested.volatiles | 1333.476 |
| Wheeled | 1 | 3 | 3 | center.contested.silicates | 1061.814 |
| Wheeled | 1 | 8 | 2 | center.rare_elements | 1143.88 |
| Wheeled | 2 | 1 | 2 | south.contested.ferrous | 1400.218 |
| Wheeled | 2 | 2 | 2 | north.contested.volatiles | 1304.868 |
| Wheeled | 2 | 3 | 3 | east.outpost.silicates | 1129.184 |
| Wheeled | 2 | 8 | 2 | center.rare_elements | 1902.163 |
| Tracked | 1 | 1 | 2 | north.contested.ferrous | 1348.041 |
| Tracked | 1 | 2 | 2 | south.contested.volatiles | 1333.476 |
| Tracked | 1 | 3 | 3 | center.contested.silicates | 1061.814 |
| Tracked | 1 | 8 | 2 | center.rare_elements | 1143.88 |
| Tracked | 2 | 1 | 2 | south.contested.ferrous | 1400.218 |
| Tracked | 2 | 2 | 2 | north.contested.volatiles | 1304.868 |
| Tracked | 2 | 3 | 3 | east.outpost.silicates | 1129.184 |
| Tracked | 2 | 8 | 2 | center.rare_elements | 1902.163 |

| Crossing state | Movement | Player | Reached targets | All targets |
|---|---|---:|---:|---:|
| Baseline | Infantry | 1 | 26 | 26 |
| Baseline | Infantry | 2 | 26 | 26 |
| Baseline | Wheeled | 1 | 26 | 26 |
| Baseline | Wheeled | 2 | 26 | 26 |
| Baseline | Tracked | 1 | 26 | 26 |
| Baseline | Tracked | 2 | 26 | 26 |
| Closed:crossing.north_bridge | Infantry | 1 | 26 | 26 |
| Closed:crossing.north_bridge | Infantry | 2 | 26 | 26 |
| Closed:crossing.north_bridge | Wheeled | 1 | 26 | 26 |
| Closed:crossing.north_bridge | Wheeled | 2 | 26 | 26 |
| Closed:crossing.north_bridge | Tracked | 1 | 26 | 26 |
| Closed:crossing.north_bridge | Tracked | 2 | 26 | 26 |
| Closed:crossing.south_ford | Infantry | 1 | 26 | 26 |
| Closed:crossing.south_ford | Infantry | 2 | 26 | 26 |
| Closed:crossing.south_ford | Wheeled | 1 | 26 | 26 |
| Closed:crossing.south_ford | Wheeled | 2 | 26 | 26 |
| Closed:crossing.south_ford | Tracked | 1 | 26 | 26 |
| Closed:crossing.south_ford | Tracked | 2 | 26 | 26 |
| AllCrossingsClosed | Infantry | 1 | 14 | 26 |
| AllCrossingsClosed | Infantry | 2 | 14 | 26 |
| AllCrossingsClosed | Wheeled | 1 | 14 | 26 |
| AllCrossingsClosed | Wheeled | 2 | 14 | 26 |
| AllCrossingsClosed | Tracked | 1 | 14 | 26 |
| AllCrossingsClosed | Tracked | 2 | 14 | 26 |
| Crossing state | From | To | Widest road-path capacity/s |
|---|---:|---:|---:|
| Baseline | 1 | 2 | 120 |
| Baseline | 2 | 1 | 120 |
| Closed:crossing.north_bridge | 1 | 2 | 90 |
| Closed:crossing.north_bridge | 2 | 1 | 90 |
| Closed:crossing.south_ford | 1 | 2 | 120 |
| Closed:crossing.south_ford | 2 | 1 | 120 |
| AllCrossingsClosed | 1 | 2 | 0 |
| AllCrossingsClosed | 2 | 1 | 0 |

- Warning [DisconnectedStarts|AllCrossingsClosed|Infantry|1|start:2]: No measured route between starts in this crossing state.
- Warning [DisconnectedStarts|AllCrossingsClosed|Infantry|2|start:1]: No measured route between starts in this crossing state.
- Warning [DisconnectedStarts|AllCrossingsClosed|Wheeled|1|start:2]: No measured route between starts in this crossing state.
- Warning [DisconnectedStarts|AllCrossingsClosed|Wheeled|2|start:1]: No measured route between starts in this crossing state.
- Warning [DisconnectedStarts|AllCrossingsClosed|Tracked|1|start:2]: No measured route between starts in this crossing state.
- Warning [DisconnectedStarts|AllCrossingsClosed|Tracked|2|start:1]: No measured route between starts in this crossing state.
- Warning [RoadCut|AllCrossingsClosed|Road|1|start:2]: No positive-capacity road path between the nearest authored start nodes.
- Warning [RoadCut|AllCrossingsClosed|Road|2|start:1]: No positive-capacity road path between the nearest authored start nodes.
- Warning [ExpansionDistance|Baseline|Infantry|0|resource:8]: Across starts: minimum=1143.88, maximum=1902.163; exceeds ratio 1.25. Review deliberate asymmetry; this is not a fairness certificate.
- Warning [ExpansionDistance|Baseline|Wheeled|0|resource:8]: Across starts: minimum=1143.88, maximum=1902.163; exceeds ratio 1.25. Review deliberate asymmetry; this is not a fairness certificate.
- Warning [ExpansionDistance|Baseline|Tracked|0|resource:8]: Across starts: minimum=1143.88, maximum=1902.163; exceeds ratio 1.25. Review deliberate asymmetry; this is not a fairness certificate.

Errors: 0; review warnings: 11; accepted warnings: 0. Complete route matrix is in the accompanying JSON.

## battlefield.split_basin revision 2: ReviewRequired

Map SHA-256: 635ca23e5fa63285c4bb6b7e4b00b8615804af67cf638ee48e36dda21573a36e; terrain SHA-256: ee7f0c7e872c2e32528729eca98171ebbcbd9d0d4d846a007fb4d5eb0d9562cd; content SHA-256: 205d0887e816d8922c43996a6a8ad85d177cbbf168e551a87ce76d9cc81efd63.
Authored deterministic terrain recipe; no RNG or match seed is consumed.
Grid 16 m / sectors 8 cells; local opening <= 450 m; asymmetry warning ratio > 1.25.

| Movement | Player | Resource | Local deposit | Route m | Quantity | Extraction/s |
|---|---:|---:|---|---:|---:|---:|
| Infantry | 1 | 1 | west.ferrous | 268 | 5500 | 7.5 |
| Infantry | 1 | 2 | west.volatiles | 206.318 | 3500 | 5 |
| Infantry | 1 | 3 | west.silicates | 386.678 | 4000 | 5.5 |
| Infantry | 2 | 1 | east.ferrous | 304.333 | 5500 | 7.5 |
| Infantry | 2 | 2 | east.volatiles | 203.961 | 3500 | 5 |
| Infantry | 2 | 3 | east.silicates | 432.333 | 4000 | 5.5 |
| Wheeled | 1 | 1 | west.ferrous | 268 | 5500 | 7.5 |
| Wheeled | 1 | 2 | west.volatiles | 206.318 | 3500 | 5 |
| Wheeled | 1 | 3 | west.silicates | 386.678 | 4000 | 5.5 |
| Wheeled | 2 | 1 | east.ferrous | 304.333 | 5500 | 7.5 |
| Wheeled | 2 | 2 | east.volatiles | 203.961 | 3500 | 5 |
| Wheeled | 2 | 3 | east.silicates | 432.333 | 4000 | 5.5 |
| Tracked | 1 | 1 | west.ferrous | 268 | 5500 | 7.5 |
| Tracked | 1 | 2 | west.volatiles | 206.318 | 3500 | 5 |
| Tracked | 1 | 3 | west.silicates | 386.678 | 4000 | 5.5 |
| Tracked | 2 | 1 | east.ferrous | 304.333 | 5500 | 7.5 |
| Tracked | 2 | 2 | east.volatiles | 203.961 | 3500 | 5 |
| Tracked | 2 | 3 | east.silicates | 432.333 | 4000 | 5.5 |

| Movement | Player | Reachable permitted yard samples m² |
|---|---:|---:|
| Infantry | 1 | 145920 |
| Infantry | 2 | 145920 |
| Wheeled | 1 | 145920 |
| Wheeled | 2 | 145920 |
| Tracked | 1 | 145920 |
| Tracked | 2 | 145920 |


| Movement | Player | Expansion resource | Reachable deposits | Nearest deposit | Route m |
|---|---:|---:|---:|---|---:|
| Infantry | 1 | 1 | 2 | west.contested.ferrous | 567.972 |
| Infantry | 1 | 2 | 2 | west.contested.volatiles | 543.737 |
| Infantry | 1 | 3 | 2 | west.outpost.silicates | 1004.991 |
| Infantry | 2 | 1 | 2 | east.contested.ferrous | 574.725 |
| Infantry | 2 | 2 | 2 | east.contested.volatiles | 550.49 |
| Infantry | 2 | 3 | 2 | east.outpost.silicates | 1011.744 |
| Wheeled | 1 | 1 | 2 | west.contested.ferrous | 567.972 |
| Wheeled | 1 | 2 | 2 | west.contested.volatiles | 543.737 |
| Wheeled | 1 | 3 | 2 | west.outpost.silicates | 1004.991 |
| Wheeled | 2 | 1 | 2 | east.contested.ferrous | 574.725 |
| Wheeled | 2 | 2 | 2 | east.contested.volatiles | 550.49 |
| Wheeled | 2 | 3 | 2 | east.outpost.silicates | 1011.744 |
| Tracked | 1 | 1 | 2 | west.contested.ferrous | 567.972 |
| Tracked | 1 | 2 | 2 | west.contested.volatiles | 543.737 |
| Tracked | 1 | 3 | 2 | west.outpost.silicates | 1004.991 |
| Tracked | 2 | 1 | 2 | east.contested.ferrous | 574.725 |
| Tracked | 2 | 2 | 2 | east.contested.volatiles | 550.49 |
| Tracked | 2 | 3 | 2 | east.outpost.silicates | 1011.744 |

| Crossing state | Movement | Player | Reached targets | All targets |
|---|---|---:|---:|---:|
| Baseline | Infantry | 1 | 19 | 19 |
| Baseline | Infantry | 2 | 19 | 19 |
| Baseline | Wheeled | 1 | 19 | 19 |
| Baseline | Wheeled | 2 | 19 | 19 |
| Baseline | Tracked | 1 | 19 | 19 |
| Baseline | Tracked | 2 | 19 | 19 |
| Closed:crossing.basin_bridge | Infantry | 1 | 19 | 19 |
| Closed:crossing.basin_bridge | Infantry | 2 | 19 | 19 |
| Closed:crossing.basin_bridge | Wheeled | 1 | 19 | 19 |
| Closed:crossing.basin_bridge | Wheeled | 2 | 19 | 19 |
| Closed:crossing.basin_bridge | Tracked | 1 | 19 | 19 |
| Closed:crossing.basin_bridge | Tracked | 2 | 19 | 19 |
| Closed:crossing.outer_ford | Infantry | 1 | 19 | 19 |
| Closed:crossing.outer_ford | Infantry | 2 | 19 | 19 |
| Closed:crossing.outer_ford | Wheeled | 1 | 19 | 19 |
| Closed:crossing.outer_ford | Wheeled | 2 | 19 | 19 |
| Closed:crossing.outer_ford | Tracked | 1 | 19 | 19 |
| Closed:crossing.outer_ford | Tracked | 2 | 19 | 19 |
| AllCrossingsClosed | Infantry | 1 | 9 | 19 |
| AllCrossingsClosed | Infantry | 2 | 9 | 19 |
| AllCrossingsClosed | Wheeled | 1 | 9 | 19 |
| AllCrossingsClosed | Wheeled | 2 | 9 | 19 |
| AllCrossingsClosed | Tracked | 1 | 9 | 19 |
| AllCrossingsClosed | Tracked | 2 | 9 | 19 |
| Crossing state | From | To | Widest road-path capacity/s |
|---|---:|---:|---:|
| Baseline | 1 | 2 | 120 |
| Baseline | 2 | 1 | 120 |
| Closed:crossing.basin_bridge | 1 | 2 | 90 |
| Closed:crossing.basin_bridge | 2 | 1 | 90 |
| Closed:crossing.outer_ford | 1 | 2 | 120 |
| Closed:crossing.outer_ford | 2 | 1 | 120 |
| AllCrossingsClosed | 1 | 2 | 0 |
| AllCrossingsClosed | 2 | 1 | 0 |

- Warning [DisconnectedStarts|AllCrossingsClosed|Infantry|1|start:2]: No measured route between starts in this crossing state.
- Warning [DisconnectedStarts|AllCrossingsClosed|Infantry|2|start:1]: No measured route between starts in this crossing state.
- Warning [DisconnectedStarts|AllCrossingsClosed|Wheeled|1|start:2]: No measured route between starts in this crossing state.
- Warning [DisconnectedStarts|AllCrossingsClosed|Wheeled|2|start:1]: No measured route between starts in this crossing state.
- Warning [DisconnectedStarts|AllCrossingsClosed|Tracked|1|start:2]: No measured route between starts in this crossing state.
- Warning [DisconnectedStarts|AllCrossingsClosed|Tracked|2|start:1]: No measured route between starts in this crossing state.
- Warning [RoadCut|AllCrossingsClosed|Road|1|start:2]: No positive-capacity road path between the nearest authored start nodes.
- Warning [RoadCut|AllCrossingsClosed|Road|2|start:1]: No positive-capacity road path between the nearest authored start nodes.

Errors: 0; review warnings: 8; accepted warnings: 0. Complete route matrix is in the accompanying JSON.
