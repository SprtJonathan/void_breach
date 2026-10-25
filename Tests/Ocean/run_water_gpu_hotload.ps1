param([Parameter(Mandatory)][string]$Cli, [Parameter(Mandatory)][int]$EditorPid)
$ErrorActionPreference = 'Stop'
$hotloadRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$hotloadProbe = Join-Path $hotloadRoot 'Code/Ocean/VBFFTOcean.GpuHotloadProbe.cs'
if (Test-Path -LiteralPath $hotloadProbe) { throw 'Refusing to overwrite an existing probe file.' }
function Invoke-WaterHotloadTool([string]$Name, [object]$Arguments = $null) {
    if ($null -eq $Arguments) { $reply = & $Cli --connect $EditorPid --call $Name 2>&1 }
    else { $reply = & $Cli --connect $EditorPid --call $Name ($Arguments | ConvertTo-Json -Compress) 2>&1 }
    if ($LASTEXITCODE -ne 0) { throw "sbox-cli $Name failed: $reply" }
    return (($reply -join "`n") | ConvertFrom-Json)
}
$hotloadInitial = Invoke-WaterHotloadTool 'editor_status'
if ($hotloadInitial.IsPlaying) { throw 'Stop Play before running the hotload test.' }
try {
    $null = Invoke-WaterHotloadTool 'play_start'
    $warm = Invoke-WaterHotloadTool 'run_csharp' @{code='for(var i=0;i<600 && Game.ActiveScene?.Camera.IsValid()!=true;i++) await Task.Delay(50,cancellationToken); var scene=Game.ActiveScene; if(!scene.Camera.IsValid()) throw new Exception("Runtime camera not ready"); var go=new GameObject(false,"Water GPU hotload probe"); go.SetParent(scene,false); go.WorldPosition=scene.Camera.WorldPosition-Vector3.Up*64; go.NetworkMode=NetworkMode.Never; var w=go.AddComponent<VoidBreach.Ocean.VBFFTOcean>(); w.Shape=VoidBreach.Ocean.FftWaterShape.Lake; w.Size=new Vector2(1200,1200); w.Resolution=64; w.WaveHeight=0; w.NativePhysics=false; w.UnderwaterEffects=false; go.Enabled=true; for(var i=0;i<600 && w.SimulationClock<0.3f;i++) await Task.Delay(50,cancellationToken); w.ApplySurfaceImpulse(go.WorldPosition,48,120); return new {ready=true};'}
    for ($hotloadVersion = 1; $hotloadVersion -le 3; $hotloadVersion++) {
        "namespace VoidBreach.Ocean; public sealed partial class VBFFTOcean { public int GpuLifetimeProbeVersion => $hotloadVersion; }" | Set-Content -LiteralPath $hotloadProbe
        $hotloadReady = $false
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            Start-Sleep -Milliseconds 500
            $state = Invoke-WaterHotloadTool 'editor_status'
            if (-not $state.IsPlaying) { throw 'Play stopped during the hotload test; hotload stability was not validated.' }
            if ($state.IsCompiling -or -not $state.LastCompileSucceeded) { continue }
            $probe = Invoke-WaterHotloadTool 'run_csharp' @{code='var w=Game.ActiveScene?.GetAllComponents<Component>().FirstOrDefault(c=>c.GameObject.Name=="Water GPU hotload probe" && c.GetType().FullName=="VoidBreach.Ocean.VBFFTOcean"); return new {version=w?.GetType().GetProperty("GpuLifetimeProbeVersion")?.GetValue(w)};'}
            if ($probe.version -eq $hotloadVersion) { $hotloadReady = $true; break }
        }
        if (-not $hotloadReady) { throw "Hotload version $hotloadVersion was not applied to the live water component" }
        $live = Invoke-WaterHotloadTool 'run_csharp' @{code='var w=Game.ActiveScene.GetAll<VoidBreach.Ocean.VBFFTOcean>().First(w=>w.GameObject.Name=="Water GPU hotload probe"); var before=w.InteractionGpuDispatches; for(var i=0;i<12;i++) { w.ApplySurfaceImpulse(w.WorldPosition,48,120); await Task.Delay(50,cancellationToken); } return new {progress=w.InteractionGpuDispatches>before,dispatches=w.InteractionGpuDispatches};'}
        if (-not $live.progress) { throw 'GPU simulation stopped after hotload' }
        Write-Output "HOTLOAD $hotloadVersion PASS dispatches=$($live.dispatches)"
    }
} finally {
    if (Test-Path -LiteralPath $hotloadProbe) { Remove-Item -LiteralPath $hotloadProbe }
    if (Get-Process -Id $EditorPid -ErrorAction SilentlyContinue) {
        try { $null = Invoke-WaterHotloadTool 'run_csharp' @{code='foreach(var go in Game.ActiveScene?.GetAllObjects(true).Where(g=>g.Name=="Water GPU hotload probe").ToArray() ?? Array.Empty<GameObject>()) go.Destroy(); return new {cleaned=true};'} } catch { Write-Warning $_ }
        try { $null = Invoke-WaterHotloadTool 'play_stop' } catch { Write-Warning $_ }
    }
}
