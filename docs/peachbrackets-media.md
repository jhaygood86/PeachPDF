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
* **Not done.** Rotation tags are not applied; a stream that only starts to play after more data arrives is not a case, since the file is complete before
  playback; `PlaybackRate` in real time seeks (no pitch correction).

## One provider for the platform

`PeachBrackets.Media.Platform` has one call, `PlatformMediaProvider.Create(options)`, that returns the provider for the operating system the program runs
on (null when it has no decoder, so a document shows its posters). Today every platform is served by the GStreamer provider; the Media Foundation,
AVFoundation and Android providers take over their platforms as their packages land. The WPT runner uses it with `DeterministicClock`, so the
audio and video tests run on a real decoder on a developer's machine and on the CI image alike.

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
* Not supported: `srcObject` (always null), `captureStream`, text tracks, `webkitAudioDecodedByteCount`-style extensions, `<source>` fallback to a later source
  when the first chosen one fails to open (the element fails).

## Limits

* One picture per step; no `audio` output beyond what a provider does itself.
* Not supported: `MediaStream`, `srcObject`, WebCodecs, media source extensions, text tracks, built-in controls.
