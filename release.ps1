<#
.SYNOPSIS
    Build, test and pack Jev.net; optionally push to nuget.org.

.EXAMPLE
    ./release.ps1 -Version 1.2.3                 # pack only -> ./artifacts/Jev.net.1.2.3.nupkg
    ./release.ps1 -Version 1.2.3 -Push           # pack and push using $env:NUGET_API_KEY
    ./release.ps1 -Version 1.2.3 -Push -ApiKey oy2...
    ./release.ps1 -Version 1.2.3 -Push -Source https://nuget.pkg.github.com/mrrasmussendk/index.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [switch] $Push,
    [string] $ApiKey = $env:NUGET_API_KEY,
    [string] $Source = 'https://api.nuget.org/v3/index.json',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$artifacts = Join-Path $PSScriptRoot 'artifacts'
if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }

Write-Host "==> Building $Version" -ForegroundColor Cyan
dotnet build Jev.net.sln --configuration Release "-p:Version=$Version"
if ($LASTEXITCODE) { exit $LASTEXITCODE }

if (-not $SkipTests) {
    Write-Host '==> Testing' -ForegroundColor Cyan
    dotnet test Jev.net.sln --no-build --configuration Release
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}

Write-Host '==> Packing' -ForegroundColor Cyan
dotnet pack Jev.net/Jev.net.csproj --no-build --configuration Release "-p:Version=$Version" --output $artifacts
if ($LASTEXITCODE) { exit $LASTEXITCODE }

Get-ChildItem $artifacts | ForEach-Object { Write-Host "    $($_.Name)" }

if (-not $Push) {
    Write-Host "`nPack only. Re-run with -Push to publish, or push a tag (git tag v$Version; git push origin v$Version) to let GitHub Actions do it." -ForegroundColor Yellow
    exit 0
}

if (-not $ApiKey) {
    throw 'No API key. Pass -ApiKey or set the NUGET_API_KEY environment variable.'
}

Write-Host "==> Pushing to $Source" -ForegroundColor Cyan
dotnet nuget push (Join-Path $artifacts "Jev.net.$Version.nupkg") --api-key $ApiKey --source $Source --skip-duplicate
if ($LASTEXITCODE) { exit $LASTEXITCODE }

Write-Host "`nPublished Jev.net $Version. Tag it so the source matches the package:" -ForegroundColor Green
Write-Host "    git tag v$Version; git push origin v$Version"
