# CI repair qualification

## Canonical build

The application icon declared a fourth, 256-pixel PNG whose payload was absent. Its directory now contains only the complete 16-, 32- and 48-pixel images, with corrected offsets; the original image payloads are preserved. Canonical Release solution builds succeed without clearing `ApplicationIcon` or bypassing resource compilation.

## Regression fixtures

The complete asset catalog contains 307 entries, including current source artwork. All five full-catalog authoring assertions now expect that exact count; asset IDs, runtime formats and semantic content checks remain intact.

Player strategic overlays consume their dedicated copied snapshots. They do not implicitly enable developer-debug capture. Regression coverage checks all six player modes, preservation of explicitly requested developer data, and the Power entry in the complete overlay cycle. The tactical action fixture supplies an identified target before requesting direct attack; the target-availability guard remains unchanged. The terrain override fixture verifies the current canonical dirt fallback when only grass has a runtime override.

Research requires power. The persistence fixture builds a valid power plant through a recorded command before starting research, then verifies reconstructed progress/material state and the authoritative hash. The presentation fixture supplies a generator in the player's power network. The full-roster smoke completes Industrial Standardization and verifies its Field Engineering unlock before producing the Combat Engineer. Production capability and research power gates remain unchanged.

## Native smoke draw contracts

The previous fixed total of 26 draws was tied to an older camera view. Terrain draws now qualify against the renderer's one-draw-per-visible-chunk contract, with valid visibility bounded by total map chunks. The canonical roster retains its separate maximum of eleven instance mesh/material batches; total measured draws must equal terrain plus instance draws and remain within visible chunks plus eleven. Extra instance batches, duplicated terrain submissions and unaccounted extra draws fail qualification.

The observed Windowed 1600×900 view uses 18 terrain draws plus 11 instance draws; the BorderlessFullscreen 5120×2160 view uses 22 plus 11. Both canonical window-mode smoke checks pass with successful Presents, matching client/surface dimensions, no pending resize/occlusion, balanced texture accounting and zero material binding failures. Existing texture/sample, residency and descriptor limits remain enforced. The local Windows Graphics Tools debug layer was unavailable; zero warning/error counters alone do not establish debug-layer qualification.

## Startup worker ownership

Asset loading and frontend preparation use two transient, dedicated CPU/I/O workers. Blocking startup work therefore does not wait for shared thread-pool capacity on smaller machines. Both workers remain cancellable and are joined before shutdown; platform, renderer and simulation ownership is unchanged. Regression coverage verifies that both operations run on distinct dedicated threads separate from their caller.

## Window recreation

Before creating a replacement window, the platform drains all pending messages and resets its quit state. Windows generates the previous window's WM_QUIT only once higher-priority messages are exhausted, so filtering for quit alone could leave shutdown pending. The recreation regression covers both an empty queue and an explicitly queued thread message, retaining valid-handle, open-window and successful-pump assertions.

## Cargo recovery queue ownership

Cargo replacement remains the critical production goal until the live fleet reaches its target. Unbuilt requests cannot haul their own missing inputs. When a high-priority supply or reconnaissance request lacks materials, cargo recovery can cancel it and start an already queued cargo replacement. Running production retains its slot and reserved inputs. Supply and reconnaissance queueing preserve replacement materials while the live cargo fleet is deficient. The targeted regressions verify pending-versus-live capacity, blocked high-priority preemption and preservation of running production. Physical logistics and production costs remain authoritative.

## Blocking disposal regression ownership

The concurrent session-disposal fixture uses two dedicated workers and waits for both callers to enter before releasing session construction. Blocking joins do not depend on shared test thread-pool capacity. The existing five-second checks, completed-worker assertion and exactly-once result disposal remain enforced.

## Production refill recovery

Cargo recovery refill targets depend on the live fleet, including when replacements are already queued. A missing cargo vehicle or a logistics-capable factory blocked on inputs raises production-material deliveries to Critical. This lets functioning carriers refill production before the fleet is exhausted, while operational field supply remains Critical. Healthy, unblocked factories keep their normal priorities. Regression cases cover a healthy fleet, a missing truck, an unbuilt replacement and blocked production with a healthy fleet; they retain exact material thresholds and depot priorities. No inputs, replacement vehicles or match outcomes are granted.

## Validation status

Formation recovery detaches a member blocked on a local slot and routes it from its own position to the existing destination. Remaining members retain their shared route. A projected formation also advances after its members reach their local slots, including orders already removed by ground movement upon arrival; an offset group centroid cannot leave a completed route leg pending forever. Regression coverage checks independent routing and traversal past an asymmetric obstacle, both of which fail with the prior implementation. Continuing strategic recovery retains an already accepted retreat toward the same destination instead of rebuilding its formation at each decision.

Ground movement accumulates meaningful distance improvement instead of counting a small forward half of a collision oscillation as progress. Slow continuous movement can accumulate progress across ticks; repeated displacement between the same positions becomes Stuck. A blocked managed navigation waypoint retries the original destination from the unit's current position, retaining command ownership and physical route/fuel qualification. New waypoints ignore stale stall state from the preceding order. The slow-travel, oscillation and blocked-route regressions fail with the former implementation.

Forward units whose remaining physical fuel cannot reach their recovery destination hold position for mobile resupply instead of exhausting their fuel on an impossible retreat. Reconnaissance redeployment requires the existing offensive readiness, fuel and ammunition thresholds, including after an earlier retreat has finished. Tank and reconnaissance regressions verify that fuel remains available while waiting and that a physically loaded supply truck transfers its stock to the waiting unit.

Once the minimum attack force and objective-pressure units are established, missing forward hubs or unsupported hubs raise construction raw-material priorities to Critical. The existing reserve quantities remain unchanged. The reserve yields to cargo replacement until the live transport fleet is restored. This prevents blocked industrial production from consuming the ore needed to construct forward logistics while preserving initial mobilization and transport recovery. The regression isolates healthy power and checks all three priority cases; the established-force case fails with the prior policy.

After recovery releases an advancing supply escort, offensive planning waits for the next decision instead of immediately issuing another advance. Automatic resupply can claim the released provider on the intervening tick. Active reconnaissance escorts and existing loading/rescue assignments retain ownership. Existing bounded offensive and supply-loading tests qualify this handoff without changing their tick budgets.

Reconnaissance supply escorts remain attached until the opposing command core is currently identified. Incidental enemy-unit sightings do not complete that objective. A recovered scout without an active resupply order can resume reconnaissance before completing obsolete retreat movement, using the configured readiness, fuel and ammunition admission thresholds. The regression retains shared-route checks, verifies active escort movement after unrelated enemy contact, and verifies recovered-scout redeployment; both new cases fail with the prior policy.

The reconnaissance destination lies inside the scout's radar identification range, allowing identification even when terrain blocks visual observation. Its approach distance derives from the existing sensor definition instead of the previous fixed 260-meter offset. All three escort cases verify the physical destination against the opposing command core and fail the former offset; no intelligence contact is granted.

Blocked steel production raises Ferrous Ore input delivery to Critical, so an otherwise healthy construction reserve cannot prevent raw material from reaching an empty smelter. A satisfied output target retains the normal High priority and existing 80/240 input thresholds. The readiness fixture waits for both attack readiness and expansion within its original 50,000-tick budget, avoiding an assumption about which milestone occurs first.

Canonical Release solution build: zero warnings/errors. Complete solution suite: 1,097 tests, 1,095 passed, zero failed, two native lifetime cases skipped unless explicitly enabled. Focused validation: 45 presentation cases, both repaired game cases and all 54 asset cases pass. Canonical map and clean runtime-asset compilation qualify. Negative smoke-budget probes reject an extra instance batch and a duplicate terrain draw. Updated full-suite and two-processor startup validation results are recorded on the repair pull request.

Published workflow conclusions, packaging and hosted qualification results belong to the repair pull request's final validation record. Local timings are observations and do not establish a startup or rendering speedup. The dedicated self-hosted GPU lane and full interactive lifecycle/release checks remain separate qualification requirements.
