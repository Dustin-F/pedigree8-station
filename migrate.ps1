# Re-runnable migration: Pedigree8 (read-only) -> migration-output\station-data
# Pedigree8's Docker containers must be running. Run again any time, e.g. right before cut-over.
# Your lab owner email is kept in local.settings.txt (not uploaded to GitHub):  OwnerEmail=you@example.com
param(
    [string]$EnvFile = "D:\Coding Projects\Pedigree8\.env",
    [string]$OwnerEmail,
    [string]$Out = "$PSScriptRoot\migration-output\station-data",
    [switch]$LocalDates
)

if (-not $OwnerEmail) {
    $local = Join-Path $PSScriptRoot "local.settings.txt"
    if (Test-Path $local) {
        $OwnerEmail = (Get-Content $local | Where-Object { $_ -match '^\s*OwnerEmail\s*=' } | Select-Object -First 1) -replace '^\s*OwnerEmail\s*=\s*', ''
    }
}
if (-not $OwnerEmail) { throw "Set OwnerEmail=... in local.settings.txt or pass -OwnerEmail." }

$dotnet = "$env:ProgramFiles\dotnet\dotnet.exe"
& $dotnet build "$PSScriptRoot\tools\Pedigree8.Migrate" -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$migrateArgs = @("--env-file", $EnvFile, "--owner-email", $OwnerEmail.Trim(), "--out", $Out)
if ($LocalDates) { $migrateArgs += "--local-dates" }

& $dotnet "$PSScriptRoot\tools\Pedigree8.Migrate\bin\Release\net8.0\Pedigree8.Migrate.dll" @migrateArgs
exit $LASTEXITCODE
