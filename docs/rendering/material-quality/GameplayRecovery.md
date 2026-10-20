# Supply-role recovery

The follow-up explicitly includes the five baseline gameplay failures in this PR.
Supply trucks inherit CargoTransport for freight storage but are intentionally
excluded from CargoFuelServiceController. Three battlefield-resupply gates had
classified them as ordinary cargo vehicles, leaving their propulsion tank with
no service owner. Automatic decisions, provider eligibility and field transfers
now use the same distinction: a SupplyTruck retains battlefield resupply;
ordinary cargo uses physical dock service or explicit mobile rescue.

All 78 focused battlefield supply, vehicle docking, tactical combat and cargo
fuel tests pass. The tactical regression now covers both an unavailable cargo
vehicle receiving mobile rescue and rejection of static proximity transfers.
It retains delivery order/lifecycle assertions and checks source fuel conservation.
The two longer expansion/offensive scenarios are being checked independently.
No deadline, stock cost, admission threshold or physical docking rule is relaxed.

Strict rendering timing gates remain open; this functional correction does not
establish a rendering performance result. The previous visual approval remains
bound to production source 57fa863f and its six recorded image pairs.
