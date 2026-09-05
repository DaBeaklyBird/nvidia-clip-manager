# Third-party notices

NVIDIA Clip Manager source is under the [MIT license](LICENSE).

## FFmpeg

The installer obtains **FFmpeg 8.1.1 Essentials** directly from [GyanD/codexffmpeg](https://github.com/GyanD/codexffmpeg/releases/tag/8.1.1), not from this repository. It is invoked as an independent command-line program; FFmpeg libraries are not linked into this app. Gyan Essentials is a GPL-enabled build; consult the downloaded archive's LICENSE and documentation and [FFmpeg licensing](https://ffmpeg.org/legal.html). Upstream source: [FFmpeg](https://git.ffmpeg.org/ffmpeg.git); build details/source links: [Gyan Windows builds](https://www.gyan.dev/ffmpeg/builds/). Applicable components such as libx264 retain their own licenses.

Pinned download:

`https://github.com/GyanD/codexffmpeg/releases/download/8.1.1/ffmpeg-8.1.1-essentials_build.zip`

SHA-256: `6f58ce889f59c311410f7d2b18895b33c03456463486f3b1ebc93d97a0f54541`

The installer retains the downloaded engine archive and extracted documentation in the app's local data directory. It requires the user to review software licenses before installation. No NVIDIA or Apple artwork is used.

## .NET

The self-contained Windows app and installer include Microsoft's .NET 8 runtime. The app distribution retains runtime license and third-party notice files. See [dotnet/runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) and [third-party notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT).
