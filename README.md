# NVIDIA Clip Manager

A Windows tray app that watches NVIDIA recordings, cleans up `.DVR.mp4` names, repairs recoverable damage, and optionally queues finished MP4s for iCloud Photos.

Independent community project. Not affiliated with NVIDIA or Apple.

## Install

Download **NvidiaClipManagerSetup.exe** from [Releases](https://github.com/DaBeaklyBird/nvidia-clip-manager/releases). Windows 10/11 x64. No separate .NET installation needed.

The per-user installer downloads the pinned FFmpeg 8.1.1 Essentials engine from its publisher on GitHub and validates its SHA-256 checksum. Internet access is needed for installation. This early release is unsigned.

Choose your clips and backup folders, then **Start watching**. Select **Start with Windows** if desired. Closing the window leaves the app in the tray; right-click the tray icon to pause or quit. **Process existing clips** also checks the old library. Work runs one clip at a time with low-priority, limited-thread FFmpeg processes.

## What happens to clips?

| Input | Result |
|---|---|
| Healthy `Desktop … .DVR.mp4` | `Desktop … .mp4`, no video re-encoding |
| Repairable `Desktop … .DVR.mp4` | `Desktop … repaired.mp4` |
| Healthy regular `.mp4` | Original left as-is |
| Missing index/data or failed verification | Original retained, **Needs attention** |

`.DVR.mp4` is already an MP4; removing `DVR` cleans its filename. New files are noticed every five seconds and must settle for at least ten seconds and become available for read-only access before processing.

The app first tries lossless remuxing. If needed, it re-encodes H.264 video and AAC audio. It checks full decoding, duration, dimensions, audio track count, channels, sample rate, and timestamp preservation before publishing a replacement. Original bytes are kept in a unique backup location outside the watched folder. Naming collisions never overwrite another clip. **Restore original** copies the backup back while keeping the repaired version and backup.

A durable job journal restores interrupted replacements after restart. Failed clips are not retried endlessly; use **Retry selected**. Symbolic links/junctions in clips and backups are excluded. Keep enough space for originals, verified replacements and optional iCloud copies.

Renamed clips remain in the recording folder. NVIDIA Gallery may need a refresh; Gallery refresh behavior has not been tested against every NVIDIA App version.

## Optional iCloud Photos

Install and sign into **iCloud for Windows**, enable **Photos**, and choose its actual Photos folder in Clip Manager. Enable the upload option to copy completed clips—including healthy normalized clips—there. The app never asks for your Apple password.

This is an upload handoff to Apple's Windows client. **Queued to iCloud** means the file was placed there, not that an iPhone download is confirmed. Apple handles account storage, network availability and synchronization. See [Apple's upload instructions](https://support.apple.com/guide/icloud-windows/icw2c68705cc/icloud).

Recording `creation_time` is preserved in MP4 metadata. If missing, an NVIDIA filename timestamp is interpreted in the PC's configured recording timezone. Ambiguous daylight-saving times or unknown dates pause cloud export instead of inventing a date. Historical recordings made in another timezone require adjusting `RecordingTimeZone` in settings before processing. Hash-based export names prevent repeated handoffs. Retry failures from history; while watching, pending exports are retried every five minutes.

The local export path and embedded timestamps are tested. Real iCloud synchronization and iPhone Photos ordering need a signed-in device test; they are not claimed verified by this release.

## Limits

Not every corrupt clip is repairable. Missing frame data cannot be recreated. A clean decode is not proof every original visual detail survived; re-encoding may conceal damaged frames. The app rejects detected shortening and lost tracks. Extensive damage stays untouched for specialist or partial recovery. HDR/multichannel material requires care; the conservative re-encode path is H.264 8-bit/AAC and is restricted to SDR mono/stereo recordings. Lossless processing can retain other formats.

## Build and test

Requires Windows and the .NET 8 SDK. No NuGet dependencies beyond .NET desktop runtime components.

```powershell
./build.ps1
dotnet run --project tests/Tests.csproj -- 'C:\path\to\ffmpeg\bin' 'C:\path\to\new-test-folder'
```

An optional third test argument is a real NVIDIA recording; the test only copies it into the test directory. Never point the test directory at your library. Tests generate their own media, verify recoverable packet damage, refuse incomplete media, exercise backups/restores, timestamps, filename collisions, cancellation, watcher behavior and a simulated iCloud folder. No recordings, account data or local configuration are committed to GitHub.

App settings, history and downloaded engine live in `%LOCALAPPDATA%\NvidiaClipManager`. Program files live in `%LOCALAPPDATA%\Programs\NvidiaClipManager`. Uninstall from Windows Installed Apps; clips, backups, history and engine cache remain intact.

## License

App: MIT. FFmpeg is a separate executable downloaded from its publisher, governed by its own license. See [third-party notices](THIRD-PARTY.md).
