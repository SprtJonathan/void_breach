$ErrorActionPreference = 'Stop'
$fftRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$fftRunner = Join-Path $fftRoot 'tmp/fft-checks'
New-Item -ItemType Directory -Force -Path $fftRunner | Out-Null
[xml]$fftProject = Get-Content (Join-Path $fftRoot 'Code/void_breach.csproj')
$fftReference = $fftProject.Project.ItemGroup.Reference | Where-Object Include -like '*Sandbox.Engine.dll' | Select-Object -First 1
$fftEngine = Split-Path (Resolve-Path (Join-Path (Join-Path $fftRoot 'Code') $fftReference.Include)).Path
$fftEngineXml = [System.Security.SecurityElement]::Escape($fftEngine)
$fftRootXml = [System.Security.SecurityElement]::Escape($fftRoot)
@"
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
 <ItemGroup>
  <Compile Include="$fftRootXml/Tests/Ocean/FftOceanChecks.cs" />
  <Compile Include="$fftRootXml/Code/Ocean/FftOceanSpectrum.cs" />
  <Reference Include="Sandbox.Engine"><HintPath>$fftEngineXml/Sandbox.Engine.dll</HintPath></Reference>
  <Reference Include="Sandbox.System"><HintPath>$fftEngineXml/Sandbox.System.dll</HintPath></Reference>
 </ItemGroup>
</Project>
"@ | Set-Content (Join-Path $fftRunner 'FftOceanChecks.csproj')
dotnet run --project (Join-Path $fftRunner 'FftOceanChecks.csproj') -v:quiet
if ($LASTEXITCODE -ne 0) { throw 'FFT checks failed' }
$fftFluidProject = (Get-Content (Join-Path $fftRunner 'FftOceanChecks.csproj') -Raw).Replace('FftOceanChecks.cs', 'FluidSurfaceChecks.cs').Replace('</ItemGroup>', @"
  <Compile Include="$fftRootXml/Code/Ocean/FftSurfaceSampler.cs" />
  <Compile Include="$fftRootXml/Code/Ocean/FftWaterDomain.cs" />
  <Compile Include="$fftRootXml/Code/Ocean/FftMeshTessellation.cs" />
  <Compile Include="$fftRootXml/Code/Water/FluidSurfaceField.cs" />
  <Compile Include="$fftRootXml/Code/Water/FluidSurfaceRay.cs" />
  <Compile Include="$fftRootXml/Code/Water/FluidContactMath.cs" />
 </ItemGroup>
"@)
$fftFluidProject | Set-Content (Join-Path $fftRunner 'FluidSurfaceChecks.csproj')
dotnet run --project (Join-Path $fftRunner 'FluidSurfaceChecks.csproj') -v:quiet
if ($LASTEXITCODE -ne 0) { throw 'Fluid surface checks failed' }
