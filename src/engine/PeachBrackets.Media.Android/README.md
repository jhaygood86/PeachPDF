# PeachBrackets.Media.Android

An Android media provider for [PeachBrackets](https://www.nuget.org/packages/PeachBrackets): the `audio` and `video` elements of an HTML document play through the
platform's `MediaPlayer` (over `MediaCodec`), so they play what the device has decoders for.

```csharp
var options = new HtmlDocumentOptions
{
    MediaProvider = new AndroidMediaProvider(),
};
```

* While a video plays by the player's own clock it renders into an `ImageReader`; the newest image is the picture. For a document drawn with
  `PeachDrawing.Skia` on Android 10 or later it stays in the decoder's hardware buffer and is sampled as a texture with no copy; otherwise it is converted from YUV
  on the CPU and turned by the stream's rotation.
* `MediaProviderOptions.DeterministicClock` plays by the document's clock: there is no player, and each picture is what `MediaMetadataRetriever` returns for the
  position the document has reached.
* `CanPlayType` answers from the decoders `MediaCodecList` reports.
* `PeachBrackets.Android`'s `HtmlView` uses it by default (`HtmlView.MediaProvider`).
