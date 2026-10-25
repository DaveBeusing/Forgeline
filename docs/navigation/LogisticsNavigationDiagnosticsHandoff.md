# Logistics navigation diagnostics integration notes

The formerly prepared encoder is now connected to a complete developer view. See [LogisticsNavigationDiagnostics.md](LogisticsNavigationDiagnostics.md) for actual controls, sources, bounds and qualification limits.

The integration path is Program launch authorization → ClientApplication controller/current selection → PresentationInteractionState demand → simulation-thread PresentationExtractor/Game adapter → immutable PresentationSnapshot → ClientRenderFrame/HUD/layers. Export re-reads the latest publication at the explicit action and uses the existing bounded writer. No UI code queries live ECS or navigation dictionaries.

Source ownership remains in GroundMovementSystem, LogisticsNavigationCoordinator, PredictiveTrafficPlanner, queue/dock systems and the cargo delivery completion boundary. NavigationDiagnosticSnapshot is the additive copied observation model, alongside the existing common contracts; authoritative save schema remains 9. No future routing, scheduling or regional producer is represented as connected.

Optional diagnostic traces and per-viewer delivery windows reset on disable/session restore and are excluded from authoritative hashes. Tests compare independent normal/diagnostic execution, query work and serialized continuation. Do not replace unavailable evidence with inferred routes, contact points, foreign state or zero-valued counters.

The branch integrates shared contracts/build gates, reviewed lifecycle work, render publication work and predictive traffic dependencies. Keep their required CI and gameplay gates. Before each ordinary protected merge reconcile Directory.Build.props to exactly one PATCH above the current master; candidate branches currently share the same candidate version. Do not waive the unchanged 80,000-tick terminal match or required debug-layer checks. Final CI and any remaining environmental limitation belong in the PR status, not a claim of full acceptance.
