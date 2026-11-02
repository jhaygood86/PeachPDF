# PeachBrackets.Android

`PeachBrackets.Android` is a [.NET for Android](https://learn.microsoft.com/en-us/dotnet/android/) (not MAUI) `HtmlView` that shows an HTML
document laid out by [PeachBrackets](peachbrackets.md) as one scrolling, zoomable surface. It paints with
[PeachDrawing.Skia](peachdrawing-skia.md) into an `SKCanvasView`, and it does the integration an application would otherwise write itself:
it lays the document out for the view's width, paints only what is in view, turns touch into scrolling, fling and zoom, follows the
[pointer and touch behavior the document asks for](html-css-support.md#pointer-interaction), opens links, and handles selection and copy.

```bash
dotnet add package PeachBrackets.Android
```

It targets `net10.0-android` (and `net11.0-android` when built with the .NET 11 SDK), API level 24 and later, and builds with the `android`
workload (`dotnet workload install android`). Because of that it is not in `PeachPDF.slnx`: build it by project.

> **Status: pre-1.0.** The public surface is `HtmlView`, `HtmlLinkTappedEventArgs`, `HtmlTouchStartedEventArgs`, `HtmlTappedEventArgs` and
> `HtmlLoadFailedEventArgs`.

## Quick start

```csharp
var view = new HtmlView(this) { Html = "<h1>Hello</h1><p>A <a href='https://peachpdf.net/'>link</a>.</p>" };
SetContentView(view);
```

One CSS pixel is one density-independent pixel, so a page looks as it does in a mobile browser. The document is laid out again, after a
short pause, when the view is resized or the theme changes. It is laid out as one continuous page; to paginate it, use
[PeachBrackets](peachbrackets.md) directly. On Android 15 and later an activity draws edge to edge, so put the view in a container that
fits the system windows (`SetFitsSystemWindows(true)`) or apply the insets yourself.

## Members

| Member | |
|---|---|
| `Html` | What to show. |
| `PageBackground` | The colour behind the page. |
| `Input` | The pointing devices the `hover` / `pointer` media queries report. By default the view asks the system which are attached (a touch screen is coarse and cannot hover; a mouse, trackpad or stylus is fine and can). |
| `Document`, `Zoom`, `SelectedText` | The loaded `HtmlDocument` (replaced whenever the view reloads), the zoom factor, and the selected text. |
| `CopySelection()`, `SelectAll()`, `ScrollToElement(id)` | Clipboard, selection and scrolling. Ctrl+C and Ctrl+A work with a keyboard. |
| `DocumentLoaded`, `DocumentFailed` | A document has laid out, or could not be loaded. |
| `LinkTapped` | Raised before a link is followed; set `Cancel` to handle it yourself. |

## Touch integration points

Each stage of a touch is an event, so an application can follow, or take over, what the view does:

| Event | When |
|---|---|
| `TouchStarted` | A finger goes down, after the document has said what it allows there. `Allowed` (a `TouchActions`) starts as the element's `touch-action` intersected with its ancestors'; clear flags to take gestures away for this touch. `Interaction` says whether the point is inert and whether its text can be selected. |
| `Tapped` | A short touch that is not a link and not a scroll. The selection is cleared. |
| `LongPressed` | A long press, before the word under the finger is selected. |
| `LinkTapped` | A link was tapped (cancellable). Addresses open with `Intent.ActionView` for `http`, `https`, `mailto` and `tel` only; a link to a place in the document scrolls to it. |
| `ScrollChanged`, `ZoomChanged` | The view scrolled (a drag, a fling, `ScrollToElement`) or zoomed. |

The view handles the gestures themselves with Android's own detectors: a drag scrolls and a release flings (an `OverScroller`), a pinch zooms
about the fingers, and a double tap zooms in and back out. What may start is read from the document at touch-down: `touch-action: none` stops
panning and zooming for a touch that starts on that element, `pan-x` / `pan-y` / `pinch-zoom` allow only those, and a page without
`touch-action` allows all of them, including double-tap zoom (`manipulation` does not).

## What it does with the document's interaction styles

| The document says | The view |
|---|---|
| `cursor` | Under a mouse or stylus, `OnResolvePointerIcon` gives the system pointer icon nearest to the keyword. A custom `url()` image is shown as its keyword. |
| `:hover`, `:active` | Reported to the document as a mouse or stylus hovers and as a finger goes down and up (so `:active` styles a pressed element on touch), one update at a time, so a rule may restyle or even resize an element. |
| `pointer-events`, `interactivity`, `inert` | Honored by every hit test: an element that does not receive pointer events lets the touch through to what is beneath. |
| `user-select: none` | A long press does not select there, and the text is left out of the copy. |
| `@media (hover)`, `(pointer)` | Match the devices attached to the device (or `Input`). |
| Selection | A long press selects the word and shows the floating Copy / Select all menu; dragging after the long press extends the selection. |

## Accessibility

The view is an accessibility **node provider** over the page's [accessibility tree](accessibility.md): each part of the page is a virtual view
with the class of the widget it is like (`Button`, `CheckBox`, `RadioButton`, `EditText`, `Spinner`, `SeekBar`, `ProgressBar`, `ImageView`,
`ListView`, `GridView`, `TextView`), so TalkBack says what it is. Text, bounds and visibility are those on screen; headings are marked as headings
(API 28 and later); check boxes, radio buttons and switches report their checked state; a disabled control is not enabled; expandable parts report
expanded or collapsed (API 30 and later); live regions are announced; and a link or button is clickable (a link follows the link when TalkBack
activates it, raising `LinkTapped`, as a tap does). Accessibility focus scrolls the node into view, and the host node scrolls forward and
backward. The tree is rebuilt when a document loads or reflows. You can see the tree an accessibility service sees with
`adb shell uiautomator dump`.

**Keyboard**: the view takes focus, and Tab and Shift+Tab step through the page's own tab stops - links, buttons, form controls and anything with a
`tabindex`, in the order HTML defines. Past the last stop (or before the first) the key is left to Android, which moves the focus to the next view, so
the view is not a keyboard trap; coming in from another view with Tab (or Shift+Tab) lands on the first (or last) stop. Enter follows the link that has
the focus. The part with the focus is `HtmlView.FocusedNode`, and `FocusChanged` is raised when it changes; the page's `:focus` and `:focus-visible`
rules apply to it (the built-in style draws a dotted outline) and the view scrolls it into view. To an accessibility service the parts that can take
the focus are focusable, the one that has it is focused, `ACTION_FOCUS` moves the page's focus there, and `TYPE_VIEW_FOCUSED` is sent when it moves.

## Fonts

Text is drawn with the fonts of the device. The view registers Roboto as `sans-serif` and `Roboto`, Noto Serif as `serif` and the system
monospace font from `/system/fonts`, and a document's own `@font-face` fonts work as on any other target.

## Limits of this version

- Form controls are painted but not interactive.
- Selection has no drag handles; a drag after the long press extends it.
- A custom `url()` cursor is shown as its keyword.
- Zoom re-renders at the new scale, which is slow on very large pages.
- Form controls can be focused with Tab, but nothing can be typed into them.

## Trying it, on an emulator

`src/examples/PeachBrackets.Android.Example` is a small app with an `HtmlView` full screen. It logs (tag `PEACH_SMOKE`) what the document
says at a few points and every touch event, so the view can be driven from `adb` and watched with `logcat`:

```bash
dotnet build src/examples/PeachBrackets.Android.Example -c Debug      # with an SDK that has the android workload
adb install -r src/examples/PeachBrackets.Android.Example/bin/Debug/net10.0-android/net.peachpdf.brackets.example-Signed.apk
adb shell am start -n net.peachpdf.brackets.example/crc64992d5e0e71762578.MainActivity
adb shell input swipe 540 1700 540 900 300        # a scroll
adb shell input swipe 330 504 330 504 900         # a long press: selects the word under the finger
adb shell input tap 660 505                       # a tap: on the link, raises LinkTapped
adb exec-out screencap -p > screen.png
```

The window-free parts (pointer-icon mapping, device capabilities, page mapping, launchable schemes, scroll and zoom arithmetic and the
`touch-action` pan filter) are covered by `src/tests/PeachBrackets.Android.Tests`, which compiles their source into a plain .NET project, so
they run without an emulator.
