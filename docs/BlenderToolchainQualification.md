# Blender authoring qualification

Qualified implementation: `98ba49a43077ae5bd1d22777f956a96a843699c4`, based on
`bd685db469e58f9c16b6bd26b54fa43302da0d9b`. Version **0.1.393**, one PATCH.
The final commit only adds qualification evidence and clarifies the typed-array
invocation example. [Machine-readable evidence](evidence/blender-toolchain/qualification.json)
contains exact capabilities, cases, source identity, package provenance and raw-log hashes.

## Results

| Gate | Result |
| --- | --- |
| Release solution build | Passed; zero warnings/errors |
| Executable protocol fixture | 25 cases passed; no Blender required |
| Real Blender background export | Passed on Windows x64, exact 5.2.2 release |
| Independent export comparison | All source and compiled-runtime files byte-identical |
| Canonical smoke compilation | Two assets: one mesh and one material; 3,237 payload bytes |
| Source-less smoke integrity | Passed with nonexistent source directory |
| Real Unicode arguments / Python exception handling | Passed; expected exit 23 and UTF-8 marker/exception retained |
| Production source hashes and status | Unchanged before/after native qualification |
| Existing asset process/lock contracts | Passed |
| Existing asset regression tests | 30 passed, zero failed/skipped |
| Complete production catalog | 1,337 assets qualified; 0 rebuilt, 1,337 cache-verified |
| Headless smoke | 64/64 ticks, seed 12345, 20 Hz, 1,000 entities |
| Reproducible publish | Two publish trees have identical checksums |
| Installed runtime-only player | Passed Ready/Ready, exit 0, no stderr/dropped startup events |

Native tool identity: **Blender 5.2.2 LTS**, build `d13f752e3b9c`, embedded Python
**3.13.13**, AMD64 / 64-bit. Blender executable SHA-256:
`f141a3c5754500e6f84361099a4cd53f0bb7662b6a0d2cfaaa8fbb63e92234a0`.
PowerShell 7.6.5; SDK 10.0.401. This proves the
recorded Windows installation, not byte identity on every supported host/build.

The actual exported cube has 24 vertices, 12 triangles, one material, UVs/normals/
tangents and outward CCW winding. Baked glTF Y-up bounds are `[-1,0,-1]` to
`[1,2,1]` at metre scale 1.0. GLB SHA-256:
`366563c95202218ceb6220e0e72891f43f4db5f135ffde23131c47195e5ca228`.
The normal compiler reports no generated UV/normal/tangent data or fallback sections.
Both exported source trees and both compiled catalogs compare byte-for-byte.
Existing production IDs, sockets, collision, materials, LODs and runtime assets
are not edited. Production source Git tree remains `7dd72bcd2be51f57ddba83e0b7de5bd117364ee8`.

The 25 protocol cases are explicitly fixture-based, including missing executable,
unsupported version/architecture/bpy, log retention, Unicode/quotes/empty arguments,
external Python isolation, timeout/cancellation killing parent and child, discovery
precedence, output lease and containment. They do not impersonate native Blender
evidence. Separate real runs cover installed Blender, its bundled bpy/exporter,
compilation, deterministic output and Python exception semantics.

## Corrected validation failure

The first final native qualification successfully exported into a Unicode/bracketed
path, then its compiler-log redirection treated brackets as PowerShell wildcards.
The helper now uses `Out-File -LiteralPath`. Fresh bracketed paths pass both the
25-case fixture suite and complete native qualification, including compiler logs.
The failed log is preserved and hashed alongside the passing runs. No check,
timeout, version policy or asset acceptance limit was weakened.

## Package and runtime independence

Archive: `FORGELINE-0.1.393-dev-win-x64.zip`; **54,646,568 bytes**.
SHA-256: `407465716f43de3e81aa400c8903cc20fad4f23501feb3305f87ebc2999c4848`.
Clean source provenance points to the qualified implementation; all **1,461**
extracted manifest files match their hashes. No Blender binary, `.blend` file or
Python authoring script is packaged. Existing UI skin/audio/world contracts pass.

The installed executable ran from an empty directory with isolated settings,
the runtime asset override removed and a nonexistent Blender override. It reached
Ready/Ready, exited zero, retained no files in its working directory and emitted
zero stderr bytes. The unchanged draw/texture validator passes: nine instance
draws, 45,718,384-byte peak texture residency, 69 descriptors, zero binding failures.
Audio stopped with 33 blocks, zero failures/drops and no retained resources. This
is a short Windows smoke, not sustained-performance or artwork acceptance.

## CI and execution limits

[Normal CI](https://github.com/DaveBeusing/Forgeline/actions/runs/39051224060) at the qualified source has passed the
new Blender protocol-contract step. At the recorded snapshot, the main job is
still compiling/qualifying the production catalog; the full workflow is not
claimed green. The final evidence-only commit will trigger a new CI run.

The optional native workflow is implemented but **not dispatched**. It requires
a provisioned self-hosted Windows/X64 runner with the `blender` label, pinned
Blender and PowerShell/SDK. No software install or runner provisioning is implied.
Linux x64 discovery/execution support is implemented but not natively qualified;
macOS/ARM is rejected. Native D3D debug-layer qualification was not run.

The full solution suite was not repeated locally for this authoring-only scope.
Its previously recorded parallel failures remain visible in the
[graphics qualification](GraphicsBudgetQualification.md); this change does not
claim to fix them. Focused asset contracts, full production integrity, native
Blender, package reproducibility, headless and installed-player checks pass.
No client/simulation/renderer/compiler implementation or project references change.
No merge, tag, release or production authoring migration is performed.

## Reproduction

Use [installation and launcher instructions](../tools/blender/README.md), then:

```powershell
pwsh ./tools/blender/Test-BlenderToolchain.ps1 -OutputRoot artifacts/blender-contracts-recheck
dotnet build ForgeLine.sln --configuration Release
pwsh ./tools/blender/Test-BlenderSmoke.ps1 -OutputRoot 'artifacts/blender qualification/recheck Grüße [a]'
pwsh ./build/Test-RuntimeAssetPipeline.ps1 -OutputRoot artifacts/blender-runtime-recheck
pwsh ./build/Build-RuntimeAssets.ps1 -Mode development -NoBuild
dotnet test --project tests/ForgeLine.Assets.Tests/ForgeLine.Assets.Tests.csproj --configuration Release --no-build -- --parallel none --filter-class ForgeLine.Assets.Tests.AssetPipelineTests ForgeLine.Assets.Tests.RuntimeAssetQualificationTests
```

Use fresh contract/native roots; prior evidence is not automatically deleted.
The native qualifier intentionally retains a failed per-run report for its expected
Python exception test, while the overall qualification must report success.
