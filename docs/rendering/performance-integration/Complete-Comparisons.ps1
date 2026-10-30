param([string]$RepositoryRoot,[string]$OutputRoot)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath($RepositoryRoot)
$output=[IO.Path]::GetFullPath($OutputRoot,$repo)
function Invoke-MatchedComparison([string]$Current,[string]$Baseline,[string]$Mode='Matched'){
    $label=$Current+'-vs-'+$Baseline
    try {
        & (Join-Path $repo 'build/Compare-ReferencePerformance.ps1') -CurrentRoot (Join-Path $output $Current) -BaselineRoot (Join-Path $output $Baseline) -ComparisonMode $Mode -ReportPath (Join-Path $output ($label+'.performance.json'))
        & (Join-Path $repo 'build/Compare-VisualReferenceEvidence.ps1') -CurrentRoot (Join-Path $output $Current) -BaselineRoot (Join-Path $output $Baseline) -ComparePixels -ReportPath (Join-Path $output ($label+'.images.json'))
    } catch {
        [ordered]@{comparison=$label;mode=$Mode;status='unqualified';error=$_.Exception.Message} | ConvertTo-Json | Set-Content (Join-Path $output ($label+'.unqualified.json'))
    }
}
foreach($suffix in @('fixed','sweep','production')){
    Invoke-MatchedComparison "candidate-a-$suffix" "baseline-a-$suffix"
    Invoke-MatchedComparison "candidate-b-$suffix" "baseline-b-$suffix"
    Invoke-MatchedComparison "baseline-b-$suffix" "baseline-a-$suffix"
    Invoke-MatchedComparison "candidate-b-$suffix" "candidate-a-$suffix"
}
Invoke-MatchedComparison 'memory-upload-a' 'candidate-a-production' 'StaticMemory'
Invoke-MatchedComparison 'memory-upload-b' 'candidate-b-production' 'StaticMemory'
Invoke-MatchedComparison 'memory-upload-b' 'memory-upload-a'
Write-Output 'All matched cohorts, source inventories, advisory comparisons and strict pixel differences retained.'
