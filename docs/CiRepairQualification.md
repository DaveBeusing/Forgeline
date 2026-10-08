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

## Validation status

Canonical Release solution build: zero warnings/errors. Complete solution suite: 1,074 tests, 1,072 passed, zero failed, two native lifetime cases skipped unless explicitly enabled. Focused validation: 45 presentation cases, both repaired game cases and all 54 asset cases pass. Canonical map and clean runtime-asset compilation qualify. Negative smoke-budget probes reject an extra instance batch and a duplicate terrain draw.

Published workflow conclusions, packaging and hosted qualification results belong to the repair pull request's final validation record. Local timings are observations and do not establish a startup or rendering speedup. The dedicated self-hosted GPU lane and full interactive lifecycle/release checks remain separate qualification requirements.
