#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishRoot,
    [Parameter(Mandatory)][string]$ArchivePath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Source,
    [Parameter(Mandatory)][string]$OutputRoot
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$root=[IO.Path]::GetFullPath($OutputRoot)
$allowed=(Join-Path $repo 'artifacts')+[IO.Path]::DirectorySeparatorChar
if(-not $root.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $root)){throw 'Choose a fresh package-smoke root below artifacts.'}
$publish=[IO.Path]::GetFullPath($PublishRoot)
$provenance=Get-Content -LiteralPath (Join-Path $publish 'build-provenance.json') -Raw|ConvertFrom-Json
if($provenance.sourceRevision -ne $Source){throw 'Packaged source differs from the requested source.'}
$archive=[IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ArchivePath))
try {
    $entries=@($archive.Entries|ForEach-Object FullName)
    $forbidden=@($entries|Where-Object { $_ -match '(?i)(^|/)(assets/source|blender|ForgeLine[.]AssetCompiler)(/|[.]|$)|[.](blend|blend1|py)$' })
    if($forbidden.Count){throw 'Player archive includes authoring/compiler dependencies.'}
} finally {$archive.Dispose()}
[void][IO.Directory]::CreateDirectory($root)
$info=[Diagnostics.ProcessStartInfo]::new((Join-Path $publish 'ForgeLine.Client.exe'))
$info.WorkingDirectory=$publish;$info.UseShellExecute=$false;$info.CreateNoWindow=$true
$info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
$info.Environment['FORGELINE_RUNTIME_ASSETS']=Join-Path $publish 'assets/runtime'
$info.Environment['PATH']=(Join-Path $env:SystemRoot 'System32')+';'+[IO.Path]::GetDirectoryName((Get-Command dotnet).Source)
foreach($argument in @('--smoke-test','--skip-splash','--settings-root',(Join-Path $root 'settings'),
    '--startup-diagnostics-output',(Join-Path $root 'startup.json'),
    '--visual-qualification-output',(Join-Path $root 'visual.json'))){$info.ArgumentList.Add($argument)}
$startedUtc=(Get-Date).ToUniversalTime().ToString('o')
$process=[Diagnostics.Process]::Start($info)
$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
$timeout=$false
while(-not $process.WaitForExit(1000)) {
    if(((Get-Date).ToUniversalTime()-[datetime]$startedUtc).TotalSeconds -gt 90){$timeout=$true;$process.Kill($true);$process.WaitForExit();break}
}
$out=$stdout.GetAwaiter().GetResult();$err=$stderr.GetAwaiter().GetResult()
[IO.File]::WriteAllText((Join-Path $root 'stdout.log'),$out)
[IO.File]::WriteAllText((Join-Path $root 'stderr.log'),$err)
$runtimeLoaded=$out.Contains('[assets:runtime] mode=player manifest=loaded') -and $out.Contains((Join-Path $publish 'assets/runtime'))
@{source=$Source;startedUtc=$startedUtc;completedUtc=(Get-Date).ToUniversalTime().ToString('o');exitCode=$process.ExitCode;
    timedOut=$timeout;runtimeLoadedFromPackage=$runtimeLoaded;archiveEntries=$entries.Count;
    archiveSha256=(Get-FileHash -LiteralPath $ArchivePath).Hash;authoringFilesInArchive=$forbidden.Count;
    method='Fresh child settings; authoritative packaged runtime root; player archive contains no source, Blender or compiler; child PATH contains only system and .NET directories. System-wide Blender installation is not removed.'}|
    ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $root 'status.json') -Encoding utf8NoBOM
if($timeout -or $process.ExitCode -ne 0 -or -not $runtimeLoaded){throw 'Source-less packaged player failed; inspect retained logs.'}
Write-Output 'Packaged player loaded its own runtime-only content and exited successfully.'
