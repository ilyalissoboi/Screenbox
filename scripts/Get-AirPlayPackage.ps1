<#
.SYNOPSIS
    Places the pinned send-airplay2 NuGet package in the packages-airplay feed folder.

.DESCRIPTION
    Screenbox.Core references the SendAirPlay2 package, which is not on nuget.org yet
    (docs/AIRPLAY_INTEGRATION.md). nuget.config lists the repository's packages-airplay
    folder as a package source; this script fills it before a restore.

    The version is the one Screenbox.Core.csproj references. When
    packages-airplay\SendAirPlay2.<version>.nupkg already exists (for example a local
    pack copied from a send-airplay2 checkout), the script leaves it alone. Otherwise it
    downloads the asset of the send-airplay2 GitHub prerelease nuget-v<version> and
    accepts it only if its SHA-256 matches the hash recorded below for that version.

    When the reference moves to a new prerelease, add that version's hash to
    $KnownPackages.

.EXAMPLE
    PS> ./scripts/Get-AirPlayPackage.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# SHA-256 of each published prerelease asset, by package version.
$KnownPackages = @{
    '0.3.0-ci.196' = 'f94543305baa1dff9c01e03ba3565640f723e8b61a467aae823219d573e2bb2c'
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Screenbox.Core\Screenbox.Core.csproj'
$reference = ([xml](Get-Content $project -Raw)).SelectSingleNode("//PackageReference[@Include='SendAirPlay2']")
if ($null -eq $reference) {
    throw "Screenbox.Core.csproj has no SendAirPlay2 package reference"
}
$version = $reference.Version

$feed = Join-Path $root 'packages-airplay'
$package = Join-Path $feed "SendAirPlay2.$version.nupkg"
if (Test-Path $package) {
    Write-Host "SendAirPlay2 $version is already in packages-airplay"
    exit 0
}
if (-not $KnownPackages.ContainsKey($version)) {
    throw "No recorded hash for SendAirPlay2 $version. Copy a local pack into packages-airplay, or add the prerelease's SHA-256 to this script."
}

$uri = "https://github.com/ilyalissoboi/send-airplay2/releases/download/nuget-v$version/SendAirPlay2.$version.nupkg"
$download = "$package.download"
Write-Host "Downloading SendAirPlay2 $version from the send-airplay2 prerelease"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri $uri -OutFile $download -UseBasicParsing
$hash = (Get-FileHash $download -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $KnownPackages[$version]) {
    Remove-Item $download
    throw "SendAirPlay2 $version has SHA-256 $hash, not the recorded $($KnownPackages[$version])"
}
Move-Item $download $package
Write-Host "SendAirPlay2 $version placed in packages-airplay"
