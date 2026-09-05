# v0.1.1 preview

NVIDIA Clip Manager now explicitly scans and watches every game/app folder inside the selected NVIDIA clips folder, including nested folders created after the watcher starts. **Process existing clips** recursively checks the existing library as well.

- Automatic background detection of finished NVIDIA MP4 recordings.
- Clean `.mp4` filenames; `repaired.mp4` for successfully repaired clips.
- Lossless container repair first, conservative SDR H.264/AAC recovery second.
- Full output decoding, duration/track/date checks, collision-safe names, original backups and restore.
- Restart recovery for interrupted replacements.
- Optional iCloud Photos folder handoff, recording timestamps and duplicate protection.
- Per-user installer with a hash-verified FFmpeg download; self-contained .NET app.

Validation: 27 local automated checks pass, now using a new clip created under `Minecraft/Survival` rather than at the root. Installer payload extraction and launching the packaged desktop app passed. The UI was visually inspected using its preview mode. An extensively corrupt real NVIDIA recording was refused rather than silently replaced with incomplete footage.

This is an unsigned early preview. iCloud-to-iPhone synchronization/date ordering and NVIDIA Gallery refresh still need live device testing. Not every damaged recording can be repaired. No originals are deleted; backups should be retained until you have reviewed the results.

Install `NvidiaClipManagerSetup.exe`, choose folders, then Start watching. Existing clips are processed only when requested. iCloud export and Windows startup are opt-in.
