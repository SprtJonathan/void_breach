param(
    [ValidateSet('interactions','benchmark','benchmark-water')][string]$Action = 'interactions',
    [int]$EditorPid = 0
)
$ErrorActionPreference = 'Stop'
$liveRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$liveEditors = @(Get-Process -Name 'sbox-dev' -ErrorAction SilentlyContinue)
if ($EditorPid -eq 0) {
    if ($liveEditors.Count -ne 1) { throw 'Exactly one s&box editor must already be open. This script never launches an editor.' }
    $EditorPid = $liveEditors[0].Id
} elseif ($EditorPid -notin $liveEditors.Id) { throw 'The requested editor is not running. This script never launches an editor.' }
$liveFiles = [ordered]@{
    'NativeWaterInteractionChecks.cs' = 'NativeWaterInteractionChecks.cs'
    'NativeWaterPerformance.cs' = 'NativeWaterPerformance.cs'
    'NativeWaterLiveBridge.cs' = 'WaterLiveValidation.cs'
}
$liveTargets = @($liveFiles.Values | ForEach-Object { Join-Path $liveRoot "Editor/$_" })
foreach ($liveTarget in $liveTargets) { if (Test-Path -LiteralPath $liveTarget) { throw "Refusing to overwrite $liveTarget" } }
New-Item -ItemType Directory -Force -Path (Join-Path $liveRoot 'tmp') | Out-Null
$liveLease = [System.IO.File]::Open((Join-Path $liveRoot 'tmp/native-water-validation.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
$liveCommand = Join-Path $liveRoot 'tmp/water-live-command.json'
$liveState = Join-Path $liveRoot 'tmp/water-live-state.json'
$liveInstalled = @()
function Send-WaterCommand([string]$Command, [int]$TimeoutSeconds = 20) {
    $liveNonce = [guid]::NewGuid().ToString()
    @{pid=$EditorPid;action=$Command;nonce=$liveNonce} | ConvertTo-Json | Set-Content -LiteralPath $liveCommand -Encoding utf8
    $liveDeadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 150
        if (Test-Path -LiteralPath $liveState) {
            $liveText = Get-Content -LiteralPath $liveState -Raw
            $liveReply = try { $liveText | ConvertFrom-Json } catch { $null }
            if ($liveReply -and $liveReply.nonce -eq $liveNonce) { return $liveReply }
            if ($liveText -match '^System\..*Exception:') { throw $liveText }
        }
    } while ((Get-Date) -lt $liveDeadline)
    throw 'The existing editor did not acknowledge the validation command. No editor was launched.'
}
try {
    foreach ($livePair in $liveFiles.GetEnumerator()) {
        $liveTarget = Join-Path $liveRoot "Editor/$($livePair.Value)"
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $livePair.Key) -Destination $liveTarget
        (Get-Item -LiteralPath $liveTarget).LastWriteTime = Get-Date
        $liveInstalled += $liveTarget
    }
    Start-Sleep -Milliseconds 700
    $liveReadyDeadline = (Get-Date).AddSeconds(30)
    do {
        $liveReady = Send-WaterCommand 'status'
        if ($liveReady.version -ge 5 -and -not $liveReady.building) { break }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $liveReadyDeadline)
    if ($liveReady.version -lt 5 -or $liveReady.building) { throw 'The existing editor did not finish compiling the validation helpers.' }
    if ($liveReady.compile | Where-Object { $_.success -eq $false -or $_.errors.Count -gt 0 }) { throw 'Fix editor compilation errors before running native checks.' }
    $null = Send-WaterCommand $Action
    $liveOutputName = switch ($Action) {
        'interactions' { 'water-interaction-native.log' }
        'benchmark' { 'water-perf-live.log' }
        'benchmark-water' { 'water-perf-live-ocean.log' }
    }
    $liveOutput = Join-Path $liveRoot "tmp/$liveOutputName"
    $liveDone = if ($Action -eq 'interactions') { 'INTERACTION_DONE' } else { 'PERF_DONE' }
    $liveDeadline = (Get-Date).AddMinutes(3)
    do {
        Start-Sleep -Milliseconds 250
        $liveLog = if (Test-Path -LiteralPath $liveOutput) { Get-Content -LiteralPath $liveOutput -Raw } else { '' }
        if ($liveLog -match $liveDone) { break }
    } while ((Get-Date) -lt $liveDeadline)
    Write-Output $liveLog
    if ($liveLog -notmatch $liveDone -or $liveLog -match ' FAIL|ERROR ') { throw 'Native water checks failed or timed out.' }
    if ($Action -eq 'interactions') {
        foreach ($liveCheck in @('NATIVE_SHOT','PELLET','PROPAGATION','SPRAY','PLAYER_ENTRY','PLAYER_SWIM','PLAYER_EXIT','DISTANT_SHOT','WIND_NO_REBUILD','NATIVE_VOLUME','BUOYANCY','DENSE_SINK')) {
            if ($liveLog -notmatch "$liveCheck PASS") { throw "Missing native check: $liveCheck" }
        }
    }
} finally {
    if ($liveInstalled.Count -eq $liveFiles.Count) {
        try { $null = Send-WaterCommand 'cancel' 5 } catch { Write-Warning $_ }
    }
    foreach ($liveTarget in $liveInstalled) { if (Test-Path -LiteralPath $liveTarget) { Remove-Item -LiteralPath $liveTarget } }
    if (Test-Path -LiteralPath $liveCommand) { Remove-Item -LiteralPath $liveCommand }
    $liveLease.Dispose()
}
