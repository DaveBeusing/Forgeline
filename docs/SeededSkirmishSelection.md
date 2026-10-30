# Seeded skirmish selection

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

## Player flow

New Game starts with Authored Map. Its existing scenario order, Practice entries, seed adjustment, default selection and Enter-to-start behavior remain available. The map-source choice follows the authored seed row, preserving the original detail row coordinates and focus. Tab reaches that choice; Left/Right, Enter or a pointer click switches to Generated Map.

Generated Map accepts an unsigned 64-bit seed: decimal 0..18446744073709551615 or an explicit 0x prefix followed by 1..16 hexadecimal digits. Unprefixed hexadecimal, signs, separators, whitespace in typed input and overflow are rejected. The editor retains decimal/hex spelling when copying; pasted text is trimmed and bounded. Ctrl+A selects the seed, text replaces that selection, Backspace removes characters, and Ctrl+C/V copy/paste. Dedicated Randomize, Copy and Paste rows are reachable by pointer, Tab, Up/Down and wheel. Randomize chooses an explicit new ulong; it never silently retries failed seeds.

The size presets are 2048, 3072 and 4096 metres square (8, 12 and 16 chunks). Temperate and Temperate / Sparse use the supported temperate recipe with scatter density 650 or 350 permille. Both retain the same gameplay profiles. Player count is fixed to two, matching the current strategic compiler. No arbitrary profile, generator version or extra participant is exposed.

Validate Map performs preparation without constructing a match. Generating, Verifying, Cancelling, Ready and failure states are distinct; progress is indeterminate rather than a fabricated percentage. The seed, size and preset cannot change while preparation owns a worker. Escape or Cancel requests cooperative cancellation and keeps pumping the frontend until the worker completes. Failure preserves the editable configuration and allows a retry. Completing validation never starts gameplay automatically: a new Start action is required.

Before Start, the menu displays generator version and a gameplay-hash prefix. Full gameplay and visual SHA-256 values are available as four labelled 16-character rows each. Focus scrolling uses the same bounded geometry as rendering and pointer hit-testing; it keeps actions and hashes reachable at high DPI and small supported resolutions. New labels and feedback use English and German resource tables.

## Ownership and verified content

NewGameSelectionController uses the existing ClientSessionLoadingCoordinator for one dedicated preparation owner. It calls GeneratedMapCompiler, writes a temporary package, publishes its cache directory and reloads through GeneratedMapPackage.Load with the startup catalog's ContentSignature and expected manifest hash. CPU terrain-material qualification uses TerrainVisualProfileResolver.CreateEnvironment, including its existing closed-edge query adapter. No terrain, strategic layout, scatter or hash algorithm is implemented in the UI.

Packages live below %LOCALAPPDATA%/FORGELINE/Saves/generated-maps/<manifest SHA-256>. Directory names are restricted to lowercase 64-character SHA-256 identities. A temporary sibling is published only after export completes; an existing identity is verified instead of overwritten. The loader's bounded files, fixed filenames, version/profile checks, reparse-point rejection, regeneration and integrity gates remain mandatory. There is no directory scan that registers packages as authored scenarios.

The controller transfers only the verified composition identity and manifest hash to the normal client session request. New Game, Load and Restart resolve that exact cached package against the current host asset identity. Save restoration performs an additional bounded save read on the construction owner to select its required package before normal reconstruction; it does not trust a saved path. Missing/tampered packages, asset conflicts and incompatible generator versions fail without substituting an authored map. Copying a save without its required package is therefore insufficient; preserve its generated-maps cache when backing up saves.

CPU terrain profiles and copied chunk-decoration snapshots are prepared before match construction, on the construction owner. The normal PresentationExtractor receives the existing IChunkDecorationProvider, and ClientRenderHost passes the prepared TerrainPresentationProfile into the existing TerrainRenderer constructor. Renderer implementation, shaders, residency policies and gameplay authority remain unchanged. Generated decoration creates no simulation entity, picking target or saved WorldObject. No graphics API or mutable frontend model is accessed by the worker.

Authored and Practice sessions continue through ScenarioRegistry's pinned allowlist. Generated restart/save identity includes the exact original map package. The existing session-loading loop, scheduler ownership, cancellation, first gameplay Present and disposal gates remain in charge of activation.

## Limits and qualification

Valid recipe bounds do not guarantee that a seed qualifies. Existing resource-opening, elevation and strategy audits may reject a seed/size combination, especially the smallest size. Terrain material palettes must also satisfy the existing four-layer contract. The UI reports rejection and leaves the configuration available; it does not relax audits, choose a replacement seed or start a partial match. Cancel waits for bounded non-interruptible CPU sections to complete; no worker is forcibly terminated.

SeededSkirmishTests cover decimal/hex limits, invalid/oversized input, actual controller actions, pointer/focus/scroll geometry, keyboard mode switching, bounded text order/repeat/suppression, cancellation, failure/retry, stale candidates, exact generated save/restart identity, missing cache and asset conflicts. Existing authored/frontend, session, Input and Windows-platform regressions are retained unchanged.

SeededSkirmishNativeTests capture 18 WARP cases: authored, seed editing, generation progress, invalid input, ready and complete gameplay hash at 1280x720 / 96 DPI / scale 1, 1280x720 / 192 DPI / scale 2 and 3440x1440 / 96 DPI / scale 1. Captures require repeated pixel equality, complete font output and matching published geometry. FORGELINE_SEEDED_CAPTURE_ROOT optionally retains PNGs and qualification.json; FORGELINE_QUALIFICATION_SOURCE labels their source. These are structural software-renderer checks with a synthetic host asset signature. Physical GPU gameplay, production-asset appearance, live clipboard contention and full packaged interaction remain separate acceptance work.

CI runs the selection/session and native view contracts alongside existing generated-map and frontend gates. The normal match-terminal gate remains required. Current source, build/test results and complete build-test status are recorded in the pull request; historical evidence is not presented as current visual approval.
