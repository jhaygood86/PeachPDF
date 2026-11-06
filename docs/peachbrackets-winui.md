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

The package depends on only the parts of the Windows App SDK it compiles against (`Microsoft.WindowsAppSDK.WinUI`, `.Foundation`,
`.InteractiveExperiences` and `.Base`), not the all-in-one `Microsoft.WindowsAppSDK` package, which also brings the AI, ML, Search and Widgets
components (over 100 MB of ONNX Runtime and DirectML). The application decides how it gets the Windows App SDK runtime: reference
`Microsoft.WindowsAppSDK` itself for the whole thing, or `Microsoft.WindowsAppSDK.Runtime` (and `.DWrite` if it uses it) for a lean package.

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
| `ScrollMode` | `Auto` (the default) makes the view a viewer that scrolls and zooms itself; `Disabled` makes it a block in other layout, as tall as its document. See [Using the view in a list](#using-the-view-in-a-list). |
| `ContentHeight`, `ContentHeightChanged` | The height of the laid-out document in device-independent pixels, and the event raised when it changes. |
| `IsLayoutSynchronous` | Says the document needs nothing asynchronous, so it is loaded and laid out in the same call that needs its size. |
| `IsZoomEnabled` | Whether Ctrl+wheel and pinch zoom the document (only a view that scrolls itself zooms). |
| `SelectionColor` | The colour behind selected text. |
| `UserStyleSheet`, `DocumentMargin` | A style sheet applied before the document's own, and the space left around the document (none by default). |
| `AnimationsEnabled` | Plays CSS animations, transitions and animated images (GIF, WebP); off by default and always off when Windows' animation effects are. See [Animations](#animations). |
| `FontSet` | The fonts the view resolves families from; the shared [`HtmlViewFonts`](#fonts) set unless you give it another. |
| `DocumentLoaded`, `DocumentFailed` | A document has laid out, or could not be loaded. |
| `LinkClicked` | Raised before a link is followed; set `Cancel` to handle it yourself. |

## Images, style sheets and fonts

A document's images, style sheets and fonts load from `data:` URIs and from files. Web addresses are not fetched unless the host sets `AllowNetworkAccess` (off by default, so a document cannot make the application call out); changing it loads the document again. `IsLayoutSynchronous` still refuses anything that would have to be fetched. The loader is `PeachBrackets.HostResourceLoader`, which a host of its own (a `RenderContext` that answers `GetResourceStream`, or `RasterRenderContext.ResourceLoader`) can use too. Its requests carry `HostResourceLoader.UserAgent`: by default the `User-Agent` Chromium sends on the same platform (`Mozilla/5.0 (<platform>; <oscpu>) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36`, with the platform and CPU values Chromium reports, such as `Windows NT 10.0; Win64; x64`, `Macintosh; Intel Mac OS X 10_15_7`, `X11; Linux x86_64` or `Linux; Android 10; K`) followed by `PeachBrackets/<version>`; set the property to send something else. A host that fetches a page itself can send the same string.

### Sharp images on dense displays and when zoomed

The view tells the document the display's scale (`XamlRoot.RasterizationScale`) as its device pixel ratio, so a `srcset` image is loaded at the density the display needs rather than the largest on offer. When the reader zooms in, and the zoom has settled, or the window moves to a display with another scale, the document chooses its images again and loads denser ones without laying out again; see [Responsive images on screen](peachbrackets.md#responsive-images-on-screen).

## One surface, not pages

The view lays its document out as `MediaType.Screen`: one surface as wide as the view, with no pages, no gap between pages and nothing cut at a page height, however tall the document is. In `Auto` mode the view tells the document where it is scrolled (`HtmlDocument.SetViewport`), so `position: fixed`, `position: sticky` and `background-attachment: fixed` follow the scroll, and it draws fixed and stuck boxes on a layer over the scroller (`DocumentPaintLayer.ViewportAttached`) so they never move with the content on the way to their place; the viewport height is the height of the view, and a change of it lays the document out again. A document with a `background-attachment: fixed` image is painted differently: the view draws the visible part of the page itself, on the non-scrolling layer, on every scroll step, and the scrolled canvas only provides the extent, so the picture never moves with the content. An auto-height view (`ScrollMode = Disabled`) has no scroller of its own: it takes the finite height its host offered when it was first measured as the viewport for `vh`, and none when it was offered an unbounded one.

## Using the view in a list

A chat message, a comment or a card is a small HTML document that sits in a list of others. Set `ScrollMode` to `Disabled` and the view stops
being a viewer and becomes a block:

```csharp
var view = new HtmlView
{
    ScrollMode = HtmlViewScrollMode.Disabled,   // as tall as the document; no scroller of its own
    IsLayoutSynchronous = true,                 // the document needs nothing asynchronous
    PageBackground = Colors.Transparent,
    Html = messageHtml,
};
```

- **Height follows the content.** The view measures to the document's height at the width it is given, so a list sizes the row without
  any code of yours. `ContentHeight` is the same number as a property, and `ContentHeightChanged` is raised when it changes. When the width
  changes the document is laid out again at once, in the same layout pass, and the height is the new one.
- **The document starts at the top-left corner.** There is no gutter, no inset and no gap between pages; the only space around the document is
  `DocumentMargin`, which is none unless you set it.
- **The wheel, touch panning and zoom are left alone.** The view has no scroller of its own, so a wheel turn over a row, or a finger that starts on
  one, scrolls the list. Pinch and Ctrl+wheel zoom are for a view that scrolls itself.
- **Everything else still works**: selection and copy, links (`LinkClicked`), the cursor, hover and the other interaction styles, and
  accessibility. A link to a place in the document asks the list to bring that place into view.

### Knowing the height without waiting

Loading and laying out a document is asynchronous in general, because a document may fetch images and style sheets. A document whose pieces are
inline (`data:` URIs), already registered ([fonts](#fonts)) or otherwise answered without waiting needs none of that, and `IsLayoutSynchronous`
says so. The view then loads and lays the document out inside the call that needs its size, so the height of a row is exact the first time
it is measured and a virtualized list does not jump when a row settles.

If the document then tries to do something that would have to wait (fetch an image over the network, load a web font by URL), loading fails
with an `InvalidOperationException` that names the resource, delivered through `DocumentFailed`; nothing blocks. The same switch exists on
[`HtmlDocumentOptions.SynchronousOnly`](peachbrackets.md#synchronous-layout) for code that drives the engine directly. Even without it the
view finishes inside the first measure whenever everything the document needs is already available; the switch is what makes that a guarantee.

### Recycling

A virtualized list reuses views, setting `Html` again on one it took from the pool. Setting `Html` (or `Source`) to a different value
discards the previous document at once, so a recycled row never shows the old content, and loads the new one the next time the view is measured
(immediately, when `IsLayoutSynchronous` is on). Setting it to the value it already has does nothing. A change of width, theme, `SelectionColor`,
`UserStyleSheet` or `DocumentMargin` loads the document again, and keeps showing the old one until the new one is ready.

### Fonts

A font added to `HtmlViewFonts` is parsed once and shared by every view and every document:

```csharp
HtmlViewFonts.AddFile(@"C:/app/fonts/Inter-Regular.ttf", new AddOptions { FamilyName = "Inter" });
```

Add the application's fonts at start-up and refer to them by family name from the HTML. A list of a hundred rows pays for each font once, and a
font added later is seen by a view the next time it loads a document. Adding the same font again with the same description does nothing. A view
that should keep its fonts apart takes a `FontSet` of its own (`HtmlView.FontSet`, set before it loads). An `@font-face` rule that names a file
still works; the font is added to the same set. Views that use the DirectWrite text engine keep their own faces.

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

## Animations

Set `AnimationsEnabled` and the view plays the document's CSS animations, transitions and animated images, stepping the document on the display's own frame clock
(`CompositionTarget.Rendering`) only while something is playing, so an idle page costs nothing. See [Animations and transitions](peachbrackets.md#animations-and-transitions)
for what plays. A box that animates `transform` or `opacity` is kept as a texture on the graphics device (a [`RetainedLayer`](peachdrawing-core.md#retained-layers) of
[`PeachDrawing.Win2D`](peachdrawing-win2d.md)) and drawn again each frame, so it follows the display's refresh rate (120 fps and more on a fast display) without painting what is inside it;
other properties that cannot change layout repaint the rectangle they cover, painted in one pass however the surface cuts it into regions. A property that moves or
resizes boxes (`width`, `margin`, `top`, `font-size`, flex and grid sizing) plays by laying the document out again on each frame and repainting where something moved, so its
cost follows the size of the document; text is positioned to an eighth of a pixel while it plays (`Win2dRenderContext.TextPositionSteps`), so text that moves each frame does not need a new bitmap on the device for every position. Nothing plays when Windows' "Animation effects" setting is off, and `prefers-reduced-motion` then reports `reduce`. Changing
`AnimationsEnabled` loads the document again.

Animations on a scroll or view timeline follow the view's own scrolling, and the view steps the document on every scroll step for them (the demo has a showcase). `AnimationEvents` reports the animation and transition events of each frame (see [Events and controlling the animations](peachbrackets.md#events-and-controlling-the-animations)), and `view.Document.GetAnimations()` lists what is running to pause, seek or cancel it.

Limits: a `background-image` GIF shows its first frame; what an animated box paints is recorded at the resolution the view draws at, so a box that scales up a lot is resampled.

The demo has a toggle for it, four screen-only showcases of keyframes, transitions, layout and 120 boxes at once, one of an animated GIF, and `--anim-bench <result file>`, which plays them
in a real window and writes the frame rate, the time between frames and what stepping the document and repainting cost (run it in Release on an otherwise idle machine).

## Accessibility

The view is exposed to UI Automation as a **Document** whose children follow the page's [accessibility tree](accessibility.md): a paragraph,
a list item or a heading (with its `AutomationHeadingLevel`) is **Text**, a link is a **Hyperlink** with the **Invoke** pattern (invoking it
follows the link exactly as a click does, including `LinkClicked`), and controls, lists, tables, images and separators have the control type of what
they are (Button, CheckBox, RadioButton, Edit, ComboBox, Slider, ProgressBar, List, ListItem, Table, HeaderItem, Image, Separator...). Landmarks
(`main`, `nav`, `search`, forms, banners) report their `AutomationLandmarkType`, live regions their `AutomationLiveSetting`, a description is the
help text, and a disabled control is not enabled. Every node has its bounding rectangle on the screen and reports whether it is scrolled out
of view, so Narrator can read the page, jump by heading or landmark and follow links. The tree is rebuilt whenever a document loads or reflows, and the view compares the new tree with the old one (`AccessibilityTreeDiff`): it tells UI Automation nothing when the tree is the same, raises a structure change when parts came or went, a property change when a check box was toggled, and a notification (`RaiseNotificationEvent`) for what a live region now says - assertive regions interrupt, polite ones wait for the reader to be idle.

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
turns line breaks into spaces, and a password is never copied). Pressing the mouse on a text field puts the caret there at once, and dragging selects the text between the press and the pointer (it does not yet scroll a long value while dragging past the edge). A text field also has the Value pattern, so Narrator reads its text and a screen reader
can set it. The view asks Windows for a text service context (`CoreTextEditContext`) so an input method, handwriting, dictation and the emoji panel can
put text in a field and place their windows beside the caret; where the window does not offer one, typed characters arrive as the view's own
character events and everything but composition still works. A progress bar, meter, slider, scroll bar or spin button has the RangeValue pattern - its current number, its least and most, and a small step (a hundredth of the extent) and a large one (a tenth) - so Narrator says "30 percent" and a slider can be set; only a slider that is not read-only or disabled accepts a new value, and it is kept within its range. The document, and each text field, has the Text pattern: a screen reader reads it by character, word, line and paragraph (a line is a block of text, since the engine lays text out into lines only to paint it), searches it, and asks where a stretch of it is on screen - the answer is the rectangle of the block it is in. The pattern reports the selection of the text field that has the focus and selects a range inside it, and the selection of the page's own text when it lies inside one block (across several blocks too). It reports the text attributes language, heading style, read-only, font name, size and weight, italic, underline and strikethrough, and the text and background colours (a block's own, taken from its style).

A text area takes Enter (a line break), Up and Down (by line, keeping the column) and Home and End (to the ends of the line); a press on any text field places the caret at once and dragging selects. A right click, or a touch hold, on a text field selects the word under it (a hold) and shows a menu of Cut, Copy, Paste and Select all; the field keeps its focus while the menu is open. A press on a range control sets it and dragging moves the thumb. A click on a drop-down opens its options as a menu, on a date field a calendar, on a time field a clock and on a colour field a colour picker, each writing the choice back to the control (a date-and-time field changes the date and keeps the time); a file field opens the system's file picker (limited to the kinds its `accept` names, one file or several as `multiple` says) and shows the names of the files chosen; their contents are read (for a submission) and held by the document. Clicking or invoking a submit button of a form raises `FormSubmitting` with the form as it would be sent (`Submission.Serialize()` makes the body); the view sends nothing itself. Enter in a one-line field submits its form as a browser does (through the default button, or when it is the only such field), a form is checked first - when a value is not valid the first such control gets the focus and `FormInvalid` is raised with the problems instead of `FormSubmitting` - an image button sends where it was pressed, and clicking or invoking a reset button puts the form's controls back. The mouse wheel over a text area that has more lines than show scrolls the text area, and at its first or last line the page scrolls. An application that changes the document itself calls `ApplyUpdate` with the result so the view repaints and a screen reader hears what a live region now says.

## Limits of this version

- Form controls are painted but not interactive: they can be focused with Tab, but nothing can be typed into them or clicked.
- Zoom scales the rendered surface; it is not re-rendered at the new scale, so very high zoom is soft.
- A `url()` cursor image uses its first frame, and an SVG image is not used (the keyword's pointer is shown instead).
- Find in the page is available through `Document` (`FindText`) but has no built-in user interface.

## The demo

`src/examples/PeachBrackets.WinUI.Example` is a showcase browser: every showcase of the PDF test harness and a set of screen-only ones (a page far taller than any page, viewport units, sticky headers, a fixed toolbar, a fixed background, responsive media queries, light and dark, hover and cursors, pointer media features, in-page links, form controls, selection, columns on a long page, chat messages) in an `HtmlView`, with a panel of the knobs a host has: the width and height of the box the widget sits in (fixed or as tall as its content, and a list mode that shows the document in each of 100 recycled rows), `ScrollMode`, `IsZoomEnabled`, `IsLayoutSynchronous`, `DocumentMargin`, `PageBackground`, `SelectionColor`, the theme, the input devices, `TextRendering`, `UserStyleSheet` and the shared font set. Every change applies live. Build and run it from `src`:

```bash
dotnet build examples/PeachBrackets.WinUI.Example/PeachBrackets.WinUI.Example.csproj -p:Platform=x64
```

The build runs the test harness once with `--html-only` to write the showcases as HTML (no PDFs) and copies them beside the program; `-p:SkipShowcaseExport=true` builds without them. `--smoke-all <file>` loads every showcase and checks that each is one surface and that the sticky and fixed ones follow the viewport.

## Trying it

`src/examples/PeachBrackets.WinUI.Example` is a small app with an `HtmlView`. Started with `--smoke <file>` it loads a page, checks what the
view and the document report (pointers, hover restyle, `user-select`, `touch-action`), writes the result to the file and exits, which is how the
control is exercised without anyone looking at it.

The control is also tested in a real window: `src/tests/PeachBrackets.WinUI.UITests` runs one WinUI application on its own thread for the whole test
run, puts an `HtmlView` in its window and checks what it loads, lays out and reports to UI Automation (control types, heading levels, link
invocation, bounding rectangles). Run it with `dotnet test --project tests/PeachBrackets.WinUI.UITests/PeachBrackets.WinUI.UITests.csproj` on a
Windows machine with a desktop session. `HtmlViewListTests` there puts a hundred views of mixed heights in a virtualized `ItemsRepeater` inside a
`ScrollViewer`, scrolls it down and back up in large steps, and checks that every row is as tall as its document, shows its own content, has been
painted when it is in view, and supports selection and links, that views are reused, and that a row has no scroller of its own.
