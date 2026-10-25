$ErrorActionPreference = 'Stop'
$vbProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$vbCodeProject = Join-Path $vbProjectRoot 'Code/void_breach.csproj'
$vbBuildOutput = Join-Path $vbProjectRoot 'tmp/survival-build'
$vbRunnerRoot = Join-Path $vbProjectRoot 'tmp/survival-math'
New-Item -ItemType Directory -Force -Path $vbBuildOutput, $vbRunnerRoot | Out-Null
dotnet build $vbCodeProject --no-restore "-p:OutputPath=$vbBuildOutput/" "-p:DocumentationFile=$vbBuildOutput/void_breach.xml" -v:quiet
if ($LASTEXITCODE -ne 0) { throw 'Production build failed' }
foreach ($vbCheck in @('validate_soldier_assets.py', 'validate_combat_balance.py', 'validate_survival_assets.py')) {
    python (Join-Path $PSScriptRoot $vbCheck)
    if ($LASTEXITCODE -ne 0) { throw "$vbCheck failed" }
}
[xml]$vbProjectXml = Get-Content -LiteralPath $vbCodeProject
$vbEngineReference = $vbProjectXml.Project.ItemGroup.Reference | Where-Object Include -like '*Sandbox.Engine.dll' | Select-Object -First 1
$vbEnginePath = (Resolve-Path (Join-Path (Split-Path $vbCodeProject) $vbEngineReference.Include)).Path
$vbEngineFolder = Split-Path $vbEnginePath
$vbCodeDll = [System.Security.SecurityElement]::Escape((Join-Path $vbBuildOutput 'void_breach.dll'))
$vbEngineDll = [System.Security.SecurityElement]::Escape($vbEnginePath)
$vbSystemDll = [System.Security.SecurityElement]::Escape((Join-Path $vbEngineFolder 'Sandbox.System.dll'))
$vbTestSource = [System.Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'SurvivalMathChecks.cs'))
@"
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
 <ItemGroup>
  <Compile Include="$vbTestSource" />
  <Reference Include="void_breach"><HintPath>$vbCodeDll</HintPath></Reference>
  <Reference Include="Sandbox.Engine"><HintPath>$vbEngineDll</HintPath></Reference>
  <Reference Include="Sandbox.System"><HintPath>$vbSystemDll</HintPath></Reference>
 </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $vbRunnerRoot 'SurvivalMathChecks.csproj')
dotnet run --project (Join-Path $vbRunnerRoot 'SurvivalMathChecks.csproj') -v:quiet
if ($LASTEXITCODE -ne 0) { throw 'Survival math checks failed' }
