# Screenbox AirPlay Preview 0.2.0-preview.1 (Windows x64)

Experimental Windows 11 x64 Release build of this Screenbox fork.

- Audio-only casting with original validated stereo AAC/M4A and CBR/VBR MP3.
- Optional stereo-AAC conversion for the covered FLAC/PCM/Ogg Opus/Vorbis subset.
- Independent audio/video policy and display metadata for every mixed-queue item.
- A fix for casting while VLC is still opening an item's track metadata.
- Existing video/HDR support and Settings > Casting, with conversion off by default
  on a fresh installation and existing preferences preserved on upgrade.

Download `Screenbox-AirPlay-Preview-0.2.0-preview.1-x64.zip`, verify it against
`SHA256SUMS.txt`, extract it and follow `INSTALL.md`. The ZIP includes the unsigned
MSIX, both signed x64 Microsoft framework dependencies, the integrity-checking
installer, build information and notices. Installation requires 64-bit Windows
PowerShell as administrator. Compatible installed frameworks are reused.

Package version `0.2.0.0` updates the existing **Screenbox AirPlay Preview**
identity and preserves its app data. Store and debug apps retain separate
identities/data. The source ZIP includes exact packaging metadata and rebuild
instructions. No automatic updater, Store upload, signing certificate or Sentry
DSN is configured.

Built from merged commit `b2b987d68b844ef0e3f577cc0622d223a329adf9`, using public
`SendAirPlay2.StereoAac 0.6.0-ci.330`. All 176 isolated release-source logic tests,
the actual Release MSIX build, all 40 pinned native/source/notice files, framework
metadata and complete installer/source checksum coverage pass. The exact installer
ZIP contents pass an in-place upgrade from 0.1.0.0; activation, AppContainer
identity and running executable hash verify on the recorded Windows 11 x64 host.
This is an installation/process smoke check, without a new visual or receiver claim.
All four checks also pass in
[clean-main workflow 39430175011](https://github.com/ilyalissoboi/Screenbox/actions/runs/39430175011)
at that exact merged commit, including the opt-in package build and tests.

Installer ZIP: 81,425,296 bytes, SHA-256
`63c3f25948520e5555f91121930391e697a230cf8de1dcc2020451c277e646a0`.
Source ZIP: 13,990,405 bytes, SHA-256
`aed7985b140a9e47e930906129d7cdc6bbd30e1d1dd9b8a337a2e9c728aee06e`.

Prior bounded Mac/macOS 26.7.1 and Apple TV/tvOS 26.6 playback/control/mixed-queue
observations retain their original development-runtime hashes. The later
opening-metadata fix has separate regression/build evidence; these results do
not establish a new receiver run for this preview identity. Conversion produces
stereo and retains the 512-MiB cache budget. The finite input subset does not
promise every codec/container, unrestricted whole-film scaling, gapless playback,
audio-only speaker/RAOP transport, preserved converted surround or Chromecast video.

Screenbox retains GPL-3.0 and existing notices. The native library/binding retain
Apache-2.0, and the separate dynamic FFmpeg runtime retains LGPL-2.1-or-later.
The installed ThirdParty/SendAirPlay2 payload includes complete FFmpeg source,
overlay, configuration, SPDX and rebuild material; retain it with redistribution.
