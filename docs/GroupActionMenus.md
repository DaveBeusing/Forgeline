# Group action menus

Base master: `1ef23a9fc7ab9195d0d1bdadd97081016ea97861`. Branch: `ui/group-action-rings`. Version: `0.1.388-dev`, one PATCH.

## Behavior and parity

The existing object ring also owns multiple selected objects. It is one context, not one control per soldier. An immutable selected-identity set and component-covered group facts cross the input/render boundary. The first category is Tactical. The Group sector exposes all eight existing composition filters, damaged/supply focus, formation cycling and camera focus through three pages. Single-object categories remain on the global bar. Group inspector facts and exact accepted/rejected feedback remain informational; the old rectangular group buttons are no longer dispatched or drawn during gameplay.

Attack, AttackMove, Stop, Hold, Retreat, artillery targeting/cancel and recovery use the existing tactical request adapter with the entire intended selection. Eligibility is displayed before activation; authoritative partial results remain authoritative. Ordinary Move continues through the existing single right-click movement dispatcher and shared-route contract. Formation changes the existing local formation choice for subsequent commands. Supply policy remains the existing single-entity policy; there is no invented group Repair or Supply mutation. Type filters alter only current selection, never saved control-group membership or simulation movement groups.

Anchor lookup visits selected authorized identities only, interpolating one render instance per simulation entity. A visible selection midpoint is bounded by the existing ring fit algorithm, independently of group size/spread. A partially visible selection uses its visible projected midpoint and says Part visible; command participants remain the full selection. No visible authorized anchor closes the ring while retaining selection. Missing or foreign identities cannot contribute a position.

Selection reconciliation prunes destroyed generations and lost control on copied snapshot changes, independently of camera visibility. Selection/session/membership revisions cancel an armed action; two to one switches to the single ring, zero closes. The original ring action identity and geometry checks remain shared. Outside clicks and targeting reuse transient menu routing. Saved slots remain organizational state, separate from current selection and simulation-owned movement groups.

## Checkpoint and qualification

The initial integration includes pure/copied-model group size cases 0/1/2/10/50/100/1000, full membership Stop request parity and exactly-once consumption, stale selection/generation/session, all twelve group controls, offscreen anchor/command parity, reconciliation and zero-allocation unchanged-context reuse. Release client test build passes. Focused ring tests: 35 passed; existing group/geometry/nonmodal presentation tests: 78 passed.

Two initial group fixture assertions were incorrect: an unchanged two-member selection legitimately dispatches, and the original thousand-member fixture has 833 visible members. All cases and original positions remain; unchanged selection explicitly checks the exact Stop membership and once-only dispatch, and visibility is independently counted through the camera. The first test build also corrected an IReadOnlyCollection/IReadOnlyList fixture argument. Failed original logs remain in `artifacts/group-rings-*`.

Remaining: add bounded native combined-HUD references and resource/aggregation measurements; execute complete affected projects, full solution, canonical asset/headless/package/terminal validation and inspect current-source CI. Human in-game/physical-display/motion review remains open. No full matrix, CI success, merge or release is claimed.
