# Stock draft freshness and contextual values

Status: implemented for #399, #400 and #401.

Stock drafts are bound to full facility/resource identity and a copied accepted
policy baseline: full policy identity, existence, enabled state, priority and
all three thresholds. Dynamic inventory and transport observations do not reset
an edited draft. A changed clean baseline refreshes directly. A conflicting dirty
draft is replaced by current accepted values with a persistent review message;
the first Apply acknowledges review without submitting a command, and another
deliberate Apply submits the reviewed values. Matching accepted values from an
own Apply do not create a false conflict. Session/selection transitions retain
the existing invalidation and command boundary.

This protects against changes visible in the completed snapshot. It does not add
a simulation compare-and-swap revision: commands accepted after that snapshot
still follow the existing ordered upsert contract. No command codec, payload or
authoritative field changes are introduced by the editor corrections.

A tooltip includes draft thresholds and proposal text only for the selected
material. Other hovered materials show their own copied stock and saved policy.
Core parameter segments show an EDIT prefix and a visible arc for the active
keyboard field even with no pointer hover. Existing M / quantity bindings remain
configurable; pointer field controls select the same state.

Controller tests cover threshold/enabled/priority changes, removal/recreation,
clean refresh, own acceptance and the review/apply sequence. Tooltip tests inspect
selected and other resources. A render regression checks actual arc vertices
before and after a keyboard field switch. Native visual/manual acceptance remains
separate from these CPU checks.
