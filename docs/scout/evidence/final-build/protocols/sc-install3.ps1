Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('..', $PSScriptRoot)
$zip = Join-Path $repo 'artifacts/sp3/FORGELINE-0.1.434-dev-win-x64.zip'
$install = [IO.Path]::GetFullPath('../scout-install3', $repo)
if (Test-Path -LiteralPath $install) { throw 'Fresh installation target already exists.' }
$player = Join-Path $install 'player'
[IO.Compression.ZipFile]::ExtractToDirectory($zip, $player)
$verified = 0
foreach ($line in Get-Content -LiteralPath (Join-Path $player 'manifest.sha256')) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw 'Malformed package checksum.' }
    $expectedHash = $Matches[1]
    $file = [IO.Path]::GetFullPath($Matches[2], $player)
    if (-not $file.StartsWith($player + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Checksum leaves installation.' }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) { throw ('Installed checksum mismatch: ' + $file) }
    $verified++
}
$authoring = @(Get-ChildItem -LiteralPath $player -File -Recurse | Where-Object { $_.Extension -in @('.blend','.py','.glb','.gltf') -or $_.Name -like 'ForgeLine.AssetCompiler*' })
if ($authoring.Count) { throw 'Authoring/compiler content entered player package.' }
$provenance = Get-Content -LiteralPath (Join-Path $player 'build-provenance.json') -Raw | ConvertFrom-Json
if (-not $provenance.sourceTreeClean -or $provenance.sourceRevision -ne '9a2b60d7a046192de7aa663b0f7a83369919215a') { throw 'Unexpected package source.' }
$empty = Join-Path $install 'empty-work'
New-Item -ItemType Directory -Path $empty | Out-Null
$settings = Join-Path $install 'fresh-settings'
$logRoot = Join-Path $repo 'artifacts/si3'
New-Item -ItemType Directory -Path $logRoot | Out-Null
$oldPath = $env:PATH
try {
    $env:PATH = (Join-Path $env:WINDIR 'System32') + ';C:\Program Files\dotnet'
    $process = Start-Process -FilePath (Join-Path $player 'ForgeLine.Client.exe') -ArgumentList @('--smoke-test','--render-stress','1000','--settings-root',$settings,'--startup-diagnostics-output',(Join-Path $logRoot 'startup.json')) -WorkingDirectory $empty -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $logRoot 'stdout.log') -RedirectStandardError (Join-Path $logRoot 'stderr.log')
    if ($process.ExitCode -ne 0) { throw ('Installed smoke failed: ' + $process.ExitCode) }
}
finally { $env:PATH = $oldPath }
@{ schemaVersion = 1; sourceSha = $provenance.sourceRevision; sourceTreeClean = $provenance.sourceTreeClean;
   player = $player; zipSha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant();
   verifiedFiles = $verified; authoringAndCompilerFiles = $authoring.Count; installedSmokeExit = $process.ExitCode;
   smokeWorkingDirectoryFiles = @(Get-ChildItem -LiteralPath $empty -File -Recurse).Count;
   childPath = 'Windows System32 and .NET only'; settings = $settings; genericStressIsNotScoutWorkload = $true } |
   ConvertTo-Json | Set-Content (Join-Path $repo 'artifacts/sc-installation3.json') -Encoding utf8NoBOM
Write-Output 'Installed checksums and isolated player smoke passed.'

$global:LASTEXITCODE = 0
