<# .SYNOPSIS
Verifies native DLLs and complete audio redistribution material in an extracted MSIX layout.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$LayoutDirectory,
    [switch]$StereoAac
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$layout = (Resolve-Path -LiteralPath $LayoutDirectory).Path
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$msbuild = & $vswhere -version '[18.0,19.0)' -latest -products * -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'Visual Studio 2026 MSBuild is required.' }
$profile = $StereoAac.IsPresent.ToString().ToLowerInvariant()
$evaluated = & $msbuild (Join-Path $root 'Screenbox.Core/Screenbox.Core.csproj') -nologo `
    "-p:AirPlayStereoAac=$profile" -p:Platform=x64 '-getProperty:AirPlayPackageId,AirPlayPackageVersion,AirPlayPackageSha256' | Out-String
if ($LASTEXITCODE -ne 0) { throw 'Cannot evaluate package profile.' }
$pin = ($evaluated | ConvertFrom-Json).Properties
$package = Join-Path $root "packages-airplay/$($pin.AirPlayPackageId).$($pin.AirPlayPackageVersion).nupkg"
if ((Get-FileHash -LiteralPath $package).Hash.ToLowerInvariant() -ne $pin.AirPlayPackageSha256) { throw 'Pinned package hash mismatch.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package)
$count = 0
try {
    foreach ($entry in $archive.Entries) {
        $relative = $null
        if ($entry.FullName -like 'runtimes/win-x64/native/*.dll') {
            $relative = [IO.Path]::GetFileName($entry.FullName)
        } elseif ($StereoAac -and ($entry.FullName -like 'source/ffmpeg/*' -or
                $entry.FullName -in @('THIRD-PARTY-NOTICES.txt', 'LICENSE.txt', 'AUDIO-PROFILE.json', 'BUILD-INFO.txt'))) {
            if (-not $entry.Name) { continue }
            $relative = 'ThirdParty/SendAirPlay2/' + $entry.FullName
        }
        if (-not $relative) { continue }
        $installed = Join-Path $layout $relative
        if (-not (Test-Path -LiteralPath $installed -PathType Leaf)) { throw "Missing packaged payload: $relative" }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $expected = [Convert]::ToHexString($sha.ComputeHash($stream)) }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ((Get-FileHash -LiteralPath $installed).Hash -ne $expected) { throw "Packaged payload hash mismatch: $relative" }
        $count++
    }
    if (-not $StereoAac) {
        foreach ($codec in @('avcodec-63.dll', 'avformat-63.dll', 'avutil-61.dll', 'swresample-7.dll')) {
            if (Test-Path -LiteralPath (Join-Path $layout $codec)) { throw "Codec runtime leaked into ordinary layout: $codec" }
        }
    }
} finally { $archive.Dispose() }
Write-Host "Verified $count pinned native/source/notice files in the extracted app layout."
