# PeachBrackets.Media.MediaFoundation

A [PeachBrackets](https://www.nuget.org/packages/PeachBrackets) media provider over Windows Media Foundation that works from **any .NET program on Windows**, whatever its
target framework (plain `net10.0` included): generated Win32 bindings, no WinRT, Win2D or GStreamer.

```csharp
var options = new HtmlDocumentOptions
{
    MediaProvider = new MediaFoundationMediaProvider(),
};
```

* Pictures come from a Media Foundation source reader (RGB output, so the system's video processor does the YUV conversion) and are pixels any canvas draws. For a document
  drawn with Win2D, `PeachBrackets.Media.Windows` keeps pictures on the GPU instead.
* Sound is rendered through WASAPI while a session plays by its own clock.
* `MediaProviderOptions.DeterministicClock` plays by the document's clock: the position moves only as the document's time does and a picture is the one that shows there.
* `CanPlayType` answers from the decoders and container handlers this Windows has.
