# Guided strategy practice

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

Choose an optional **Practice** scenario in New Game. Central Divide remains the first selection and starts normal skirmish immediately. Practice uses the registered compiled Central Divide terrain and current assets, with two human participants and no strategic opponent automation.

| Scenario key | Completed-tick outcomes |
| --- | --- |
| practice.command-recon.v1 | Move the Scout at least 20 m; identify the stationary training opponent through owned sensors; apply actual damage. |
| practice.build-production.v1 | Complete a generating Power Plant; complete a Smelter; complete a real Steel production cycle. |
| practice.supply-recovery.v1 | Deliver Ferrous Ore by conveyor into a Smelter; complete a later processing cycle in that same Smelter; physically load the authored Supply Truck; transfer battlefield ammunition into the authored squad. |

Hints explain the next step and why an outcome counts. Read, Later, Next and hiding guidance never complete an objective. Existing binding prompts and card pagination apply. Pause offers **Retry practice** and **Skip practice / Menu**. Retry creates a fresh session using the lesson settings. Returning to the menu permits normal skirmish selection.

## Normal commands and finite setup

Practice starts with standard finite stock, with Steel set to 1,800. There are no recurring grants, free transfers, instant construction or altered weapon rules. Industry setup purchases one Extractor. Supply setup purchases two Power Plants, an Extractor and a Smelter through ordinary placement, payment and construction. Their construction takes normal simulation time. Placement or funding failure produces an explicit retry explanation.

For Command & Recon, select the Scout and move east along the road towards the public training area at X 1020, Z 1536. Attack Move can establish contact and engage. The stationary training opponent holds fire; normal fog of war, range and ammunition rules apply.

For Industry, build working power and a Smelter, then use Logistics / Inputs to arrange Ore delivery with Cargo or a compatible conveyor. Existing starting Steel cannot confirm production.

For Supply Recovery, wait for setup construction and connect Extractor output to Smelter input using the normal conveyor tool. A later cycle must occur in that receiving Smelter. Request supply for the empty Supply Truck at the Core. Wait until it finishes leaving the loading bay, then move it near the Rifle Squad east of the Core and request squad resupply. The squad starts empty with automatic recovery disabled so it waits for the player's mobile supply plan. Recovering that squad directly from a fixed provider produces a retry explanation: this lesson requires the physical mobile leg. Local conveyor processing and battlefield truck delivery are separate objectives.

Owned critical unit/Core loss, setup rejection or observation overflow fails explicitly. Failure never grants a completed objective; Retry restores the authored initial state.

## Save, replay and headless

Save payload 12 optionally carries PracticeProgress. Practice saves require matching lesson/version/player/tick, bounded timestamps and a consistent status. Restore reconstructs the registered fixture and recorded commands, then compares derived progress; corrupt or divergent progress is rejected. Replay reconstructs the same observer. Envelope 1 and authoritative schema 18 remain unchanged; older ordinary skirmish saves retain their existing compatibility path.

Headless accepts the three scenario keys through --scenario. Existing --telemetry-output includes the copied optional Practice snapshot. Ordinary match telemetry omits it.

See [the observation contract](adr/BoundedGuidedPractice.md). Native authored-card captures and actual command walkthroughs qualify different boundaries; screenshots alone do not establish objective completion.
