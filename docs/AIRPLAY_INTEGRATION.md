# AirPlay casting: integration design (draft)

Status: **design for approval, nothing implemented.** Written 2026-10-09
against `main` at `6870eb0f`; revised 2026-10-10 for the library's HLS remux,
the delivery decision and two review findings. It plans adding AirPlay video
casting to Screenbox through the
[send-airplay2](https://github.com/ilyalissoboi/send-airplay2) library,
alongside the existing Chromecast casting. User decisions, engineering
proposals and open questions are kept apart below.

## What the library provides

send-airplay2 (Apache-2.0) is a native AirPlay 2 video sender with a C interface
(API version 3) and a C# binding (`SendAirPlay2`, netstandard2.0, Native
AOT-compatible). It has been tested on one Apple TV 4K (tvOS 26.6) from a
packaged UWP test app built like Screenbox (modern .NET UWP, Native AOT, MSIX)
on x64 and x86:

| Capability | C# API |
| --- | --- |
| Discover receivers (one bounded mDNS scan) | `Receivers.Discover(TimeSpan)` |
| Pair by PIN into an app-supplied credential store | `Pairing.Pair(PairOptions, PinReader)` |
| Cast a file through a read callback | `Cast.Create(CastOptions, MediaSource)`, `Start` |
| How the file reaches the TV | `CastOptions.Delivery`: `HlsRemux` or `Progressive` |
| Start mid-file | `CastOptions.StartPositionSeconds` |
| Status and controls | `GetStatus`, `WaitForChange`, `Play`, `Pause`, `Seek`, `Stop` |
| Why a cast ended | `CastStatus.EndReason`: `MediaEnd`, `SenderStop`, `ConnectionLost`, ... |
| Remove local credentials | `Pairing.ForgetProfile` |

The library serves the media itself over HTTP to the Apple TV, so the receiver
connects back to the app, and it wakes a sleeping receiver before playing.
**It does not transcode** and does not cast network streams.

- **HLS remux** (`HlsRemux`): the library builds HLS from an MP4/MOV or MKV
  file as the TV requests it, without transcoding:
  - video: H.264 or HEVC
  - audio: AAC, AC-3 or E-AC-3. It takes the default-flagged remuxable audio
    track, otherwise the first.
  - text subtitles: SubRip, WebVTT and ASS in MKV, tx3g and WebVTT in MP4
  A file it cannot remux is refused by `Start` with `MediaUnsupported` or
  `MediaMalformed` before anything is sent to the TV. Starting the remux reads
  only the file's index (an MKV's Cues, an MP4's `moov`); an MKV without Cues is
  scanned first.
- **Progressive** (`Progressive`): the file as is, for containers the TV plays
  directly (MP4/MOV). No codec check.
- **Start position:** tvOS 26 ignores the start position in the play request, so
  the library seeks there once the TV plays, before `Start` returns. The first
  second or so may show the beginning of the file.

Tested on that receiver through the UWP test app (2026-10-10): a 6.3 GB MKV and
an MP4 cast through the remux from brokered `StorageFile` access, starting in
about 2.5 s, with pause, play, seeks, subtitles, start positions and the natural
end.

## User decisions

2026-10-09:

1. **Fork:** the work happens in `ilyalissoboi/Screenbox`, branch from `main`.
2. **Capabilities unchanged:** keep `internetClient` and
   `privateNetworkClientServer`, as for Chromecast. On a network Windows marks
   Public, the receiver cannot connect back, so casting fails; Screenbox then
   shows a message suggesting switching the network to Private.
   `internetClientServer` may be proposed later as its own PR.
3. **Cast ended from the Apple TV remote** (Stop or Home): Screenbox **stops at
   the last known position**; it does not resume local playback.
4. **Library delivery:** a **local package feed** for now; publishing on
   nuget.org is deferred until end-to-end casting is confirmed locally.
   Refined 2026-10-10 (decision 8).
5. **Play queue while casting:** by default, when a cast item reaches its end,
   Screenbox **casts the next item in the queue**.
6. **Upstream:** the eventual goal is a PR to `huynhsontung/Screenbox`, so the
   AirPlay code stays isolated and Chromecast behavior is unchanged.

2026-10-10:

7. **Delivery: HLS remux for every file**, MP4/MOV included, rather than
   playing MP4 directly. Whether a file can be cast is decided by the library's
   refusal, not by a list in Screenbox. Chosen over "remux MKV, play MP4
   directly", which has more receiver testing behind it, for one code path,
   subtitles from MP4 files, and AC-3/E-AC-3 in MP4.
8. **Package source for CI:** a GitHub runner has no local feed, so the
   package is published as a **GitHub prerelease** asset on send-airplay2
   (each release approved by the user) and downloaded into the fork's feed
   folder before restore, rather than committing the package or publishing on
   nuget.org early.

## Proposed design (engineering proposals)

### Architecture overview

```
CastControl (flyout)  ──>  CastControlViewModel  ──>  ICastService
        renderer list: Chromecast (LibVLC) + AirPlay (send-airplay2)
                                                  │
                     Chromecast: VlcPlayer.SetRenderer (unchanged)
                     AirPlay:    AirPlayCastCoordinator
                                   ├─ pairing (PIN dialog, PasswordVault store)
                                   ├─ Cast session (SendAirPlay2.Cast, HlsRemux)
                                   └─ AirPlayMediaPlayer : IMediaPlayer
                                        swapped into PlayerContext while casting
```

### Discovery and the renderer list

- `Renderer` today wraps a LibVLC `RendererItem`. Give it a kind
  (`Chromecast` / `AirPlay`) and, for AirPlay, the discovered receiver's
  identity, display name, address and port. `Target` stays LibVLC-only.
- An `AirPlayReceiverWatcher` runs `Receivers.Discover(5 s)` on a background
  thread while the cast flyout is open (the same lifetime as `RendererWatcher`'s
  `StartDiscovering`/`StopDiscovering`), repeating every few seconds, and raises
  found/lost events from scan differences, keyed by the receiver id.
  `CastControlViewModel` merges both watchers into one `Renderers` list.
- A receiver is removed only after it has been missing from two consecutive
  scans: in testing, one scan right after a cast ended missed the Apple TV.
- Only receivers with a castable address are listed. Names are untrusted
  network text: display-escaped, never logged (Screenbox already logs only a
  name hash).

### Pairing and credentials

- A C# `PasswordVaultCredentialStore : ICredentialStore` (as in the library's
  test host): one `PasswordCredential` per profile under the resource
  `Screenbox.AirPlay`, the record Base64-encoded. PasswordVault holds at most 20
  credentials per app and may roam with the user's account; both are acceptable
  for a handful of TVs.
- The profile name is derived from the receiver id (lower-case, `airplay-` plus
  the id's hex digits), so each Apple TV has its own pairing. Discovery identity
  is unauthenticated, but the stored credentials pin the receiver's key: a
  different device answering at that address fails authentication.
- First cast to an unpaired receiver (`ProfileNotFound`) starts pairing: the TV
  shows a PIN, a `ContentDialog` with a `PasswordBox` collects it, then the cast
  continues. The PIN is cleared after use and never logged.
- "Forget this AirPlay device" removes local credentials (a follow-up; the TV's
  own pairing list is unaffected).

### Casting and transport control

Chromecast casting keeps VLC as the player: VLC itself streams to the
Chromecast, so pause, seek and position keep working through `IMediaPlayer`.
AirPlay is a separate pipeline (the TV fetches the media; controls go over MRP),
so local playback must step aside and the transport controls must follow the
cast. Proposal:

- **`AirPlayMediaPlayer : IMediaPlayer`** wraps the current `Cast`. While
  casting, `PlayerContext.MediaPlayer` is swapped from the VLC player to it, and
  back afterwards. `SeekBarViewModel`, `PlayerControlsViewModel`,
  `VolumeViewModel`, `PlaybackSessionViewModel` and `PlayQueueCoordinator`
  already re-subscribe when `PlayerContext.MediaPlayer` changes
  (`PropertyChangedMessage<IMediaPlayer?>`), so the seek bar, play/pause button
  and queue follow the swap without AirPlay code.
- **System media transport controls need a change.** `PlayerElementViewModel`
  handles the system play/pause/stop/seek buttons through its `VlcMediaPlayer`
  property (`PlayerContext.MediaPlayer as VlcMediaPlayer`, null while casting),
  and subscribes its position/state handlers, which update the system controls,
  only to the VLC player it created. Proposed:
  - route those button handlers through `PlayerContext.MediaPlayer` (any
    `IMediaPlayer`);
  - move the position/state subscriptions to a re-subscription on
    `PropertyChangedMessage<IMediaPlayer?>`, as the other view models do.
  The pointer-wheel and gesture seeks in the same view model also use
  `VlcMediaPlayer`; they stay inert while casting in the first version. Next
  and Previous on the system controls go through `PlayQueueCoordinator`, which
  already uses the current player.
- Members map onto the cast:
  - `Play`/`Pause` → MRP play/pause; `Position` set → seek.
  - `Position`, `PlaybackState` and `NaturalDuration` come from `WaitForChange`
    on a background thread, marshalled to the UI thread.
  - `MediaEnded` is raised when the cast ends with `EndReason.MediaEnd`.
  Members the cast cannot honour are inert:
  - volume and mute: the TV's volume is not controlled;
  - rate, fixed at 1;
  - track, chapter and subtitle changes: the existing `is VlcMediaPlayer`
    checks already disable those pickers;
  - frame stepping.
- **Starting:**
  1. Remember the VLC position and pause VLC (the item stays loaded).
  2. Open the `StorageFile` read-only as the media source.
  3. `Cast.Create` with `Delivery = HlsRemux` and `StartPositionSeconds` = the
     remembered position, then `Start`, on a background thread. It blocks: it
     may wait up to 10 s more if the TV is waking, and an MKV without Cues is
     scanned first.
  4. Swap the player only after `Start` succeeds. On failure keep VLC and show
     the reason.
- **Ending** (user Stop, receiver Stop/Home, connection loss): stop and
  dispose the cast, swap back to the VLC player, seek it to the last known cast
  position and leave it **paused** (user decision 3).

### End of an item and the play queue

The library ends a session at the end of each item (`MediaEnd`, distinct from
a remote Stop/Home, which ends as `ConnectionLost`); an ended `Cast` cannot be
reused. `PlayQueueCoordinator.OnEndReached` reacts to `MediaEnded` by
enqueueing its decision on the dispatcher queue. That decision is one of three:
- `PlaySingle(next)`, which sets `MediaPlayer.PlaybackItem` and calls `Play()`;
- for repeat-one, `Position = 0` on the same player;
- nothing, at the end of the queue with repeat off.

So the adapter needs explicit rules:

- After a `MediaEnd`, the adapter stays the active player, raises
  `MediaEnded`, and then enqueues its own check on the same dispatcher queue at
  the same priority. That check runs after the coordinator's decision.
- **`PlaybackItem` set** (the next item; also Next/Previous while casting):
  dispose the old cast and start a new one on the same receiver at position 0
  (user decision 5). `Play()` afterwards is a no-op while starting. Each item is
  a new session, so there is a short start gap (about 2.5 s, more if the TV
  dozed off), not gapless playback.
- **`Position` set on an ended cast** (repeat-one): start a new cast of the same
  item at that position.
- **No decision by the time the check runs** (end of the queue): end casting,
  swap back to VLC, paused at the end of the last item.
- If the next item cannot be cast (not a local file, or `Start` refuses it with
  `MediaUnsupported`/`MediaMalformed`), casting ends and Screenbox switches back
  to VLC with that item loaded and paused at its start, with a message.

Tests in `Screenbox.Core.Tests` drive the coordinator against a fake cast for
each case: next item, repeat-one, end of queue, refused item, and Next/Previous
while casting.

### Media eligibility

- Only local files (`StorageFile` sources) can be cast; network streams cannot.
- No codec list in Screenbox (user decision 7). The flyout offers AirPlay for
  any local video, and `Start` refuses what the remux cannot serve before the
  TV is involved. Screenbox maps:
  - `MediaUnsupported` (another codec, such as VP9, AV1, MPEG-2 or DTS, or
    another container) and `MediaMalformed` to messages;
  - the playback state stays local.

### Subtitles and audio tracks while casting

- Text subtitles inside the file are sent to the TV as selectable tracks. The
  track the file marks as default (Matroska default flag, or an enabled MP4
  text track) is shown without opening a menu. Other tracks are chosen in the
  Apple TV's own subtitle menu.
- Not carried over in the first version:
  - subtitle files Screenbox loads next to the video (`.srt`);
  - image subtitles (PGS, VobSub);
  - the subtitle or audio track picked in Screenbox: the remux takes the
    default audio track. Choosing tracks from Screenbox needs sender-side
    selection, which the library does not offer yet.
- Screenbox's track pickers stay disabled while casting (the existing
  `is VlcMediaPlayer` checks).

### What Screenbox shows while casting

- **Follows the receiver:**
  - the seek bar's position and duration;
  - the play/pause state;
  - the system media transport controls, after the change above.
  They come from the cast's status while `AirPlayMediaPlayer` is the active
  player, and seeking or pausing in Screenbox moves the TV.
- **Does not follow:** the video area. Local VLC is paused, so the video
  element keeps the frame from when the cast started. Playing it locally in
  sync would decode the video twice and drift; not proposed.
- **Proposed:** a "Casting to ‹receiver›" overlay over the video area while an
  AirPlay cast is active, so the still frame does not look like a hang. The
  cast flyout already shows "Casting to" with the device name. Chromecast's
  presentation is not changed.

### Messages

Shown through the existing `NotificationMessage`, with ReswPlus strings:

| Situation | Message (draft) |
| --- | --- |
| Start fails with a connection error after the receiver was discovered | AirPlay couldn't connect back to this device. If your network is set to Public in Windows settings, change it to Private and try again. |
| Not a local file | Only files on this device can be cast with AirPlay. |
| `MediaUnsupported` | This video's format can't be cast with AirPlay. |
| `MediaMalformed` | This file couldn't be read for AirPlay. It may be damaged. |
| Authentication failure | Pairing with this Apple TV is no longer valid. Pair again. |
| Cast ended by the TV | Stopped casting. Playback is paused here at the last position. |
| Next queue item can't be cast | Stopped casting: the next item can't be cast with AirPlay. |

No UWP API reports whether the network is Private or Public (to be verified
during implementation), so the first message names the likely cause rather
than asserting it.

### Isolation for an upstream PR

User decision 6 makes upstream the goal, so the AirPlay code lives in its own
feature folders (`Screenbox.Core/Casting/AirPlay`, `Screenbox/Controls` for
the dialog and overlay), the shared pieces (`Renderer`, `ICastService`,
`CastControlViewModel`) gain the AirPlay kind without changing Chromecast's
code paths, and every new string is localized through ReswPlus. The
`PlayerElementViewModel` change routes the system controls through
`IMediaPlayer` for every player, which keeps VLC and Chromecast behavior the
same. The capability question stays a separate PR.

### Threading

Discovery, pairing, cast start and status waits block, so they run off the UI
thread; results go through `DispatcherQueue`, as `CastControlViewModel` already
does for renderer events. The library never calls app code on its own session
threads; the media source's read callback runs on library threads and only
reads the file.

### Packaging the library

- A NuGet package built by the send-airplay2 repository, carrying the C#
  binding and the UWP-built native DLLs for `win-x86`, `win-x64` and
  `win-arm64` (`send_airplay2.dll`, OpenSSL `libcrypto`; Botan is linked in),
  referenced by `Screenbox.Core`. The native DLLs link the app C runtime
  (VCLibs), which the MSIX tooling already declares. The remux adds no
  dependency.
- **For now a local feed** (user decisions 4 and 8):
  - send-airplay2's `scripts/pack_nuget.ps1` packs the package; its CI packs
    all three architectures, and that package is published as a GitHub
    prerelease tagged `nuget-v<version>`.
  - The fork's `nuget.config` adds the in-repo folder `packages-airplay` as a
    source next to nuget.org; package files there are not committed.
  - `scripts/Get-AirPlayPackage.ps1` places the version `Screenbox.Core.csproj`
    references there before a restore: it keeps a local pack already in the
    folder, or downloads the prerelease asset and accepts it only with the
    SHA-256 recorded in the script. The test and Copilot setup workflows run
    it first.
  - Before an upstream PR the package moves to nuget.org and the folder,
    source and script are removed, since upstream restores only from
    nuget.org.
- `NOTICE.md` gains send-airplay2 (Apache-2.0), OpenSSL (Apache-2.0), Botan
  (BSD-2-Clause) and Boost (BSL-1.0).
- The Windows App Certification Kit passed the library's test app except one
  test, Supported APIs, which flags the .NET Native AOT runtime in the app's
  executable, which Screenbox already ships; repeated 2026-10-10 with the remux
  in the library, same result.

## Implementation phases

Each phase is its own PR in the fork, built with Visual Studio 2026 MSBuild and
checked on the TV where it touches receiver behavior.

1. **Package:** the pack script in send-airplay2, the local feed in the fork's
   `nuget.config`, the package referenced from `Screenbox.Core`; `NOTICE.md`.
2. **Discovery:** AirPlay receivers in the cast flyout next to Chromecast
   devices (listing only).
3. **Pairing:** PasswordVault store, PIN dialog, profile naming.
4. **Casting:**
   - `AirPlayMediaPlayer` and the player swap;
   - the system media transport controls change;
   - start and end handling, stopping at the last position;
   - the end-of-item rules and casting the next queue item;
   - the casting overlay and messages.
5. **Follow-ups:** forget device; nuget.org publishing and removing the local
   feed before the upstream PR.

Tests:
- **Pure logic in `Screenbox.Core.Tests`:** profile naming, end-of-cast position
  handoff, the player swap and the end-of-item rules against a fake cast.
- **Manual TV checks:** discovery, pairing, an MP4 and an MKV cast, controls from
  the app and the system controls, remote Stop/Home, the next queue item,
  repeat-one, the end of the queue, a refused file and a sleeping TV.

The library's test app can run scripted TV checks of the library side, so
Screenbox's manual checks can focus on its own behavior.

## Open questions

None blocking. Possible later work, not planned:

- **Casting formats the remux cannot serve** (VP9, AV1, MPEG-2, DTS audio;
  network streams): VLC as a transcoder to HLS that the library serves. It
  needs library support for growing presentations, costs CPU, and seeking
  restarts the transcode. LibVLC itself has no AirPlay renderer.
- **Choosing subtitle and audio tracks from Screenbox**, once the library
  supports selecting them on the receiver.
- **HDR:** the remux does not yet declare HDR in its playlists, so HDR10 files
  would be announced as SDR; not tested. Dolby Vision is refused.

## Risks

- Swapping `PlayerContext.MediaPlayer` is the least invasive way to route the
  controls, but every consumer of player events sees a non-VLC player for the
  cast's duration; each consumer is reviewed in phase 4. The ones found so far
  are listed above (`PlayerElementViewModel`'s system controls and gestures,
  and `PlayQueueCoordinator`'s end handling).
- The remux path has fewer receiver runs than direct MP4 playback (accepted in
  user decision 7). The library refuses what it cannot remux, but the
  receiver's handling of unusual files is unverified.
- An MKV without Cues is scanned before the cast starts. That takes seconds for
  a large file on local disk, more through brokered access; not measured in a
  packaged app.
- One receiver model and firmware tested so far; other Apple TVs and AirPlay
  TVs from other makers are unverified.
- Casting fails on Public networks by design (decision 2).
