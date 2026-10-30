# Battlefield status readability

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

Owned unit status is copied at the completed-tick presentation boundary. The existing selection summary and combat-group member projection carry suppression, artillery mission status and an active resupply flag. Selection strength comes from UnitCombatReadiness. There is no separate maintenance statistic.

The capture rejects foreign, destroyed and stale-generation entities before reading operational facts. It carries no target, enemy type, position, stock or command reference. Multi-selection does not borrow an individual member's details. The existing intelligence contact, attack-location, sensor-range and selected-order paths retain their authority and visibility rules.

## Selection and hover

The contextual inspector shows copied strength when available, active Suppressed/Pinned state, active resupply and the artillery mission state. NoAmmo explains insufficient ammunition for a shot and asks for resupply; WaitingReload explains the current reload wait. An empty ammunition fraction without a NoAmmo mission is described as empty stock, without claiming a new firing validation result. Cancelled missions do not invent a cancellation cause. Fuel depletion describes mobility/supply and is never asserted to block artillery fire.

## Battlefield markers

Selected owned units receive an objective/status marker. Supply and All also enable unselected low/critical supply and active suppression markers through the existing F10 cycle and minimap selector. Overlays Off removes unselected status detail. No new settings or simulation commands are introduced.

Priority is selection, Pinned, Unsupplied, Critical, Suppressed, Low Supply. Each update examines at most the first 4,096 published render instances once, using six stable priority buckets. Selection membership retains at most 4,096 identities. A disabled unselected overlay returns before that scan. Projection reuses one camera matrix per frame. Membership uses the existing copied group lookup. Tactical views admit at most 64 markers and eight labels. Camera distance 600 and above admits at most 32 smaller markers and four labels. Occupied screen rectangles suppress overlapping labels/markers. Retained fixed scratch arrays are cleared each frame; no history or unbounded queue grows.

Stable symbols retain meaning after the label budget is exhausted: P pinned, S suppressed, ! critical/empty supply, L low supply and > active objective. Opaque dark backing, explicit labels and symbols supplement tint. Existing friendly/contact/attack shapes remain distinct. Markers draw above the battlefield and behind normal HUD surfaces inside the viewport safe area; there are no permanent new terrain lines.

The marker pass requires matching presentation/group session and tick, an active nonterminal player snapshot, an owned visible render instance and an authorized copied member. Replacing the session, losing visibility, clearing selection or changing the overlay recomputes the frame without retained markers.

## Qualification

Regression coverage lives in BattlefieldStatusOverlayTests and BattlefieldStatusNativeTests. It covers foreign and recycled identities, copied artillery causes, dense formation priority, tactical/strategic views, toggle and stale-session rejection, warm allocation, native pixel repeatability and absent private-state paint. Native fixtures isolate HUD geometry on a dark background; they do not establish live battle or sustained GPU performance acceptance.

Executed checks, raw CPU measurements, native images and current limitations are recorded in [qualification](qualification/battlefield-status/README.md).
