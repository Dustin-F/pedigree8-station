# Publishes the current version as a GitHub Release that tablets can update from.
# 1. Raise <Version> in Directory.Build.props   2. Commit and push   3. Run:  .\publish-release.ps1 -Notes "What changed"
# The release contains the program only — never station data, backups or migration output.
param([Parameter(Mandatory = $true)][string]$Notes, [string]$Repo = "Dustin-F/pedigree8-station")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dotnet = "$env:ProgramFiles\dotnet\dotnet.exe"
$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version
if (-not $version) { throw "No <Version> in Directory.Build.props." }

gh release view "v$version" --repo $Repo *> $null
if ($LASTEXITCODE -eq 0) { throw "Release v$version already exists. Raise the version in Directory.Build.props first." }
if (git -C $root status --porcelain) { throw "There are uncommitted changes. Commit and push them first." }

& $dotnet test (Join-Path $root "tests\Pedigree8.Core.Tests") -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Tests failed. Nothing published." }

$out = Join-Path $root "release\$version"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
& $dotnet build (Join-Path $root "src\Pedigree8.Station") -c Release --nologo -v quiet "-p:OutDir=$out\app/" "-p:DebugType=none"
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
Get-ChildItem "$out\app" -Filter *.pdb | Remove-Item
if (Test-Path "$out\app\seed") { throw "Release must not contain station data." }

$zip = Join-Path $out "Pedigree8Station-app-$version.zip"
Compress-Archive -Path "$out\app\*" -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii -NoNewline

gh release create "v$version" $zip "$zip.sha256" --repo $Repo --title "Pedigree8 Station $version" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "Publishing failed." }
Write-Host "Published v$version. Tablets get it from Backup & settings > Check for updates." -ForegroundColor Green
