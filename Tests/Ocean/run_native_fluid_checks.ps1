param([int]$EditorPid = 0)
# Reuse the existing editor; the older multi-scene harness is retained as historical test source.
& (Join-Path $PSScriptRoot 'run_live_water_checks.ps1') -Action interactions -EditorPid $EditorPid
