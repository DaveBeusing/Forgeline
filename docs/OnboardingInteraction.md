# Early logistics guidance

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

The optional opening guide stays at the top left of the available safe area. Its
panel uses alpha 0.84 and opaque text, a 220 ms SmoothStep collapse, HINT > restore
and Shift+F12. Reduced Motion and interrupted presentation settle the logical
state. Guidance never submits simulation commands or blocks actions until a
lesson has been read.

## Reading and gameplay are separate

Twenty stable content keys cover the starting Core and two Cargo Trucks, power,
real Ore extraction, Smelter construction and policy, Ore delivery, Steel,
Vehicle Factory construction and inputs, Scout production, truck roles, depot
construction and deliveries, Supply Truck production/loading, field supply,
recovery, scouting and continued play. The truck-roles and continued-play cards
are explanations rather than achievable simulation goals. The existing eighteen keys
remain stable. `belt-production` separately explains compatible base conveyors and
committed processing output; `research` explains the existing paid research flow.
Neither lesson adds a command, unlock or required reading order.
The belt-production card explains temporary Volatiles conveyor delivery to
Refineries and Fuel conveyor collection into compatible owned local storage,
without suggesting pipelines are implemented. Regional Cargo and battlefield
Supply Truck logistics retain their own roles. The contact card
explains escorts, rerouting and transport reserves against observed raids without
revealing hidden enemy state.

Every lesson independently records Offered, Viewed or Deferred within the
current session. NEXT/PREV select lessons; PAGE </PAGE > traverse the complete
text. READ traverses any remaining pages before marking the explanation Viewed;
LATER marks it Deferred. Neither action changes gameplay progress. Minimizing
marks nothing as read. Reading state is not persisted. A new session resets it.

Completed gameplay is observed even while minimized, disabled or temporarily
suppressed. Confirmed goals retain their explanations and explicitly say that no
repeat action is needed. Out-of-order commands remain available and are observed.
An accepted policy, assigned transport, actual unloading, completed production
and field transfer retain separate meanings. Starting Core supply is availability,
not delivery or mobile resupply evidence. When more specific selection help is not
available, a current completed-tick Operations diagnosis contributes its observed
block reason and an existing inspection action. This contextual advice never
counts requested, confirmed incoming or loaded quantities as delivered stock,
never invents an alert, and never sends a gameplay command.

## Layout and input

Detailed cards use adaptive left-column height and measured word wrapping. All
body pages, including active action, threshold, priority and adjustment bindings,
remain reachable at a 14 physical-pixel minimum. Page construction and rendering
use the same layout and font metrics; essential body instructions are never
replaced by ellipsis. Supported compact displays use additional pages rather
than hiding the explanation. Six distinct navigation targets precede the
whole-background minimize target. The header/background still minimizes; the
restore tab owns only its painted bounds.

When no transient command surface or camera mapping owns navigation, the current
NEXT ITEM binding cycles guide focus through its six controls and minimize. The
current ACTIVATE binding executes the focused guide action. Active bindings are
listed on the lesson pages, including remapped controls. The guide is temporarily suppressed while Operations owns the
left column; its reading state and completed-tick observation continue. Mouse navigation remains
available independently of keyboard ownership. A guide press is suppressed until
release; an existing world-origin hold, resize, session change, modal interval or
focus loss cannot acquire stale guide navigation. The shared copied
OnboardingPresentation version 2 carries detailed bounds and focus to rendering.

## Existing player controls

Use BUILD for construction, LOGISTICS / INPUTS / USE INPUTS / APPLY POLICY for
local production stock, PROCESS / QUEUE for Steel, and UNITS / QUEUE for vehicles.
INPUTS costs come from the active catalog through the existing editor; the guide
has no independent threshold calculator.

To load a Supply Truck, move it near the intended stocked depot so that depot is
the nearest eligible loading building. Select the truck, open SUPPLY, choose
REQUEST RESUPPLY, then activate REQUEST SUPPLY. The existing action chooses a
loading building; it is not a manual source picker. Actual queue, alignment and
loading remain authoritative. Cargo Fuel is distinct from drive Fuel. Use the
same existing Supply controls or automatic policy for eligible field recipients.
OPERATIONS / BLOCKED, FOCUS FACILITY and OPEN EXISTING CONTROLS expose existing
causes. Current selected work and logistics failures reuse their established
help, without inventing a new diagnosis.

[Evidence, recovery and qualification](OnboardingQualification.md).
