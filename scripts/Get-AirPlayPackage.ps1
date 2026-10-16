<#
.SYNOPSIS
    Downloads and verifies the selected AirPlay package before restore.
.DESCRIPTION
    Evaluates the same profile properties as Screenbox.Core. Ordinary builds use
    the codec-free package; -StereoAac selects the experimental x64 profile.
    Existing cached files must match the pin too. No unverified local-pack bypass.
.EXAMPLE
    ./scripts/Get-AirPlayPackage.ps1 -StereoAac -Platform x64
#>
[CmdletBinding()]
param(
    [switch]$StereoAac,
    [ValidateSet('x64', 'x86', 'arm64')][string]$Platform = 'x64',
    [string]$FeedDirectory
)
$ErrorActionPreference = 'Stop'
if ($StereoAac -and $Platform -ne 'x64') {
    throw 'The opt-in AirPlay stereo AAC profile requires Platform=x64.'
}
$root = Split-Path -Parent $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -version '[18.0,19.0)' -latest -products * -find 'MSBuild\**\Bin\MSBuild.exe' |
    Select-Object -First 1
if (-not $msbuild) { throw 'Visual Studio 2026 MSBuild is required.' }
$project = Join-Path $root 'Screenbox.Core/Screenbox.Core.csproj'
$profile = $StereoAac.IsPresent.ToString().ToLowerInvariant()
$evaluated = & $msbuild $project -nologo "-p:Platform=$Platform" "-p:AirPlayStereoAac=$profile" `
    '-getProperty:AirPlayPackageId,AirPlayPackageVersion,AirPlayPackageSha256,AirPlayReleaseTag' | Out-String
if ($LASTEXITCODE -ne 0) { throw "Cannot evaluate the AirPlay package profile: $evaluated" }
$properties = ($evaluated | ConvertFrom-Json).Properties
$id = $properties.AirPlayPackageId
$version = $properties.AirPlayPackageVersion
$expected = $properties.AirPlayPackageSha256
$tag = $properties.AirPlayReleaseTag
if (-not $id -or -not $version -or $expected -notmatch '^[a-f0-9]{64}$' -or -not $tag) {
    throw 'Incomplete AirPlay package pin.'
}
$feed = if ($FeedDirectory) { [IO.Path]::GetFullPath($FeedDirectory) } else { Join-Path $root 'packages-airplay' }
New-Item -ItemType Directory -Force $feed | Out-Null
$package = Join-Path $feed "$id.$version.nupkg"
function Assert-PackageHash([string]$Path) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
        throw "AirPlay package hash mismatch for $id $version. Preserve the file for inspection and restore the pinned artifact."
    }
}
if (Test-Path -LiteralPath $package) {
    Assert-PackageHash $package
    Write-Host "Verified cached $id $version"
    return
}
$download = "$package.download.$([Guid]::NewGuid().ToString('N'))"
try {
    $uri = "https://github.com/ilyalissoboi/send-airplay2/releases/download/$tag/$id.$version.nupkg"
    Invoke-WebRequest -Uri $uri -OutFile $download -UseBasicParsing
    Assert-PackageHash $download
    Move-Item -LiteralPath $download -Destination $package
} finally {
    if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download }
}
Write-Host "Verified $id $version in packages-airplay"
