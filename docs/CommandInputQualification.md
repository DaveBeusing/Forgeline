# Tactical command input qualification

Product and test source: 5edff41339dce9040be3d05e916780e7c2e84b8a. Base master:
201af6aad7ace07df21608c1d058d23b0828b070. Version 0.1.504-dev; one PATCH per PR.

## Executed validation

- Complete Release solution build: zero warnings and errors, SDK 10.0.402.
- Complete Input suite: 27 passed.
- Affected selection, tactical, transient-menu, tooltip, action and command-marker cases: 175 passed.
- Client binding/settings, input lifetime, host submission, native cursor and marker cases: 57 passed.
- Total distinct selected cases: 259 passed, zero failed or skipped. The earlier focused 53-case pass is a subset, not an additional count.
- Project reference validator: 46 projects, no cycles or architecture violations.

The attack feedback regression retains its 34-line accepted shape assertion and now
requires a matching accepted result first. Pending local intent has 22 lines and no
accepted attack circle. Original broad runs reported this outdated expectation and
missing runtime manifests; neither run is presented as passing. The expectation was
corrected at the authority boundary and a separately copied compatible cache supplied
the missing native prerequisite. The complete selected sets then passed unchanged.

## Native marker evidence

The current renderer drew all four states using D3D12/WARP at 1280×720 and raster DPI 96.
Each state has two equal readbacks; all four outputs differ. Every complete state label
is present, with zero missing/dropped glyphs or skin assets/quads. Pending uses an explicit
hollow square; accepted/partial use the existing target artwork and rejected the invalid
symbol. Labels distinguish all outcomes without relying on color.

The four PNGs (Pending.png, Accepted.png, Partial.png and Rejected.png), repeated-readback
results and source/assembly/image identities are retained with the local qualification
artifacts. No remote screenshot is claimed here. The captures are isolated renderer output over a clear
background, not terrain screenshots or full-gameplay acceptance. The existing 1,363-asset
cache was compiled with 1.12.0 and has content signature
b1ddf984833ad10ef043b12bdd90a0e0730eda199f322548c5419b10eac2f480.

It was copied without modifications for UI compatibility qualification. This does not
claim fresh production-asset compilation, physical-GPU timing or current package qualification.

The first marker fixture omitted the current match facts required by the renderer,
so its complete-label assertion failed. Supplying the correct read model fixed the
fixture. Visual inspection also found the pending overlay insufficiently visible;
the hollow-square implementation and its geometry assertion address that finding.
No visual golden, allocation limit, timeout or gameplay assertion was relaxed.

## Reproduction

Use the repository's normal restore and Release build. The selected Presentation classes
are Selection*, Tactical*, Transient*, HoverTooltip*, PlayerAction* and Authoritative*.
Selected Client classes are GameplayBinding*, ClientUserSettings*, FrontendInputLifetime*,
TacticalCursor*, ClientSimulationHost* and CommandMarkerNative*. Use the Microsoft.Testing.Platform
--filter-class options and --parallel none. Runtime-dependent cases require a qualified
assets/runtime catalog. Set FORGELINE_COMMAND_MARKER_OUTPUT to a new empty directory to
export the native marker PNGs; existing evidence is never overwritten.

## Remaining acceptance

Required PR CI, a fresh current-production asset/package path, physical input/focus/display
journeys, bright/dark terrain contrast and human gameplay review remain open. Baseline master
CI 40597373272 already failed the technology/gameplay step; its logistics-navigation workflow
also failed. These baseline observations do not establish a feature-head result. Keep the
PR draft until required checks and remaining acceptance are resolved. No merge or release.
