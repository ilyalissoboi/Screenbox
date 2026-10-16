# Experimental x64 AirPlay audio conversion

2026-10-16. The opt-in host integration is implemented on
`codex/airplay-automatic-audio-x64`. This adds a build profile and an explicit
user preference; it is not enabled in ordinary builds. Local logic and package
checks pass. Fresh Screenbox receiver/queue observations are still pending.

## Build and package selection

Ordinary builds retain codec-free `SendAirPlay2 0.3.0-ci.207`. Build the experimental
profile with Visual Studio 2026 MSBuild, consistently passing the same property
through restore, tests and packaging:

```powershell
.\scripts\Get-AirPlayPackage.ps1 -StereoAac -Platform x64
msbuild Screenbox.Core.Tests\Screenbox.Core.Tests.csproj -restore -p:Configuration=Release -p:Platform=x64 -p:AirPlayStereoAac=true
.\Screenbox.Core.Tests\bin\airplay-stereo-aac\x64\Release\net10.0-windows10.0.26100.0\Screenbox.Core.Tests.exe
msbuild Screenbox\Screenbox.csproj -restore -p:Configuration=Release -p:Platform=x64 -p:AirPlayStereoAac=true -p:ContinuousIntegrationBuild=true -p:AppxBundle=Never -p:AppxPackageDir=C:\AudioDeploy\ -p:UapAppxPackageBuildMode=SideloadOnly
```

`AirPlay.Package.props` owns the selected identity/version/tag/hash and compile
symbol. The opt-in pin is `SendAirPlay2.StereoAac 0.5.0-ci.282`, SHA-256
`5280f0868721476b2cae9018bd8197c3de3c9f356408781209ec57fd4b9ba4ff`, from the
[published prerelease](https://github.com/ilyalissoboi/send-airplay2/releases/tag/nuget-stereo-aac-v0.5.0-ci.282).
The downloader verifies existing files as well as new downloads. It preserves
rejected cache files for inspection. x86/ARM64, conflicting runtime identifiers
and malformed profile flags are refused. Both package identities must not enter
one dependency graph. Intermediate, binary and publish outputs are separated.

The opt-in app contains six exact native DLLs, notices, licenses, build/profile
metadata, the FFmpeg source archive, overlay, configuration and SPDX material
under `ThirdParty\SendAirPlay2`. The existing dynamically linked LGPL profile
is consumed unchanged; the core/binding remain Apache-2.0. The Screenbox adapter
is original host code following the callback contract, with no new third-party
implementation or dependency. Do not substitute VLC's codec binaries.

After extracting the actual MSIX to a fresh directory, run:

```powershell
.\scripts\Test-AirPlayDistribution.ps1 -StereoAac -LayoutDirectory C:\AudioLayout
```

It checks every packaged native/source/notice file against the hash-pinned
package. Without `-StereoAac`, it checks the two ordinary native files and refuses
the four added codec DLLs. `Test-AirPlayPackage.ps1` tests cached hashes,
corruption preservation and profile refusals offline after downloading both pins.
The separate PR-only CI job builds/tests/packages the opt-in profile; existing
ordinary builds and tag publication keep their current profile.

## User policy and storage

Only the experimental build shows **Allow stereo audio conversion for AirPlay**
in Settings, initially off. The description explains that surround/lossless
audio becomes stereo and the choice applies to the next cast. The host always
sets the permission explicitly: `PreserveOriginal` while off, `AllowStereo` while
on. It uses `AutomaticAudioHls` with `AppleHlsV1`. Eligible AAC-LC/AC-3/E-AC-3 is
copied; the finite supported DTS-core/FLAC/Opus/Vorbis/PCM/TrueHD subset may be
converted to 48-kHz stereo AAC. Video is copied. This does not infer receiver
capabilities or switch to a different original audio track after failure.

Each permitted cast reserves an exclusive child of the app's TemporaryFolder
before opening its brokered source. The native sealed-byte quota is 512 MiB;
this is a finite limit, not reserved disk space or whole-film admission proof.
Permission-off casts allocate no cache. The binding consumes source/cache on
every `Cast.Create` result. A pre-transfer cancellation or open failure abandons
only the untransferred reservation. Unused copy/refusal release removes an empty
reservation even when Clear is not called.

Fragments use exclusive temporary files and atomic publication. I/O is serialized
and polls borrowed cancellation controls. Cleanup closes the context, attempts all
known files, and removes its directory without recursion. It never adopts an
unknown file after a failed create/rename. Release retries failed owned-file
cleanup; failures are logged by type without paths. There is no broad cache
scavenger or eviction. Abrupt process termination can leave app-temporary files;
crash-recovery ownership/scavenging is a separate design.

Retired cast Stop/Dispose runs off the dispatcher in a serial cleanup chain. New
source/cache admission waits for that chain. Generation checks after cleanup,
cache reservation, source open and native start reject superseded requests;
Stop also invalidates starts that have not created a native cast yet. During a
queue transition only the newest item request survives. A fresh direct start is
refused until an existing initial/queue start returns, preventing overlapping
source/cache preparation. Failure categories distinguish audio selection, source,
codec, storage, quota/limits and generation timeouts. Terminal mapping respects
the first end reason. Local paused handoff and the existing queue semantics are
retained; Chromecast and local audio policy are unchanged.

## Validation checkpoint

Visual Studio 2026 builds both Core profiles and x64 app MSIX profiles. Logic
tests pass 111/111 ordinary and 125/125 opt-in, including immutable positional
reads, cancellation during seal, collisions/unknown-file preservation, locked-file
cleanup retry, unused/pre-transfer release, sibling isolation, cleanup ordering,
superseded admission, logger failure containment and explicit policy/error
mapping. These are pure logic tests and do not instantiate XAML. Existing
translation, legacy migration and third-party trimming warnings remain visible.
XAML formatting and `git diff --check` pass. Actual MSIX inspection passes for
two ordinary native files and all 40 opt-in native/source/notice files.

Library D88 observations apply to its recorded native/test-host builds, not this
Screenbox host. Before claiming host interoperability, verify the installed
AppContainer and six loaded DLL hashes, then manually check the setting/default,
AAC copy with conversion off, DTS refusal while off, DTS conversion while on,
sync/channel pattern/start near 45 seconds/pause/resume/seeks/Stop, Mac consent
cancellation and retry, and natural queue advance copy-to-conversion and back on
the recorded Mac/Apple TV. Confirm local playback stays paused and caches drain.
Use only the original repository-owned controls. No private film is cast.

Manual observations are requested in chat; Computer Use is not used to confirm
visual changes. Audio-only speakers and discrete converted surround remain
hardware-deferred. Chromecast verification remains hardware-deferred; sleeping
Apple TV casting has its separate earlier observation.
