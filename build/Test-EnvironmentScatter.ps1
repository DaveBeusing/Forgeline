param([string]$OutputRoot = 'artifacts/environment-scatter')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$project = 'tools/ForgeLine.MapCompiler/ForgeLine.MapCompiler.csproj'
function Invoke-ScatterCompiler {
    param([string[]]$Options, [bool]$ShouldSucceed = $true)
    & dotnet run --project $project --configuration Release --no-build -- --scatter-diagnostics @Options
    if (($LASTEXITCODE -eq 0) -ne $ShouldSucceed) { throw "Unexpected scatter compiler exit code: $LASTEXITCODE" }
}
$first = Join-Path $OutputRoot 'first.json'
$second = Join-Path $OutputRoot 'second.json'
$plots = Join-Path $OutputRoot 'references'
Invoke-ScatterCompiler -Options @('--seed','17','--maximum-instances','10000','--qualification-output',$first,'--reference-plots',$plots)
Invoke-ScatterCompiler -Options @('--seed','17','--maximum-instances','10000','--qualification-output',$second)
$a = Get-Content -LiteralPath $first -Raw | ConvertFrom-Json
$b = Get-Content -LiteralPath $second -Raw | ConvertFrom-Json
if ($a.SceneSha256 -ne '0db7e1f080eb9270ecd09cfacff4a5ad670666bcba4390aa01412e5bf9434be4' -or
    $a.SceneSha256 -ne $b.SceneSha256 -or $a.InstanceCount -ne 8035 -or
    $a.BlockedFootprintCount -ne 0 -or $a.Chunks.Count -ne 144 -or
    ($a.Distribution.PSObject.Properties.Value | Measure-Object -Sum).Sum -ne $a.InstanceCount) { throw 'Scatter report contract failed.' }
$treeCounts = @{}
foreach ($name in @('dense-forest','clearing','forest-edge','technical-yard')) {
    [xml]$svg = Get-Content -LiteralPath (Join-Path $plots "$name.svg") -Raw
    $circles = @($svg.SelectNodes('//*[local-name()="circle"]'))
    if ($circles.Count -lt 100) { throw "Reference plot $name has no useful placement evidence." }
    $treeCounts[$name] = @($circles | Where-Object { $_.GetAttribute('fill') -eq '#28664d' }).Count
    if ($name -eq 'technical-yard' -and @($circles | Where-Object { $_.GetAttribute('fill') -eq '#b47642' }).Count -eq 0) { throw 'Technical yard has no props.' }
}
if ($treeCounts['dense-forest'] -le $treeCounts['clearing']) { throw 'Reference forest/clearing distribution is reversed.' }
Invoke-ScatterCompiler -Options @('--maximum-instances','0') -ShouldSucceed $false
Invoke-ScatterCompiler -Options @('--density','1001') -ShouldSucceed $false
Invoke-ScatterCompiler -Options @('--unknown-option') -ShouldSucceed $false
Write-Host 'Scatter export, reference plots and rejection contracts passed.'
exit 0
