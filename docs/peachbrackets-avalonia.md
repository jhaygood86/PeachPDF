# PeachBrackets.Avalonia

`PeachBrackets.Avalonia` is an [Avalonia](https://avaloniaui.net/) 12 `HtmlView` control that shows an HTML document laid out by
[PeachBrackets](peachbrackets.md) as one scrolling, zoomable surface. It paints with [PeachDrawing.Skia](peachdrawing-skia.md) onto the Skia surface
Avalonia leases to a custom draw operation (the graphics hardware when Avalonia has a GPU, the CPU otherwise), and it does the integration an
application would otherwise write itself: it lays the document out for the control's width, paints only what is in view, turns the mouse, keyboard,
touch and wheel into scrolling and zoom, follows the [pointer and touch behavior the document asks for](html-css-support.md#pointer-interaction), opens
links, handles selection and copy, edits form fields, and exposes the page to assistive technology.

```bash
dotnet add package PeachBrackets.Avalonia
```

It targets `net10.0` and `net11.0` and needs no workload, so it builds on any machine. It needs **Avalonia 12** with Avalonia's Skia renderer (the
default on every desktop platform): Avalonia 11 uses SkiaSharp 2, which cannot be loaded beside the SkiaSharp 3 that PeachDrawing.Skia is built on.

> **Status: pre-1.0.** The public surface is `HtmlView`, `HtmlViewFonts`, `HtmlViewScrollMode` and the event argument types
> (`HtmlLinkActivatedEventArgs`, `HtmlPageClickedEventArgs`, `HtmlLoadFailedEventArgs`, `HtmlFormSubmittingEventArgs`, `HtmlFormInvalidEventArgs`,
> `HtmlAnimationEventsEventArgs`); `PublicApi.txt` beside the project is the reviewed snapshot.

## Quick start

```csharp
var view = new HtmlView { Html = "<h1>Hello</h1><p>A <a href='https://peachpdf.net/'>link</a>.</p>" };
window.Content = view;
```

or in XAML, with `xmlns:peach="using:PeachBrackets.Avalonia"`:

```xml
<peach:HtmlView Source="file:///C:/pages/report.html" />
```

One CSS pixel is one device independent pixel, so a page looks as it does in a desktop browser. The document is laid out again, after a short pause,
when the control is resized, the theme variant changes or the display scale changes. It is laid out as one continuous page; to paginate it, use
[PeachBrackets](peachbrackets.md) directly.

## Members

| Member | |
|---|---|
| `Html`, `Source` | What to show: markup, or the address of a `file:` document (or an `http`/`https` one when `AllowNetworkAccess` is on). `Html` wins when both are set. |
| `ScrollMode` | `Auto` (default): the control scrolls and zooms the document. `Disabled`: the control is as tall as the document (`ContentHeight`) so a parent scrolls it, which suits a list row or a page with several views. |
| `IsZoomEnabled`, `Zoom`, `SetZoom` | Zoom with Ctrl and the wheel, Ctrl and plus, minus or 0, or a pinch; limited to 0.5 to 4. Images are chosen again for the new zoom once it settles. |
| `AllowNetworkAccess`, `DocumentBaseUri` | Whether `http`/`https` resources may be loaded (off by default, so a document cannot make the application call out), and the address relative addresses resolve against (`Source` by default). |
| `SelectionColor`, `UserStyleSheet`, `DocumentMargin`, `PageBackground` | The selection color, a reader's style sheet, the space around the document, and the color behind the page. All but `PageBackground` take effect on the next load. |
| `AnimationsEnabled`, `AnimationEvents` | Whether CSS animations, transitions and animated images play (off by default), and the events of each frame. |
| `IsLayoutSynchronous` | Load and lay out on the UI thread before the next frame, for documents that need no fetching. |
| `Input` | The pointing devices the `hover` and `pointer` media queries report (a mouse on a desktop, a touch screen on a phone or tablet by default). |
| `FontSet`, `HtmlViewFonts` | The fonts documents may use besides the installed ones; `HtmlViewFonts.Shared` is used unless `FontSet` gives one. |
| `Document`, `SelectedText`, `FocusedNode` | The loaded `HtmlDocument`, the selected text, and the part of the page with the keyboard focus. |
| `CopySelection`, `SelectAll`, `ScrollToElement`, `ApplyUpdate` | Copy, select all, scroll an element to the top, and take in the result of a change made through the document's own API. |
| `DocumentLoaded`, `DocumentFailed`, `LinkActivated`, `PageClicked`, `FormSubmitting`, `FormInvalid`, `FocusChanged`, `ScrollChanged`, `ZoomChanged`, `ContentHeightChanged` | The control's events. Set `Cancel` on `LinkActivated` to handle a link yourself. The control sends no form: `FormSubmitting` hands you the form as it would be submitted. |

## What it does with the document's interaction styles

| What the document says | What the control does |
|---|---|
| `cursor` | The pointer follows the keyword; a `url()` cursor image (data, file, or http with `AllowNetworkAccess`; at most 2 MB, fitted to 128 px) is used once loaded, with its hotspot. |
| `:hover`, `:active` | The pointer state is applied as it moves and is pressed; only the latest position is applied while a restyle runs. |
| `pointer-events`, `inert` | The engine's own hit testing is used, so an inert subtree is not hovered, clicked, focused or selected. |
| `user-select` | Text that may not be selected is not. A double click selects a word, a triple click a block. |
| `touch-action` | A touch scroll, pinch or double-tap zoom is allowed only where the document allows it. |
| Links | A click raises `LinkActivated`, then follows the link: `http`, `https`, `mailto` and `tel` addresses are launched through Avalonia's launcher, an anchor scrolls to its target (honoring `scroll-margin` and `scroll-padding`). |
| Scroll containers | `overflow: auto` and `scroll` boxes and text areas take the wheel, the document-painted scroll bars and the keys before the page does. |
| `content-visibility: auto` | Boxes that come near the view are shown and those that left are skipped. |

## Form controls and text editing

Check boxes, radio buttons, buttons, ranges (also with the arrow, Home, End and Page keys), `details` (also through the expand/collapse automation pattern) and reset and submit controls work on a click or the keyboard. Shift and click extends the selection, on the page and in a text field, and a pinch pans the page as its focal point moves. Select, date, time, color and
file controls open an Avalonia flyout or the platform's file picker. Text fields and text areas are edited in place: caret, selection, typing, word and
line moves, cut, copy and paste, an input method (through `TextInputMethodClient`, offered only while a text field has the focus) and a context menu, which takes the keyboard focus (arrows, Enter and Escape work in it) without the page losing its own. Tab and Shift+Tab move through the document's
focusable parts and then on to the next Avalonia control, so there is no keyboard trap.

## Accessibility

The control's automation peer is a document whose children are the page's accessibility tree: roles, names, heading levels, landmarks, bounds that
follow scroll and zoom, and invoke, toggle, value, range and scroll actions. Changes raise the matching structure and property events. See
[Accessibility](accessibility.md).

## Fonts

Documents see the fonts installed on the machine and any added to `HtmlViewFonts.Shared` (or to the `FontSet` of one control) before they load.

## Limits

* Avalonia 12 and its Skia renderer only.
* The document is one continuous surface, not paginated.
* Avalonia has no accessibility text pattern, grid or table pattern, focus-changed event or live-region notification: live regions are announced as a change of the region's name, and tables expose table, header and cell peers only.
* There is no system caret blink time to read, so the caret blinks every 530 ms.
* A cursor image uses its first frame; SVG cursor images are not supported.
* Color controls open sliders and a swatch, not a full color picker.
* No find UI is built in (`HtmlDocument.FindText` exists).

## The demo

`src/examples/PeachBrackets.Avalonia.Example` is a small desktop app with an `HtmlView`, an Open button and a status line. Run it with
`dotnet run --project src/examples/PeachBrackets.Avalonia.Example`, or with `-- --smoke <file>` to load a page, print what the document reports and exit.
