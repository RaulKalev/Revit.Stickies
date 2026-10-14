<#
.SYNOPSIS
    Builds Stickies for Revit 2024 and 2026 and packs the release zip handed to colleagues.

.DESCRIPTION
    Output: artifacts\Stickies-<version>.zip containing Revit2024\Stickies.dll, Revit2026\Stickies.dll, the installer
    (Install.cmd / Uninstall.cmd / Install-Stickies.ps1) and a short README.txt. The version comes from
    Stickies\Stickies.csproj.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Stickies\Stickies.csproj'
$version = (([xml](Get-Content $project)).Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

dotnet build $project -c Release -p:StickiesUseDefaultOutput=true
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$artifacts = Join-Path $root 'artifacts'
$stage = Join-Path $artifacts "Stickies-$version"
$zip = "$stage.zip"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

$bin = Join-Path $root 'Stickies\bin\Release'
foreach ($pair in @(@('net48', 'Revit2024'), @('net8.0-windows', 'Revit2026'))) {
    $dir = New-Item -ItemType Directory -Force -Path (Join-Path $stage $pair[1])
    Copy-Item (Join-Path $bin "$($pair[0])\Stickies.dll") $dir
}
foreach ($file in 'Install.cmd', 'Uninstall.cmd', 'Install-Stickies.ps1') {
    Copy-Item (Join-Path $root "Deploy\$file") $stage
}
@"
Stickies $version - sticky notes for drawing review in Revit 2024 and 2026.

Install:   close Revit, then double-click Install.cmd.
Uninstall: double-click Uninstall.cmd.

Stickies is then on the RK Tools tab in Revit.
"@ | Set-Content -Path (Join-Path $stage 'README.txt') -Encoding UTF8

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "Release package: $zip" -ForegroundColor Green
