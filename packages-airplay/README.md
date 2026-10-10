# packages-airplay

A local NuGet feed for the SendAirPlay2 package (AirPlay casting,
`docs/AIRPLAY_INTEGRATION.md`) until it is published on nuget.org. `nuget.config`
lists this folder as a package source. Package files here are not committed.

- `scripts/Get-AirPlayPackage.ps1` downloads the version `Screenbox.Core.csproj`
  references from the send-airplay2 GitHub prerelease and checks its SHA-256.
- To try a local build of send-airplay2, copy its pack
  (`scripts/pack_nuget.ps1` there) into this folder and reference its version.
