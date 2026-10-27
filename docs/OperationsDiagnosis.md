# Operations diagnosis and bounded chain observation

## Scope and authority

The diagnosis explains one owned, singly selected facility, conveyor or Cargo Truck at one completed simulation tick. It is a copied observation, not a second economic authority or a permanent UI graph of the base. Opening Operations, tracing a link and focusing an entity issue no simulation command, order, repair, stock transfer or notification.

The existing local production, policy, request and cargo rows remain available. A processing or unit-production facility expands an upstream material trace only while its actual block is NoInput. Healthy work, completed work, power failure and output-capacity failure retain their own local explanation rather than borrowing an unrelated input-chain cause. After material recovery the additional chain disappears; a remaining observed power or capacity block is still reported.

The selected player, session, completed tick and full entity generation must agree. New chain subjects, endpoints, inventory observations and cause identities must also be owned, controllable and present in the copied World-visible render instances. Conflicting building, conveyor or unit-factory ownership fails closed. A missing or hidden endpoint ends that observation: it supplies no stock, identity, position or private cause. Off-screen authorized world objects may be inspected, but their markers require actual on-screen projection.

## Stable conveyor facts

ConveyorSimulationSystem produces transient ConveyorFlowObservation values from the same connected nodes, inventories, budgets and policy limits used for the completed transfer tick. Up to three distinct immediate facts are retained in stable reason/entity/resource order:

- destroyed transport route, damaged span and destroyed supporting foot;
- unavailable drive power;
- an actually disconnected port;
- an observed rejecting resource at a connected building port;
- source inventory reserved by existing work;
- source stock retained by its existing export policy;
- full physical destination capacity;
- a destination stock target already committed, distinct from physical capacity;
- observed empty input, or Unknown when no more precise immediate fact is established.

A cause identity names the actual generation-stable piece, foot, source or destination. A first fact is not proof that no other constraint exists. Missing evidence stays unknown; an empty buffer is not evidence that an unseen supplier is empty. The projection redacts a fact whose cause object is not authorized.

These observations are not ECS components and are not serialized. The existing persisted ConveyorFlowState, including its historical Cause string, remains unchanged. Current player-facing diagnosis and the conveyor overview use the new observations instead of the old combined full/reserved/offline/rejecting explanation. At the current integration baseline, envelope 1, payload schema 8 and authoritative schema 10 remain unchanged by Operations observation. Earlier schema-6 statements described the original chain implementation, not the current save format.

## Links and production inputs

| Link kind | Evidence | Meaning |
| --- | --- | --- |
| ConveyorConnection | Current input/output identities from a conveyor actually observed this tick | A real directed port connection; no truck request or delivery promise |
| TransportRequest | A real current nonterminal request ID and authorized live endpoints | Existing requested work; not delivered stock |
| Existing OperationsRoute | Enabled or disabled logistics topology in the separate overview | Network context; not inserted as a speculative supplier edge |

ProductionInput rows identify the input inventory and a missing ingredient of the actual active recipe or unit definition. For an upstream processor, the active recipe must produce the resource being followed before its missing inputs can explain that resource. An unrelated recipe, idle producer or unavailable recipe observation ends that material explanation rather than becoming an invented dependency.

The collector indexes actual current conveyor connections and the distribution subsystem's current copied requests once during tick extraction. It follows relevant resource contexts from the selected object. It does not search for a nearby or theoretically suitable supplier, infer a missing truck request, predict a future production cycle, or treat planned topology as accepted transport.

## Quantities are independent observations

Rows describe different views of economic work and must not be summed across subjects.

- Demand is the local shortfall to an enabled policy target or the missing free amount of an observed active ingredient.
- LocalQuantity is physical destination stock. ReservedQuantity and FreeQuantity remain separate; free stock is local stock minus actual inventory reservations.
- OutstandingRequestQuantity is existing request demand. ConfirmedIncomingQuantity is the assigned/reserved or loaded subset already reported by distribution, not additional stock.
- LoadedCargoQuantity is observed transport load; it is not a completed delivery. Current truck/order generation, endpoints, resource and lifecycle guards remain required.
- SourceFreeQuantity is populated only when this tick establishes the particular authorized source inventory and resource. Null means not observed; 0 means a measured empty free amount. This is inventory-free stock, not an export-policy surplus, capacity promise or total base stock. A policy may retain physically free stock.
- A conveyor selected without a known resource does not fabricate per-resource stock. Queue positions and waiting counts still require actual copied service observations.

Link SourceInventory and DestinationInventory preserve the real port inventory identities where observation can establish them. The collector reuses the authoritative conveyor and cargo inventory resolvers and requires inventory continuity at the destination before extending a material chain. A real upstream request retains its current failure and quantity even if no source is known; it then supplies neither an invented link nor a measured source-free amount. Generic local quantities, source-free stock, requested work, reservations, incoming cargo and physical load are never combined into one availability value. The detail displays a source-free amount only when its source/resource/inventory observation is unambiguous; otherwise the independent copied rows and diagnostic output preserve the unknown or branching evidence.

## Bounded traversal and retained data

The existing budgets are unchanged: 128 copied rows, 64 copied links and at most eight upstream observation steps beyond the root. Resource contexts share this global trace budget; it is not eight steps for every branch.

The collector retains supported branches in stable endpoint/request order, tracks full-generation identities along each path, recognizes a cycle reached within the budget and stops at missing observations. It reports extra branches, omitted steps, omitted rows and omitted links. A cycle beyond the trace limit is a truncated observation, not a claim that the whole graph has been searched. Counts refer to omitted observation slots and visits, not a deduplicated count of possible root causes.

TraceUpstream returns at most eight copied links and visits both typed conveyor edges and real request edges. Multiple candidates do not become a guaranteed unique cause. Snapshot rows and links are copied and immutable, so a retained snapshot does not change after production, inventory, selection or session changes. ToDiagnosticLines includes current lifetime, source-free observation, inventory identity, cause identity and truncation information.

## Existing player workflow

Select the affected owned object and open Operations/BLOCKED. The detail names its immediate reported block and, for an actual missing ingredient, the copied catalog resource name. TRACE NEXT focuses an authorized immediate cause or observed upstream connection. Observed endpoints are inspected in stable order; the focus cursor does not promise exhaustive navigation through every branch. Extra branches, cycle, missing observation and omitted counts remain visible; navigation is not an automatic root-cause or repair decision.

The existing FOCUS FACILITY and OPEN EXISTING CONTROLS act on the inspected upstream facility. The current single selected facility is retained within the existing 256-facility overview cap, including when it lies beyond the first 256 stable entries. A held press binds its original target. At release the controller rechecks current session, tick, owner, World visibility and complete entity generation; a changed, hidden, foreign or recycled target cannot receive that gesture. Existing pause/help, modal, focus-loss, resize, session-change and row-change cancellation remain active. Marker positions come only from copied current render instances; no position is reconstructed for a missing endpoint.

Example: an Ammunition Plant reports NO INPUT / Electronics. TRACE NEXT follows its real conveyor back toward an Electronics Plant. Its active Electronics recipe reports missing Silicates with a measured free amount of zero. Inspect that plant's existing production/logistics controls and restore its real input or connection. A restored connection can expose a remaining NO POWER condition. Only actual material arrival and usable power resume the recipe; a requested delivery or a cleared warning does not. The existing recovery onboarding card uses this same workflow.

## Cost and rendering boundary

There are no per-frame ECS, inventory or production scans. Tick extraction copies the bounded diagnostic slice; render owners reuse copied marker/link arrays and lookup storage. The existing world overlay keeps 64 markers, 64 links, 12 facility captions (four at strategic zoom), four link captions and its 4,096-vertex primitive batch. Chain subjects take priority over unrelated blocked overview markers. Hidden, missing or off-screen endpoints do not receive invented marker geometry.

[Source-pinned CPU observations](OperationsChainMeasurements.json) record the selected-only projection, expanded chain projection and retained world-overlay composition in the existing dense owned-inventory fixture: 1,000 owned facilities and 1,783 copied instances. The fixture contains a real active missing-input recipe but no added conveyor network, so it does not measure the worst-case 4,096-module network. The real short/long paid-network cases and the maximal copied 128-row/64-link HUD fixture separately validate behavior and bounds.

Measurements are single in-process observations with 32 warmups and 128 samples on an unisolated host. They do not establish a universal latency budget, GPU/frame-rate performance, reference-hardware acceptance or human native readability. Zero warm managed allocation is checked for retained HUD/overlay composition; tick-side snapshot allocation is measured rather than presented as zero.

## Measured input delivery, wait and backlog windows

The existing Operations header now exposes FLOW >. Select and FOCUS an owned facility so that its normal selected diagnosis is current, keep Operations open and use FLOW > to cycle its observed input resources. The existing Tab focus sequence includes FLOW >; Activate advances the displayed resource. The header retains facility, route and shared-cause counts. Resource stock totals remain visible when no facility is inspected.

DEL is the actual quantity received by the named entity, input inventory and resource during the observed window. The /s value divides that quantity by observed simulation seconds. It does not sum resources, count source production, infer transport from connections or label loaded cargo as delivered. Both successful Cargo Truck unload chunks and successful conveyor transfers enter the journal directly at the inventory-transfer boundary. Multiple positive transfer quanta within the same tick are all included, independently of the existing milestone journal's coalescing. Intermediate conveyor buffers are separate receiving inventories, so their rates must never be added into a whole-chain delivery total.

The rolling maximum is 600 completed ticks. COVER shows the retained tick count out of 600 and actual simulation seconds; this is not wall-clock time. At a 50 ms tick, a full window spans 30 simulation seconds. A newly watched input starts after the current completed tick: DEL and /s are N/A until a complete subsequent tick has been observed. A measured zero is possible only after that observation. The scope follows the current selected diagnosis and the selected processor's compatible recipe inputs, including healthy recovery and cycle-completion ticks. Closing Operations, losing authorization, changing the receiving inventory or resource, removing the subject from the bounded diagnosis, or a gap in extraction ends that scope and starts fresh coverage when observed again. This is deliberately not a retrospective whole-match ledger.

WAIT is the fraction of known observed simulation time with a reported facility blocker. It is not an attribution that the displayed resource caused every blocked tick. It includes power, material, capacity, route and explicit pause blocks where the actual facility or policy observation establishes them. It is not a predicted delivery delay or generic utilization. Its adjacent tick count is independent from transfer coverage: unknown block states contribute neither healthy time nor waiting time.

BACKLOG is the remaining outstanding quantity of a single current observed stock policy, using the existing distribution read model after actual delivery progress. Its bracketed minimum/maximum and tick count cover only known backlog samples. Missing policies, stale distribution data, ambiguous policy views and unsupported aggregate request slices remain N/A. Policy shortfall, local stock and the original requested amount of an individual order are not substituted for remaining backlog.

REQ, RSV, CONF and LOAD retain separate meanings. REQ is current remaining requested policy work; RSV is reserved local inventory stock; CONF is the existing incoming reservation/load observation; LOAD is the copied remaining load of one unambiguous matching transport request. Ambiguous multiple requests keep LOAD unknown. These views overlap and are never added together. DEL remains the independent measured receiving-inventory quantity. Historical window values and current quantities therefore refer to deliberately different time scopes.

The optional journal retains at most 128 entity/inventory/resource subjects across viewers, each with exactly 600 sample slots: at most 76,800 retained samples. Projection work examines at most 128 diagnosis rows and the first 4,096 copied render instances. Selected compatible input resources receive admission before additional chain rows. LIMITED SUBJECTS and omitted-subject diagnostics expose bounded admission or a truncated chain/visibility slice. Subjects outside the slice remain unobserved. These limits make no claim of complete-base coverage.

## Shared-cause warning groups

The existing notification system retains its condition identities and original alert categories. A power warning now reports the number of proven shared causes and expands into groups by actual PowerNetworkMembership when that same current network has a measured deficit. Only enabled, currently underpowered, authorized owned consumers are members. Similar NoPower labels, individual disabled consumers or unrelated network IDs do not establish a common power cause. The group does not guess which generator caused the deficit.

Other groups require the identical current generation-stable conveyor cause object, normalized reason and resource. A missing or unauthorized cause does not create a group. A group requires at least two distinct observed affected entities. Operations / NOTIFICATIONS lists each retained member separately, with the actual network or cause identity. The existing facility list is not collapsed or discarded.

Power alert FOCUS cycles through currently authorized members. Held presses bind the original membership; changes cancel the gesture instead of selecting a replacement object. Expansion content also closes on membership changes. Operations' existing TRACE NEXT continues to prefer the observed upstream chain. At the end of that trace, or when a selected affected facility has no chain, RELATED > uses the same control to visit a member of its shared-cause group. FOCUS FACILITY and OPEN EXISTING CONTROLS remain available and keep their existing owner/session/tick/generation revalidation. No additional repair, delivery or policy command is emitted.

Shared-cause capture examines at most 4,096 copied instances and 4,096 existing power-network records, retains at most 64 cause candidates and 256 total member references, and reports limited admission. Single-member candidates also consume this cap. An empty group list means no shared cause was established in that slice, not that the base is healthy. Group counts are observed counts rather than estimates of omitted facilities.

See [bounded Operations observation](adr/BoundedOperationsObservation.md) for ownership, compatibility and measurement decisions.

## Validation and remaining acceptance

OperationsDiagnosisTests and OperationsActionGuidanceTests cover typed branches, unknown versus measured-zero source stock, independent quantities and active ingredient priority. OperationsChainDiagnosisTests uses normal paid construction, original recipes and finite standard starting stock to prove the Ammunition/Electronics/Silicates failure and recovery, remaining power diagnosis, an eight-piece paid cycle, actual trace truncation, removed observations, stale generations, retained snapshots and read-only authoritative hashes.

OperationsTests covers held target replacement, hidden/foreign/recycled targets, opening existing controls, prior lifecycle cancellation, actual link meanings, marker/primitive limits and zero warm allocation, including the maximal copied dense chain. ConveyorDiagnosisTests checks real reserved stock, full destination, destroyed support, damaged span, incompatible retained cargo, power and disconnected ports. OperationsChainMeasurementTests retains the dense read-only/cost fixture.

The focused Operations CI step is additional to the complete canonical validation in [Development](Development.md). Final source-specific results belong in the PR; pending or skipped gates are not passes. Physical D3D12/reference-hardware performance, human native readability, multi-display input and accessibility review remain separate acceptance work. Existing independent budget/fixture blockers remain tracked in issues #344 and #345; no threshold, golden or natural terminal predicate is relaxed here.

### Throughput and grouping delivery status

The throughput/grouping extension is implemented with manual user acceptance outstanding. Its preparation uses product-project compilation only. Existing tests are read-only behavioral references; no tests, smoke, native, benchmark or automated qualification runs are added or manually executed for this change. Existing historical test and measurement records above do not qualify the new extension. Required CI is retained unchanged and may run automatically after normal branch or PR publication.

Manual review should cover partial truck unloads, simultaneous conveyor quanta, warm-up and scope resets, production recovery, stale distribution observations, shared versus separate power networks, membership changes during a held click, every retained member's existing controls, compact layouts and resource paging. No measured performance improvement or complete gameplay acceptance is claimed.
