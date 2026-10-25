param([Parameter(Mandatory)][string]$Cli, [Parameter(Mandatory)][int]$EditorPid, [int]$Cycles = 10)
$ErrorActionPreference = 'Stop'
$stressRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$stressLog = Join-Path $stressRoot 'tmp/water-gpu-lifetime.log'
function Invoke-WaterTool([string]$Name, [object]$Arguments = $null) {
    if (-not (Get-Process -Id $EditorPid -ErrorAction SilentlyContinue)) { throw "Editor $EditorPid exited" }
    if ($null -eq $Arguments) { $reply = & $Cli --connect $EditorPid --call $Name 2>&1 }
    else { $reply = & $Cli --connect $EditorPid --call $Name ($Arguments | ConvertTo-Json -Compress) 2>&1 }
    $toolExit = $LASTEXITCODE
    $reply | Add-Content -LiteralPath $stressLog
    if ($toolExit -ne 0) { throw "sbox-cli $Name failed ($toolExit): $reply" }
    return (($reply -join "`n") | ConvertFrom-Json)
}
$stressInitial = Invoke-WaterTool 'editor_status'
if ($stressInitial.IsPlaying) { throw 'Stop Play before the lifecycle stress test; it controls Play/stop itself.' }
$stressNativeLog = Join-Path $stressInitial.Paths.Logs 'sbox-dev.log'
$stressNativeOffset = if (Test-Path -LiteralPath $stressNativeLog) { (Get-Item -LiteralPath $stressNativeLog).Length } else { 0 }
"START $(Get-Date -Format o) pid=$EditorPid cycles=$Cycles" | Set-Content -LiteralPath $stressLog
try {
    for ($stressCycle = 1; $stressCycle -le $Cycles; $stressCycle++) {
        $null = Invoke-WaterTool 'play_start'
        $stressReply = Invoke-WaterTool 'run_csharp' @{path=(Join-Path $PSScriptRoot 'WaterGpuLifetimeNative.cs')}
        if (-not $stressReply.passed) { throw 'Runtime cycle did not pass' }
        $null = Invoke-WaterTool 'play_stop'
        $stressDrain = Invoke-WaterTool 'run_csharp' @{code='await Task.Delay(1000,cancellationToken); GC.Collect(); GC.WaitForPendingFinalizers(); await Task.Delay(1000,cancellationToken); var t=typeof(VoidBreach.Ocean.VBFFTOcean).Assembly.GetType("VoidBreach.Water.FluidGpuResources"); return new { alive = true, pending = t is null ? 0 : (int)t.GetProperty("PendingBatches").GetValue(null), released = t is null ? 0 : (int)t.GetProperty("ReleasedBatches").GetValue(null) };'}
        if ($stressDrain.pending -gt 0) { throw "GPU retirements remain pending after Play stop: $($stressDrain.pending)" }
        $stressProcess = Get-Process -Id $EditorPid
        $stressLine = "CYCLE $stressCycle PASS dispatches=$($stressReply.dispatches) retired=$($stressReply.retired) pending=$($stressReply.pending) privateMiB=$([math]::Round($stressProcess.PrivateMemorySize64/1MB))"
        $stressLine | Add-Content -LiteralPath $stressLog
        Write-Output $stressLine
    }
    # Earlier crashes occurred tens of seconds after apparently successful cleanup.
    $null = Invoke-WaterTool 'run_csharp' @{code='for(var i=0;i<45;i++) { await Task.Delay(1000,cancellationToken); if(i%5==0) { GC.Collect(); GC.WaitForPendingFinalizers(); } } return new { alive = true };'}
    "SURVIVED_DELAYED_CLEANUP PASS" | Add-Content -LiteralPath $stressLog
    Write-Output 'SURVIVED_DELAYED_CLEANUP PASS'
    if (Test-Path -LiteralPath $stressNativeLog) {
        $stressStream = [System.IO.File]::Open($stressNativeLog, 'Open', 'Read', 'ReadWrite')
        try {
            if ($stressStream.Length -ge $stressNativeOffset) { $null = $stressStream.Seek($stressNativeOffset, 'Begin') }
            $stressReader = [System.IO.StreamReader]::new($stressStream)
            $stressNewLog = $stressReader.ReadToEnd()
            $stressReader.Dispose()
        } finally { $stressStream.Dispose() }
        $stressFaults = $stressNewLog -split "`n" | Where-Object { $_ -match 'Bad deletion request|ResourceHandleToData|returning error texture resource|Water GPU resource retirement failed' }
        if ($stressFaults) { $stressFaults | Add-Content -LiteralPath $stressLog; throw 'Native texture lifetime errors occurred, even though the process survived.' }
        Write-Output 'NATIVE_TEXTURE_LIFETIME_LOG PASS'
    }
} catch {
    "FAIL $_" | Add-Content -LiteralPath $stressLog
    throw
} finally {
    if (Get-Process -Id $EditorPid -ErrorAction SilentlyContinue) {
        try {
            $stressFinal = Invoke-WaterTool 'editor_status'
            if ($stressFinal.IsPlaying) { $null = Invoke-WaterTool 'play_stop' }
        } catch { Write-Warning "Could not restore stopped Play state: $_" }
    }
}
