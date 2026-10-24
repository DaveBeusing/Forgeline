# Logistics setup and operation

Constructing a building spends Command Core construction stock. It does not fill the new building's local production inventory. Cargo Trucks replenish building inputs through regional distribution; Supply Trucks carry Fuel/Ammunition for field recipients and use a separate drive tank. A Supply Truck is not an available regional Cargo Truck.

## World loading and holding visualization

When an owned logistics building or assigned vehicle is selected or hovered, the game can show its currently validated loading area (yellow diagonal hatch) and assigned queue/holding positions (cyan reverse hatch). A blocked or unavailable copied assignment uses a warning tint; the hatch itself is visual guidance, not a new route or placement command. The active Logistics context may reveal a bounded subset of these current, authoritative zones without permanently painting all yards. No hatch is created for an empty/unvalidated loading bay or an unassigned vehicle, and the printed shape is never used to determine occupancy. Existing `WAITING FOR LOADING BAY`, `ALIGNING`, `LOADING` and `LEAVING BAY` statuses remain authoritative.

## Optional selected-facility recovery rules

- Missing recipe input: select the owned processor, queue the intended recipe, open Operations → RULES → NEW DRAFT → INPUT STOCK POLICY. Select the actual recipe input, choose MIN/TARGET/MAX, priority and cooldown, then APPLY. Minimum zero is supported. A confirmed policy is not loaded or delivered cargo; existing disabled policies and manually removed policies remain under manual control.
- Full output: select the processor, open RULES, choose OUTPUT RECOVERY and APPLY. Observe the exact managed request and last successful action in DETAILS. Clear the actual output through normal consumption/collection; the rule resumes only its own request. A manual pause/resume suspends automation. DISABLE or REMOVE cannot undo that manual pause.

These controls reuse the normal command gateway and finite production/distribution systems. No rule is active merely because its local draft is visible. See [Operations rules and recovery](OperationsAutomation.md).

## First production chain

1. Use BUILD to construct power, a Smelter and a Vehicle Factory at valid sites. Suitable buildings connect automatically to nearby road nodes. There is no manual CONNECT ROUTE step.
2. Select the Smelter, open LOGISTICS and select the INPUTS card for its Steel recipe. USE INPUTS prepares editor values from the current recipe. Apply the required resource with APPLY POLICY. Choose enough additional steps for the downstream requirement, using the recipe output; PROCESS selects the recipe, its production mode and QUEUE after local inputs arrive.
3. Select the Vehicle Factory and open LOGISTICS. Page to INPUTS: Scout Vehicle, choose the number of additional steps with the existing decrease/increase controls, then USE INPUTS. The default is one additional unit, with no extra buffer. An active reserved unit is separate from these additional steps.
4. Select each required resource and use APPLY POLICY. Preparation creates no policy or order. Saved manual rules remain unchanged until that resource is submitted. Each command reports its own pending/accepted/rejected outcome; accepting one resource does not mean every resource was applied. Reapplying updates the same target/resource policy.
5. Watch local/free/reserved stock, requested demand and assigned incoming loads separately. Cargo Trucks choose their real source and assignment automatically. A request is not a delivery; incoming stock is not local factory input.
6. When the copied unit card has all available local inputs, open UNITS, select Scout Vehicle and QUEUE. Actual production reserves inputs and consumes them through its normal lifecycle.
7. Replenish a Supply Depot's Fuel/Ammunition with LOGISTICS and real Cargo Trucks. Supply Trucks load physically at a suitable building bay; SUPPLY exposes field resupply. Their own drive tank remains distinct from field cargo and assigned provider stock.

All requirements and explanatory cost lists come from the active recipe/unit catalog. A changed catalog updates the preview; there is no fixed Scout cost table in help. Additional steps range from 1 to 100. A preview includes current reservations (still physically stored until consumption) and outstanding demand, without adding assigned loads twice. It rejects a proposal whose combined projected occupancy exceeds shared storage. Free space is not allocated independently to every material. Existing stock unrelated to the recipe also occupies storage; consume or move it before retrying a rejected proposal.

## Manual quantity editing

Opening LOGISTICS for a resource without a saved stock policy prepares a local
MIN 50 / TARGET 100 / MAX 150 draft. The draft is not saved and does not place
an order until APPLY POLICY is submitted and accepted. If a known shared total
inventory capacity is 100–149, the default is 50/100/100; at 50–99 it is
50/50/50. Below 50 there is no valid new manual draft. An unspecified capacity
does not mean the inventory has zero physical space.

Choose the field with FIELD and use the existing increase/decrease controls
(or their configured keys). Manual threshold edits land on exact 50-unit
boundaries. Existing saved values, including zero and values such as 75,
remain unchanged until that particular field is edited. Editing never silently
changes another threshold. MIN must be no greater than TARGET, and TARGET no
greater than MAX. If an increase is blocked, raise the next threshold first
(for example, raise MAX before TARGET). When a decrease is blocked, lower the
previous threshold first. An unavailable step is shown disabled with a reason.
The total inventory capacity also restricts increasing MAX; the editor never
substitutes an incomplete step such as 25.

INPUTS continues to derive actual recipe materials and its additional production
steps remain 1–100 in single steps, independent of the manual quantity grid.
ORDER entries remain read-only. Requested, reserved, loaded and delivered cargo
are real quantities selected by the logistics simulation and need not be
multiples of 50. No editor change itself guarantees a delivery.

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

Confirmed access obstructions, missing drive Fuel and blocked exits enter OPERATIONS / BLOCKED. Access and storage blocks focus the copied loading-building identity; a missing drive-Fuel block focuses the truck and opens Supply. Normal FIFO waiting remains informational. Static providers show their own real Fuel/Ammunition stock and use Logistics policies; recipient resupply commands are unavailable for buildings.

Logistics and Supply tooltip details wrap within the viewport instead of truncating each explanation to one line. Catalog input tooltips include all four Scout materials; compact/high-DPI qualification uses the same measured wrapping as drawing.

## Fifty-unit policy editing and current transport orders

The LOGISTICS radial quantity controls now propose policy quantities in 50-unit steps, starting at 50. Decrease never changes an already edited positive value to less than 50. Values already stored in an existing policy are preserved exactly, including a zero minimum or quantities that are not multiples of 50; the editor only changes the field the player adjusts, without moving another threshold. To raise TARGET beyond MAX, increase MAX first. `INPUTS` additional steps are still recipe-based units (1–100), not cargo stacks; the preview remains a proposal until each policy is explicitly applied. A desired stock target is **not** an instruction to transport exactly that quantity on one truck: dispatch respects current deficits, cargo capacity, source surplus, physical route and destination capacity.

The LOGISTICS radial item pages now continue beyond resource policies and INPUTS to read-only `ORDER` entries for live requests of the selected locally owned building or assigned Cargo Truck. The category reports the count. Open an order's Parameters for its stable ID, resource, requested/loaded quantities, actual state, assigned truck and own permitted origin/destination. `PENDING` is only queued demand, `ASSIGNED` reserves transport, `IN TRANSIT` is physical work, and `RETRY` or `FAILED` displays the recorded reason (e.g., NO TRUCK, NO ROUTE, CAPACITY or DEST FULL). Neither accepted stock-policy commands nor reserved/announced quantities prove a completed delivery. At most 128 order entries are copied per selected object; the full count is still reported. The outgoing/absent or unauthorized selection exposes no current order details.

## Cargo fleet capacity diagnosis

A `NO TRUCK` retry does not by itself prove an undersized fleet: distinguish trucks busy on legitimate delivery, usable idle trucks, vehicle propulsion Fuel starvation, inaccessible routes, missing source stock, full destinations and FIFO bay occupancy. The `INSUFFICIENT CARGO TRANSPORT CAPACITY` warning appears only after persistent, nonshrinking actionable demand and a completely occupied, viable owned fleet. Its details list current backlog, active, idle and effective truck counts and point to an owned affected facility. First inspect LOGISTICS order causes; add Cargo Trucks only when the alert persists despite valid stock, traversable routes, working Fuel and usable loading bays. The alert never purchases or spawns vehicles.

## Reading local demand and transport progress

For an owned selected facility or Cargo Truck, the focused [Operations Diagnosis](OperationsDiagnosis.md) copies the current completed-tick state without changing any orders. **Local stock** is the physically present quantity; **free stock** excludes reservations. **Demand** is the configured enabled target minus local stock. **Outstanding requests** already include assigned work; **confirmed incoming** is part of that outstanding quantity and must not be added again. **Loaded cargo is not delivery**—only an actual unload increases the destination inventory. When the source inventory, route, truck, or loading bay cannot be verified from the current copied state, the diagnosis reports it as unobserved rather than guessing.

## Optional operations rules

The opt-in [Operations Rules and Recovery](OperationsAutomation.md) automation can install one missing explicitly configured input stock policy or temporarily pause and resume an output-blocked production request. It uses the existing validated commands, does not bypass physical transport, and yields to manual overrides. Normal production and distribution systems already stop and retry their own work; enabling a rule is not required for normal operation.
