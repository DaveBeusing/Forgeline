# Blender authoring entrypoint

The pinned toolchain is **Blender 5.2.2 release, x64**. `toolchain.json` defines the
exact version; other patches and prerelease builds fail. PowerShell **7.4+** and
Windows/Linux x64 are required. Windows native execution is qualified; Linux
acceptance is implemented but has not been natively qualified. macOS/ARM hosts
fail explicitly. Version pinning does not establish cross-platform byte identity;
executable SHA-256 and Blender build hash are retained per run.

Blender, its bundled Python and `bpy` are authoring dependencies only. No external
Python, pip, virtual environment, addon download or runtime/client/headless
reference is introduced. Existing production exporters and assets remain unchanged.
See [asset ownership](../../docs/AssetPipeline.md).

## Preproduction conveyor kit

The editable 12-module raised conveyor dimensions and matching socket frames are
specified by [the visual kit](../../docs/ConveyorVisualLanguage.md). The
portable contract validator is `python tools/blender/conveyor_contract.py`;
its tests are discovered by ordinary CI. It does not run Blender or export
meshes, and the source JSON must not be treated as a compiler asset declaration.

## Installation and discovery

Install/extract exactly **5.2.2 Windows x64** from the
[official release archive](https://download.blender.org/release/Blender5.2/),
checking the vendor's checksums. No automatic download or install is performed.
Discovery order:

1. `-BlenderPath` executable path, then `FORGELINE_BLENDER_PATH`.
2. First `blender.exe` (Windows) or `blender` (Linux) application on PATH.
3. Repository `artifacts/toolchains/blender-5.2.2/blender.exe` (Linux: `blender`).
4. Windows `%ProgramFiles%/Blender Foundation/Blender 5.2/blender.exe`, or Linux
   `/opt/blender-5.2.2/blender` and `/usr/local/blender-5.2.2/blender`.

Explicit missing paths and discovered wrong versions fail without silent fallback.
Pass the executable rather than its directory. Fix PATH in the current session:

```powershell
$env:PATH = 'C:\Program Files\Blender Foundation\Blender 5.2;' + $env:PATH
Get-Command blender
blender --version
pwsh ./tools/blender/Invoke-Blender.ps1 -Action Check
# Override discovery:
pwsh ./tools/blender/Invoke-Blender.ps1 -Action Check -BlenderPath 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
```

For direct embedded-Python diagnostics, follow Blender's
[argument execution order](https://docs.blender.org/manual/en/latest/advanced/command_line/arguments.html):

```powershell
blender --background --factory-startup --python-exit-code 23 --python-expr "import bpy, struct; print(bpy.app.version_string, struct.calcsize('P') * 8)"
```

## Script contract

```powershell
pwsh ./tools/blender/Invoke-Blender.ps1 -Action Smoke -OutputRoot 'artifacts/blender smoke'
# Call directly from a PowerShell 7.4+ session to pass a typed array:
& ./tools/blender/Invoke-Blender.ps1 -Action Script -Script tools/my-export.py -InputRoot assets/source -OutputRoot artifacts/my-export -ScriptArguments @('variant one','literal "quotes"')
```

`my-export.py` illustrates a future script; it is not a supplied production exporter.
Actions are `Check`, `Smoke` and `Script`. Default output is
`artifacts/blender/check`; select scoped output roots for exports. Relative paths
for executable, script, input, output and cancellation resolve against the
repository, independently of the caller's working directory. Input must be an
existing directory. Script requires a trusted `.py` file; Check/Smoke reject Script
arguments rather than ignoring them.

Each call leases its output directory and creates `runs/<unique-id>/` as its
working/log directory. Output must be below repository `artifacts/`; source/runtime
targets, traversal and ancestor junctions/symlinks are rejected. Old output is never
recursively cleaned. Smoke overwrites only its own deterministic `source/` files;
qualification requires a fresh root and preserves earlier evidence.

Processes use background/factory startup, disabled automatic execution, offline
mode, one thread and Python exception exit **23**. Shell interpolation and
`--python-use-system-env` are not used. External Python and virtual-environment
variables are removed in the child only; IO/logs use UTF-8. Explicit scripts are
trusted code: the output convention is not an operating-system file sandbox.

A script receives `-- --request <absolute-json-path>` with:

```json
{
  "schemaVersion": 1,
  "inputRoot": "<absolute existing input directory>",
  "outputRoot": "<absolute artifacts subdirectory>",
  "runRoot": "<absolute per-invocation directory>",
  "arguments": ["variant one", "literal \"quotes\""]
}
```

Read it with Python's standard library and import `bpy` inside Blender. JSON keeps
spaces, Unicode, quotes, leading dashes and empty arguments intact; ScriptArguments
cannot add Blender switches. Pass array parameters by calling the script directly
inside PowerShell, as above; native `pwsh -File` argument forwarding does not
preserve a typed PowerShell array. Write reproducible exports below outputRoot, temporary
files below runRoot, and treat inputRoot as read-only by authoring convention.

Both output-root and per-run `report.json` record success/error and binary/embedded
capabilities. Separate version/probe/script stdout and stderr logs retain UTF-8.
Nonzero exit and absent/invalid capability reports fail. A version banner alone
never qualifies `bpy` support.

`-TimeoutSeconds` defaults to 120 **per process** (version/probe/script); timeout
kills the launched tree. `-CancellationFile path/to/cancel.signal` checks a marker
before launch and while waiting. Create it to cancel, remove it before retrying.
Finally cleanup terminates a still-running child on unwinding. Failure evidence
is retained and stale success is not reused. Fix path/version/bpy errors rather
than suppressing them; increase timeout only for exports that need it.

## Validation and CI

```powershell
# No Blender required: explicitly labelled executable protocol fixture.
pwsh ./tools/blender/Test-BlenderToolchain.ps1 -OutputRoot artifacts/blender-contracts-local
# Actual Blender and canonical compiler, with fresh output root:
dotnet build ForgeLine.sln --configuration Release
pwsh ./tools/blender/Test-BlenderSmoke.ps1 -OutputRoot 'artifacts/blender qualification'
```

The smoke cube exports glTF 2.0 GLB with one material, UVs, normals, tangents and
outward CCW winding. Actual binary geometry is checked: metre-scale Y-up bounds
`[-1,0,-1]` to `[1,2,1]`, 24 vertices and 12 triangles. Object transforms are baked
because the existing compiler reads mesh data rather than node transforms. Its two
asset IDs exist only under artifacts. Production IDs, sockets, collision, gameplay
footprints, LOD/material identities and simulation ownership remain unchanged.

Protocol contracts cover discovery/precedence, missing/unsupported binaries,
bpy/architecture/version failures, Unicode/argument boundaries, UTF-8 logs,
external Python isolation, nonzero exit, timeout/cancellation with child termination,
output containment and concurrent-output exclusion. Results are **fixture-only**.
Native qualification exports twice, compares all source/runtime files, compiles
through ForgeLine.AssetCompiler and validates without source discovery. It also
checks real Unicode arguments and expected Python exit 23, and compares production
source hashes/status before/after. No pip packages are required.

Normal CI runs protocol contracts without installing Blender. The separate manual
**Blender authoring qualification** workflow uses a provisioned self-hosted runner
labelled `Windows`, `X64`, `blender`, with PowerShell 7.4+, the pinned SDK and Blender
5.2.2. An optional executable input is passed as an environment value. Without such
a runner it queues; normal build/player CI does not acquire Blender. Local native
evidence does not claim that manual workflow was executed.

Upgrades require an intentional policy and fixture version change plus native
compiler/idempotency qualification and geometry/material review. No automatic
version range or production migration is provided. Future exporters must retain
the existing asset compiler and production source/runtime boundaries.

## Production source import/export

[Source round trips](../../docs/BlenderAssetRoundTrip.md) extend this launcher with
asset-definition-based tank/building imports, editable projects, strict GLB export,
state/LOD/collision/socket validation and original-vs-compiled renderer checks.
Use `Invoke-BlenderRoundTrip.ps1` with an explicit fresh artifacts output directory.
Existing production authors retain ownership; generated source clones are never
applied to the game automatically.

## Surface and LOD preparation

The pinned production entrypoint adds bounded modifier evaluation, PBR inspection, LOD/state cost reports and repeated compiled qualification. See [surface production](../../docs/BlenderSurfaceProduction.md).

## Rigid motion authoring

Use Invoke-BlenderMotion.ps1 and Test-BlenderMotion.ps1 for the existing tank/factory part metadata, reviewed mechanical pivots and compiled runtime matrix parity. See [mechanical authoring](../../docs/BlenderMechanicalAuthoring.md).

Saved Directorate tank production: [authoring and recovery](../../docs/BlenderTankProduction.md). Use `Invoke-BlenderTank.ps1` for isolated exports and `Test-BlenderTank.ps1` for reopened-source, edit rejection and compiler integrity checks.

Independent rigid clips: [supported assets and native review](../../docs/RigidClipAssets.md). `Test-BlenderRigidClips.ps1` compares the committed fixture with two actual exports and source-less compiled validation.

## Infantry skeletal masters

`infantry_skin.py` reconstructs the four-figure Directorate rigs from retained component geometry. `Test-BlenderInfantry.ps1` reopens production masters, checks repeated compiled payload parity, performs an actual key edit and validates source-less loading. See [skin contracts and commands](../../docs/InfantrySkinAssets.md).

The optional [procedural CPU atlas producer](../../docs/ProceduralPbrProduction.md) preserves the existing atlas and runtime budgets in isolated, verifiable source generations. Production promotion and art approval remain explicit; see its qualification report.

## Logistics truck masters

Use `Invoke-BlenderLogisticsTruck.ps1` to export the saved Cargo/Supply masters and
`Test-BlenderLogisticsTruck.ps1` for native saved-edit, reproducibility and source-less
compiler checks. See [truck production](../../docs/LogisticsTruckBlender.md).
