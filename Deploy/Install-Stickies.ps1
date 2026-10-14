<#
.SYNOPSIS
    Installs (or removes) Stickies for the current Windows user. No administrator rights needed.

.DESCRIPTION
    For each supported Revit version that is installed (2024, 2026), copies Stickies.dll to
    %AppData%\Autodesk\Revit\Addins\<year>\Stickies\ and writes Stickies.addin next to it, so Revit loads Stickies at
    startup. Run it from the unzipped release folder (it expects Revit2024\ and Revit2026\ next to it).
    Close Revit first: a running Revit keeps the old DLL locked.

.EXAMPLE
    .\Install-Stickies.ps1                 # every installed supported Revit version
    .\Install-Stickies.ps1 -Year 2026      # one version
    .\Install-Stickies.ps1 -Uninstall      # remove Stickies again
#>
[CmdletBinding()]
param(
    [ValidateSet('2024', '2026')]
    [string[]] $Year,
    [switch] $Uninstall
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$builds = @{ '2024' = 'Revit2024'; '2026' = 'Revit2026' }
$addInId = 'B4F1A9D2-6C37-4E85-9F20-7D1E3A5C8B61'

function Test-RevitInstalled([string] $y) {
    (Test-Path "$env:ProgramFiles\Autodesk\Revit $y") -or (Test-Path "$env:ProgramData\Autodesk\Revit\Addins\$y")
}

if (-not $Year) {
    $Year = @($builds.Keys | Sort-Object | Where-Object { Test-RevitInstalled $_ })
    if (-not $Year) {
        Write-Host 'No supported Revit version (2024 or 2026) was found on this computer.' -ForegroundColor Yellow
        exit 1
    }
}

foreach ($y in $Year) {
    $addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$y"
    $target = Join-Path $addins 'Stickies'
    $manifest = Join-Path $addins 'Stickies.addin'

    if ($Uninstall) {
        if (Test-Path $manifest) { Remove-Item $manifest -Force }
        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        Write-Host "Revit ${y}: Stickies removed."
        continue
    }

    $source = Join-Path $here (Join-Path $builds[$y] 'Stickies.dll')
    if (-not (Test-Path $source)) {
        Write-Host "Revit ${y}: $source is missing, skipped." -ForegroundColor Yellow
        continue
    }

    New-Item -ItemType Directory -Force -Path $target | Out-Null
    try {
        Copy-Item $source (Join-Path $target 'Stickies.dll') -Force
    }
    catch {
        Write-Host "Revit ${y}: could not replace Stickies.dll. Close Revit and run the installer again." -ForegroundColor Red
        continue
    }
    # Files from a downloaded zip are marked as coming from the internet; Revit refuses to load those.
    Get-ChildItem $target -File | Unblock-File

    $dll = Join-Path $target 'Stickies.dll'
    @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Stickies</Name>
    <Assembly>$dll</Assembly>
    <FullClassName>Stickies.App</FullClassName>
    <AddInId>$addInId</AddInId>
    <VendorId>RaulKalev</VendorId>
    <VendorDescription>Raul Kalev (RK Tools)</VendorDescription>
  </AddIn>
</RevitAddIns>
"@ | Set-Content -Path $manifest -Encoding UTF8

    Write-Host "Revit ${y}: Stickies installed. Start Revit and look for Stickies on the RK Tools tab." -ForegroundColor Green
}
