# API 6 audio-only routing in the opt-in build

2026-10-20. After native PR #96 merged as `b57981b`, the user authorized
Screenbox integration of the published API-6 package. Implementation `59d83910`
updates only the experimental x64 pin to `SendAirPlay2.StereoAac 0.6.0-ci.330`.
The [public prerelease](https://github.com/ilyalissoboi/send-airplay2/releases/tag/nuget-stereo-aac-v0.6.0-ci.330)
package SHA-256 is
`9567655c5c4f0183fddeaea9f497d869d28b79036d599cbf5d87f241e74eb3ad`.
Ordinary builds retain codec-free `SendAirPlay2 0.3.0-ci.207` and their existing
M4A progressive/video-remux behavior. No new dependency or codec is adopted.

## Per-item policy and ownership

The opt-in coordinator uses parsed track presence to choose an explicit policy
for each current or queued local file. Audio with no video selects AppleAudioV1;
any video track selects AppleHlsV1. Extension and MIME do not decide whether an
audio codec is supported. Unknown track metadata fails before native casting.
The library still validates content, selected tracks and finite timelines; no
failed cast retries another policy or representation.

The audio profile serves the original validated stereo AAC/M4A or MP3 bytes
progressively, or converts the covered FLAC/PCM/Ogg subset to stereo AAC HLS
when the existing stereo-conversion setting is on. With the setting off,
required conversion refuses through the existing format notification. The finite
[library contract](https://github.com/ilyalissoboi/send-airplay2/blob/746c520e17d812102a14ba4edae47a81239c4c81/docs/automatic-audio-only.md)
is narrower than all files with these codec/container names.

Permission is configured separately and cannot overwrite the item's profile.
The setting still defaults off and the quota stays 512 MiB. When conversion is
enabled, the host offers a dedicated empty cache reservation per cast. A
progressive result does not seal/read/clear native fragments; releasing the
unused context removes the host's empty reservation. With permission off no
store is offered. Existing source/store transfer, drain, cleanup and paused
local handoff remain under the coordinator's lifetime owner.

For queued opt-in items, complete metadata is required before classifying absence
of video. An independently owned brokered descriptor is parsed if necessary,
without opening the local player; its grant is disposed in `finally`. Stop and
superseding requests cancel that parse and the request ticket is checked before
native start. Each new item independently selects its policy and display size:
audio is `(0, 0)`, current video retains VLC's effective size, and queued video
uses its own sample-aspect/orientation metadata. Neither policy nor dimensions
are inherited from a preceding item.

## Validation gate: passed in development 1.0.0.15

VS 2026 MSBuild compiles both pure logic profiles. Tests pass 142/142 ordinary
and 163/163 opt-in, covering audio-container classification, video-track precedence,
missing tracks, mixed-item profile/MIME reset, permission independence, finite
quota and existing dimension/cancellation/lifetime controls. Both pinned downloads,
corruption refusals and architecture/profile guards pass.

Both actual Release MSIX builds and distribution checks pass: two ordinary DLLs
without codec leakage, and all 40 opt-in native/source/notice files matching
ci.330. Existing translation, legacy migration and third-party trim warnings
remain. Development 1.0.0.15 updates in place with identity/data preserved and
only its extracted manifest version changed. Four actual package/AppContainer,
executable and six-DLL captures match. Inspect final PR-head CI separately;
local receiver observations do not verify separately built CI artifacts.

The user confirms these focused written checks on Mac/macOS 26.7.1 and Apple
TV/tvOS 26.6, with versions unchanged:

- MP3 with conversion off on both: nonzero start, RIGHT/BOTH markers,
  pause/resume, forward/backward seeks, Stop, local paused and app responsive.
- Native FLAC with conversion off on Mac: format/conversion refusal,
  no receiver playback and local paused.
- Native FLAC with conversion on, both: nonzero start, markers, controls,
  both seeks, Stop, local paused and app responsive.
- MP3 → converted PCM MOV audio → video → AAC, both: natural queue advance
  with correct sound/video and local paused after the queue ended.
- Rapid Next twice reached the third video's picture/sound on both. Next
  followed immediately by Stop during transition closed playback without delayed
  restart, left local playback paused and kept the app responsive.

Conversion is restored OFF and the app remains open/paused. Final owned cache
directories/files are zero without cleanup mutation. No further manual check is
pending. No Computer Use visual confirmation or unchanged cancellation/full
codec listening matrix was repeated. The complete sanitized executable/module,
MSIX, fixture and observation evidence is in
[native evidence PR #97](https://github.com/ilyalissoboi/send-airplay2/pull/97).
Earlier checkpoints retain their original binary scopes; this focused gate does
not establish all variants, gapless playback or sustained long-input behavior.

No setting/layout changes or new release are introduced. Speaker/RAOP transport,
discrete converted surround, Chromecast hardware, user-deferred remote-Stop
classification and long-input/cache-quota evaluation remain deferred.
