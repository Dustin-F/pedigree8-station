# Copies the latest migration output into this PC's station data folder for testing.
# Any existing station data is moved aside first, never deleted.
$source = "$PSScriptRoot\migration-output\station-data"
$target = Join-Path $env:LOCALAPPDATA "Pedigree8"

if (-not (Test-Path "$source\station.db")) { throw "Run migrate.ps1 first." }
if (Get-Process Pedigree8Station -ErrorAction SilentlyContinue) { throw "Close Pedigree8 Station first." }

if (Test-Path $target) {
    $aside = "$target.before-$(Get-Date -Format yyyyMMdd-HHmmss)"
    Move-Item $target $aside
    Write-Host "Previous station data moved to $aside"
}

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item "$source\station.db" $target
if (Test-Path "$source\media") { Copy-Item "$source\media" $target -Recurse }
Write-Host "Station data ready in $target"
