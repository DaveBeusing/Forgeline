# Material and shadow reproduction

Use Windows x64, the pinned .NET SDK, PowerShell 7.4+ and a real D3D12 adapter.
Python with NumPy/Pillow is used only for diagnostic analysis/archiving; it is
not a player dependency. Freeze baseline and candidate outputs before hardware
measurement. Preserve their actual assembly source versions and hashes; later
documentation commits do not silently relabel an existing binary.

The executed baseline source is `bfc16a953d731a844321a0848a2ebadc71c5875f`.
The executed final product source is `57fa863f540f283ffbed742923de3e9fa6b3f80c`.
Both use the same sealed runtime signature and explicit request configuration.
The source/binary hashes of the identical component-probe program are in
`artifacts/material-quality/probe-provenance.json`, retained in the evidence index.

## Build and functional checks

```powershell
dotnet restore ForgeLine.sln --artifacts-path artifacts/material-quality/final
dotnet build ForgeLine.sln --configuration Release --no-restore --artifacts-path artifacts/material-quality/final
pwsh ./build/Validate-ProjectReferences.ps1
pwsh ./build/Build-RuntimeAssets.ps1 -Mode development -NoBuild -QualificationOutput artifacts/material-quality/development-assets.json -ProgressLog artifacts/material-quality/development-assets.jsonl
dotnet test --solution ForgeLine.sln --configuration Release --no-build --artifacts-path artifacts/material-quality/final
```

The full-suite exit 2 is retained, not treated as success: five unchanged Game
failures remain. The earlier baseline focused selection passed 53 tests. New
GPU-output/variant contracts plus existing material/shadow cases passed 50 before
the final full-suite qualification; the final full Presentation module passes.

After the suite, the ordinary launch output was updated and tested:

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release --no-build -- --smoke-test
```

## Full-client and component hardware pairs

`Measure-MaterialQuality.ps1` takes `BaselineClient`, `BaselineSource`,
`CandidateClient`, `CandidateSource`, `RuntimeRoot`, `OutputRoot`. Supply the frozen
baseline/final apphosts under the corresponding `artifacts/material-quality`
build outputs, the full SHAs above, `assets/runtime`, and a fresh output root.
It declares B-A-A-B before starting, checks named competing processes, writes
source-qualified requests and preserves every cohort. Fixed references cover two
scenes/three zooms at High; sweep references cover two scenes/all four profiles
at normal RTS distance. All cases use 1920x1080, 64 warmups, 256 samples and two
exact repeated readbacks. The unchanged gate is 15%; failed gates remain failed.

`Prepare-MaterialProbe.ps1` takes `ClientDirectory` and a fresh `OutputRoot`.
Prepare it separately against each frozen client DLL directory. The generated
project exists only below artifacts and is archived as text after compilation.
`Measure-WorldPairs.ps1` takes `BaselineProbe`, `BaselineSource`, `CandidateProbe`,
`CandidateSource`, `RuntimeRoot`, `OutputRoot`. It runs the identical public-renderer
program in B-A-A-B order, eight cases per member, with a fixed mechanical pose,
a deterministic target pan and distinct completed-fence GPU intervals. Dedicated
atmosphere/particle passes are omitted here; full-client evidence remains separate.

```powershell
python build/rendering/material-quality/Analyze-WorldPairs.py artifacts/material-quality/world-isolated
python build/rendering/material-quality/Build-Review.py artifacts/material-quality docs/rendering/material-quality
```

The full-client comparisons have 13 CPU exceedances, no total-GPU exceedances.
The component comparisons have 25 exceedances; neither path is fully accepted.
An overlapping terrain build invalidated an earlier cohort. A helper request
serialization error happened before graphics startup; a later helper summary
error was repaired from existing reports without recapturing or altering metrics.
All attempts are retained. No cohort is selected because it happens to pass.

## Rejected analytic experiments and evidence

The opt-in `MaterialSequenceTests` accepts `FORGELINE_MATERIAL_SEQUENCE_OUTPUT`
and `FORGELINE_MATERIAL_BASELINE_ASSEMBLY`. Source `9d5cf396` evaluates the first
squared-slope candidate; source `44efde21` evaluates the conservative variant.
Each uses the actual baseline/candidate BRDF constants, three normal frequencies,
twelve panels, four B-A-A-B members and 32 readbacks per member. Analytic
frequencies are stress proxies, not real camera distances or authored game maps.
Mean motion, brightness, tails and second differences remain visible; no single
metric is a perceptual acceptance score. Both variants were rejected.

`Analyze-Sequences.py` evaluates the original reports and RGBA gzip frames.
`Retain-Sequences.py <pack-directory>` reconstructs every retained pixel frame
and checks its hash. One of each exact repeated pair is sufficient to reconstruct
the original duplicate; both cohort/frame hash lists remain in the raw reports.
The two experiment directories contain lossless XOR/gzip packs and indexes.

`Retain-Evidence.py` writes content-addressed native PNGs and gzip text records.
PNG bytes are unchanged. Repository/profile prefixes and UTF-8 BOMs in text are
normalized with separate original/retained/stored hashes. `--pending-tests`
omits a live full-suite log; the final pass retains its completed log instead.
The evidence index is an artifact locator, not an acceptance certificate.

Original goldens are never replaced. Human material and shadow-contact decisions
are separate from numerical correctness, visibility contracts and timing gates.
