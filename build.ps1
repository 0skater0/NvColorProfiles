#requires -Version 7
<#
.SYNOPSIS
  Builds the portable, self-contained single-file NvColorProfiles.exe (win-x64) and zips it.
.DESCRIPTION
  Output: artifacts/portable/NvColorProfiles.exe  +  artifacts/NvColorProfiles-portable-win-x64.zip
  Self-contained => no .NET runtime needed on the target machine. Just unzip and run.
#>
param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "src/NvColorProfiles/NvColorProfiles.csproj"
$outDir = Join-Path $root "artifacts/portable"

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

dotnet publish $project -c $Configuration -r win-x64 --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $outDir

$exe = Join-Path $outDir "NvColorProfiles.exe"
if (-not (Test-Path $exe)) { throw "Build did not produce $exe" }

$zip = Join-Path $root "artifacts/NvColorProfiles-portable-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $exe -DestinationPath $zip

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Portable build ready:" -ForegroundColor Green
Write-Host "  exe: $exe ($sizeMb MB)"
Write-Host "  zip: $zip"
