param([string]$OutputRoot = 'artifacts/procedural-terrain')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$project = 'tools/ForgeLine.MapCompiler/ForgeLine.MapCompiler.csproj'
function Invoke-TerrainCompiler {
    param([string[]]$Options, [bool]$ShouldSucceed = $true)
    & dotnet run --project $project --configuration Release --no-build -- --generate-terrain @Options
    if (($LASTEXITCODE -eq 0) -ne $ShouldSucceed) { throw "Unexpected terrain compiler exit code: $LASTEXITCODE" }
}
$first = Join-Path $OutputRoot 'first.json'
$second = Join-Path $OutputRoot 'second.json'
Invoke-TerrainCompiler -Options @('--qualification-output', $first)
Invoke-TerrainCompiler -Options @('--qualification-output', $second)
$a = Get-Content -LiteralPath $first -Raw | ConvertFrom-Json
$b = Get-Content -LiteralPath $second -Raw | ConvertFrom-Json
if ($a.Diagnostics.TerrainSha256 -notmatch '^[0-9a-f]{64}$' -or
    $a.Diagnostics.TerrainSha256 -ne $b.Diagnostics.TerrainSha256 -or
    $a.Diagnostics.ChunkCount -ne 64 -or $a.Diagnostics.StoredSampleCount -ne 270400 -or
    $a.Diagnostics.SeamMismatchCount -ne 0 -or
    $a.Diagnostics.MaximumSampleGradient -gt $a.Recipe.MaximumGradient) { throw 'Terrain report contract failed.' }
$recipe = Join-Path $OutputRoot 'flat-recipe.json'
@{
    Generation = @{ TerrainProfileId = 'terrain.landforms.v1'; Seed = 0; Origin = @{ X = -1; Z = -1 } }
    BaseHeightMillimeters = 1234
    HillHeightMillimeters = 0; RidgeHeightMillimeters = 0; DetailHeightMillimeters = 0
    DrainageDepthMillimeters = 0
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $recipe
$flat = Join-Path $OutputRoot 'flat.json'
Invoke-TerrainCompiler -Options @('--recipe-input', $recipe, '--qualification-output', $flat)
$c = Get-Content -LiteralPath $flat -Raw | ConvertFrom-Json
if ($c.Diagnostics.ChunkCount -ne 1 -or $c.Diagnostics.MinimumHeightMeters -ne 1.234 -or
    $c.Diagnostics.MaximumHeightMeters -ne 1.234 -or $c.Diagnostics.MaximumSampleGradient -ne 0) { throw 'Flat terrain report failed.' }
Set-Content -LiteralPath $recipe -Value '{"Version":2}'
Invoke-TerrainCompiler -Options @('--recipe-input', $recipe) -ShouldSucceed $false
Set-Content -LiteralPath $recipe -Value '{"UnknownParameter":1}'
Invoke-TerrainCompiler -Options @('--recipe-input', $recipe) -ShouldSucceed $false
Set-Content -LiteralPath $recipe -Value '{"Generation":{"TerrainProfileId":"terrain.landforms.v1","WidthInChunks":256,"HeightInChunks":256}}'
Invoke-TerrainCompiler -Options @('--recipe-input', $recipe) -ShouldSucceed $false
Invoke-TerrainCompiler -Options @('--unknown-option') -ShouldSucceed $false
Write-Host 'Procedural terrain export and rejection contracts passed.'
