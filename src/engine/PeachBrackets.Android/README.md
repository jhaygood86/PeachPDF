# PeachBrackets.Android

A [.NET for Android](https://learn.microsoft.com/en-us/dotnet/android/) (not MAUI) `HtmlView` that shows an HTML document as one
scrolling, zoomable surface. The document is laid out by [PeachBrackets](https://www.nuget.org/packages/PeachBrackets/) and painted with
[PeachDrawing.Skia](https://www.nuget.org/packages/PeachDrawing.Skia/) (through `SkiaSharp.Views`) onto the graphics hardware or, if
asked, the CPU. There is no WebView underneath. The view lays the document out for its width, paints only what is in view, turns touch
into scrolling, fling and zoom, follows the pointer and touch behavior the document asks for, opens links, handles selection and copy,
edits form controls and exposes the page to TalkBack.

```bash
dotnet add package PeachBrackets.Android
```

**Requirements:** an Android app built with the `android` workload (`dotnet workload install android`); `net10.0-android` (and
`net11.0-android` when built with the .NET 11 SDK); Android API level 24 (7.0) or later. The package depends on `PeachBrackets`,
`PeachDrawing.Skia` and `SkiaSharp.Views`.

## Quick start

```csharp
[Activity(Label = "Viewer", MainLauncher = true)]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var view = new HtmlView(this)
        {
            Html = "<h1>Hello</h1><p>A <a href='https://peachpdf.net/'>link</a>.</p>",
        };
        SetContentView(view);
    }
}
```

One CSS pixel is one density-independent pixel, so a page looks as it does in a mobile browser. The document is laid out again, after a
short pause, when the view is resized or the theme changes. On Android 15 and later an activity draws edge to edge, so put the view in a
container that fits the system windows (`SetFitsSystemWindows(true)`) or apply the insets yourself.

## Features

**Loading.** `Html` sets the document. Reading fonts, parsing and layout run on a pool thread, so a slow load never holds the main thread;
`DocumentLoaded` and `DocumentFailed` (`e.Exception`) are raised on the main thread. Images, style sheets and fonts load from `data:` URIs
and files; web addresses only when you set `AllowNetworkAccess = true` (off by default).

**Links.** Cancel a tapped link to handle it yourself; otherwise `http`, `https`, `mailto` and `tel` addresses open with `Intent.ActionView`
and a link to a place in the document scrolls to it.

```csharp
view.LinkTapped += (s, e) =>
{
    if (e.Uri.StartsWith("app://")) { e.Cancel = true; Navigate(e.Uri); }
};
```

**Touch.** A drag scrolls and a release flings, a pinch zooms about the fingers, a double tap zooms in and back out. What may start is
read from the document's `touch-action` at touch-down. Each stage is an event: `TouchStarted` (clear flags of `e.Allowed` to take gestures
away), `Tapped`, `LongPressed`, `ScrollChanged`, `ZoomChanged`. `Zoom` is the zoom factor.

**Mouse, trackpad and keyboard.** Hover and `:hover` rules, pointer icons for `cursor`, the wheel, Ctrl with plus and minus to zoom, Tab to
move focus, and arrows, Page and Home/End to scroll, so it works on tablets, ChromeOS and resizable windows too. Declare the configuration
changes your activity handles (`screenSize`, `smallestScreenSize`, `screenLayout`, `orientation`, `density`, `uiMode`...) to survive a resize
without a restart.

**Selection and the clipboard.** A long press selects the word and shows the floating Copy / Select all menu; dragging extends it.
`SelectedText`, `CopySelection()`, `SelectAll()`, and `ScrollToElement("id")`.

**Look.** `PageBackground`, and `Input` (the pointing devices the `hover` and `pointer` media queries report; by default the view asks the
system). The view reports the screen density as the device pixel ratio, so a `srcset` image loads at the density the screen needs.

**Forms.** Check boxes, radio buttons, text fields (with the soft keyboard chosen from the field's `type`), drop-downs, date, time and colour
dialogs, range controls and file fields are drawn from their CSS and work. For a file field forward the result from your activity:

```csharp
protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
{
    if (!view.HandleActivityResult(requestCode, resultCode, data))
        base.OnActivityResult(requestCode, resultCode, data);
}
```

`FormSubmitting` gives the form as it would be sent (`e.Submission.Serialize()`; the view sends nothing itself) and `FormInvalid` reports failed
validation.

**Animations.** `AnimationsEnabled = true` plays CSS animations, transitions and animated images (off while Android's animator duration scale
is 0); `AnimationEvents` reports their events.

**Graphics hardware.** `new HtmlView(context)` draws on an OpenGL surface, keeping the page as GPU texture tiles while it scrolls;
`new HtmlView(context, useGpu: false)` draws on the CPU, for a device or emulator without a working GL driver.

**Accessibility.** The view is an accessibility node provider over the page's accessibility tree, so TalkBack reads text, headings, links,
controls and live regions, explores by touch, and follows links. Tab steps through the page's tab stops and `FocusedNode` is the part with
the focus.

**The document itself.** `view.Document` is the loaded `PeachBrackets.HtmlDocument`, for `FindText`, element bounds and annotations. After
changing it yourself call `ApplyUpdate(result)`.

## Limits

- Selection has no drag handles; a drag after the long press extends it.
- A custom `url()` cursor image is shown as its keyword.
- Zoom re-renders at the new scale (the tiles are painted again), which is slow on very large pages.
- The page is one continuous surface; to paginate a document use PeachBrackets directly.

## More

- [PeachBrackets.Android guide](https://peachpdf.net/peachbrackets-android.html): every member, touch integration points, accessibility, form controls, tablets and resizable windows, emulator testing.
- [PeachBrackets guide](https://peachpdf.net/peachbrackets.html) and [HTML and CSS support](https://peachpdf.net/html-css-support.html): what the engine renders.

BSD 3-Clause.
