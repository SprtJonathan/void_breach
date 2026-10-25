param([int]$EditorPid = 0, [switch]$WaterView)
$waterAction = if ($WaterView) { 'benchmark-water' } else { 'benchmark' }
& (Join-Path $PSScriptRoot 'run_live_water_checks.ps1') -Action $waterAction -EditorPid $EditorPid
