[CmdletBinding()]
param([string]$OutputRoot = 'artifacts/generated-maps')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $assetIdentity = 'a' * 64 # Explicit synthetic host identity for the headless fixture.
    $runRoot = Join-Path $OutputRoot ([Guid]::NewGuid().ToString('N'))
    $first = Join-Path $runRoot 'first'
    $second = Join-Path $runRoot 'second'
    $compiler = 'tools/ForgeLine.MapCompiler/bin/Release/net10.0/ForgeLine.MapCompiler.dll'
    $headless = 'src/ForgeLine.Headless/bin/Release/net10.0/ForgeLine.Headless.dll'
    foreach ($folder in @($first, $second)) {
        dotnet $compiler --generate-map --seed 17 --width-chunks 12 --height-chunks 12 --output $folder --asset-sha256 $assetIdentity --preview-output (Join-Path $folder 'preview.svg') --qualification-output (Join-Path $folder 'qualification.json')
        if ($LASTEXITCODE -ne 0) { throw 'Generated compiler qualification failed.' }
    }
    foreach ($name in @('map.flgmap.json', 'geography.json', 'terrain.bin')) {
        $left = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $first $name)).Hash
        $right = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $second $name)).Hash
        if ($left -cne $right) { throw "Independent generated-map exports differ: $name" }
    }
    $recipe = Join-Path $runRoot 'recipe.json'
    $recipeMap = Join-Path $runRoot 'recipe-map'
    $header = Get-Content -LiteralPath (Join-Path $first 'map.flgmap.json') -Raw | ConvertFrom-Json
    $header.Recipe | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $recipe -Encoding utf8NoBOM
    dotnet $compiler --generate-map --recipe $recipe --output $recipeMap --asset-sha256 $assetIdentity
    if ($LASTEXITCODE -ne 0) { throw 'Recipe-driven generation failed.' }
    if ((Get-FileHash -LiteralPath (Join-Path $recipeMap 'map.flgmap.json')).Hash -cne (Get-FileHash -LiteralPath (Join-Path $first 'map.flgmap.json')).Hash) { throw 'Recipe/scalar export mismatch.' }
    dotnet $compiler --generate-map --seed 17 --output (Join-Path $runRoot 'collision') --asset-sha256 $assetIdentity --preview-output (Join-Path $runRoot 'collision/terrain.bin')
    if ($LASTEXITCODE -ne 1) { throw 'Diagnostic/package path collision was accepted.' }
    dotnet $compiler --generate-map --recipe $recipe --seed 17 --output (Join-Path $runRoot 'conflict') --asset-sha256 $assetIdentity
    if ($LASTEXITCODE -ne 1) { throw 'Recipe/scalar conflict was accepted.' }
    $manifest = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $first 'map.flgmap.json')).Hash.ToLowerInvariant()
    dotnet $compiler --load-generated-map $first --asset-sha256 $assetIdentity --expected-manifest-sha256 $manifest
    if ($LASTEXITCODE -ne 0) { throw 'Generated package verification failed.' }
    $save = Join-Path $runRoot 'match.save.json'
    $replay = Join-Path $runRoot 'match.replay.json'
    $loadArgs = @('--generated-map', $first, '--generated-asset-sha256', $assetIdentity, '--generated-manifest-sha256', $manifest)
    dotnet $headless @loadArgs --profile validation --ticks 20 --save-output $save --replay-output $replay
    if ($LASTEXITCODE -ne 0) { throw 'Generated headless match failed.' }
    dotnet $headless @loadArgs --load-input $save
    if ($LASTEXITCODE -ne 0) { throw 'Generated save recovery failed.' }
    dotnet $headless @loadArgs --replay-input $replay
    if ($LASTEXITCODE -ne 0) { throw 'Generated replay validation failed.' }
    dotnet $headless --generated-map $first --generated-asset-sha256 ('b' * 64) --ticks 1
    if ($LASTEXITCODE -ne 1) { throw 'Asset conflict must return a controlled failure (exit 1).' }
    dotnet $compiler --generate-map --seed invalid --output (Join-Path $runRoot 'invalid') --asset-sha256 $assetIdentity
    if ($LASTEXITCODE -eq 0) { throw 'Invalid seed was accepted.' }
    Write-Host "Generated map contracts passed: $runRoot; manifest=$manifest."
}
finally { Pop-Location }
