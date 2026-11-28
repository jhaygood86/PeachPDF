# PeachBrackets.Media.Apple

An Apple media provider for [PeachBrackets](https://www.nuget.org/packages/PeachBrackets): the `audio` and `video` elements of an HTML document play through
AVFoundation on macOS and iOS.

```csharp
var options = new HtmlDocumentOptions
{
    MediaProvider = new AppleMediaProvider(),
};
```

* While a video plays by the player's own clock an `AVPlayerItemVideoOutput` hands over the pictures as `CVPixelBuffer`s.
* `MediaProviderOptions.DeterministicClock` plays by the document's clock: there is no player, and each picture is what an `AVAssetImageGenerator` renders at
  exactly the position the document has reached.
* `CanPlayType` asks AVFoundation (`AVUrlAsset.IsPlayableExtendedMimeType`).
* `PeachBrackets.Apple`'s `HtmlView` uses it by default (`HtmlView.MediaProvider`).
