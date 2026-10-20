# Experimental x64 AirPlay audio conversion

**Current API-6 integration (2026-10-20):** the opt-in build now pins
published `0.6.0-ci.330` and selects the audio-only or video profile independently
for each current/queued item's parsed tracks. Both logic/MSIX/payload profiles
and the focused written Mac/Apple TV MP3/FLAC/mixed-queue/transition gate pass in
development 1.0.0.15. Conversion is restored OFF; final owned cache is empty.
Read [the current scope and evidence](AIRPLAY_AUDIO_ONLY_POLICY.md). The older
checkpoints below retain their original package/binary scope.

**PR #16 review follow-up:** current video now preserves VLC's natural display
size; an unopened queued video resolves its own metadata instead of inheriting
an audio item's zero dimensions. Logic suites pass 142/157 and both MSIX/payload
profiles pass; loaded-runtime/focused manual checks in 1.0.0.14 are pending. Earlier 1.0.0.13 playback evidence remains scoped to
its original bytes. See [the active follow-up](AIRPLAY_AUDIO_FILES.md#review-follow-up-video-dimensions).

**2026-10-18 audio-file follow-up:** [AAC/M4A routing](AIRPLAY_AUDIO_FILES.md)
adds progressive `audio/mp4` for local M4A queue items in both build profiles,
bypassing video-required HLS and the conversion cache. Both logic/MSIX/payload
profiles pass; the recorded opt-in x64 Mac/Apple TV controls, cancellation,
mixed queue and transition-Stop gate passes by written user confirmation.
This does not add automatic conversion of unsupported audio-only codecs or a
new native package.

**2026-10-17 HDR package update:** the opt-in x64 pin now selects published
`0.5.0-ci.294`, including bounded HEVC SPS PQ/HLG range declarations in HLS
masters. This corrects the recorded HDR10/Mac grey-screen startup failure.
The user reports later Apple TV testing suggests the same HDR issue, with no
recurrence since the correction. Audio policy, stereo permission, quota, host
controls and ordinary codec-free ci.207 remain unchanged. Local logic tests pass
113/113 ordinary and 127/127 opt-in; cached/new pin and corruption/profile
controls pass. The ordinary MSIX builds and matches its two native files without
codec leakage. The opt-in MSIX builds and matches all 40 native/source/notice
files. Development 1.0.0.11 is registered in place from that actual MSIX, preserving
app data and leaving the source manifest unchanged. The AppContainer,
inspected executable and all six loaded published ci.294 DLL hashes match
before and after playback. The user confirms both Mac and Apple TV passed
90-second HDR/DTS playback from zero, pause/resume, forward/backward seeks,
Stop closure and local-paused handoff. Final owned cache is zero directories/bytes.
These bounded checks use the separately user-authorized input without retaining
its identity/path/content. Most recently reported versions are macOS 26.7.1 and
tvOS 26.6; the current reply reports no change without explicitly reconfirming
versions. No whole-film, HLG, speaker or discrete converted-surround claim is added.
The tested MSIX comes from implementation `66660bf7`; later changes are tests/docs.
The D91 observations below remain historical ci.282 results, not tests of the
new ci.294 bytes.

The first opt-in CI attempt fails two cleanup-admission cases because the test
gate releases itself after five seconds before a delayed assertion runs. The
follow-up keeps that gate closed until explicit test release, drains cleanup in
`finally`, and uses a separate bounded completion watchdog. Both 113/127 logic
suites pass again. This changes tests only, not the installed app or cast policy;
all four corrected-head CI jobs pass at `fbb01d20` in
[run 39005238604](https://github.com/ilyalissoboi/Screenbox/actions/runs/39005238604).
Inspect final documentation-head CI separately. No further D94 manual check is
pending; the full evidence is in [native PR #78](https://github.com/ilyalissoboi/send-airplay2/pull/78).

2026-10-16. The opt-in host integration is implemented on
`codex/airplay-automatic-audio-x64` in [PR #13](https://github.com/ilyalissoboi/Screenbox/pull/13).
This adds a build profile and an explicit
user preference; it is not enabled in ordinary builds. Local logic and package
checks pass. Recorded receiver checks are described below; the latest full queue
gate status is in [native checkpoint PR #73](https://github.com/ilyalissoboi/send-airplay2/pull/73).

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
symbol. The opt-in pin is `SendAirPlay2.StereoAac 0.6.0-ci.330`, SHA-256
`9567655c5c4f0183fddeaea9f497d869d28b79036d599cbf5d87f241e74eb3ad`, from the
[published prerelease](https://github.com/ilyalissoboi/send-airplay2/releases/tag/nuget-stereo-aac-v0.6.0-ci.330).
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
The separate PR/manual CI job builds/tests/packages the opt-in profile; existing
ordinary builds and tag publication keep their current profile.
Manual workflow dispatch defaults to ordinary validation; enable the boolean
`airplay_stereo_aac` input to include the experimental profile. Release creation
is limited to tag-push events.

## Casting settings layout check (2026-10-17)

Development 1.0.0.12 comes from implementation `6d3a30b4`. Both Release MSIX
profiles build and verify their two/40 pinned payload files; its running
executable matches the inspected opt-in MSIX. Registration preserves app data
and source manifest. The user confirms the Casting group, audio icon alignment
and toggle persistence after leaving/reopening Settings. This uses written
instructions, without Computer Use visual confirmation or a new receiver test.

## User policy and storage

Only the experimental build shows **Allow stereo audio conversion for AirPlay**
in Settings under its own **Casting** group, with the existing audio icon and
standard settings-card layout. The group heading and card are both hidden in
ordinary builds. The preference is initially off. The description explains that surround/lossless
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

The flyout offers Cancel while an initial AirPlay start is pending. It invokes
coordinator cancellation even before a native cast/source exists, keeps the busy
state until start returns, and disables selection/pair/forget/cast while pending.
Active casts and queue transitions retain Stop casting. Both profiles include
this cancellation path; the stereo permission/profile remain opt-in only.

## Validation checkpoint

Visual Studio 2026 builds both Core profiles and x64 app MSIX profiles. Logic
tests pass 113/113 ordinary and 127/127 opt-in, including immutable positional
reads, cancellation during seal, collisions/unknown-file preservation, locked-file
cleanup retry, unused/pre-transfer release, sibling isolation, cleanup ordering,
superseded admission, logger failure containment and explicit policy/error
mapping. These are pure logic tests and do not instantiate XAML. Existing
translation, legacy migration and third-party trimming warnings remain visible.
XAML formatting and `git diff --check` pass. Actual MSIX inspection passes for
two ordinary native files and all 40 opt-in native/source/notice files.

Development version 1.0.0.9 is registered in place from a real extracted MSIX,
preserving app data/pairings. It includes the final guard that treats a
superseded start as cancelled even if a concurrent failure completed first.
It also exposes the startup Cancel action. The source manifest keeps its normal
version. This process passes AppContainer and six loaded-DLL hash checks. The
user confirms setting/default off and unchanged macOS 26.7.1/tvOS 26.6. Mac
unanswered-consent cancellation through Cancel passes prompt closure,
responsiveness, paused local playback and no error. AAC copy with conversion off
passes on Mac and Apple TV: start near 45 seconds, synchronization/channel
pattern, pause/resume, both seeks, Stop closure and local paused handoff. DTS
refusal with conversion off passes on the Mac; converted DTS with conversion on
passes those same checks on both receivers. A 250-ms cache sampler across the DTS
checks observes at most one owned directory, peak 430,412 bytes, and zero
directories/bytes afterward. Sampling does not capture every instantaneous state.
The linked native checkpoint records natural/rapid queue observations separately.
The preceding 1.0.0.8 process passed AppContainer/six loaded-DLL hash checks; it
was replaced before receiver testing. Initially, GitHub reported no PR runs and manual dispatch returned
HTTP 422, "Actions has been disabled for this repository," although its read-only
permissions metadata reported enabled. The user enabled workflows; manual run
`38870523110` at `bff9e2fc` and automatic PR run `38871102108` at runtime head
`a6f1fcc9` pass all four jobs. Recheck final evidence heads. Native documentation
PR #73 passed all 17 checks at `52c5a3b`.

Review follow-up: a failed start whose request is still current restores local
playback if it was previously playing, including a cancellation returned by the
receiver/library. Caller Cancel/Stop invalidates the request and returns before
that recovery branch, preserving paused handoff. Both logic suites are rerun.
The recorded 1.0.0.9 receiver observations precede this recovery-only correction;
they do not claim a new receiver-side rejection observation.

Library D88 observations apply to its recorded native/test-host builds, not this
Screenbox host. The complete host gate verifies the installed
AppContainer and six loaded DLL hashes, then manually checks the setting/default,
AAC copy with conversion off, DTS refusal while off, DTS conversion while on,
sync/channel pattern/start near 45 seconds/pause/resume/seeks/Stop, Mac consent
cancellation and retry, and natural queue advance copy-to-conversion and back on
the recorded Mac/Apple TV. It also checks rapid Next supersession and Stop during
a transition, confirming local playback stays paused and caches drain. Consult
the linked checkpoint for completed versus pending observations; this procedure
does not request repeating completed tests.
That D91 procedure uses only the original repository-owned controls. The D94
HDR pin follow-up may use the separately user-authorized input, with its identity,
path and contents omitted from committed evidence; it does not repeat the D91
queue gate or imply whole-film validation.

Manual observations are requested in chat; Computer Use is not used to confirm
visual changes. Audio-only speakers and discrete converted surround remain
hardware-deferred. Chromecast verification remains hardware-deferred; sleeping
Apple TV casting has its separate earlier observation.
