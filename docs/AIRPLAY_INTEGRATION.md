# AirPlay casting: integration design (draft)

Status: **design for approval, nothing implemented.** Written 2026-10-09 against
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
- **Ending** (user Stop, receiver Stop/Home, connection loss, end of media):
  stop and dispose the cast, swap back to the VLC player, seek it to the last
  known cast position and leave it **paused** (user decision 3).
- **Media eligibility:** only local files (`StorageFile` sources) in containers
  the Apple TV plays (`.mp4`, `.m4v`, `.mov`) are offered; others show
  "This file can't be cast with AirPlay" instead of failing on the TV.

### Messages

Shown through the existing `NotificationMessage`, with ReswPlus strings:

| Situation | Message (draft) |
| --- | --- |
| Start fails with a connection error after the receiver was discovered | AirPlay couldn't connect back to this device. If your network is set to Public in Windows settings, change it to Private and try again. |
| Unsupported source or format | This file can't be cast with AirPlay. |
| Authentication failure | Pairing with this Apple TV is no longer valid. Pair again. |
| Cast ended by the TV | Stopped casting. Playback is paused here at the last position. |

No UWP API reports whether the network is Private or Public (to be verified
during implementation), so the first message names the likely cause rather
than asserting it.

### Threading

Discovery, pairing, cast start and status waits block, so they run off the UI
thread; results go through `DispatcherQueue`, as `CastControlViewModel` already
does for renderer events. The library never calls app code on its own session
threads; the media source's read callback runs on library threads and only
reads the file.

### Packaging the library

- A NuGet package from the send-airplay2 repository carrying the C# binding and
  the UWP-built native DLLs for `win-x86`, `win-x64` and `win-arm64`
  (`send_airplay2.dll`, OpenSSL `libcrypto`; Botan is linked in), referenced by
  `Screenbox.Core`. The native DLLs link the app C runtime (VCLibs), which the
  MSIX tooling already declares.
- `NOTICE.md` gains send-airplay2 (Apache-2.0), OpenSSL (Apache-2.0), Botan
  (BSD-2-Clause) and Boost (BSL-1.0).
- The library passed the Windows App Certification Kit except one test that
  flags the .NET Native AOT runtime itself, which Screenbox already ships.

## Implementation phases

Each phase is its own PR in the fork, built with Visual Studio 2026 MSBuild and
checked on the TV where it touches receiver behavior.

1. **Package:** the NuGet package in send-airplay2 (with CI), referenced from
   `Screenbox.Core`; `NOTICE.md`.
2. **Discovery:** AirPlay receivers in the cast flyout next to Chromecast
   devices (listing only).
3. **Pairing:** PasswordVault store, PIN dialog, profile naming.
4. **Casting:** `AirPlayMediaPlayer`, the player swap, start/end handling,
   stop at last position, messages.
5. **Follow-ups:** forget device, continuing the play queue while casting.

Tests: pure logic in `Screenbox.Core.Tests` (profile naming, eligibility,
end-of-cast position handoff and the player swap against a fake cast); manual
TV checks for discovery, pairing, cast, controls, remote Stop/Home and a
sleeping TV.

## Open questions (for the user)

1. **Library delivery:** publish the `SendAirPlay2` package on nuget.org (needed
   if this is ever proposed upstream; Screenbox restores only from nuget.org)
   or use a local package feed in the fork for now?
2. **Play queue while casting:** at the end of a cast item, end the cast and
   return to local (simplest, proposed for the first version), or cast the next
   queue item?
3. **Upstream goal:** is a PR to `huynhsontung/Screenbox` intended? If so, the
   AirPlay code should stay isolated (feature folder, no changes to Chromecast
   behavior) and the capability question stays a separate PR.

## Risks

- Swapping `PlayerContext.MediaPlayer` is the least invasive way to route the
  controls, but every consumer of player events sees a non-VLC player for the
  cast's duration; each consumer is reviewed in phase 4 (the play-queue
  coordinator in particular, which sets `PlaybackItem` on the current player).
- One receiver model and firmware tested so far; other Apple TVs and AirPlay
  TVs from other makers are unverified.
- Casting fails on Public networks by design (decision 2).
