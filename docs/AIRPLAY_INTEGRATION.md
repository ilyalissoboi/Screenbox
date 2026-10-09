# AirPlay casting: integration design (draft)

Status: **design for approval, nothing implemented; open questions resolved
2026-10-09.** Written 2026-10-09 against
`main` at `6870eb0f`. It plans adding AirPlay video casting to Screenbox through
the [send-airplay2](https://github.com/ilyalissoboi/send-airplay2) library,
alongside the existing Chromecast casting. User decisions, engineering
proposals and open questions are kept apart below.

## What the library provides

send-airplay2 (Apache-2.0) is a native AirPlay 2 video sender with a C interface
and a C# binding (`SendAirPlay2`, netstandard2.0, Native AOT-compatible). It has
been tested on one Apple TV 4K (tvOS 26.6) from a packaged UWP test app built
like Screenbox (modern .NET UWP, Native AOT, MSIX) on x64 and x86:

| Capability | C# API |
| --- | --- |
| Discover receivers (one bounded mDNS scan) | `Receivers.Discover(TimeSpan)` |
| Pair by PIN into an app-supplied credential store | `Pairing.Pair(PairOptions, PinReader)` |
| Cast a file through a read callback; status, play, pause, seek, stop | `Cast.Create`, `Start`, `GetStatus`, `WaitForChange`, `Play`, `Pause`, `Seek`, `Stop` |
| Remove local credentials | `Pairing.ForgetProfile` |

The library serves the file itself over HTTP to the Apple TV, so the receiver
connects back to the app, and it wakes a sleeping receiver before playing. It
plays what the Apple TV supports natively (MP4/MOV with H.264, HEVC, AAC,
E-AC-3 tested); **it does not transcode** and does not cast network streams.

## User decisions (2026-10-09)

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
5. **Play queue while casting:** by default, when a cast item reaches its end,
   Screenbox **casts the next item in the queue**.
6. **Upstream:** the eventual goal is a PR to `huynhsontung/Screenbox`, so the
   AirPlay code stays isolated and Chromecast behavior is unchanged.

## Proposed design (engineering proposals)

### Architecture overview

```
CastControl (flyout)  ──>  CastControlViewModel  ──>  ICastService
        renderer list: Chromecast (LibVLC) + AirPlay (send-airplay2)
                                                  │
                     Chromecast: VlcPlayer.SetRenderer (unchanged)
                     AirPlay:    AirPlayCastCoordinator
                                   ├─ pairing (PIN dialog, PasswordVault store)
                                   ├─ Cast session (SendAirPlay2.Cast)
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
AirPlay is a separate pipeline (the TV fetches the file; controls go over MRP),
so local playback must step aside and the transport controls must follow the
cast. Proposal:

- **`AirPlayMediaPlayer : IMediaPlayer`** wraps one `Cast`. While casting,
  `PlayerContext.MediaPlayer` is swapped from the VLC player to it, and back
  afterwards. The seek bar, play/pause button, system media transport controls
  and volume view model already re-subscribe when `PlayerContext.MediaPlayer`
  changes (`PropertyChangedMessage<IMediaPlayer?>`), so they work without
  per-view-model AirPlay code.
- Members map onto the cast: `Play`/`Pause` → MRP play/pause, `Position` set →
  seek, `Position`/`PlaybackState`/`NaturalDuration` from `WaitForChange` on a
  background thread, marshalled to the UI thread, `MediaEnded` at end of media.
  Members the cast cannot honour are inert: volume and mute (the TV's volume is
  not controlled), rate fixed at 1, no track/chapter/subtitle changes (the
  existing `is VlcMediaPlayer` checks already disable those pickers), frame
  stepping ignored.
- **Starting:** remember the VLC position, pause VLC (the item stays loaded),
  open the `StorageFile` read-only as the media source, `Cast.Create` +
  `Start` on a background thread (blocking; it may wait up to 10 s more if the
  TV is waking), starting at the remembered position. Swap the player only after
  `Start` succeeds; on failure keep VLC and show the reason.
- **Ending** (user Stop, receiver Stop/Home, connection loss): stop and
  dispose the cast, swap back to the VLC player, seek it to the last known cast
  position and leave it **paused** (user decision 3).
- **End of an item** (the library reports `media_end`, distinct from a remote
  Stop/Home, which ends as `connection_lost`): the adapter raises `MediaEnded`,
  the play-queue coordinator advances as usual and sets the next
  `PlaybackItem` on the current player, the adapter, which starts a new cast of
  that item on the same receiver (user decision 5). Next/Previous while casting
  take the same path. Each item is a new session, so there is a short start gap
  (about 2 s, more if the TV dozed off), not gapless playback. If the next item
  can't be cast (not a local file or not a supported format), the cast ends and
  Screenbox switches back to VLC with that item loaded and paused at its start,
  with a message. The end of the queue (with repeat off) ends the cast the same
  way, paused at the end of the last item.
- **Media eligibility:** only local files (`StorageFile` sources) in containers
  the Apple TV plays (`.mp4`, `.m4v`, `.mov`) are offered; others show
  "This file can't be cast with AirPlay" instead of failing on the TV.

### What Screenbox shows while casting

- **Follows the receiver:** the seek bar's position and duration, the
  play/pause state, and the system media transport controls. They come from the
  cast's status while `AirPlayMediaPlayer` is the active player, and seeking or
  pausing in Screenbox moves the TV.
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
| Unsupported source or format | This file can't be cast with AirPlay. |
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
code paths, and every new string is localized through ReswPlus. The capability
question stays a separate PR.

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
  (VCLibs), which the MSIX tooling already declares.
- **For now a local feed** (user decision 4): a script in send-airplay2 builds
  the three UWP architectures and packs the package into a folder, and the
  fork's `nuget.config` adds that folder as a source next to nuget.org (proposed:
  the sibling checkout's `..\send-airplay2\packages-local`, so it works with
  both repositories side by side). Before an upstream PR the package moves to
  nuget.org and that source is removed, since upstream restores only from
  nuget.org.
- `NOTICE.md` gains send-airplay2 (Apache-2.0), OpenSSL (Apache-2.0), Botan
  (BSD-2-Clause) and Boost (BSL-1.0).
- The library passed the Windows App Certification Kit except one test that
  flags the .NET Native AOT runtime itself, which Screenbox already ships.

## Implementation phases

Each phase is its own PR in the fork, built with Visual Studio 2026 MSBuild and
checked on the TV where it touches receiver behavior.

1. **Package:** the pack script in send-airplay2, the local feed in the fork's
   `nuget.config`, the package referenced from `Screenbox.Core`; `NOTICE.md`.
2. **Discovery:** AirPlay receivers in the cast flyout next to Chromecast
   devices (listing only).
3. **Pairing:** PasswordVault store, PIN dialog, profile naming.
4. **Casting:** `AirPlayMediaPlayer`, the player swap, start/end handling,
   stop at last position, casting the next queue item, the casting overlay,
   messages.
5. **Follow-ups:** forget device; nuget.org publishing and removing the local
   feed before the upstream PR.

Tests: pure logic in `Screenbox.Core.Tests` (profile naming, eligibility,
end-of-cast position handoff and the player swap against a fake cast); manual
TV checks for discovery, pairing, cast, controls, remote Stop/Home and a
sleeping TV.

## Open questions

None blocking. Resolved on 2026-10-09 as user decisions 4-6. A possible later
phase, not planned: casting formats the Apple TV cannot play (MKV, network
streams) by using VLC as a transcoder to HLS that the library serves. That
needs new library support for live, unsized streams, costs CPU, and seeking
restarts the transcode; LibVLC itself has no AirPlay renderer.

## Risks

- Swapping `PlayerContext.MediaPlayer` is the least invasive way to route the
  controls, but every consumer of player events sees a non-VLC player for the
  cast's duration; each consumer is reviewed in phase 4. The play-queue
  coordinator sets `PlaybackItem` on the current player; the design relies on
  that to cast the next item, so its interaction with the adapter needs tests.
- One receiver model and firmware tested so far; other Apple TVs and AirPlay
  TVs from other makers are unverified.
- Casting fails on Public networks by design (decision 2).
