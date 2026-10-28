# After-action strategic debrief

The finished-match overlay shows the authoritative local result, termination reason and a bounded timeline. It uses existing completed-action evidence from GameplayProgressJournal. It does not reconstruct strategy from final inventories or expose headless balance reports.

## Controls and failure behavior

The result opens automatically for the current terminal session. Click a timeline row for its factual explanation; the category button cycles All, Industry, Supply, Recon, Infrastructure, Force Preservation and Result. Next cycles six-row pages. Coverage explains missing evidence. The existing Next Item (Tab) and Activate (Enter) bindings move focus and activate controls. R restarts and Escape returns through the established terminal-session acknowledgement path. These keys remain available when evidence is absent.

Save Current explicitly writes last-match.debrief.json next to client settings. Load Saved reads that single file; Current restores the current match view. Saved reports are labeled SAVED FIRST/LATEST and retain their own scenario, duration and scoped outcome, independently of the current result heading. Saving always writes the current report. There is no automatic disk access at match completion, file browser, append-only history or background archive.

Missing, corrupt, unsupported-version, foreign-player and inaccessible files produce fixed feedback text. They never change the public result or disable Return/Restart. Foreign reports cannot be saved over the local slot. Mouse gestures are cancelled on session, viewport, focus or modal changes; event-row activation also revalidates report identity and row. Exit controls do not depend on optional evidence identity. Leaving the terminal session clears the transient view.

## Evidence and limits

The existing journal retains first/latest observations per player and event/resource/definition key, not a complete chronological log. A row is a retained observation; its quantity is the amount of that event, never a match total, income rate or complete delivery history. First/latest copies of the same source event become one row. The view does not imply continuous coverage between retained rows.

| Category | Published evidence | Explicitly unavailable |
|---|---|---|
| Industry | committed extraction/output, research, observed work blocked/resumed | historical blocker causes, utilization and capacity totals |
| Supply | cargo pickup/delivery, completed loading visits, physical fuel/ammunition transfers, supply truck completions | shortage duration, resilience score, forecasts |
| Recon | completed Scout production | first contact times and enemy discovery history |
| Infrastructure | committed conveyor handoffs | territory/objective change history and historical network disruptions |
| Force preservation | no supported journal events | casualty and battle history |
| Result | public local win/loss/draw, result tick and recorded termination reason | strategic causal attribution |

Work resumption is a recorded transition after an observed block, without a recorded trigger. Temporal ordering does not prove that power, ammunition, economy or combat caused the result. Starting units/stock and configured policies are not completed production. Safe historical coordinates are absent from this producer, so the view offers no map jumps. It never queries hidden enemy ECS, offline opponent metrics or spectator state.

## Durable contract

MatchDebriefSummary schema 1 is independent of authoritative saves, command replays and debug logs. Maximums are 128 events, 64 KiB UTF-8, JSON depth 8, 128 scenario characters and 48 subject characters. Enums have explicit numeric values. Times are logical ticks plus TickRate; StartedAtTick, ObservedThroughTick and nullable ResultAtTick distinguish an incomplete observation from a finished result. Unknown termination reason remains Unknown.

Each immutable row contains Tick, Kind, report-local SubjectId, bounded display Subject, optional positive finite Quantity and First/Latest/PublicResult evidence flags. Dense subject tokens identify owned targets only within this report. Global event identities, entity indices/generations, source/actor identities, order identities, positions, seeds, opponent economy and raw diagnostic metrics are excluded. Session/player identify the observation scope. A saved file is local historical data, not authenticated match authority or a world-action target.

The codec rejects unknown properties, invalid bounds/enums/times, malformed results, oversize files, future schema and wrong expected player. Reads check size before allocation. Writes use a same-directory temporary file, flush then replace; failed writes clean up their temporary file. No save/replay migration is required. See [durable-summary decision](adr/BoundedPlayerMatchDebrief.md).

## Ownership and headless export

The simulation-owner extractor captures once at the first completed terminal tick, then shares the immutable summary in PresentationSnapshot. UI/rendering never walk ECS or journal storage. The capture uses the same existing journal as guidance; no new producer, gameplay authority or collector is enabled. The headless host can export the identical format explicitly:

    dotnet run --project src/ForgeLine.Headless --configuration Release --no-build -- --scenario central-divide --profile validation --ticks 12 --seed 7511 --debrief-output artifacts/own.debrief.json --debrief-player 1

Only one fresh match and a configured participant are accepted. Debrief output must have an independent full path from save/replay/telemetry/diagnostic outputs. A tick budget or cancellation before a result produces Incomplete with no result row. Existing cancellation and require-terminal exit semantics remain in force. Offline --telemetry-output reports can contain all-player balance data and are not suitable player-UI inputs.

## Qualification

Run the Release solution build, MatchDebriefTests in Game and Presentation, ClientDebriefStoreTests in Client, and build/Test-MatchDebrief.ps1. CI runs these contracts after Build. Tests cover actual core loss and surrender, scoped outcomes without authoritative hash mutation, terminal snapshot caching, missing/corrupt/future/foreign records, bounded sources and output, immutable copies, stale gestures, display scales and warm zero-allocation HUD rendering.

Native Windows D3D12 qualification:

    dotnet run --project src/ForgeLine.Client --configuration Release --no-build -- --debrief-capture artifacts/debrief-native assets/runtime

Use a fresh output directory. The runner renders the existing world fixture with authored scoped summaries through the actual GameplayHudRenderer: timeline, detail, coverage, empty Forces, missing evidence, corrupt reload feedback and saved report. It checks repeated pixels, glyph completeness, submission health and Return/Restart hit rectangles at four display/DPI/scale combinations. qualification.json records assembly hashes, device, fixture, text diagnostics and image hashes. This is native rendering and authored input evidence, not a natural match, approved golden or physical-monitor/input certification. The existing Operations HUD resources are reused; the terminal view adds no permanent graphics surface.
