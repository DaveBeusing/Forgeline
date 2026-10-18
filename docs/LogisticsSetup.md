# Logistics setup and operation

Constructing a building spends Command Core construction stock. It does not fill the new building's local production inventory. Cargo Trucks replenish building inputs through regional distribution; Supply Trucks carry Fuel/Ammunition for field recipients and use a separate drive tank. A Supply Truck is not an available regional Cargo Truck.

## First production chain

1. Use BUILD to construct power, a Smelter and a Vehicle Factory at valid sites. Suitable buildings connect automatically to nearby road nodes. There is no manual CONNECT ROUTE step.
2. Select the Smelter, open LOGISTICS and select the INPUTS card for its Steel recipe. USE INPUTS prepares editor values from the current recipe. Apply the required resource with APPLY POLICY. Choose enough additional steps for the downstream requirement, using the recipe output; PROCESS selects the recipe, its production mode and QUEUE after local inputs arrive.
3. Select the Vehicle Factory and open LOGISTICS. Page to INPUTS: Scout Vehicle, choose the number of additional steps with the existing decrease/increase controls, then USE INPUTS. The default is one additional unit, with no extra buffer. An active reserved unit is separate from these additional steps.
4. Select each required resource and use APPLY POLICY. Preparation creates no policy or order. Saved manual rules remain unchanged until that resource is submitted. Each command reports its own pending/accepted/rejected outcome; accepting one resource does not mean every resource was applied. Reapplying updates the same target/resource policy.
5. Watch local/free/reserved stock, requested demand and assigned incoming loads separately. Cargo Trucks choose their real source and assignment automatically. A request is not a delivery; incoming stock is not local factory input.
6. When the copied unit card has all available local inputs, open UNITS, select Scout Vehicle and QUEUE. Actual production reserves inputs and consumes them through its normal lifecycle.
7. Replenish a Supply Depot's Fuel/Ammunition with LOGISTICS and real Cargo Trucks. Supply Trucks load physically at a suitable building bay; SUPPLY exposes field resupply. Their own drive tank remains distinct from field cargo and assigned provider stock.

All requirements and explanatory cost lists come from the active recipe/unit catalog. A changed catalog updates the preview; there is no fixed Scout cost table in help. Additional steps range from 1 to 100. A preview includes current reservations (still physically stored until consumption) and outstanding demand, without adding assigned loads twice. It rejects a proposal whose combined projected occupancy exceeds shared storage. Free space is not allocated independently to every material. Existing stock unrelated to the recipe also occupies storage; consume or move it before retrying a rejected proposal.

## Policy meanings

- MIN triggers replenishment when stock plus outstanding requests falls below it.
- TARGET is the refill goal.
- MAX is validated and saved policy metadata. The current dispatcher does not enforce it as a hard inventory ceiling.
- Shared inventory capacity is a separate physical storage contract. It still limits unloading.

INPUTS cards prepare drafts only. A draft target includes materials for the selected additional steps and the inputs still reserved by active work. The current stored rule and its command outcome remain separate from the editor. Closing, losing the selection or changing the session discards the draft. Opening another building cannot inherit its values.

## Read the actual material path

Selected owned Cargo Trucks expose the catalog material, trip quantity, actually loaded cargo, real source/destination and copied service state. Policies show the selected source and assigned Cargo Truck when known. Details never reveal a foreign source inventory or hidden unit. Unobserved access remains explicitly unobserved: an enabled network node does not certify a route, physical approach or free bay.

WAITING FOR LOADING BAY, ALIGNING, LOADING, UNLOADING and LEAVING BAY use confirmed simulation queue/dock data. Queue position is not inferred from distance. An ordinary resource WAITING state is not a queue slot. Operations retains production queue counts separately from loading-bay waiting counts; regular FIFO waiting is not introduced as a new error notification.

NO SOURCE STOCK calls for checking free source surplus, production and reservations. NO TRUCK calls for checking Cargo work, bindings and drive Fuel; owned Cargo counts describe overlapping observations, not route-specific availability. NO ROUTE calls for checking infrastructure and actual access. CAPACITY is graph throughput, distinct from bay occupancy. Full input/output storage requires consumption or collection. NO INPUT opens existing Logistics controls; NO POWER and PAUSED retain the corresponding production/power context.

Use OPERATIONS, the relevant category or BLOCKED, select a row, FOCUS FACILITY and OPEN EXISTING CONTROLS. Source-endpoint focus uses existing authorized navigation. Focusing changes no world orders. Existing nonmodal close/outside-click behavior, world interaction and remapped bindings remain in force.

## Verification

ProductionStockProposalTests cover shared capacity, reservations, outstanding demand and catalog/step changes. PlayerActionReadModelTests cover captured catalog inputs, ownership and separate Supply cargo/drive inventories. PlayerActionPanelTests and GameplayInteractionJourneyTests cover preview versus submission, target replacement, additional steps and remapped activation. PlayerConstructionProductionFlowTests exercise the insufficient old defaults followed by catalog-based policies, real Core-to-Smelter input delivery, Steel production and Cargo delivery to the first Scout, plus the existing physical forward-supply recovery chain.

Native LogisticsInputs and LogisticsPolicyTooltip states extend the existing reference lane. Authored UI fixtures demonstrate structure; they do not replace the actual transport regression or human motion, legibility and physical-display acceptance. Required full CI and natural terminal-match gates remain unchanged.
