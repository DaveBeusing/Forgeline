# Building demolition

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

Players may select their own completed buildings or construction sites and press
**Delete** (rebindable under gameplay controls). The action is available with the
selected building's radial ring open, but is ignored while a modal dialog, key
binding editor, placement action, tactical targeting, or inactive/paused match
owns the input. One key press submits one request; held-key repeats do not
submit new requests.

## Construction cancellation

A construction site uses the normal cancellation path. The original inventory's
outstanding reservations are released exactly once during the economy phase;
construction materials are **not** also refunded at 40%.

## Completed structures

A completed structure records the exact resource quantities consumed at
construction completion. The authoritative demolition command refunds 40% of
each consumed material to the owner's primary Command Core. Remaining health
does not change this fraction. Inventory contents, fuel, ammunition, power,
manufacturing costs and other operational supplies do not count as construction
materials.

The primary Command Core and all Command Core structures are protected against
this convenience action, keeping surrender and combat victory rules separate.

The transaction is per target. Targets in a selection are de-duplicated and
processed by stable entity identity. Each target's full refund is validated
against the Command Core's **shared remaining inventory capacity** as well as
the acceptance rules for every material. If there is no valid owned Core,
capacity is insufficient, or a material is rejected, the building remains
intact and no partial refund is credited. The next target is checked using the
updated inventory totals from earlier accepted demolitions.

A structure with non-empty attached storage or reserved stock cannot be
demolished until the contents are moved or consumed. Active production
requests also prevent demolition. Map-spawned starting buildings without paid
construction use a zero refund; post-start buildings without a recorded payment
are rejected rather than assigned a price from a possibly newer catalog.

Building removal deletes the associated entity and the stock policies owned by
the building. The existing lifecycle and logistics registration systems
reconcile spatial occupancy and stale logistics graph references. Unit orders
that point at removed structures are handled by their regular invalid-target
paths. This action does not award combat kills or combat destruction effects.

## Loaded conveyor demolition

Selected owned conveyors and junctions use the same Delete command as buildings.
Ordinary unreserved cargo is no longer a reason to reject removal. For each
eligible material in the authoritative module buffer, exactly 50% is moved into
bounded owned recovery storage and 50% is destroyed; fractional quantities retain
the inventory's deterministic double precision. A full cargo reservation rejects
demolition without modifying stock. Other attached/in-transit inventories are
never counted as module cargo.

Recovery is capped at 4,096 material units per owner. Each simulation economy
tick drains it into the owner's Command Core and then owned storage depots
using accepted InventoryStore transfers. Recovery remains available across
checkpoint/save/replay while recipients have no room. If the half-cargo transfer
cannot fit even in the bounded recovery inventory, the module stays in place
and the player receives a capacity rejection. Repeated Delete on an already
destroyed full EntityId cannot credit cargo twice.

The UI identifies recovered and destroyed cargo per resource, separate from
any building construction refund. **Conveyor construction Steel is never
refunded.** Combat-caused conveyor wrecks retain their buffers and do not
automatically salvage cargo; explicit owned demolition can remove them.

## Simulation and persistence

Client input routes through the command gateway and an order-processing
simulation system. Presentation does not alter inventories or ECS entities.
The command stores stable target identities and is recorded in replay; it
reconstructs the same cancellation or demolition decisions on playback.
The current authoritative snapshot schema is 19 and the save document payload
schema is 12. Earlier schemas are incompatible with the loaded-conveyor salvage
simulation semantics; historical building demolition rules are not silently
recomputed from changed catalog data.

This feature does not change the separately configured stock-policy quantity
steps or selected-unit destination indicators.
