# The PeachBrackets.Media public API must not mirror a competitor's

Same standing rule as [[core-public-api-must-not-mirror-a-competitor]] and [[text-public-api-must-not-mirror-a-competitor]]:
a maintainer of a commercial competitor has already objected that another PeachDrawing-family library looked "too similar" to
theirs. `PeachBrackets.Media` is the seam between a document and a platform's decoder. Its vocabulary - session, provider, frame,
ready state - is shared with the HTML media element model (`readyState`, `networkState`, `MediaError`), Media Foundation,
AVFoundation, ExoPlayer and GStreamer, so the discipline is: take names from the HTML standard where an element API is mirrored
(`MediaReadyState` values, `MediaErrorCode` values: those are the standard's, not anyone's product), and choose our own names and
our own shape for everything the standard does not name.

## The rules

The five rules of [[text-public-api-must-not-mirror-a-competitor]] (requirements first, structural differences not renames,
naming discipline, clean-room process, the register below kept current with every PR that adds public API).

## Register

| Name | Origin |
| --- | --- |
| `IMediaProvider` / `IMediaSession` | Ours. A provider opens a session for a request; a session is the opened resource. Not `IMediaPlayer`/`MediaSource`: those are platform product names. |
| `MediaRequest` | Ours: address, kind, render context, a loader callback. |
| `MediaFrame` | Ours: a leased, upright, RGB picture with a time and a release callback. Not a WebCodecs `VideoFrame` (which has a different shape and ownership). |
| `MediaReadyState`, `MediaErrorCode` | The HTML standard's `readyState` and `MediaError.code` values, with C# names. |
| `MediaChange`, `MediaChangedEventArgs` | Ours: one coalesced change notification instead of the per-event surface of a player. |
| `MediaProviderOptions.DeterministicClock` | Ours: the session follows the document's clock; for tests. |
| `AcquireFrame` / `AcquireFrameAtAsync` | Ours: the picture is a lease the caller disposes. |
| `Update(TimeSpan documentTime)` | Ours: how a deterministic session follows the document's clock. |
