# Logistics navigation diagnostics

## Implemented scope

The developer navigation view is connected from the simulation through immutable presentation copies to the client HUD, six independently selectable world layers, and a one-shot local JSON export. It is disabled by default. Start the client with the explicit standalone option `--developer-navigation-diagnostics`; debug overlay visibility alone does not enable it.

Select one visible owned Cargo Truck or Supply Truck. The panel reports the original command, request and generation, remaining route, current movement and traffic restrictions, progress/wait age, real replan events, queue/dock ownership, propulsion fuel, accepted motion and checked lookahead when those producers have current evidence. Unknown observations remain unavailable. No gameplay command is submitted by the diagnostic controls.

| Control | Action |
| --- | --- |
| Shift+F10 | Open or close the authorized panel. |
| F10 / Ctrl+F10 | Scroll forward / backward. |
| F11 | Focus the next layer. |
| Shift+F11 | Toggle the focused layer. |
| Ctrl+F11 | Disable all six layers. |
| Shift+F12 | Export the current eligible selection once and show the result. |

The layers are oriented hull, copied remaining route, actual accepted motion/recovery target, authorized current blocker hull, owned passage reservations, and sampled bodies along an actual geometry-checked lookahead. A blocker hull is the observed body, not an inferred contact location. Lookahead is shown only when the existing motion query produced it; capture adds no geometry query or planning work. Queue/dock rectangles continue to belong to the existing logistics overlay.

## Source ownership and availability

The simulation-thread producer captures current own, controlled, alive, rendered vehicles. It never substitutes the global last-completed path for a selected vehicle's route. Original commands, requests and routes from an earlier owner are withheld after ownership transfer. Foreign blocker identities and geometry are withheld even when a traffic reason can be shown anonymously.

Each copied observation carries its source tick and navigation revision. Replan events retain their event tick, old/new request, original goal and reason. Reservations include the command generation, admission and last-decision ticks, entered state and active command. A missing historical grant geometry revision is null; an unknown or stale reservation provenance makes the reservation layer unavailable. Selection generation, session, viewer, current publication tick, render visibility and ownership must agree before rendering or export. Selection/session changes clear layer state and feedback immediately.

The connected modules are traffic coordination, continuous motion and diagnostics. The future footprint-routing kernel, scheduler and regional updates remain explicitly NotConnected. Search/cache/workspace/regional counters, dirty regions and timing/allocation windows without a producer remain unavailable. Their absence is not encoded as zero or healthy. Existing legacy hierarchical routing remains in use.

Completed deliveries come from the actual positive cargo delivery completion boundary, filtered by source/destination ownership. The optional window is `(startExclusive, endInclusive]`, begins when capture is requested, and resets when closed, reopened or the session/viewer changes. Route arrival is not counted as delivery. These diagnostic windows are not saved and do not consume or renumber gameplay events.

## Bounds and disabled behavior

Capture accepts at most 4,096 rendered own identities and publishes at most 128 vehicles, 128 remaining route points per vehicle and 32 owned reservations per vehicle. An incomplete route is identified as partial; incomplete reservation evidence is withheld. Layers remain below 1,024 lines. The HUD lazily allocates text resources only while visible and clips/scrolls its bounded rows.

Optional motion, horizon and replan traces are populated only while requested, bounded and cleared on disable/restore. They do not enter authoritative save/hash state. The report encoder has a nonexpanding 64 KiB UTF-8 buffer, a 128-character restricted ASCII map identifier and 256-character build fields. No unbounded trace, background export or retry loop is installed. Disabled-cost statements are structural, not performance measurements.

## Report and output

The connected report uses client report schema 2. The internal schema-1 partial-observation encoder remains a compatibility helper; the live controller requires the connected current view. Schema 2 includes build/map/seed/session/viewer identity, actual contract/module/limit values, selected source facts and bounded route/reservations, authorized geometry, and a count-only visible own fleet aggregate. Unsupported counters and instrumentation have explicit availability metadata.

The controller reads the latest publication at the export action and reconciles it with the actual current selection and active session. The encoder independently checks the copied identities. Reusing matching old arguments does not authorize an export through the controller.

Output is `%LOCALAPPDATA%/FORGELINE/Diagnostics/navigation-<unique-id>.json`. The writer creates its unique temporary file with CreateNew, writes and flushes the bounded report, then publishes without overwriting. Failure cleanup removes only its own temporary file; preexisting files remain intact. Export results distinguish ObservationUnavailable, LocalDirectoryUnavailable and WriteFailed. The HUD displays the result once.

## Qualification

Release builds and targeted source tests run with .NET SDK 10.0.401. Current source tests cover the real delivery window, ownership transfer, successful replan provenance, and identical motion/query work with lookahead observation on/off. Client tests cover authorization, focus/session/selection turnover, independent layers, fresh export identity, bounded encoding, write failures and no-overwrite cleanup. Presentation tests cover immutable filtered copies, stale/dead/wrong-owner rejection, layer disable, and authoritative save/continuation parity.

Native WARP captures exercise the real world, terrain, mesh, HUD and diagnostic layers at 1280x720 with UI scales 1 and 2 (including scrolled rows), and 1920x1080 scale 1. The explicit capture entry point is `--navigation-diagnostics-capture <empty-output-directory> <runtime-assets> --developer-navigation-diagnostics`. It writes four images, one actual selected report and a qualification record. This developer fixture is separate from normal startup and is not invoked by the panel.

The environment does not provide the required D3D12 debug layer. WARP structural captures do not establish hardware performance, a complete manual gameplay acceptance, or a debug-layer pass. Final run counts and CI status are recorded on the PR; existing repository gates remain mandatory.
