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
| `PageBackground` | The colour behind the page. While it is opaque (the default) the view's surface has no alpha channel, which lets `Lcd` text blend against what is behind it. |
| `TextRendering` | How text is drawn; see [`PeachDrawing.Win2D`](peachdrawing-win2d.md#text-rendering-peachdrawing-engine). `Auto` (the default) gives body text hinting, the weight Windows gives it and, when ClearType is on, its colour stripes; large text stays crisp vector text. |
| `Input` | The pointing devices the `hover` / `pointer` media queries report. By default the control asks the system which are attached. |
| `Document` | The loaded `HtmlDocument` (replaced whenever the view reloads), for anything the control does not wrap: find, element bounds, annotations. |
| `SelectedText`, `CopySelection()`, `SelectAll()` | Selection and the clipboard. Ctrl+C and Ctrl+A work when the control has focus. |
| `ScrollToElement(id)` | Scrolls an element to the top. |
| `DocumentLoaded`, `DocumentFailed` | A document has laid out, or could not be loaded. |
| `LinkClicked` | Raised before a link is followed; set `Cancel` to handle it yourself. |

## What it does with the document's interaction styles

| The document says | The control |
|---|---|
| `cursor` | Sets the pointer over the element under the mouse: the keyword, mapped to the nearest system pointer (see the table in [PeachDrawing.Win2D](peachdrawing-win2d.md#cursors)). A custom `url()` cursor image is shown as the pointer itself: the first image that loads (a `data:`, `http`, `https` or `file` address, up to 2 MB, scaled down to 128 pixels a side) becomes a native cursor with the hotspot the style or a `.cur` file gives. Until it has loaded, or when none can be used, the keyword's pointer stands in. |
| `:hover`, `:active` | Reported to the document as the pointer moves and the button goes down and up, one update at a time, so a rule may restyle or even resize an element; only the regions that changed are repainted when nothing moved. |
| `pointer-events`, `interactivity`, `inert` | Honored by every hit test: an element that does not receive pointer events lets the pointer through to what is beneath. |
| `user-select: none` | A selection does not start there, and the text is left out of the copy. |
| `touch-action` | At touch-down, the scroller is allowed to pan and to zoom only as far as the element under the finger permits (`none` stops both). |
| `@media (hover)`, `(pointer)` | Match the devices attached to the machine (or `Input`). |
| Links | A click (not a drag) on a link opens `http`, `https`, `mailto` and `tel` addresses with the system handler; a link to a place in the document scrolls to it. Other schemes are never launched. |
| Selection | Drag to select, double-click to select a word, long-press with a finger for a word. |

## Accessibility

The view is exposed to UI Automation as a **Document** whose children follow the page's [accessibility tree](accessibility.md): a paragraph,
a list item or a heading (with its `AutomationHeadingLevel`) is **Text**, a link is a **Hyperlink** with the **Invoke** pattern (invoking it
follows the link exactly as a click does, including `LinkClicked`), and controls, lists, tables, images and separators have the control type of what
they are (Button, CheckBox, RadioButton, Edit, ComboBox, Slider, ProgressBar, List, ListItem, Table, HeaderItem, Image, Separator...). Landmarks
(`main`, `nav`, `search`, forms, banners) report their `AutomationLandmarkType`, live regions their `AutomationLiveSetting`, a description is the
help text, and a disabled control is not enabled. Every node has its bounding rectangle on the screen and reports whether it is scrolled out
of view, so Narrator can read the page, jump by heading or landmark and follow links. The tree is rebuilt whenever a document loads or reflows.

A table (or `role="grid"`) has the **Grid** and **Table** patterns - its row and column counts, the cell at a place (a cell that spans reaches every place it covers), and its column and row headers - and each cell has **GridItem** and **TableItem**: its row, column and spans, the table it belongs to, and the header cells that head it, so Narrator announces "Qty, column 2, Pear" and moves between cells with its table commands.

**Keyboard**: the view is a tab stop. Tab and Shift+Tab step through the page's own tab stops - links, buttons, form controls and anything with a
`tabindex`, in the order HTML defines - and past the last (or before the first) the key moves the XAML focus on to the next control, so the view is not a
keyboard trap. Enter follows the link that has the focus. The part with the focus is `HtmlView.FocusedNode`; the page's `:focus` and `:focus-visible`
rules apply to it (the built-in style draws a dotted outline), the view scrolls it into view, and UI Automation is told so Narrator reads it. A screen
reader's `SetFocus` on a node moves the page's focus there, and `IsKeyboardFocusable` and `HasKeyboardFocus` say which nodes can take it and which has it.

`src/examples/PeachBrackets.WinUI.Example/Verify-Accessibility.ps1` starts the example and checks that tree through the UI Automation client
API (it needs a desktop session).

## Form controls

The view asks the document to draw form controls with their look (`PaintFormControls`), so a check box, radio button, text field, button, drop-down
and range are drawn from their own CSS like the rest of the page; no XAML widget is placed over it. A click on a check box or radio button toggles it
(`HtmlDocument.Activate`), and so do Space and Enter on one that has the focus. The UI Automation peer of a check box, radio button or switch supports
the Toggle pattern, which does the same and reports the state.

A click on a text field gives it the focus and puts the caret where the click was; the field shows its selection, the text an input method is composing
and a blinking caret (at the system's blink rate). Typed characters go in at the caret, and the keys of a text box work: Left, Right, Home and End (with
Shift to select, with Ctrl to move by word), Backspace and Delete (with Ctrl by word), Ctrl+A, Ctrl+C, Ctrl+X and Ctrl+V (a paste into a one-line field
turns line breaks into spaces, and a password is never copied). A text field also has the Value pattern, so Narrator reads its text and a screen reader
can set it. The view asks Windows for a text service context (`CoreTextEditContext`) so an input method, handwriting, dictation and the emoji panel can
put text in a field and place their windows beside the caret; where the window does not offer one, typed characters arrive as the view's own
character events and everything but composition still works. Picking from a drop-down and moving a range are not handled yet.

## Limits of this version

- Form controls are painted but not interactive: they can be focused with Tab, but nothing can be typed into them or clicked.
- Zoom scales the rendered surface; it is not re-rendered at the new scale, so very high zoom is soft.
- A `url()` cursor image uses its first frame, and an SVG image is not used (the keyword's pointer is shown instead).
- Find in the page is available through `Document` (`FindText`) but has no built-in user interface.

## Trying it

`src/examples/PeachBrackets.WinUI.Example` is a small app with an `HtmlView`. Started with `--smoke <file>` it loads a page, checks what the
view and the document report (pointers, hover restyle, `user-select`, `touch-action`), writes the result to the file and exits, which is how the
control is exercised without anyone looking at it.

The control is also tested in a real window: `src/tests/PeachBrackets.WinUI.UITests` runs one WinUI application on its own thread for the whole test
run, puts an `HtmlView` in its window and checks what it loads, lays out and reports to UI Automation (control types, heading levels, link
invocation, bounding rectangles). Run it with `dotnet test --project tests/PeachBrackets.WinUI.UITests/PeachBrackets.WinUI.UITests.csproj` on a
Windows machine with a desktop session.
