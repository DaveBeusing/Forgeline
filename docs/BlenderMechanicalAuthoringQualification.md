# Mechanical authoring qualification

Recorded on 2026-10-17 for product 0.1.396. Machine-readable results and eight
review images: [qualification.json](evidence/blender-motion/qualification.json).
Authoring instructions: [BlenderMechanicalAuthoring.md](BlenderMechanicalAuthoring.md).

## Source identities and environment

- Authoring/export validation: `7d32243199cbab549b533bc138adab9b7413a8e7`.
- Native before/after references: `ebb203c5e5ddc86d55a99139cf5c5165e1e249ba`.
- Final runtime, focused tests and package: `c8e0a064aaace6c01cfb50bb45801c9ff1af8bed`.
  The later change restricts detailed production-state reads to the owning player;
  earlier reference images do not qualify that ownership change.
- Windows x64, .NET SDK 10.0.401, Blender 5.2.2, CPU Cycles previews,
  NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU. Debug layer disabled.

All three source revisions were clean when their corresponding binaries were
built. The subsequent documentation commit does not change runtime code.

## Automated results

Release solution build: zero warnings/errors. All 62 Python contracts, 12 focused
asset tests and 43 final presentation tests pass. The final presentation selection
includes lifecycle, pause/resume, ownership, warmed allocation, geometry batching,
compiled edited-geometry sharing and independent runtime pose comparisons. This is
a focused qualification, not a claim that every solution test was executed locally.

The native Blender path imports two fixture families: tank and vehicle factory,
29 assets, 19 meshes, 11,296 triangles and 14 mechanical parts. Two independent
exports and an export from a reopened rest project have identical source/runtime
bytes; 57 default compiled meshes were compared. A fourth export changes wheel
rate to -1.75, shifts one permitted wheel pivot by 0.01 metre and changes factory
door travel to 0.3. Its shared geometry remains compatible with the baseline.

All 616 Blender/runtime matrices pass the existing 0.00002 comparison bound;
maximum measured error is approximately 0.0000035763. Eleven sampled states cover
idle, forward/reverse, stopped/paused, firing/recovery, production, power loss,
destruction and LOD. These are sampled pose checks, not a continuous video or
judder acceptance. Eight invalid scene edits are rejected. Generic import/surface
facts are intentionally skipped when their separate opt-in inputs are absent;
the expanded motion-only run has three passes and two such skips. The final
43-test selection has no skips.

Compiler integrity, source-less integrity, all 1,337 production assets, runtime
process contracts and a 64-tick headless smoke also pass. The manual provisioned
Blender workflow was added but has not been dispatched; its local execution path
was exercised.

## Native visuals and resources

Ten High-quality 1920x1080 reference cases compare the original runtime cache
against the exported fixture overlay: eight close-tactical cases and tank/factory
strategic cases. Repeated captures are pixel-stable, with no fallback mesh and no
resource or draw-count changes. Eight before/after pairs are pixel-identical.
Tank formation close view changes six pixels (maximum channel delta one); factory
production close view changes 14 pixels (maximum channel delta two). Both fail a
zero-tolerance exact-pixel comparison. No tolerance was relaxed and no approved
golden or human visual acceptance is inferred. Review remains pending.

Selected images include native firing/factory before and after, plus four Blender
idle/firing/production previews. Blender previews illustrate the authoring setup;
they do not assert engine shader parity.

Paired 100/1,000-instance native stress smokes preserve nine instance draw calls,
45,718,384 peak resident texture bytes and 69 peak descriptors. Existing draw,
64 MiB scene texture and 512-descriptor qualification bounds remain unchanged.
Every process exits successfully, reaches startup/session Ready, writes no stderr
and leaves its empty working directory empty. Short timings are observational;
they are not a sustained FPS or portable performance acceptance.

## Distribution

The clean final runtime source produces a reproducible ZIP:
`FORGELINE-0.1.396-dev-win-x64.zip`, 54,647,872 bytes,
SHA-256 `3e945106bbf3120f74a8bd5571e556e1c95918abe4f6f4fdaa2db556c40a126d`.
All 1,461 manifest file hashes verify after isolated extraction. Authoring Python,
Blender projects and glTF files are absent. UI, font/icon and audio package checks
pass. An extracted-player 1,000-instance smoke passes with an intentionally absent
Blender executable, no runtime asset override and an empty working directory;
resource budgets remain unchanged.

One build at the normal development output failed because a running game locked
its binaries; that failed log is retained. Subsequent builds use the SDK-supported
isolated artifacts output. A raw test-host invocation rejected runner options;
the supported project test invocation then completed successfully. Neither setup
failure was treated as a passing qualification.

## CI and remaining acceptance

[PR run](https://github.com/DaveBeusing/Forgeline/actions/runs/39064387565) fails
WARP `BattlefieldCombat-640x360-Low-NormalGameplay` with a ten-second GPU fence
timeout. The [exact base run](https://github.com/DaveBeusing/Forgeline/actions/runs/39062770122)
fails at the same capture and timeout. Limits and timeouts were not changed and
failed runs were not retried. The PR remains draft pending CI and human visual
review. There is no merge, release or protected-branch update in this qualification.
