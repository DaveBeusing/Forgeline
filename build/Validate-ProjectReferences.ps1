[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projects = Get-ChildItem -Path $RepositoryRoot -Recurse -Filter *.csproj -File |
    Where-Object { $_.FullName -notmatch "[\\/](bin|obj)[\\/]" }

$graph = @{}
$nameByPath = @{}

foreach ($project in $projects) {
    [xml]$xml = Get-Content -Path $project.FullName -Raw
    $references = @(
        $xml.Project.ItemGroup.ProjectReference |
            Where-Object { $_ -ne $null } |
            ForEach-Object {
                [System.IO.Path]::GetFullPath((Join-Path $project.DirectoryName $_.Include))
            }
    )

    $graph[$project.FullName] = $references
    $nameByPath[$project.FullName] = [System.IO.Path]::GetFileNameWithoutExtension($project.Name)
}

foreach ($projectPath in $graph.Keys) {
    foreach ($reference in $graph[$projectPath]) {
        if (-not $graph.ContainsKey($reference)) {
            throw "Project '$($nameByPath[$projectPath])' references an unknown project path '$reference'."
        }
    }
}

$visitState = @{}

function Visit-Project {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.List[string]]$Stack
    )

    $state = if ($visitState.ContainsKey($ProjectPath)) { $visitState[$ProjectPath] } else { 0 }

    if ($state -eq 1) {
        $cycle = @($Stack | ForEach-Object { $nameByPath[$_] }) + $nameByPath[$ProjectPath]
        throw "Circular project reference detected: $($cycle -join ' -> ')"
    }

    if ($state -eq 2) {
        return
    }

    $visitState[$ProjectPath] = 1
    $Stack.Add($ProjectPath)

    foreach ($reference in $graph[$ProjectPath]) {
        Visit-Project -ProjectPath $reference -Stack $Stack
    }

    $Stack.RemoveAt($Stack.Count - 1)
    $visitState[$ProjectPath] = 2
}

foreach ($projectPath in $graph.Keys) {
    Visit-Project -ProjectPath $projectPath -Stack ([System.Collections.Generic.List[string]]::new())
}

function Get-ProjectPath {
    param([Parameter(Mandatory = $true)][string]$ProjectName)

    $match = @($nameByPath.Keys | Where-Object { $nameByPath[$_] -eq $ProjectName })

    if ($match.Count -ne 1) {
        throw "Expected exactly one project named '$ProjectName', found $($match.Count)."
    }

    return $match[0]
}

function Get-ReachableProjectNames {
    param([Parameter(Mandatory = $true)][string]$StartProjectName)

    $start = Get-ProjectPath -ProjectName $StartProjectName
    $seen = [System.Collections.Generic.HashSet[string]]::new()
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push($start)

    while ($pending.Count -gt 0) {
        $current = $pending.Pop()

        foreach ($reference in $graph[$current]) {
            if ($seen.Add($reference)) {
                $pending.Push($reference)
            }
        }
    }

    return @($seen | ForEach-Object { $nameByPath[$_] })
}

$core = Get-ProjectPath -ProjectName "ForgeLine.Core"
if ($graph[$core].Count -ne 0) {
    throw "ForgeLine.Core must not reference higher-level projects."
}

$headlessForbidden = @(
    "ForgeLine.Platform.Windows",
    "ForgeLine.Graphics",
    "ForgeLine.Audio",
    "ForgeLine.Presentation",
    "ForgeLine.UI",
    "ForgeLine.Client"
)

$headlessReachable = Get-ReachableProjectNames -StartProjectName "ForgeLine.Headless"
foreach ($forbidden in $headlessForbidden) {
    if ($headlessReachable -contains $forbidden) {
        throw "ForgeLine.Headless must not depend on '$forbidden'."
    }
}

$simulationForbidden = @(
    "ForgeLine.Platform.Windows",
    "ForgeLine.Graphics",
    "ForgeLine.Audio",
    "ForgeLine.Presentation",
    "ForgeLine.UI",
    "ForgeLine.Client",
    "ForgeLine.Editor"
)

$simulationReachable = Get-ReachableProjectNames -StartProjectName "ForgeLine.Simulation"
foreach ($forbidden in $simulationForbidden) {
    if ($simulationReachable -contains $forbidden) {
        throw "ForgeLine.Simulation must not depend on '$forbidden'."
    }
}

$graphicsReachable = Get-ReachableProjectNames -StartProjectName "ForgeLine.Graphics"
if ($graphicsReachable -contains "ForgeLine.Game") {
    throw "ForgeLine.Graphics must not depend on ForgeLine.Game rules."
}

Write-Host "Project reference graph validated: $($projects.Count) projects, no cycles, architecture boundaries preserved."
