# PeachBrackets.Apple

An `HtmlView` for macOS (AppKit `NSScrollView`) and iOS (UIKit `UIScrollView`) apps that shows an HTML document as one scrolling,
zoomable surface. The document is laid out by [PeachBrackets](https://www.nuget.org/packages/PeachBrackets/) and painted with
[PeachDrawing.CoreGraphics](https://www.nuget.org/packages/PeachDrawing.CoreGraphics/); there is no WKWebView underneath. The view
lays the document out for its width, paints only what is in view, turns the mouse, trackpad, touch or pointer into scrolling and zoom,
follows the pointer and touch behavior the document asks for, opens links, handles selection and copy, edits form controls and
exposes the page to VoiceOver.

```bash
dotnet add package PeachBrackets.Apple
```

**Requirements:** a Mac with the `macos` and `ios` workloads (`dotnet workload install macos ios`); `net10.0-macos` (macOS 11 or later)
and `net10.0-ios` (iOS 15 or later; the edit menu after a long press needs iOS 16), plus the `net11.0` ones when built with the
.NET 11 SDK. The package depends on `PeachBrackets`, `PeachDrawing.Core` and `PeachDrawing.CoreGraphics`. The two platforms have the
same members where they allow it and differ where they do not (mouse events on the Mac, touch events on iOS).

## Quick start

```csharp
// macOS
var view = new HtmlView { Html = "<h1>Hello</h1><p>A <a href='https://peachpdf.net/'>link</a>.</p>" };
window.ContentView = view;

// iOS
var view = new HtmlView { Html = "<h1>Hello</h1>", Frame = View.Bounds };
View.AddSubview(view);
```

One CSS pixel is one point, so a page looks as it does in a browser. The document is laid out again, after a short pause, when the view is
resized or the appearance (light or dark) changes.

## Features

**Loading.** `Html` sets the document. Fonts, parsing and layout run on a pool thread, and `DocumentLoaded` and `DocumentFailed`
(`e.Exception`) are raised on the main thread. `AllowNetworkAccess` (off by default) lets `http` and `https` resources load; set
`DocumentBaseUri` when the HTML is a page you fetched yourself so its relative references resolve.

**Links.** Cancel a link to handle it yourself; otherwise `http`, `https`, `mailto` and `tel` addresses go to the system.

```csharp
view.LinkActivated += (s, e) =>
{
    if (e.Uri.StartsWith("app://")) { e.Cancel = true; Navigate(e.Uri); }
};
```

**Painting.** The page is painted into 512-pixel tiles, each a Core Animation layer, so scrolling and pinch are composited by the graphics
hardware without repainting. Effects (blur, shadow, filters, masks) run in Core Image on Metal when the machine has it; `UseGpu = false`
keeps them on the CPU.

**Pointer, touch and keyboard.** Mouse and trackpad (hover and `:hover` rules, `cursor`, wheel and pinch), touch on iOS (drag,
fling, pinch, double tap, long press), and hardware keyboards (Tab and Shift+Tab step through the page's tab stops). On iOS
`Tapped`, `LongPressed` and `TouchStarted` follow each stage (clear flags of `e.Allowed` to take gestures away); on the Mac `Clicked` reports
a click that was not on a link. `ScrollChanged` and `ZoomChanged` follow the view.

**Selection and the pasteboard.** `SelectedText`, `CopySelection()`, `SelectAll()` and `ScrollToElement("id")`; Cmd+C and Cmd+A work on a Mac
and with a hardware keyboard on iOS.

**Forms.** Check boxes, radio buttons, text fields, drop-downs, date, time and color pickers, file fields and range controls are drawn from
their CSS and work (`NSTextInputClient` on macOS, `UIKeyInput` on iOS). `FormSubmitting` gives the form as it would be sent
(`e.Submission.Serialize()`; the view sends nothing itself) and `FormInvalid` reports failed validation.

**Animations.** `AnimationsEnabled = true` plays CSS animations, transitions and animated images, stepped on a display link (iOS) or a 60 Hz
timer (macOS); `AnimationEvents` reports their events.

**Look.** `PageBackground`, and `Input` (the pointing devices the `hover` and `pointer` media queries report; a Mac reports a mouse or
trackpad, an iPhone touch, an iPad with a pointer both).

**Accessibility.** The view exposes the page's accessibility tree to VoiceOver; `FocusedNode` is the part with the keyboard focus and
`FocusChanged` follows it.

**The document itself.** `view.Document` is the loaded `PeachBrackets.HtmlDocument`, for `FindText`, element bounds and annotations. After
changing it yourself call `ApplyUpdate(result)`.

## Limits

- On iOS an input method that composes text in place (marked text, as for Chinese or Japanese) commits it only when the composition ends; macOS uses marked text.
- Taking the view out of its window discards the loaded document; it loads again from `Html` when the view returns, so form values, the selection and the zoom are not kept.
- A custom `url()` cursor is shown as its keyword; selection has no drag handles.
- A Mac with a touch display is reported as having a fine pointer and hover only; direct touch on a Mac is not handled separately.
- Animated `transform` and `opacity` are not kept as textures, and zooming paints the page again at the new scale (slow on very large pages).
- The page is one continuous surface; to paginate a document use PeachBrackets directly.

## More

- [PeachBrackets.Apple guide](https://peachpdf.net/peachbrackets-apple.html): every member, painting, forms and text input, accessibility, the demo apps.
- [PeachBrackets guide](https://peachpdf.net/peachbrackets.html) and [HTML and CSS support](https://peachpdf.net/html-css-support.html): what the engine renders.

BSD 3-Clause.
