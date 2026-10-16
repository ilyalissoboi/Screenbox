<# .SYNOPSIS
Tests cached-package integrity and the explicit x64 build-profile guard offline.
Run Get-AirPlayPackage.ps1 for both profiles first.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$download = Join-Path $PSScriptRoot 'Get-AirPlayPackage.ps1'
$scratch = Join-Path $root "obj/airplay-package-test-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $scratch | Out-Null
$copies = @()
function Assert-Refused([scriptblock]$Operation, [string]$Message) {
    try { & $Operation }
    catch {
        if ($_.Exception.Message -like "*$Message*") { return }
        throw
    }
    throw "Expected refusal: $Message"
}
try {
    foreach ($stereo in @($false, $true)) {
        $name = if ($stereo) { 'SendAirPlay2.StereoAac.0.5.0-ci.282.nupkg' } else { 'SendAirPlay2.0.3.0-ci.207.nupkg' }
        $copy = Join-Path $scratch $name
        Copy-Item -LiteralPath (Join-Path $root "packages-airplay/$name") -Destination $copy
        $copies += $copy
        & $download -StereoAac:$stereo -FeedDirectory $scratch
        $bytes = [IO.File]::ReadAllBytes($copy)
        $bytes[0] = $bytes[0] -bxor 1
        [IO.File]::WriteAllBytes($copy, $bytes)
        $corruptHash = (Get-FileHash -LiteralPath $copy).Hash
        Assert-Refused { & $download -StereoAac:$stereo -FeedDirectory $scratch } 'hash mismatch'
        if ((Get-FileHash -LiteralPath $copy).Hash -ne $corruptHash) { throw 'Rejected cache was modified.' }
    }
    Assert-Refused { & $download -StereoAac -Platform arm64 -FeedDirectory $scratch } 'requires Platform=x64'
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $msbuild = & $vswhere -version '[18.0,19.0)' -latest -products * -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) { throw 'Visual Studio 2026 MSBuild is required.' }
    $project = Join-Path $root 'Screenbox.Core/Screenbox.Core.csproj'
    foreach ($properties in @(
        @('-p:AirPlayStereoAac=true', '-p:Platform=arm64'),
        @('-p:AirPlayStereoAac=true', '-p:Platform=x64', '-p:RuntimeIdentifier=win-arm64'),
        @('-p:AirPlayStereoAac=invalid', '-p:Platform=x64')
    )) {
        $output = & $msbuild $project -nologo -t:CheckAirPlayBuildProfile @properties 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -or $output -notmatch 'requires Platform=x64|must be true or false') {
            throw "Build-profile guard did not refuse: $output"
        }
    }
    Write-Host 'Both cached pins, corruption refusals, downloader architecture and three MSBuild profile refusals passed.'
} finally {
    foreach ($copy in $copies) { Remove-Item -LiteralPath $copy -ErrorAction SilentlyContinue }
    # Only the two explicitly created copies are removed; never recurse.
    Remove-Item -LiteralPath $scratch
}
$global:LASTEXITCODE = 0
