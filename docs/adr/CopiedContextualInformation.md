# Copied contextual information and local tooltip preferences

Status: implemented; manual acceptance outstanding.

## Decision

Extend the existing ring status card and placement hint. The action panel remains the only action dispatcher. Pointer item inspection and keyboard item focus share HoverTooltipResolver and its copied availability/cost/prerequisite models. Object status stays immediate; delayed action details augment it. No additional dock, inspector, command, pointer target or management surface is introduced.

Presentation extraction publishes owned production input inventory, full facility identity, position and at most seven missing-material rows. Quantities use the real facility input inventory's unreserved stock. Hostile and detected-contact publication rules remain unchanged. Render consumers neither query ECS/inventories nor reserve or move resources.

ConveyorSimulationSystem exposes its existing accepted-resource resolver for completed ports. Prospective sockets derive advisory material masks from current building/recipe/unit definitions and the selected deposit, using the same fixed eligible resource set. Extraction copies at most 128 building-port descriptions; the existing authoritative port set and endpoint picker retain their ownership and physical rules. Missing description coverage is explicit. Cached snapped detail requires current session/tick; building detail requires the current matched preview.

The existing placement panel bounds contain at most four rows with wrapping and ellipsis. Tooltip measurement and drawing share the 48-row budget and occupied safe-area placement; preferred font size can fit down or the card can be suppressed. Existing geometry, vertex budgets and input hit regions stay authoritative. No measured performance gain is claimed.

## Preferences and compatibility

HoverTooltipSettings owns a validated 0-1000 ms delay (175 default) and Standard/Large/ExtraLarge text (1.0/1.15/1.3). Existing General settings expose and persist the choices. Client settings schema 4 migrates schemas 1/2/3; only 1/2 receive the historical graphics conversion. Missing fields default; invalid tooltip data recovers independently through the existing store. Apply refreshes active-session tooltip preferences.

All additions are local preferences or ephemeral copied observations. There is no new authoritative state, command or replay input. Match envelope 1, payload 11 and authoritative schema 16 remain unchanged; historical hashes are not rewritten. A restored/new session obtains fresh observations. Port masks are compatibility information, never a transfer, routing or affordability promise.

## Delivery evidence and limits

Prepared against master 8a52bf0dfec01e3cb16fe712c54912cbddeb1ef9 (0.1.498); this PR increments PATCH once to 0.1.499. The available local checkouts do not contain that source identity, so a current affected-product compilation is unavailable in this connector workflow. Automatic PR CI results are reported separately and compilation is not functional acceptance.

Tests, benchmarks, smoke/native/headless runs and manual workflow dispatch/reruns are excluded from this delivery. No such files or gates are changed. Baseline PR 391 CI already fails during solution build on CA1001 in CargoTransportBenchmarks and VehicleServiceMeasurements.Fleet; that benchmark repair is outside this change.

Manual acceptance is outstanding for pointer/keyboard focus, every tooltip preference and reload/recovery, missing production inputs at distinct local inventories, completed/prospective/snapped ports, material and flow text, tiny viewports/high scale, modal/focus/session transitions and command-time rejection after preview. No overall qualification or release is claimed.
