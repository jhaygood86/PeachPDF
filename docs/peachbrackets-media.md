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
  rotation, matrix and range already applied by the decoder.

The sound is the provider's: the document never sees audio samples.

### A deterministic clock

`MediaProviderOptions.DeterministicClock` asks a provider's sessions to follow the document's clock instead of the player's. The position then moves
only as the time passed to `IMediaSession.Update` does, and each picture is the one at that position, found by seeking. A document that plays media
then behaves the same on a slow machine as on a fast one, which is what the test runners need.

## Limits

* One picture per step; no `audio` output beyond what a provider does itself.
* Not supported: `MediaStream`, `srcObject`, WebCodecs, media source extensions, text tracks, built-in controls.
