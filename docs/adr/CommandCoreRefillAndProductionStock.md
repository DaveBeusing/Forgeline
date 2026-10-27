# Command Core refill editing and production output stock

Status: implemented; manual acceptance outstanding.

## Problem and decision

A processing facility's status reported work and request counts but did not show
its remaining finished-material stock. Core Logistics reused a generic policy
entry: clicking a material submitted current/default thresholds immediately, and
its separately editable target could stop refill before the displayed maximum.

Reuse the current ring and command gateway. A Core material item opens its stock
editor; dedicated MIN/MAX decrease/increase controls modify a local draft. APPLY
STOCK sends the existing SetStockPolicy request with Target = Maximum and enabled.
Keyboard activation has the same open/apply stages. Other facilities keep their
three-threshold policy controls and priority semantics. Core order entries remain
read-only. The eight-sector layout, geometry publication, full entity generations,
session/selection/menu checks and pending request gates are unchanged.

The simulation already implements deficit detection, free owned Cargo Truck
selection, source surplus/reservations, Fuel, routes, loading/unloading, retry and
successive shipments. This change connects the intended two-bound control to that
authority; it does not add another scheduler or teleport inventories. Below MIN
uses existing strict deficit semantics. MAX is a refill goal, not a hard cap or
permission to cancel already carried cargo. Current failure reasons remain visible.

A completed-tick capture reads the actual production output inventory and copies
at most seven distinct catalog output resources with on-hand/reserved quantities.
The selected-object card and authorized world hover use that copy. Renderer code
queries no ECS/inventory and formats into its existing bounded text buffers.
Foreign and detected contacts retain their information boundary.

## Compatibility and evidence

No authoritative component, command, scheduler policy or serialization shape
changes. Existing Core policies are not migrated: only explicit Apply authors
Target = Maximum. Saved policies and inventory state retain envelope 1, payload 11
and authoritative schema 16; historical hashes and replay behavior are unchanged.
No local-settings schema changes.

Prepared from master 4d56cb26f7a3e16151d43972ef1e908111338469 (0.1.500), advancing
PATCH once to 0.1.501. Existing distribution/capability references were read only.
Tests, benchmarks, smoke/native/headless runs and workflow dispatch/reruns are
excluded. Current affected-product compilation is unavailable in the existing
local checkouts at this source identity; automatic PR CI is reported separately.

Manual acceptance is outstanding for the Smelter stock after production and
collection, Core item/open/apply with keyboard and pointer, saved Min/Max reload,
low-stock physical collection by free Cargo Trucks, successive shipments and real
waiting reasons, legacy values, shared capacity and lifecycle/stale input rejection.
No overall qualification, performance gain or release is claimed.
