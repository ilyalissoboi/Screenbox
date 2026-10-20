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

## Validation checkpoint

VS 2026 MSBuild compiles both pure logic profiles. Tests pass 142/142 ordinary
and 163/163 opt-in, covering audio-container classification, video-track precedence,
missing tracks, profile/MIME reset between mixed items, permission independence,
finite quota and existing dimension/cancellation/lifetime controls. Both pinned
downloads, corruption refusals and architecture/profile guards pass.

The actual opt-in Release MSIX builds; all 40 pinned native/source/notice files
match. Development 1.0.0.15 is registered in place with identity/data preserved
and only its extracted manifest version changed. Ordinary MSIX inspection and
loaded-runtime/manual checks are pending. Existing translation, legacy migration
and third-party trim warnings remain; no native interoperability is inferred
from logic or packaging success.

Written manual checks target the changed paths: MP3 with conversion off, a
required-conversion refusal, converted FLAC controls, and MP3 → converted PCM MOV
→ video → AAC queues on the recorded Mac/macOS 26.7.1 and Apple TV/tvOS 26.6.
Queue advance, rapid Next and transition Stop cover the changed metadata/profile
boundary. Reuse earlier consent-cancellation and full codec listening evidence;
do not repeat those unchanged matrices. No new settings/layout is introduced.

Speaker/RAOP transport, discrete converted surround, Chromecast hardware,
user-deferred remote-Stop classification and long-input/cache-quota evaluation
remain deferred. This is a development gate, not a new Screenbox release.
