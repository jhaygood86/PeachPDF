---
title: Media (video and audio)
layout: default
---

# Media: playing video and audio

PeachBrackets decodes no audio or video itself. A host that wants a `<video>` to show its pictures (and, on a screen, to play) gives the
document an `IMediaProvider` in `HtmlDocumentOptions.MediaProvider`, usually over the platform's own player. Without one, a `<video>` shows its
`poster`, as it always has.

## What a document does with a provider

| Document | What a `<video src>` (or its first playable `<source>`) shows |
| --- | --- |
| Paged (PDF, print) | The picture at the start of the media, or at the address's media fragment (`clip.mp4#t=12.5`, `#t=1:20`, `#t=npt:90`). The PDF holds it as an image. |
| Screen, `AnimationsEnabled` | The same first picture, then, while the session plays (the `autoplay` attribute starts it), the current one. Each new picture repaints only the video's own rectangle, and the host's frame loop keeps running while the session plays. |

The box is laid out like an image of the video's size, so `width`, `height`, `aspect-ratio`, `object-fit` and `object-position` all work. When the
provider cannot open the resource, or the document has none, the `poster` is shown.

`PdfGenerateConfig.MediaProvider` sets the same option for PeachPDF.

## Writing a provider

```csharp
public sealed class MyProvider : IMediaProvider
{
    public string CanPlayType(string mimeType) => mimeType.StartsWith("video/mp4") ? "probably" : "";

    public async ValueTask<IMediaSession?> OpenAsync(MediaRequest request, CancellationToken cancellationToken = default)
    {
        // request.Address is absolute; request.LoadAsync() reads it through the document's own resource loader
        // (so a data:, file:, https: or blob: address all work). request.RenderContext is the document's render
        // context, whose graphics device a provider that makes textures draws on.
        var player = await MyPlayer.OpenAsync(request.Address, cancellationToken);
        return player is null ? null : new MySession(player);
    }
}
```

A session reports what it has (`ReadyState`, `Duration`, `VideoWidth`/`VideoHeight`, `Position`, `IsPlaying`, `Ended`, `Error`), is told what to do
(`Play`, `Pause`, `Seek`, `PlaybackRate`, `Volume`, `Muted`, `Loop`), raises `Changed` when anything does (from any thread), and hands out
pictures:

* `AcquireFrameAtAsync(position)` finds the picture at a position and waits for it. A paged document asks once.
* `AcquireFrame()` returns the picture at the current position. Asked again before the picture changes, it returns a frame with the same
  `MediaFrame.Time`; the document paints only when the time changes.
* A `MediaFrame` is a lease: dispose it, and the provider may reuse what the picture is made of. The picture is upright and in RGB, with the stream's
  rotation, matrix and range already applied by the decoder. `MediaFrame.ColorSpace` (`Srgb` by default, `DisplayP3`, `Rec2020`) says which RGB space
  the pixels are in; a canvas converts from it.

The sound is the provider's: the document never sees audio samples.

### A deterministic clock

`MediaProviderOptions.DeterministicClock` asks a provider's sessions to follow the document's clock instead of the player's. The position then moves
only as the time passed to `IMediaSession.Update` does, and each picture is the one at that position, found by seeking. A document that plays media
then behaves the same on a slow machine as on a fast one, which is what the test runners need.

## The GStreamer provider

`PeachBrackets.Media.GStreamer` (`GStreamerMediaProvider`) plays through the GStreamer installed on the machine: `libgstreamer-1.0` on Linux, the GStreamer
runtime on Windows (found through `GSTREAMER_1_0_ROOT_MSVC_X86_64` and its siblings) and macOS (the framework or Homebrew). Nothing is bundled, and the
bindings are source-generated `LibraryImport`, so it is trim and ahead-of-time safe. `GStreamerMediaProvider.IsAvailable` says whether GStreamer starts;
without it, or without the plugin for a codec, `CanPlayType` answers `""` for what needs it and media elements fail as they do with no provider.

* **How it plays.** The resource is read through the document's loader into a temporary file (deleted when the session is disposed), and a `playbin`
  plays it; a video's pictures come from an `appsink` after `videoconvert` as RGBA, which applies the stream's YUV matrix and range. The `colorimetry` of
  the stream sets `MediaFrame.ColorSpace` (a Rec. 2020 stream is `Rec2020`). Frames are copied pixels (a `PeachDrawing.Core.Image` every backend draws by
  uploading it once); zero-copy GPU frames come with the platform providers.
* **Real time.** The pipeline plays by its own clock and the sound goes to the default output (a muted element uses no output). The end of the media and
  errors are read from the pipeline's bus whenever the document calls `Update` or asks for a picture.
* **Deterministic.** With `MediaProviderOptions.DeterministicClock` the pipeline stays paused. The position is the document's: `Update(documentTime)`
  advances it, and a picture is the one an accurate flushing seek to that position leaves as the preroll sample. A picture is reused while the position is
  inside the span it covers, so playing at the frame rate seeks once per frame, not once per step.
* **What it needs.** For `canPlayType`, the demuxer of a container (`matroskademux`, `qtdemux`, `oggdemux`, `wavparse`...) and the decoder of each named
  codec (`vp8dec`/`vp9dec`, `avdec_h264`, `avdec_h265`, `opusdec`, `vorbisdec`, `avdec_aac`...) must be installed. On Debian and Ubuntu:
  `gstreamer1.0-plugins-base`, `-good`, `-bad` and `gstreamer1.0-libav`.
* **Color.** `videoconvert` applies the stream's YUV matrix and range. A small picture with no colorimetry (or one tagged Rec. 601) is taken as Rec. 601 and
  color-managed into sRGB the way a browser shows it (a pure red becomes (247, 37, 0)), by tagging it for GStreamer's primaries conversion; such a stream
  is opened twice, once to read what it says about its colors. A picture in Rec. 2020 or Display P3 keeps its gamut and says so in
  `MediaFrame.ColorSpace`; any other is passed through as the decoder gives it.
* **Rotation.** A stream's rotation tag is applied (`videoflip method=automatic`), and `VideoWidth`/`VideoHeight` are the shown size.
* **Not done.** `Changed` is raised only when the document polls, not from GStreamer's own thread; `PlaybackRate` in real time seeks (no pitch correction).

## The Windows provider

`PeachBrackets.Media.Windows` (`WindowsMediaProvider`, Windows only) plays through `Windows.Media.Playback.MediaPlayer`, so it plays what the machine has codecs for:
H.264, HEVC, AAC, MP3 and WAV on a stock Windows, and WebM, VP9, AV1 and Ogg where their Store extensions are installed. `CanPlayType` answers from
`CodecQuery` and the byte-stream handlers Media Foundation has registered, so a codec or container that is not installed answers "no".

* **Pictures stay on the GPU.** The player is a frame server (`IsVideoFrameServerEnabled`): each picture it serves is copied with `CopyFrameToVideoSurface`
  into a `CanvasRenderTarget` on the document's `Win2dRenderContext.Device` and wrapped with `Win2dRenderContext.WrapBitmap`, so a Win2D canvas draws it
  without a copy or a read back. With a render context that is not a `Win2dRenderContext` (an Avalonia or Skia host, a raster or PDF context), each picture is made on a shared device of the
  provider's own and read back into pixels the canvas draws: a copy, where a Win2D document has none.
* **Deterministic.** With `MediaProviderOptions.DeterministicClock` the player stays paused and the position is the document's; a picture is the one the
  player serves after a seek to that position. The first picture a paused player serves on opening is an empty one, so the session seeks away and back
  before it is handed over.
* **Frame server limits.** A picture larger than 576 lines needs a hardware device (on the software device a hosted runner has, the frame never
  comes), and Media Foundation decodes a picture of 576 lines or fewer with its own Rec. 601 handling whatever the stream is tagged, so the colors of a
  small H.264 clip are a little off a browser's.
* **The WinUI view** sets it by default (`HtmlView.MediaProvider`).

## The Media Foundation provider (any target framework)

`PeachBrackets.Media.MediaFoundation` (`MediaFoundationMediaProvider`) plays through Windows Media Foundation from **any .NET program on Windows, including a plain `net10.0` one**, which
the WinRT-based provider above cannot (a plain target framework cannot reference a Windows-targeted assembly). It uses generated Win32 bindings (CsWin32, function-pointer
COM, no marshalling, AOT-safe) instead of WinRT, so nothing but the system DLLs is needed at run time.

* **Pictures.** A source reader over a copy of the file, asked for NV12 (the decoder's own output), decodes forward and goes back to a key frame
  only for a position behind it or more than two seconds ahead; the picture at a position is the last one that starts at or before it. Pictures are pixels any canvas draws (there
  is no GPU path: use the WinRT provider for a Win2D document).
* **Color.** The YUV is converted here, by the matrix and range the stream says (read from the media type: Rec. 601, 709, 240M, 2020, limited or full range), because
  Media Foundation's own conversion takes a picture of 576 lines or fewer as Rec. 601 whatever it is tagged. A stream that says nothing is taken as a browser takes it: Rec. 601
  up to 576 lines, Rec. 709 above. A Rec. 601 picture is color-managed into sRGB the way a browser does (gamma 2.0, SMPTE 170M to Rec. 709 primaries, sRGB encode), so a pure
  red shows as (247, 37, 0); Rec. 2020 and DCI-P3 streams keep their gamut and say so in `MediaFrame.ColorSpace`. Media Foundation does not pass the color tags of an H.264 stream
  in an MP4 file on to the media type (those of a VP9 stream in WebM it does), so an H.264 picture is converted by the assumption above.
* **Sound.** A second source reader decodes the audio to 32-bit float stereo at 48 kHz and a thread feeds it to a shared-mode WASAPI client (200 ms buffer). The session's own
  stopwatch is the clock the pictures follow; the sound only has to start where it starts and keep up. At another rate the sound is opened as a stream faster or slower than it was decoded, so the system's converter plays it and the pitch follows (as on tape). No output device means no
  sound and a playing video.
* **Deterministic.** With `MediaProviderOptions.DeterministicClock` the position moves only in `Update` and no sound is made; the same position always gives the same picture
  whether it was reached by playing or by seeking.
* **Defaults.** `PlatformMediaProvider.Create` returns it on Windows for a program that does not target Windows, and `PeachBrackets.Avalonia`'s `HtmlView` uses that.

## The Android provider

`PeachBrackets.Media.Android` (`AndroidMediaProvider`, API 24+) plays through the platform `MediaPlayer`, so it plays what the device has decoders for; `CanPlayType`
answers from `MediaCodecList` (decoders only).

* **Playing by the player's clock.** The player renders into an `ImageReader` (YUV, because a decoder's surface refuses an RGBA reader); the newest image is the
  picture, converted to RGBA with Rec. 601 below 577 lines and Rec. 709 above and turned by the stream's rotation (the player leaves it to the viewer).
  The reader holds only the images it was made for, so each is **closed** after it is read: disposing the binding does not close it.
* **Deterministic.** There is no player: the position is the document's, and a picture is what `MediaMetadataRetriever.GetFrameAtTime(position, Closest)` returns.
* **Not zero-copy.** A picture is copied into pixels the backend uploads; handing the `HardwareBuffer` to Skia as a texture is not done.
* **The Android view** sets it by default (`HtmlView.MediaProvider`). Verified on an emulator by `src/examples/PeachBrackets.Media.Android.Example`, a bare app that plays
  two clips in both modes and logs `OK`/`FAIL` lines under the tag `PEACH_MEDIA`.

## The Apple provider

`PeachBrackets.Media.Apple` (`AppleMediaProvider`, macOS 11+ and iOS 15+) plays through AVFoundation. **Not yet run on a Mac or an iPhone**: it was type-checked against the
iOS reference assemblies from a Windows machine and nothing more.

* **Playing by the player's clock.** An `AVPlayer` with an `AVPlayerItemVideoOutput` (BGRA); `AcquireFrame` copies the buffer for the time now, if the output has a new one.
* **Deterministic.** There is no player; a picture is what an `AVAssetImageGenerator` renders at exactly the position (zero tolerance, the track's transform applied), drawn
  into an sRGB bitmap so the system does the color management.
* **`CanPlayType`** asks `AVUrlAsset.AudiovisualMimeTypes` for the container and accepts the codecs every Apple platform has (H.264, HEVC, AAC, MP3, FLAC, ALAC, AC-3, E-AC-3).
* **The AppKit and UIKit views** set it by default (`HtmlView.MediaProvider`).

## One provider for the platform

`PeachBrackets.Media.Platform` has one call, `PlatformMediaProvider.Create(options)`, that returns the provider for the operating system the program runs
on, or null when it has none (a document then shows its posters). What it picks depends on what the **consuming program was built for**, because the package has
one assembly per target framework:

| The program targets | `Create` returns |
|---|---|
| `net10.0-windows10.0.19041.0` (or net11) | for a Win2D document the WinRT provider (pictures stay on the GPU), for any other the Media Foundation provider; GStreamer if Windows has neither |
| `net10.0-android` (package built with `-p:PeachIncludeAndroid=true`) | the Android provider |
| plain `net10.0` / `net11.0` on Windows | the Media Foundation provider, else GStreamer when installed |
| plain `net10.0` / `net11.0` elsewhere | GStreamer when installed (the Apple provider is not wired in yet) |

So an Avalonia app gets Media Foundation on Windows whichever target framework it uses. The WPT runner (plain `net10.0`) gets Media Foundation on Windows and GStreamer on Linux, both with
`DeterministicClock`, so the audio and video tests run on a real decoder on a developer's machine and on the CI image alike.

## Script media elements

With `PeachBrackets.Scripting`, a script's `audio` and `video` elements are driven by the same provider. A `src` (or `load()`, or a `source` child the provider
can play, chosen by its `type` through `CanPlayType`) opens a session, and the element behaves as in a browser, on the script's virtual clock:

* The event sequence: `loadstart`, `durationchange`, `loadedmetadata`, `resize` (video), `loadeddata`, `canplay`, `canplaythrough`; `play`, `playing`, `pause`,
  `seeking`, `seeked`, `timeupdate`, `ended`, `emptied`, `error`, `waiting`, `volumechange`, `ratechange`. `autoplay` plays at `canplaythrough`.
* `play()` returns a promise that settles when playback has started (or rejects `NotSupportedError` for a source that cannot be played, `AbortError` when
  paused or reloaded first); `pause()`, `load()`, `currentTime` (setting it seeks), `duration`, `paused`, `ended`, `seeking`, `readyState`, `networkState`,
  `currentSrc`, `error` (a `MediaError`), `volume` (validated), `muted`, `playbackRate`, `defaultPlaybackRate`, `loop`, `seekable`, `buffered`, `played`
  (`TimeRanges`), `canPlayType`, and on a video `videoWidth`, `videoHeight`, `requestVideoFrameCallback`/`cancelVideoFrameCallback` and `getVideoPlaybackQuality`.
* A provider's state is read on the virtual clock, not when the provider changes it: while anything is playing or loading a timer, every 20 ms of script time,
  tells each session the time and turns what changed into events. Use `MediaProviderOptions.DeterministicClock` for sessions that play by that clock, so the
  position a script reads is the same on every machine.
* **Canvas sources.** A `video` with a picture is a source for `drawImage`, `createPattern` and `createImageBitmap`: the picture the script's session shows at the
  script's time now, at the size of the video (`videoWidth` by `videoHeight`). A video whose `readyState` is below `HAVE_CURRENT_DATA`, or that failed, draws nothing
  (no error), has no pattern (`createPattern` returns `null`) and rejects `createImageBitmap` with `InvalidStateError`. The picture is copied when it is taken, and the
  frame is given back at once, so a bitmap or a pattern keeps its picture while the video plays on. A picture in Display P3 or Rec. 2020 is converted into
  the canvas's own space (colors outside the canvas gamut are clipped there, not before). A video from another origin than the document, asked for without a
  `crossorigin` attribute, taints the canvas, so reading it back throws `SecurityError`; the server's CORS answer is not checked (as for images).
* A script's session lives until its scripts are done; the document is then parsed again from what they left, and the video a viewer sees opens its own
  session. Playing state does not carry across.
* A `<source>` whose resource the provider cannot open fires `error` at itself and the next candidate is tried; when none is left the element has no source (`networkState` 3,
  `play()` rejects `NotSupportedError`) and fires no error of its own, which the standard keeps for a failing `src`.
* `drawElementImage` of an element that contains a video shows the video's poster: the picture of an element is made from a copy of its markup that has no media provider.
* Not supported: `srcObject` (always null), `captureStream`, text tracks, `webkitAudioDecodedByteCount`-style extensions.

## Limits

* One picture per step; no `audio` output beyond what a provider does itself.
* Not supported: `MediaStream`, `srcObject`, WebCodecs, media source extensions, text tracks, built-in controls.
