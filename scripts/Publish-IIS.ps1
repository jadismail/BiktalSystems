param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "D:\Systems\BiktalSystems\publish"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "..\Biktal.WebMVC.csproj" | Resolve-Path

Write-Host "Publishing Biktal.WebMVC ($Configuration) -> $OutputPath"
& "C:\Program Files\dotnet\dotnet.exe" publish $project -c $Configuration -o $OutputPath

New-Item -ItemType Directory -Path (Join-Path $OutputPath "logs") -Force | Out-Null

Write-Host "Publish complete."
Write-Host "Next: run scripts\Setup-IIS.ps1 as Administrator to configure IIS."
