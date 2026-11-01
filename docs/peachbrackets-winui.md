# PeachBrackets.WinUI

`PeachBrackets.WinUI` is a WinUI 3 control, `HtmlView`, that shows an HTML document laid out by [PeachBrackets](peachbrackets.md) as one
scrolling, zoomable surface. It paints with [PeachDrawing.Win2D](peachdrawing-win2d.md), and it does the integration an application would
otherwise write itself: it lays the document out for the control's width, paints only what is in view, follows the
[pointer and touch behavior the document asks for](html-css-support.md#pointer-interaction), opens links, and handles selection and copy.

```bash
dotnet add package PeachBrackets.WinUI
```

It targets `net10.0-windows10.0.19041.0` (and `net11.0-windows10.0.19041.0` when built with the .NET 11 SDK) and uses the Windows App SDK.
The application supplies the Win2D native binary by building for a platform (`x64`, `ARM64`).

> **Status: pre-1.0.** The public surface is `HtmlView`, `HtmlLinkClickedEventArgs` and `HtmlLoadFailedEventArgs`; `PublicApi.txt` beside the
> project is the reviewed snapshot.

## Quick start

```xml
<Page xmlns:peach="using:PeachBrackets.WinUI">
    <peach:HtmlView x:Name="Viewer" />
</Page>
```

```csharp
Viewer.Html = "<h1>Hello</h1><p>A <a href='https://peachpdf.net/'>link</a>.</p>";
// or: Viewer.Source = new Uri("https://example.com/"); a file: URI loads from disk
```

One CSS pixel is one device-independent pixel, so a page looks as it does in a browser. The document is laid out again, after a short pause,
when the control is resized, so it reflows the way a web page does. It is laid out as one continuous page; to paginate it, use
[PeachBrackets](peachbrackets.md) directly.

## Members

| Member | |
|---|---|
| `Html`, `Source` | What to show (`Html` wins). Both are dependency properties, so they bind. |
| `PageBackground` | The colour behind the page. |
| `Input` | The pointing devices the `hover` / `pointer` media queries report. By default the control asks the system which are attached. |
| `Document` | The loaded `HtmlDocument` (replaced whenever the view reloads), for anything the control does not wrap: find, element bounds, annotations. |
| `SelectedText`, `CopySelection()`, `SelectAll()` | Selection and the clipboard. Ctrl+C and Ctrl+A work when the control has focus. |
| `ScrollToElement(id)` | Scrolls an element to the top. |
| `DocumentLoaded`, `DocumentFailed` | A document has laid out, or could not be loaded. |
| `LinkClicked` | Raised before a link is followed; set `Cancel` to handle it yourself. |

## What it does with the document's interaction styles

| The document says | The control |
|---|---|
| `cursor` | Sets the pointer over the element under the mouse: the keyword, mapped to the nearest system pointer (see the table in [PeachDrawing.Win2D](peachdrawing-win2d.md#cursors)). A custom `url()` cursor image is shown as its keyword, since WinUI offers only system pointers. |
| `:hover`, `:active` | Reported to the document as the pointer moves and the button goes down and up, one update at a time, so a rule may restyle or even resize an element; only the regions that changed are repainted when nothing moved. |
| `pointer-events`, `interactivity`, `inert` | Honored by every hit test: an element that does not receive pointer events lets the pointer through to what is beneath. |
| `user-select: none` | A selection does not start there, and the text is left out of the copy. |
| `touch-action` | At touch-down, the scroller is allowed to pan and to zoom only as far as the element under the finger permits (`none` stops both). |
| `@media (hover)`, `(pointer)` | Match the devices attached to the machine (or `Input`). |
| Links | A click (not a drag) on a link opens `http`, `https`, `mailto` and `tel` addresses with the system handler; a link to a place in the document scrolls to it. Other schemes are never launched. |
| Selection | Drag to select, double-click to select a word, long-press with a finger for a word. |

## Accessibility

The view is exposed to UI Automation as a **Document** whose children are the page's blocks of text - paragraphs and list items as **Text**,
headings as **Text** with a heading level (`AutomationHeadingLevel`), and each link, below the block that holds it, as a **Hyperlink** with the
**Invoke** pattern (invoking it follows the link exactly as a click does, including `LinkClicked`). Every node has its bounding rectangle on the
screen and reports whether it is scrolled out of view, so Narrator can read the page, jump by heading and follow links. The tree is rebuilt
whenever a document loads or reflows. Reading comes from `HtmlDocument.GetAccessibleNodes()`, which any other host can use too.

`src/examples/PeachBrackets.WinUI.Example/Verify-Accessibility.ps1` starts the example and checks that tree through the UI Automation client
API (it needs a desktop session).

## Limits of this version

- Form controls are painted but not interactive, and there is no keyboard navigation between links or controls.
- Zoom scales the rendered surface; it is not re-rendered at the new scale, so very high zoom is soft.
- A custom `url()` cursor is shown as its keyword.
- Find in the page is available through `Document` (`FindText`) but has no built-in user interface.

## Trying it

`src/examples/PeachBrackets.WinUI.Example` is a small app with an `HtmlView`. Started with `--smoke <file>` it loads a page, checks what the
view and the document report (pointers, hover restyle, `user-select`, `touch-action`), writes the result to the file and exits, which is how the
control is exercised without anyone looking at it.
