# PeachBrackets.WinUI

A WinUI 3 `HtmlView` control that shows an HTML document as one scrolling, zoomable surface. The document is laid out by
[PeachBrackets](https://www.nuget.org/packages/PeachBrackets/) and painted with
[PeachDrawing.Win2D](https://www.nuget.org/packages/PeachDrawing.Win2D/); there is no browser or WebView underneath, and no XAML
widgets are placed over the page. The control does the integration an application would otherwise write itself: it lays the
document out for the control's width, paints only what is in view, follows the pointer and touch behavior the document asks for,
opens links, handles selection and copy, draws and edits form controls, plays animations and exposes the page to UI Automation.

```bash
dotnet add package PeachBrackets.WinUI
```

**Requirements:** Windows 10 version 1809 (10.0.17763) or later; `net10.0-windows10.0.19041.0` (and `net11.0-windows10.0.19041.0` when
built with the .NET 11 SDK); a WinUI 3 app on the Windows App SDK, built for a platform (`x64`, `ARM64`) so that the Win2D native
binary is supplied. The package references only the parts of the Windows App SDK it compiles against, not the all-in-one
`Microsoft.WindowsAppSDK` package. The application decides how it gets the runtime: reference `Microsoft.WindowsAppSDK` for the whole
thing, or `Microsoft.WindowsAppSDK.Runtime` for a lean package.

## Quick start

```xml
<Page xmlns:peach="using:PeachBrackets.WinUI">
    <peach:HtmlView x:Name="Viewer" />
</Page>
```

```csharp
Viewer.Html = "<h1>Hello</h1><p>A <a href='https://peachpdf.net/'>link</a>.</p>";

// or load from an address; a file: URI reads from disk
Viewer.Source = new Uri(@"file:///C:/docs/report.html");
```

One CSS pixel is one device-independent pixel, so a page looks as it does in a browser. The document is laid out again, after a short
pause, when the control is resized, so it reflows like a web page. `Html` and `Source` are dependency properties, so they bind.

## Features

**Links.** Handle a link yourself by cancelling it:

```csharp
Viewer.LinkClicked += (s, e) =>
{
    if (e.Uri.StartsWith("app://")) { e.Cancel = true; Navigate(e.Uri); }
};
```

**Loading and failures.** `DocumentLoaded` fires when a document has laid out, `DocumentFailed` (`e.Exception`) when it could not be loaded.
Web addresses load only when you set `AllowNetworkAccess = true` (off by default); `data:` URIs and files always work.

**Fonts.** Add fonts once to the shared set; every view uses it unless you give one its own `FontSet`:

```csharp
HtmlViewFonts.AddFile(@"C:\fonts\SourceSans3-Regular.ttf");
```

**Selection and the clipboard.** `SelectedText`, `CopySelection()` and `SelectAll()`; Ctrl+C and Ctrl+A work when the control has focus.
`SelectionColor` sets the colour behind selected text.

**Scrolling, zoom and anchors.** `ScrollToElement("section-2")` scrolls an element to the top; `IsZoomEnabled` (on by default) allows Ctrl+wheel and pinch zoom.

**A block inside a list.** Set `ScrollMode = HtmlViewScrollMode.Disabled` to make the view as tall as its document, so it can be a row in a
virtualized list and leaves the wheel and touch to the list. Read `ContentHeight` (and the `ContentHeightChanged` event), and set
`IsLayoutSynchronous = true` for a document that needs nothing asynchronous so its size is known the moment it is needed.

**Look and behavior.** `PageBackground`, `DocumentMargin`, `UserStyleSheet` (CSS applied before the document's own), `TextRendering` (how
text is drawn), and `Input` (the pointing devices the `hover` and `pointer` media queries report; by default the control asks the system).

**Animations.** `AnimationsEnabled = true` plays CSS animations, transitions and animated images on the display's frame clock, only
while something is playing. It stays off when Windows' animation effects are off. `AnimationEvents` reports `animationstart`,
`transitionend` and the rest.

**Forms.** Check boxes, radio buttons, text fields (with IME, caret, selection and clipboard), drop-downs, date, time and colour pickers, file
pickers and range controls are drawn from their CSS and work. `FormSubmitting` gives you the form as it would be sent
(`e.Submission.Serialize()` makes the body; the view sends nothing itself); `FormInvalid` reports failed validation.

**Accessibility.** The view is a UI Automation Document whose children follow the page's accessibility tree: text, hyperlinks (Invoke), controls
with Toggle, Value, RangeValue, Grid, Table, Text and Scroll patterns, headings, landmarks and live regions, so Narrator can read, jump by
heading and follow links. Tab steps through links and controls; `FocusedNode` is the part with the focus.

**The document itself.** `view.Document` is the loaded `PeachBrackets.HtmlDocument`, for what the control does not wrap: `FindText`,
element bounds, links and annotations. After changing it yourself call `ApplyUpdate(result)` so the view repaints.

## Limits

- Zoom scales the rendered surface rather than re-rendering at the new scale, so very high zoom is soft.
- A `url()` cursor image uses its first frame, and an SVG cursor image is not used (the keyword's pointer is shown instead).
- Find in the page is available through `Document` (`FindText`) but has no built-in user interface.
- The page is one continuous surface; to paginate a document use PeachBrackets directly.

## More

- [PeachBrackets.WinUI guide](https://peachpdf.net/peachbrackets-winui.html): every member, lists and recycling, accessibility, animations, forms, the demo app.
- [PeachBrackets guide](https://peachpdf.net/peachbrackets.html) and [HTML and CSS support](https://peachpdf.net/html-css-support.html): what the engine renders.

BSD 3-Clause.
