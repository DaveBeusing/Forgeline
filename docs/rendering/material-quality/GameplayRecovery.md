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

## Focused strategic fixtures

A bounded fourth-bootstrap-truck candidate passed its direct policy regression
but did not resolve either original scenario (five policy cases passed, the two
long cases still failed). It delayed the first tank in the recorded eastern
case. The production-rule change and its candidate-only test parameters are
reverted; no fleet target, unit cost or attack-admission threshold is changed.
The rejected candidate's logs and 40,000-tick public-runtime timeline are retained.

The funding trace identifies the actual missing precondition at tick 24,000:
300 generation versus 291 demand leaves nine instead of the required twenty
reserve. The funding policy fixture now supplies a stable 100-unit core generator,
using the existing component pattern, then still requires the complete bootstrap
and applies the original partial/full hub-cost assertions within the original
24,000/12/12-tick bounds. It does not depend on the timing of the next power plant.

The east-objective fixture now creates a supplied force through the production
UnitFactory and InventoryStore before asking the unchanged opponent to choose
its offensive objective. Unit readiness, minimum attackers, pressure and forward
supply admission remain active. The original 40,000-tick bound, stationary west
base and enemy-half objective assertion remain. The separate unmodified bounded
strategic-loop integration test still exercises real economy and manufacturing.

Both focused original scenario methods now pass (2/2, exit 0, 17.831 seconds).
The complete unfiltered suite remains required. No longer deadline, cheaper unit,
changed logistics rule or weaker admission criterion was introduced.

The expansion failure diagnostic records movement and nearby spatial bounds
without changing its funding assertions.
