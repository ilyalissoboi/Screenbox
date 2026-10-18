# AirPlay AAC/M4A audio files

2026-10-18, implementation in progress. The user authorizes the next Screenbox
slice after native documentation PR #82 merges as `afb9ee8`, following the
completed native D97 and packaged UWP D98 AAC/M4A gates. Those observations do
not establish receiver behavior for this Screenbox build.

## Implemented routing

For each brokered local file, including every new queue item, `.m4a` selects
progressive delivery with explicit `audio/mp4`. Extension matching ignores case
and does not depend on VLC having opened/parsing the queued item. M4A is a
container hint, not codec validation: this slice validates AAC/M4A only. It does
not claim support for every codec or protected file that can use that extension.
There is no retry/fallback conversion after a receiver rejects a file.

Both build profiles use the existing pinned progressive API: ordinary ci.207
and opt-in ci.294 are unchanged. Video retains HLS remux/automatic-audio policy.
M4A bypasses automatic-audio HLS and allocates no conversion fragment store,
regardless of the stored stereo-conversion preference. Unsupported audio-only
formats, new demuxers/decoders and audio-only HLS/transcoding are separate work.
No third-party implementation, dependency or new transport is added.

The existing coordinator continues to own pause-before-start, source lifetime,
serial cleanup, Stop/cancellation, superseded queue requests, repeat-one,
natural-end advance and paused local handoff. M4A player dimensions are zero;
queued video uses its parsed dimensions when available, with the existing
fallback otherwise. The format-error resource now refers to media rather than
only video. No new settings or layout are introduced.

## Validation checkpoint

VS 2026 MSBuild builds both Core profiles. Pure logic suites pass 122/122 ordinary
and 137/137 opt-in. Routing controls cover case-insensitive M4A, unimplemented
extensions retaining existing behavior and a return to video policy after M4A.
Existing cleanup/admission/cancellation tests remain. These tests instantiate no
XAML and do not prove receiver interoperability.

Both actual Release MSIX builds, pinned-payload inspection, in-place development
registration/running binary checks and the written manual gate are pending.
The user confirms both receivers available with macOS 26.7.1/tvOS 26.6 unchanged.
Manual checks will cover audio controls, current-position start, cancellation,
Stop/local-paused handoff, natural audio/video/audio queue advance, rapid Next
and Stop during transition. Written user confirmation is used, without Computer
Use visual confirmation. Preserve any failed attempts and exact tested bytes.

No release or native package publication is part of this slice. Audio-only
speakers/HomePod/RAOP, discrete converted surround, Chromecast hardware checks
and user-deferred remote-Stop classification retain their separate deferrals.
