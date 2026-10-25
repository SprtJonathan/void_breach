$ErrorActionPreference = 'Stop'
$vbProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$vbRunnerRoot = Join-Path $vbProjectRoot 'tmp/attack-slot-checks'
New-Item -ItemType Directory -Force -Path $vbRunnerRoot | Out-Null
$vbPoolSource = [System.Security.SecurityElement]::Escape((Join-Path $vbProjectRoot 'Code/AI/VBAttackSlotPool.cs'))
$vbTestSource = [System.Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'AttackSlotChecks.cs'))
@"
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
 <ItemGroup><Compile Include="$vbPoolSource" /><Compile Include="$vbTestSource" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $vbRunnerRoot 'AttackSlotChecks.csproj')
dotnet run --project (Join-Path $vbRunnerRoot 'AttackSlotChecks.csproj') -v:quiet
if ($LASTEXITCODE -ne 0) { throw 'Attack slot checks failed' }
