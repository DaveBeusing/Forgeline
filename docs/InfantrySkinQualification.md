# Infantry skeletal qualification

Status: implementation under qualification; human motion/readability acceptance pending.

The ordered repository precheck started from freshly fetched master `a381bda9d08685250c570d1c29d4e625f2c3d206` (integrated clip foundation). The separate vehicle-docking branch/PR has no overlapping infantry skin owner. Product version increases once from 0.1.398 to 0.1.399.

Blender 5.2.2 reopened both committed masters twice, exported all nine actions per role, matched all compiled skin/clip payloads, compiled a real key edit with a changed idle payload, and validated all eight resulting catalogs with source directories unavailable. `artifacts/infantry-authoring-qualified/result.json` retains payload hashes. Evaluated joint positions match the common runtime evaluator within 0.00002 metres; deterministic sampling/deformation has zero managed allocations after warmup. Initial allocation failures were retained and fixed by replacing an interface enumerator in the hot sampler.

The complete sealed catalog contains 1357 assets, including two skins (3,630,411 container bytes) and eighteen clips (189,114 bytes). Total runtime assets: 75,122,812 bytes. Ordinary mesh/material/texture counts are unchanged. Isolated skin/bind catalogs retain identical geometry and material inputs for cost comparison. Source-less validation passes.

Release solution build: zero warnings/errors. Focused pipeline/rigid regressions: 45 passed. Initial presentation contracts: 21 passed. Full game tests: 410 passed. Full client tests: 418 passed, two explicit native audio opt-ins skipped. Headless: 64 ticks, 1000 entities, seed 12345, no pending commands. Project-reference isolation passed.

The broad presentation run reported 1259 passes, twelve hardware opt-ins skipped and one zero-allocation failure in the unchanged Operations overview test (624 bytes, count 1000). This result remains retained; it is not accepted as a successful gate or grounds to relax its assertion. Final targeted LOD/lifecycle checks and baseline attribution remain to be recorded.

Native action/LOD captures, paired 1000-squad cost, installed-player integrity and remote CI are pending. No human acceptance, deployment or release is claimed. Existing software-fence and inactive-path timing failures described in [rigid qualification](RigidClipQualification.md) remain separate until measured on the corresponding source revision.
