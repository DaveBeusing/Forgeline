# Blender production source round-trip qualification

Qualified implementation: `945a0d57b7666d2026d9b22e3a1a4f8f815cf52d`, based on
`74e5e48f69c74a339354722afb9be091cf6e307c`. Version **0.1.394**, one PATCH.
The final commit adds documentation/evidence only. The [machine-readable record](evidence/blender-roundtrip/qualification.json)
retains per-mesh hashes, bounds/deviations, socket comparisons, source identities,
all native rejection cases, package provenance and raw-log hashes.

## Results

| Gate | Result |
| --- | --- |
| Release solution build | Zero warnings/errors |
| Blender-independent source contracts | 29 passed |
| Existing launcher protocol fixture | 25 passed |
| Native invalid-scene rejection | 19 cases rejected; two-slot/tangent positive passed |
| Production fixtures | Tank + Command Core: 19 meshes, 29 assets |
| Independent and reopened exports | Source/runtime trees byte-identical across all three variants |
| Canonical compiler / source-less integrity | Passed |
| Compiled renderer contracts | One enabled test passed; 57 mesh comparisons across three variants |
| Existing asset process/lock contracts | Passed |
| Production catalog integrity | 1,337 assets; 0 rebuilt, 1,337 source/cache-verified |
| Existing 30-case asset regression gate | **29 passed, one failed** |
| Isolated diagnostic of failed case | One passed; original gate remains failed |
| Headless smoke | 64/64 ticks, seed 12345, 20 Hz, 1,000 entities |
| Round-trip runtime overlay player | Ready/Ready, exit 0; original graphics budgets pass |
| Reproducible package / installed player | Passed |

The root families include their declared LODs, construction/damage/wreck states,
shared collisions, six materials and four texture dependencies. Runtime comparison
retains all **16,400 triangles** and **11 mechanical parts**, identical IDs,
material slots, index order, sections, dependencies, socket metadata, LOD/collision
references and mechanical geometry-sharing keys. Non-mesh payload hashes stay exact.
The original full production source Git tree is unchanged: `7dd72bcd2be51f57ddba83e0b7de5bd117364ee8`.

## Measured preservation

- Maximum local vertex position error: **3.72529029846e-09**.
- Authored source normal deviation: **0**; maximum UV component deviation: **2.98023223877e-08**.
- Maximum compiled/generated tangent vector difference: **6.017074e-06**; handedness is exact.
- Maximum socket local error: **1.43051147594e-08**.
- Maximum representative instance-world socket error: **1.28746032502e-07 m**.

All remain within the fixed [preservation tolerances](BlenderAssetRoundTrip.md).
No runtime acceptance limit was increased. Blender's split-normal preview differs
from authored float32 data by up to **0.000124890721**
per component. Authored vectors are retained separately and exported without that
editor encoding loss; both representations are checked. This is why exporting the
preview representation directly was rejected during implementation.

Native execution used Blender **5.2.2 LTS**, build `d13f752e3b9c`, bundled Python
**3.13.13**, Windows AMD64. Exact executable identity is retained in the JSON.
The generated projects were reopened and exported again; mutable `.blend` file bytes
are not claimed to be deterministic. Compiler-ready source and runtime trees are.

## Native visual comparison

Both original and round-trip full catalogs were rendered on the hardware device
with the existing capture pipeline: 1920 x 1080, High, CloseTactical, fixed views,
32 warmups, eight samples and two stable repeat captures per scene.

| Scene | Original | Round-trip | Measured difference |
| --- | --- | --- | --- |
| Main battle tank | [Before](evidence/blender-roundtrip/before-MainBattleTankProduction.png) | [After](evidence/blender-roundtrip/after-MainBattleTankProduction.png) | PNG bytes identical |
| Command Core | [Before](evidence/blender-roundtrip/before-CommandCoreProduction.png) | [After](evidence/blender-roundtrip/after-CommandCoreProduction.png) | Five of 8,294,400 RGBA channels differ, each by one byte |

Command Core maximum channel error is 1/255; mean absolute channel error is
0.000000602816 byte values. The two reference views are recorded evidence, not
artistic approval, full camera/animation coverage or a sustained FPS measurement.
The image files are unmodified native captures; before/after hashes are preserved.

## Open asset test gate

`AssetPipelineTests.DependencyChangeRebuildsDependentsWhileUnchangedBuildSkips`
failed with `UnauthorizedAccessException` while `WriteManifestAtomically` replaced
the temporary manifest using `File.Move(overwrite: true)`. The 30-case run ended
**29 passed / one failed / zero skipped**, exit 2. The compiler and asset-test source
files are unchanged from the fetched base. A single diagnostic invocation of that
same case passed with its original assertions; the precise cause remains unproven.
**The failed 30-case gate is not cleared by that isolated pass.** The PR stays draft.
No retries, sleeps, weakened assertions or compiler changes were introduced to hide it.

Earlier broader parallel-suite failures remain documented in
[graphics qualification](GraphicsBudgetQualification.md). The full solution suite
was not repeated locally for this authoring-only scope. The new compiled comparison
is explicitly opt-in without native Blender fixtures; its enabled local execution
passed. Normal CI will record that skip rather than pretend to run Blender.

## Runtime overlay and package

An isolated full-catalog overlay replaced the 29 scoped records after compiled
comparison and resealed the original 1,337-record manifest. The player loaded it,
reached Ready/Ready, exited zero and reported no stderr/dropped startup events.
The unchanged graphics checks pass: **nine instance draws**, **45,718,384-byte** peak
texture residency, **69 descriptors**, zero texture/material binding failures.
No overlay was installed into production source or the regular runtime cache.

The normal package retains original production assets and no Blender executable,
`.blend` or Python authoring file. Two publish trees compare identically; all
**1,461 extracted files** match checksums. Installed execution from an empty working
directory, with isolated settings and an invalid authoring Blender override,
passes Ready/Ready, zero exit/stderr/dropped events and the same graphics budgets.

- Archive: `FORGELINE-0.1.394-dev-win-x64.zip`
- Size: **54,646,603 bytes**
- SHA-256: `4b381e05c72f4e3445208f0b27dd295c018267cd933299ac5eef7dc1b9c40443`
- Source: qualified implementation above, `sourceTreeClean: true`

The short smokes do not establish natural match termination or sustained performance.
Native debug-layer qualification was not performed. Headless retains no Blender,
graphics or audio dependency introduced by this change.

## CI and remaining execution limits

[CI snapshot](https://github.com/DaveBeusing/Forgeline/actions/runs/39053988712) at the qualified source passed both
Blender source-geometry and launcher-protocol steps. The main workflow remains
in progress at the recorded snapshot, in compiled UI atlas/skin contracts. The final
evidence commit will trigger a fresh run; latest-head CI is not claimed green.

The optional Blender authoring workflow now includes production round-trip checks,
but was not dispatched. It needs a provisioned self-hosted Windows/X64/blender
runner. Linux native execution remains unqualified, with macOS/ARM rejected by
policy. No source replacement, merge, tag or release is performed.

For commands and recovery use the [source contract and reproduction guide](BlenderAssetRoundTrip.md).
Use fresh qualification roots; failed experimental logs remain preserved locally
and indexed by hash. Material preview assumptions, split-normal representation and
a serializer analyzer error found during implementation were corrected before the
qualified build and native runs; the open file-access test failure remains disclosed.
