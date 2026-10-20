# Screenbox AirPlay Preview 0.2.0-preview.1 candidate

2026-10-20 (Asia/Tokyo). The user authorized preparing an updated preview after
PR #17 merged as `b2b987d68b844ef0e3f577cc0622d223a329adf9`; all four checks
passed at final head `2a001be1`. Native evidence PR #97 merged as `65dd17f`, with
all 17 checks passed at `6effcf1`.

**Preparation is complete; publication is not approved or performed.**
The five exact assets are retained locally, with proposed
[publication-ready notes](releases/airplay-preview-0.2.0-preview.1.md).
The new tag must be `airplay-preview-0.2.0-preview.1` and target the original
merged source `b2b987d68b844ef0e3f577cc0622d223a329adf9`, not a later docs merge.
Do not regenerate these bytes under this version or replace earlier release assets.

## Build and verification

An isolated export of merged main builds Release/x64 with AirPlayStereoAac=true,
unsigned package version 0.2.0.0 and the existing Screenbox AirPlay Preview
identity/publisher. Only project version/copyright and manifest identity/version/
display metadata differ from the source commit; BUILDING-PREVIEW.md is added.
The source ZIP excludes all generated/downloaded files and retains LICENSE,
NOTICE.md and the empty Sentry DSN. SourceRevisionId is explicitly set and source
control discovery disabled, preventing inheritance of a parent checkout's revision;
the exact informational-version stamp is verified. Binary reproducibility is
not claimed.

All 176 isolated release-source logic tests pass. The actual MSIX, all 40 pinned
ci.330 native/source/notice files, unsigned preview manifest, both x64 framework
identities/versions/signature entries, source-only changes and complete checksums
pass inspection. [Clean-main run 39430175011](https://github.com/ilyalissoboi/Screenbox/actions/runs/39430175011)
passes all four checks, including opt-in validation. Existing translation,
legacy migration and third-party trim warnings remain.

The exact installer ZIP contents pass an elevated in-place update from preview
0.1.0.0 to 0.2.0.0. Preview identity/data are preserved; Store/debug registrations
remain unchanged. Compatible Microsoft frameworks are reused. Windows activation,
AppContainer/package identity, survival after five seconds and the running
executable hash match the inspected candidate. No visual or receiver result is
claimed; the preview is left installed/open.

## Retained publication assets

| Asset | Bytes | SHA-256 |
| --- | ---: | --- |
| Screenbox-AirPlay-Preview-0.2.0-preview.1-x64.zip | 81,425,296 | `63c3f25948520e5555f91121930391e697a230cf8de1dcc2020451c277e646a0` |
| Screenbox-AirPlay-Preview-0.2.0-preview.1-source.zip | 13,990,405 | `aed7985b140a9e47e930906129d7cdc6bbd30e1d1dd9b8a337a2e9c728aee06e` |
| BUILD-INFO.json | 2,092 | `714187a3b2cfc16454a527bcf5c2e202a630112c653385de9cc0398cfd7f2a61` |
| INSTALL.md | 3,135 | `8b65bb28f9e3e5eb4c4bd57b5e965ec667de1bfb351ccd8adff0a91a0de5f53d` |
| SHA256SUMS.txt | 398 | `db30bc2ae3c84ca2415cfad6cbbce9464a7340e11b0e9461f2dac9e909e8f825` |

The opt-in library pin stays SendAirPlay2.StereoAac 0.6.0-ci.330, SHA-256
`9567655c5c4f0183fddeaea9f497d869d28b79036d599cbf5d87f241e74eb3ad`.
No code, dependency, codec, quota or default setting is changed in preparation.
The [D112 gate](AIRPLAY_AUDIO_ONLY_POLICY.md) and later review fix retain their
original local binary/testing scopes. No unchanged listening/cancellation matrix
is repeated. Speakers/RAOP, converted discrete surround, Chromecast hardware,
remote-Stop classification and long-input/cache scaling remain deferred.

After review, obtain explicit approval for this exact candidate before publishing
the GitHub prerelease. Then verify fresh public downloads of all five assets.
This is separate from Store/winget/nuget.org publication and from previous release
approvals. No new tag/release/assets have been created here.
