# Builds the tablet package: fresh migration -> app build -> seed data -> secret scan -> zip.
# Run on the PC right before moving to the tablet. Pedigree8's Docker containers must be running.
param([switch]$SkipMigration)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dotnet = "$env:ProgramFiles\dotnet\dotnet.exe"
$stamp = Get-Date -Format "yyyyMMdd-HHmm"
$staging = Join-Path $root "package\Pedigree8Station-Setup-$stamp"
$migrated = Join-Path $root "migration-output\station-data"

if (-not $SkipMigration) {
    & (Join-Path $root "migrate.ps1")
    if ($LASTEXITCODE -ne 0) { throw "Migration failed - see the report. Package not built." }
}
if (-not (Test-Path "$migrated\station.db")) { throw "No migrated data found. Run without -SkipMigration." }

& $dotnet test (Join-Path $root "tests\Pedigree8.Core.Tests") -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Tests failed. Package not built." }

New-Item -ItemType Directory -Force $staging | Out-Null
& $dotnet build (Join-Path $root "src\Pedigree8.Station") -c Release --nologo -v quiet "-p:OutDir=$staging\app/" "-p:DebugType=none"
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
Get-ChildItem "$staging\app" -Filter *.pdb | Remove-Item

New-Item -ItemType Directory -Force "$staging\app\seed" | Out-Null
Copy-Item "$migrated\station.db" "$staging\app\seed\"
if (Test-Path "$migrated\media") { Copy-Item "$migrated\media" "$staging\app\seed\" -Recurse }
Copy-Item "$migrated\migration-report.html" $staging
Copy-Item (Join-Path $root "packaging\*") $staging

# The package must not contain any connection details or secrets from the old Pedigree8 setup.
# Secret values are read from the old .env at build time so they are never written into this repo.
$forbidden = @("DATABASE_URL", "MINIO_SECRET", "MINIO_ACCESS_KEY", "BETTER_AUTH", "postgresql://")
$oldEnv = "D:\Coding Projects\Pedigree8\.env"
if (Test-Path $oldEnv) {
    $forbidden += Get-Content $oldEnv | Where-Object { $_ -match '^\s*[A-Z_]*(SECRET|PASSWORD)[A-Z_]*\s*=\s*(.{6,})$' } | ForEach-Object { $Matches[2].Trim().Trim('"') }
}
$latin1 = [System.Text.Encoding]::GetEncoding(28591)
$hits = foreach ($file in Get-ChildItem $staging -Recurse -File) {
    $text = [System.IO.File]::ReadAllText($file.FullName, $latin1)
    foreach ($word in $forbidden) { if ($text.IndexOf($word, [StringComparison]::OrdinalIgnoreCase) -ge 0) { "$($file.Name): $word" } }
}
if ($hits) { Remove-Item $staging -Recurse -Force; throw "Secret scan FAILED, package deleted:`n$($hits -join "`n")" }

$zip = "$staging.zip"
Compress-Archive -Path "$staging\*" -DestinationPath $zip -CompressionLevel Optimal
Write-Host ""
Write-Host "Secret scan: clean ($($forbidden.Count) patterns checked)" -ForegroundColor Green
Write-Host ("Package: {0}  ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)) -ForegroundColor Green
Write-Host "Copy the zip to a USB stick, unzip it on the tablet and double-click install.bat."
