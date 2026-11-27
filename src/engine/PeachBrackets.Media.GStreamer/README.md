# PeachBrackets.Media.GStreamer

A [GStreamer](https://gstreamer.freedesktop.org/) media provider for [PeachBrackets](https://www.nuget.org/packages/PeachBrackets): the `audio` and
`video` elements of an HTML document play through the GStreamer installed on the machine.

```csharp
var options = new HtmlDocumentOptions
{
    MediaProvider = new GStreamerMediaProvider(),
};
```

* Nothing is bundled and no process is started. The native libraries (`libgstreamer-1.0`, `libgstapp-1.0`, GLib and GObject) are loaded when first used,
  through source-generated `LibraryImport` bindings, so the package is trim and ahead-of-time safe.
* A machine without GStreamer, or without the plugin for a codec, says no in `CanPlayType` for what needs it; the media element then fails as it does with
  no provider.
* `MediaProviderOptions.DeterministicClock` plays by the document's clock instead of the player's: the pipeline stays paused, and each picture is found by
  an accurate seek to the position the document is at. For tests and anything that must render the same on a slow machine as on a fast one.

On Debian and Ubuntu, `gstreamer1.0-plugins-base`, `-good`, `-bad` and `gstreamer1.0-libav` give WebM (VP8, VP9, Opus, Vorbis), MP4 (H.264, H.265, AAC) and
Ogg. See the [media guide](https://peachpdf.net/peachbrackets-media.html) for what a document does with a provider.
