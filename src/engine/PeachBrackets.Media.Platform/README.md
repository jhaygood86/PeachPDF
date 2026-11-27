# PeachBrackets.Media.Platform

One call that picks the [PeachBrackets](https://www.nuget.org/packages/PeachBrackets) media provider for the operating system the program runs on.

```csharp
var options = new HtmlDocumentOptions
{
    MediaProvider = PlatformMediaProvider.Create(),
};
```

`Create` returns null when the platform has no decoder installed, and a document then shows its posters as it does with no provider. Today every platform is
served by [`PeachBrackets.Media.GStreamer`](https://www.nuget.org/packages/PeachBrackets.Media.GStreamer); the platform decoders take over their platforms
as their packages land. Pass `new MediaProviderOptions { DeterministicClock = true }` for playback that follows the document's clock, which test runners use.
