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

## Bootstrap hauling candidate

The remaining unchanged-deadline scenarios still fail after the role correction.
Their snapshots show three eastern cargo vehicles occupied while four processing
facilities and unit factories wait for inputs available in the core. The bootstrap
fleet target capped processing demand at three until the assault force existed.
The prepared correction permits four only after the complete four-processor chain;
its mature eight-vehicle rule and all unit costs/attack admission gates remain.

The existing production-policy fixture now distinguishes a two-depot complete
chain with three cargo vehicles (recover the fourth) from four (do not request
another), while retaining the original expanded-network cases. This candidate
is submitted for remote validation while a separate terrain measurement window
excludes local builds/tests. It is not yet a passing scenario result.

The expansion failure diagnostic now records actual movement and nearby spatial
bounds without changing its 24,000-tick deadline or funding assertions.
