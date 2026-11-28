# PeachBrackets.Media.Windows

A Windows media provider for [PeachBrackets](https://www.nuget.org/packages/PeachBrackets): the `audio` and `video` elements of an HTML document play through
Windows Media Foundation (`Windows.Media.Playback.MediaPlayer`), with whatever codecs the machine has.

```csharp
var context = new Win2dRenderContext();
var options = new HtmlDocumentOptions
{
    MediaProvider = new WindowsMediaProvider(),
};
```

* A video's pictures are served by the player's frame server into Win2D bitmaps on the device of the document's `Win2dRenderContext`, and drawn from there
  without a copy or a read back.
* `CanPlayType` answers from the decoders this Windows has (`CodecQuery`) and the container handlers it registers, so WebM, VP9, AV1, HEVC and Ogg are
  answered "no" until their Store extensions are installed.
* `MediaProviderOptions.DeterministicClock` keeps the player paused and finds each picture by seeking it to the position the document's clock reached.
