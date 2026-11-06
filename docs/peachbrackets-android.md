# PeachBrackets.Android

`PeachBrackets.Android` is a [.NET for Android](https://learn.microsoft.com/en-us/dotnet/android/) (not MAUI) `HtmlView` that shows an HTML
document laid out by [PeachBrackets](peachbrackets.md) as one scrolling, zoomable surface. It paints with
[PeachDrawing.Skia](peachdrawing-skia.md) onto the graphics hardware (an OpenGL surface, `SKGLSurfaceView`) or, if asked, the CPU, and it does the integration an application would otherwise write itself:
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
| `DocumentLoaded`, `DocumentFailed` | A document has laid out, or could not be loaded. Reading the fonts, parsing and laying out run on a pool thread, so a slow load never holds the main thread (Android reports an app that takes no input for five seconds as not responding); both events are raised on the main thread. |
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
backward. The tree is rebuilt when a document loads or reflows, and the view compares the new tree with the old one (`AccessibilityTreeDiff`) so a service is told only what changed: nothing when the tree is the same, the subtree when parts came or went, the text for a renamed part and the state description for a state or a number, and for a live region that changed a content-changed event from the region itself, which is what makes TalkBack read it. You can see the tree an accessibility service sees with
`adb shell uiautomator dump`.

**Touch exploration**: with TalkBack's touch exploration on, a finger moving over the page sends hover events for the part under it (the innermost node
the tree reads), so TalkBack announces what the finger is on and moves its accessibility focus there. A progress bar, meter or slider reports its
range (`RangeInfo`: least, most and current number) and, when the author gave `aria-valuetext`, its state description (API 30 and later); a slider that
can be changed also takes the set-progress action, which sets it within its range.

**Keyboard**: the view takes focus, and Tab and Shift+Tab step through the page's own tab stops - links, buttons, form controls and anything with a
`tabindex`, in the order HTML defines. Past the last stop (or before the first) the key is left to Android, which moves the focus to the next view, so
the view is not a keyboard trap; coming in from another view with Tab (or Shift+Tab) lands on the first (or last) stop. Enter follows the link that has
the focus. The part with the focus is `HtmlView.FocusedNode`, and `FocusChanged` is raised when it changes; the page's `:focus` and `:focus-visible`
rules apply to it (the built-in style draws a dotted outline) and the view scrolls it into view. To an accessibility service the parts that can take
the focus are focusable, the one that has it is focused, `ACTION_FOCUS` moves the page's focus there, and `TYPE_VIEW_FOCUSED` is sent when it moves.

A node with text can be read by character, word or paragraph: the provider reports those movement granularities, answers the service's next and
previous actions by moving from where it last stopped (a character is a whole text element, so an emoji or a letter with an accent is one step) and
sends the text-traversed event with the piece it landed on. Lines and pages are not offered: they depend on layout the node does not carry.

## Fonts

Text is drawn with the fonts of the device. The view registers Roboto as `sans-serif` and `Roboto`, Noto Serif as `serif` and the system
monospace font from `/system/fonts`, and a document's own `@font-face` fonts work as on any other target.

## Form controls

The view asks the document to draw form controls with their look (`PaintFormControls`), so a check box, radio button, text field, button, drop-down
and range are drawn from their own CSS like the rest of the page; no native widget is placed over it. A tap on a check box or radio button toggles it
(`HtmlDocument.Activate`), and so do Space and Enter on one that has the focus. An accessibility service's click action on a check box, radio button or
switch does the same and the node reports the new checked state.

A tap on a text field gives it the focus, puts the caret where the tap landed and shows the soft keyboard; the field shows its selection, the text the
keyboard is composing and a blinking caret. The view is a text editor to the system while a field has the focus: `OnCreateInputConnection` hands the
keyboard an `InputConnection` over the field (commit, composing text and its region, deleting around the caret, the selection, the text around the
caret, the action key), with the keyboard layout and action key chosen from the field's `type` (password, email, URL, telephone, number, search). A
hardware keyboard works too: arrows, Home and End (Shift selects, Ctrl moves by word), Delete and Backspace (Ctrl by word), printable keys, and
Ctrl+A, Ctrl+C, Ctrl+X and Ctrl+V (a paste into a one-line field turns line breaks into spaces, and a password is never copied). To an accessibility
service a text field is editable and takes the set-text, set-selection and paste actions. 

A text area takes the Enter key (a line break; its keyboard offers no action button), Up and Down (by line, keeping the column) and Home and End (to the ends of the line), and its pasted line breaks are kept. A long press on a text field selects the word under the finger and shows the field's menu (Cut, Copy, Paste, Select all) beside the caret; moving the finger on extends the selection from the start of that word. A touch that begins on a range control sets it and dragging moves the thumb. A tap on a drop-down opens its options as a menu, and on a date or time field a dialog (a date-and-time field asks for the date and then the time); a colour field opens a dialog of three sliders over a swatch, and a file field the system's document picker (limited to the kinds its `accept` names, several files if `multiple`), whose result returns through the activity: the application calls `HandleActivityResult` from its `OnActivityResult` and the view uses the results for `FilePickerRequestCode`. The files' contents are read and held by the document for a submission. Tapping a submit button of a form raises `FormSubmitting` with the form as it would be sent (`Submission.Serialize()` makes the body); the view sends nothing itself. Enter, and the keyboard's action key, in a one-line field submits its form as a browser does (through the default button, or when it is the only such field), a form is checked first - when a value is not valid the first such control gets the focus and `FormInvalid` is raised with the problems instead of `FormSubmitting` - an image button sends where it was tapped, and tapping a reset button puts the form's controls back. A mouse wheel over a text area that has more lines than show scrolls it. An application that changes the document itself calls `ApplyUpdate` with the result so the view repaints and an accessibility service hears what a live region now says.

## Drawing on the graphics hardware

`new HtmlView(context)` draws on an OpenGL surface; `new HtmlView(context, useGpu: false)` draws on the CPU (an `SKCanvasView`), which suits a
device or emulator without a working GL driver. On the hardware:

- **The page is kept as textures while it scrolls.** The part of the page that does not move with the scroll (the document's content layer) is
  painted into 1024 by 1024 pixel tiles at the current zoom and kept as GPU textures, up to 16 of them (about 64 MB). A scroll step draws the
  tiles it holds, and only a tile it does not hold is painted; a frame that found every tile it needed paints one tile just past the edge the scroll
  is heading for, so the frame that reaches it finds it there. What is attached to the viewport (a fixed or stuck box) is painted over the tiles on
  every frame. A change of what the page shows (a pointer state, a selection, a focus ring, a caret, a form value) paints the tiles again, and so
  does a change of zoom, which is a different grid.
- **Animations stay off the tiles.** With `AnimationsEnabled`, a box that only moves or fades is not in the tiles: each frame draws it over them as a texture
  (`DocumentPaintLayer.Animated`, see [the animated layer](peachbrackets.md#animations-and-transitions)), under the fixed and stuck boxes. What else an animation repaints
  (a colour, a shadow, a layout property) drops only the tiles that hold the changed regions; the rest stay.
- **A page whose background is attached to the viewport** (`background-attachment: fixed`) changes under every scroll step, so it is painted
  straight, on the hardware but without tiles.
- **Shadows, filters and `backdrop-filter`** are layers with effects that Skia composes itself, and an image Skia can decode is decoded into the
  texture it uploads (see [Images](peachdrawing-skia.md#images)).

## Animations

Set `AnimationsEnabled` and the view plays the document's CSS animations, transitions and animated images, stepping the document on the display's own frame clock
(`Choreographer`) only while something is playing, so an idle page costs nothing; the clock is paused when the view is detached and starts again on its own when a
control call (`HtmlDocument.GetAnimations()`), a scroll on a scroll timeline or a state change starts one. Nothing plays when the system removes animations (the animator
duration scale is 0), and `prefers-reduced-motion` then reports `reduce`. Changing `AnimationsEnabled` loads the document again. `AnimationEvents` reports the animation and
transition events of each frame. See [Animations and transitions](peachbrackets.md#animations-and-transitions); the demo's Animation showcases show them, and
`adb shell am start ... --es showcase screen_animation_keyframes` opens one.

## Tablets, laptops and resizable windows

The view lays the document out for the width it is given and follows every change of it, so a free-form window (a tablet, ChromeOS, a Googlebook) that is resized while the app
runs lays the page out again where it is. It takes a mouse, trackpad and keyboard as well as touch: hover and `:hover` rules, pointer icons for `cursor`, the wheel,
Ctrl with plus and minus to zoom, Tab to move focus, and the arrow, Page and Home/End keys to scroll. An app that wants to survive a resize without a restart declares the
configuration changes it handles (`screenSize`, `smallestScreenSize`, `screenLayout`, `orientation`, `density`, `keyboard`, `keyboardHidden`, `navigation`, `uiMode`) and is
`resizeableActivity`; one that also runs on a laptop-class device with no touch screen marks `android.hardware.touchscreen` as not required. The demo does all of this and, from
a window 840 dp wide (the Expanded window size class), puts the list of showcases in a pane beside the page.

## Limits of this version

- Form controls are painted but not interactive.
- Selection has no drag handles; a drag after the long press extends it.
- A custom `url()` cursor is shown as its keyword.
- Zoom re-renders at the new scale (the tiles are painted again at it), which is slow on very large pages.
- Form controls can be focused with Tab, but nothing can be typed into them.

## Images, style sheets and fonts

A document's images, style sheets and fonts load from `data:` URIs and from files; web addresses only when the host sets `AllowNetworkAccess` (off by default; the requests carry `HostResourceLoader.UserAgent`, Chromium's string for Android with a `PeachBrackets/<version>` token after it), before the next load.

### Sharp images on dense screens and after a pinch

The view reports the screen density (`DisplayMetrics.density`) as the document's device pixel ratio, so a `srcset` image is loaded at the density the screen needs rather than the largest on offer. After a pinch or a double tap has settled the document chooses its images again for the new zoom and loads denser ones, without laying out again, and the tiles are painted again; see [Responsive images on screen](peachbrackets.md#responsive-images-on-screen).

## The demo

`src/examples/PeachBrackets.Android.Example` is a showcase browser: every showcase of the PDF test harness and the screen-only ones (see the [WinUI demo](peachbrackets-winui.md#the-demo)) in an `HtmlView`, with a panel of the knobs the view has: `AnimationsEnabled`, the width and height of the box it sits in, `PageBackground`, the input devices and the theme (Android 12 and later). The view lays its document out as one continuous surface and tells it where it is scrolled and zoomed, so fixed and sticky boxes follow the finger. The build runs the harness with `--html-only` and packs the documents into the APK as assets.

## Trying it, on an emulator

`src/examples/PeachBrackets.Android.Example` is a small app with an `HtmlView` full screen. It logs (tag `PEACH_SMOKE`) what the document
says at a few points and every touch event, so the view can be driven from `adb` and watched with `logcat`:

```bash
dotnet build src/examples/PeachBrackets.Android.Example -c Debug      # with an SDK that has the android workload
adb install -r src/examples/PeachBrackets.Android.Example/bin/Debug/net10.0-android/net.peachpdf.brackets.example-Signed.apk
adb shell am start -n net.peachpdf.brackets.example/crc64992d5e0e71762578.MainActivity
adb shell am start -n net.peachpdf.brackets.example/crc64992d5e0e71762578.MainActivity --ez cpu true   # the same on the CPU, to compare
adb shell input swipe 540 1700 540 900 300        # a scroll
adb shell input swipe 330 504 330 504 900         # a long press: selects the word under the finger
adb shell input tap 660 505                       # a tap: on the link, raises LinkTapped
adb exec-out screencap -p > screen.png
```

The window-free parts (pointer-icon mapping, device capabilities, page mapping, launchable schemes, scroll and zoom arithmetic and the
`touch-action` pan filter) are covered by `src/tests/PeachBrackets.Android.Tests`, which compiles their source into a plain .NET project, so
they run without an emulator.
