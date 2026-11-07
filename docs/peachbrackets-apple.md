# PeachBrackets.Apple

`PeachBrackets.Apple` is an `HtmlView` for macOS (AppKit) and iOS (UIKit) apps that shows an HTML document laid out by
[PeachBrackets](peachbrackets.md) as one scrolling, zoomable surface. It paints with [PeachDrawing.CoreGraphics](peachdrawing-coregraphics.md) and does the
integration an application would otherwise write itself: it lays the document out for the view's width, paints only what is in view, turns the mouse,
trackpad, touch or pointer into scrolling and zoom, follows the [pointer and touch behavior the document asks for](html-css-support.md#pointer-interaction),
opens links, and handles selection and copy.

```bash
dotnet add package PeachBrackets.Apple
```

It targets `net10.0-macos` (macOS 11 and later) and `net10.0-ios` (iOS 15 and later; the edit menu after a long press needs iOS 16) (and the `net11.0` ones when built with the .NET 11 SDK) and builds with the `macos` and `ios` workloads
(`dotnet workload install macos ios`) on a Mac. Because of that it is not in `PeachPDF.slnx`: build it by project. On macOS `HtmlView` is an `NSScrollView`;
on iOS it is a `UIScrollView`. The two have the same members where the platforms allow, and differ where they do not (below).

> **Status: pre-1.0.** The public surface is `HtmlView` and its event argument types: `HtmlLinkActivatedEventArgs`, `HtmlLoadFailedEventArgs`,
> `HtmlTappedEventArgs`, `HtmlTouchStartedEventArgs` (iOS), `HtmlFormSubmittingEventArgs`, `HtmlFormInvalidEventArgs` and `HtmlAnimationEventsEventArgs`;
> `PublicApi.ios.txt` and `PublicApi.macos.txt` beside the project are the reviewed snapshots, one per platform because the two views differ. See
> [Limits](#limits).

## Quick start

```csharp
// macOS
var view = new HtmlView { Html = "<h1>Hello</h1><p>A <a href='https://peachpdf.net/'>link</a>.</p>" };
window.ContentView = view;

// iOS
var view = new HtmlView { Html = "<h1>Hello</h1>", Frame = View.Bounds };
View.AddSubview(view);
```

One CSS pixel is one point, so a page looks as it does in a browser. The document is laid out again, after a short pause, when the view is resized or the
appearance (light or dark) changes. It is laid out as one continuous page; to paginate it, use [PeachBrackets](peachbrackets.md) directly.

## Members

| Member | |
|---|---|
| `Html` | What to show. |
| `PageBackground`, `AllowNetworkAccess`, `UseGpu` | The color behind the page; whether `http`/`https` resources may be loaded (off by default); whether effects run on the GPU. |
| `DocumentBaseUri` | The address the document's relative references (style sheets, images, fonts) resolve against. Set it, with `AllowNetworkAccess`, when the HTML is a page you fetched yourself: a `<base href>` in the page does not reach its style sheets, which load while it is still being parsed. |
| `AnimationsEnabled`, `AnimationEvents` | Whether the document plays its CSS animations, transitions and animated images (off by default; takes effect on the next load), and an event with the `animationstart`/`transitionend` family as they happen, on the main thread. The document is stepped on the display's clock (a display link on iOS, a 60 Hz timer in the common run-loop modes on macOS) only while something runs, and only the tiles a frame touches are painted again. Nothing plays, and `prefers-reduced-motion` reports it, when the system asks for reduced motion. A property that moves or resizes boxes lays the document out again each frame, so its cost follows the size of the document. |
| `Input` | The pointing devices the `hover` and `pointer` media queries report. By default a Mac reports a mouse or trackpad (fine, can hover) and an iPhone or iPad reports touch (coarse, cannot hover); an iPad with a pointer attached reports both and follows the one used last. |
| `Document`, `Zoom`, `SelectedText` | The loaded `HtmlDocument` (replaced whenever the view reloads), the zoom factor, and the selected text. |
| `CopySelection()`, `SelectAll()`, `ScrollToElement(id)`, `ApplyUpdate(result)` | Pasteboard, selection, scrolling, and taking in a change the document made to itself. Cmd+C and Cmd+A work on a Mac and with a hardware keyboard on iOS. |
| `DocumentLoaded`, `DocumentFailed` | A document has laid out, or could not be loaded. Reading fonts, parsing and layout run on a pool thread, so a slow load never holds the main thread; both events are raised on it. |
| `FormSubmitting`, `FormInvalid` | A form was submitted (a submit button, or Return in a one-line field) with the values it holds; or a submit was tried and a control's value does not satisfy its constraints, and the first such control has the focus. The view sends nothing itself. |
| `FocusedNode`, `FocusChanged` | The part of the page with the keyboard focus (a link, a button, a control, anything with a `tabindex`), and when it changes. Tab and Shift+Tab step through the tab stops. |
| `LinkActivated` | A link is about to be followed; set `Cancel` to handle it yourself. Only `http`, `https`, `mailto` and `tel` addresses are handed to the system. |
| `ScrollChanged`, `ZoomChanged` | The scroll position changed; a pinch ended and the page was painted again at its new zoom. |
| macOS: `Clicked` | A click that was not on a link. |
| iOS: `Tapped`, `LongPressed`, `TouchStarted` | A tap that was not on a link; a long press, before the word under the finger is selected; a touch began, after the document said which gestures it allows there (`touch-action`). The application can only take gestures away. |

## Painting and the graphics hardware

The page is painted into 512-pixel tiles, and each tile is a Core Animation layer, so scrolling, fling and pinch are composited by the graphics hardware
and never repaint on the CPU: only a tile that is newly in reach, or that something changed under, is painted. The tiles reach a quarter of a screen past
what shows, so the one a scroll is about to reveal is already there. What is attached to the viewport (a fixed or sticky box, a background with
`background-attachment: fixed`) is painted into one layer above the tiles as the view scrolls; a page whose background is attached to the viewport is
painted straight, since it changes under every scroll step. Effects (blur, shadow, filters, masks) run in Core Image on Metal when the machine has it, and
images decode with the system's codecs when it does; `UseGpu = false` keeps all of it on the CPU.

## Forms and text input

A click or tap on a control does what it does in a browser: a check box toggles, a radio button checks, a reset button resets its form, a submit button raises
`FormSubmitting` (or `FormInvalid`), and a drop-down shows its options (a menu on a Mac, an action sheet or popover on iOS). A text field takes the focus with the
caret where the point was, and the keyboard appears on iOS. In a field the keys do what a text field's keys do: the arrows, Home and End, word moves, Delete,
Select All, Cut, Copy and Paste, and Return submits a one-line field's form or breaks a line in a text area.

Date, time, date-and-time, color and file controls open the platform's own picker, and a range control follows a tap: on iOS a sheet with a `UIDatePicker`, the
system color picker and the document picker (the chosen files are read at once and handed to the control); on a Mac a small alert with a date field, the shared
color panel (the control follows it as the color is chosen) and an open panel. A range control follows a drag of its thumb (the page does not scroll while it does), and a drag that begins in a text field extends that field's selection.

While the on-screen keyboard is up on iOS the view adds room under the page for it, so the page can scroll up out of its way, and the field being edited is scrolled into view.

## Accessibility

The view builds [the document's accessibility tree](accessibility.md) and gives VoiceOver the parts of the page it stops on, in reading order: headings,
links, controls, images and text. On iOS they are `UIAccessibilityElement`s of the page view (traits for button, link, heading, image, adjustable, selected and
disabled; the value of a slider); on macOS they are `NSAccessibilityElement`s (`AXHeading`, `AXLink`, `AXButton`, `AXTextField`...). Activating a link
(VO-Space, a double tap) follows it the way a click or a tap does, through `LinkActivated`. When the document changes, the elements are taken again and a live
region's new text is announced. Containers (a section, a list, a table) only group, so they are not stops of their own; table, set and range semantics are not
exposed yet.

Images in the document are chosen for how large they are shown: after a zoom has settled the view asks for denser `srcset` and `image-set()` candidates and
paints again with what loads.

## What it does with the document's interaction styles

| The document says | macOS | iOS |
|---|---|---|
| `cursor` | The matching `NSCursor`; `cursor: none` hides it. Shapes the system has no cursor for (wait, help, zoom, diagonal resize) get the nearest one. | A pointing device shows an I-beam over text and hides for `cursor: none`; a finger has no cursor. |
| `:hover` | Follows the mouse. | Follows an attached pointing device. |
| `:active` | While the button is down. | While a finger is down. |
| `touch-action` | Not applicable to a mouse. | Scrolling and pinch are switched off for a touch where the document does not allow them. |
| `user-select: none`, `pointer-events: none` | Text is not selected there and the pointer passes through. | The same. |

## Limits

- Text input on iOS goes through `UIKeyInput`: ordinary typing, hardware keyboards and dictation work, but an input method that composes text in place (marked text, as for Chinese or Japanese) commits it only when the composition ends. macOS uses `NSTextInputClient` with marked text.
- Taking the view out of its window discards the loaded document; it loads again from `Html` when the view returns, so form values, the selection and the zoom are not kept.
- A custom `url()` cursor is shown as its keyword.
- Selection has no drag handles: on iOS a drag after a long press extends it.
- A Mac with a touch display is reported as having a fine pointer and hover only; direct touch on a Mac is not handled separately.
- Animated `transform` and `opacity` are not kept as textures on the graphics device as they are on Windows: a frame paints the tiles it touches again, so a large animated area costs more than there.
- Zooming paints the page again at the new scale, which is slow on very large pages.

## The demo

Both demos show the same documents as the Windows demo: a sample page with a control of every kind, the screen-only showcases, every showcase of the PDF test harness
(written as HTML when the demo is built, so it keeps no copy), and three pages fetched live from the web (the build needs no network; opening one does).

`src/examples/PeachBrackets.Apple.macOS.Example` is laid out like the Windows demo: a list of showcases with a category filter on the left, the selected one in a view inside
the box a host would give it in the middle (with its title and description), the knobs the view offers a host on the right (the box's width and height, `PageBackground`,
`AllowNetworkAccess`, the appearance, `Input`, `UseGpu`), and a status bar. `src/examples/PeachBrackets.Apple.iOS.Example` is a split view: the list (grouped by category, with
search) beside the showcase on an iPad, and a navigation stack on an iPhone, with the same knobs in a sheet behind the sliders button. Both are built from the project, not the
solution; set `SkipShowcaseExport=true` to build without running the harness.

## Trying it

```bash
src/examples/PeachBrackets.Apple.macOS.Example/run.sh                      # opens the window
src/examples/PeachBrackets.Apple.macOS.Example/run.sh --smoke report.txt   # checks what the document says, writes OK or FAILED, exits 0 or 1
src/examples/PeachBrackets.Apple.macOS.Example/run.sh --showcase screen_sticky_header --scroll 600 --screenshot out.png   # one showcase, scrolled, as a picture
src/examples/PeachBrackets.Apple.macOS.Example/run.sh --scroll-bench r.txt # scrolls a long page and reports the main-thread cost of each step (add --cpu to compare)
.github/scripts/ios-smoke.sh                                               # builds the iOS example, runs it on a simulator and checks its log
```
