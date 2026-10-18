# AirPlay AAC/M4A audio files

2026-10-18, recorded opt-in x64 host gate passed. The user authorizes the
Screenbox slice after native documentation PR #82 merges as `afb9ee8`, following the
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

Both actual Release MSIX builds and pinned-payload inspection pass (two ordinary
native files; all 40 opt-in native/source/notice files). Existing translation,
legacy migration and third-party trim warnings remain. Development 1.0.0.13 is
registered in place from the actual opt-in MSIX with source manifest and data
preserved; only the extracted manifest version changes. Full loaded-module
checks pass for the actual AppContainer, inspected executable and all six loaded
published ci.294 DLLs. All four fork CI jobs pass at `9c5202bf`;
release is intentionally skipped. Inspect final documentation-head CI separately.

The user confirms Mac consent cancellation closes its prompt with the app
responsive/local paused and no error. On BOTH Mac and Apple TV, conversion OFF
and ON pass M4A audible current-position start near eight seconds, pause/resume,
seek to about 32/back to eight and clean Stop/local-paused handoff. With
conversion ON/repeat OFF, the owned audio/video/audio queue advances naturally
with correct output and local paused at queue end. Rapid Next twice reaches
the third audio; separate Next/immediate Stop during a transition stops playback
without delayed restart, keeping local paused and the app responsive. The user
explicitly confirms macOS 26.7.1/tvOS 26.6 unchanged. Written user observations
are used, without Computer Use visual confirmation or a timed sender trace.

The user intentionally closes Screenbox after the checks. Post-test registered
files match, and the reopened process independently passes the exact executable,
AppContainer and six loaded hashes. This is not a same-process before/after
capture. Final owned cache is empty; combined audio/video sampling observes at
most one empty child, with no per-action attribution or continuous-coverage claim.
No further D99 receiver check is pending. The queue procedure permits seeking
near clip ends; it does not prove uninterrupted full-file/long-audio playback.
Repeat-one is unchanged, with no new receiver observation in this slice.

The tested MSIX is compiled at `aff57b6f`; later changes are documentation only.
The full [native gate record](https://github.com/ilyalissoboi/send-airplay2/blob/codex/screenbox-aac-routing-checkpoint/docs/screenbox-audio-files.md)
preserves hashes, exact user replies and limits in companion native PR #83.
Ordinary ci.207 has logic/build/payload checks, with no new receiver gate.
Unsupported audio-only conversion remains a separate follow-up; no input codec
coverage is inferred from the M4A extension. No playback failure is reported.

No release or native package publication is part of this slice. Audio-only
speakers/HomePod/RAOP, discrete converted surround, Chromecast hardware checks
and user-deferred remote-Stop classification retain their separate deferrals.
