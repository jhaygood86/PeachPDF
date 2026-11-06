# PeachBrackets.Apple: the macOS and iOS HtmlView

**Idea.** One engine (`HtmlViewEngine`) shared by an `NSScrollView` and a `UIScrollView` view. The system scroll view owns scrolling, momentum and pinch;
the page is painted into 1024 px tiles, **each tile a `CALayer` whose contents is a `CGImage`**, so a scroll or a pinch moves and scales layers on the GPU and
never repaints. A tile is made only when it is not held (the visible region plus a quarter screen each way); a repaint keeps the old tiles until the new ones are
in (no flash); what is attached to the viewport is one overlay layer painted per scroll step. A pinch lets Core Animation scale the layers while it lasts and
paints again at the new zoom when it ends.

**Found by running it.**
- The macOS view is an `NSScrollView` whose document view is a **flipped** `NSView`; tiles are drawn with a Y-down bitmap context and `originAtBottomLeft: false`.
- `NSScrollView.ContentView` names the clip view, so a custom page view must not be called `ContentView` (it hides the member).
- iOS 26+ needs the scene manifest in `Info.plist` and a scene delegate, as in the CoreGraphics iOS example. The first scroll event reports `y=-98` because the
  example sets a content inset for the status bar; that is the inset, not a defect.
- `UIEditMenuConfiguration.Create(null, point)` is the factory (not `CreateConfiguration`); `UIPointerShape.CreateBeam`, `UIPointerStyle.CreateHiddenPointerStyle`.

**Verified.** macOS example `--smoke`: OK (pages, link-cursor Pointer, card-cursor Crosshair, locked-selectable False, pinned-touch None) and a screenshot of the
rendered page; iOS example on an iPhone 17 simulator (iOS 27): RESULT ALL PASSED, main-thread stall 72 ms, scripted scroll reported, screenshot shows the
scrolled page; 114 logic/public-surface tests pass.

**Accessibility and images (added after the first cut).** The engine takes `GetAccessibilityTree()` after each load and update and hands the host the *leaf* parts
(`AppleAccessibility.IsElement`: containers only group); iOS makes `UIAccessibilityElement`s the page view exports through `accessibilityElements`, macOS makes
`NSAccessibilityElement`s as the page view's `AccessibilityChildren`. Trap found by running it: the engine raised `DocumentLoaded` *before* it built the elements, so
a handler saw none on iOS (macOS only passed because its smoke waits 400 ms) - the tree is now built first. Another: on iOS `Subviews[0]` of the scroll view is not the
page view (it may be a scroll indicator), so the example finds it by type. Both smokes now assert a heading, a link and at least four parts. Activation (VO-Space)
and live-region announcements are written but **not exercised** by any test; the macOS element frames rely on the flipped parent and were not checked with VoiceOver.

**Forms (added after accessibility).** `HtmlViewEngine.Forms.cs` holds control taps, submit/reset, Tab focus and the text-field editing (caret, selection,
composition, caret blink) over `HtmlDocument`'s edit APIs, the way Android's view does; the views supply the platform's key and menu plumbing (macOS: the page view
is an `NSTextInputClient` and maps `doCommandBySelector:`; iOS: `UIKeyInput` plus `UIKeyCommand`s for Tab/Return/arrows, and `InputView` returns a zero-size view
unless a field is being edited so a hardware-keyboard user never gets a soft keyboard). **Verified by driving real input:** the macOS smoke clicks a check box,
clicks a field and types through the window's event path, then clicks submit (checkbox toggled, text `hi`, `FormSubmitting` raised, a focused node); on the iOS
simulator taps toggled the check box, typed `hello`-style text with the keyboard and a caret, and chose `Green` in the drop-down action sheet. Traps: a click on a
window that is not key was swallowed as the activating click until the page view returned true from `AcceptsFirstMouse`; and the iOS check of `<select>` right after
a field tap fails only because the tap lands while the previous popover is still animating away (a test-timing effect, not a view defect).

**Pickers (added after forms).** The engine routes by `PickerValues.KindOf` and asks the host: `PickValue(kind, current, anchor, chosen)` (date, time, datetime,
colour) and `PickFiles(multiple, mimeTypes, extensions, chosen)`; a tap on a range calls `SetRangeAtPoint`. **Verified on the iOS simulator by tapping:** the date sheet
opened on 15 January 2026, choosing the 20th and Done set the field to `2026-01-20`; the system colour picker opened on the field's `#3366aa` and choosing red turned the
swatch red; the document picker opened; a tap on the range moved its thumb. **macOS pickers compile but were not exercised**: they are modal (`NSAlert.RunModal`,
`NSOpenPanel.RunModal`) so the smoke run cannot click through them. Test trap: after a swipe the scroll view settles under the finger for about a second, so a tap at
a coordinate read from a screenshot lands on the control that moved there (it opened the file picker instead of the date sheet); the example now scrolls back to the top
after its scripted scroll, and taps should follow a pause.

**Drags and the keyboard (added after pickers).** `HtmlViewEngine.BeginDrag/Drag/EndDrag` give a press that begins on a range control or in a text field to the control
(the thumb follows; the selection extends from the anchor the press set). iOS: the touch gate now stays alive for the whole touch (it used to fail itself in
`TouchesBegan`, which stops UIKit sending it moves) and turns scrolling off while a control owns the touch. A text field not yet focused takes the focus on the tap and
anchors there; one already focused anchors at the press. iOS also listens for `UIKeyboard` frame changes and adds the covered height to the bottom content inset (and
reveals the focused field); the example must not reset `ContentInset` on every layout or it undoes that. **Verified:** macOS smoke with real mouse-drag events (field
selection extended, range reached 10); iOS simulator: swiping along the volume slider moved the thumb to the end without scrolling the page, and with the keyboard up the
page scrolled past its normal limit.

**Review pass (a fresh reader of the diff, against Android).** Real defects found and fixed: (1) `_loadedWidth` was set only after a load succeeded, so a failed or slow
first load made every layout pass queue another reload (it is now recorded when the load starts); (2) a hover or press rebuilt the whole accessibility tree and posted a
layout notification for every mouse move (the pump now repaints only); (3) every change repainted all visible tiles synchronously and an edit asked several times
(`Invalidate` now coalesces to one update a run-loop turn); (4) a full-viewport overlay bitmap was rendered on every scroll step (it is repainted only when the
viewport-attached content, size, zoom or document changed, otherwise just moved - and `HtmlDocument.ViewportLayersChanged` was being ignored, so a sticky box that began to
stick left a stale tile; it now invalidates the tiles); (5) focus, caret and press state of the old document survived a reflow or detach (`ResetInteractionState`);
(6, 8) async work and picker callbacks could run on a disposed document (queued work and the pointer pump now hold the document and context until they finish, and the
callbacks re-check identity); (7) a click or tap on anything but a control never took the focus out of a field, and iOS only became first responder while editing (so
Tab/Return never worked outside a field) - fixed, and the view now stays first responder after a field blurs; (9) `UIEditMenuInteraction` is iOS 16 and was created
unguarded (guarded, and the package now declares iOS 15 / macOS 11 as its minimum, which made the compiler enforce it); (10) the macOS colour-panel observer on the shared
panel outlived the picker; (11) page views, touch gates and accessibility elements held strong references to the view they are native-owned by (now weak); (12) observers
were added twice if a view moved between windows without passing through none; (13) macOS marked text ignored the selected range and `replacementRange` (press-and-hold
accents), and iOS had no keyboard traits (password echo, email/number keyboards, Return label - now per field type, shown by the blue Done key in the simulator);
(14) Cut with only a caret deleted a character; (15) the publish job built without `ValidateXcodeVersion=false` or running the tests, the CI path filter missed what the
view builds on, and `ios-smoke.sh` could pick a stale bundle, an old runtime, or wait out its timeout after a failed launch. **Deliberately not done:** repainting only the
tiles a dirty region touches (invalidation is coalesced but still repaints every visible tile - a measured follow-up, not a defect); keeping a document across a window
removal (the view reloads from `Html`, so form values, selection and zoom are lost - documented); `UITextInput` marked text on iOS. **Verified after the fixes:** 139 tests,
macOS smoke (forms, field drag, range drag, accessibility), iOS smoke, and on the simulator typing, the Done key, and tapping elsewhere dismissing the keyboard.

**Scroll cost, measured (macOS example `--scroll-bench <report> [--cpu]`: 400 steps of 60 pt down the 400-section `screen_long_page`, main-thread ms per step).** Scrolling over
tiles already painted is cheap (median 0.1 ms: layers move, nothing repaints) - that part of the design held. The cost is new tiles: **a tile is painted synchronously on the
main thread and `PaintPageRegionAsync` has a fixed cost of about 8 ms per call plus about 0.02 ms per thousand pixels** (256x256: ~9 ms, 900x1024: ~29 ms - measured by timing the
call, not guessed; alloc/fill and `ToImage` are under 1 ms). With 1024 px tiles 24 of 400 steps cost 30-64 ms (dropped frames); GPU and CPU modes were identical because the page
has no effects. Fixes: 512 px tiles (~14 ms each, one frame), a per-update paint budget (visible tiles first, the rest on later run-loop turns), and invalidation by dirty
region so an edit, a caret blink or a hover repaints the one or two tiles it touches instead of every visible tile. Result: 2 dropped frames, p95 12.5 ms, max 30 ms.
**Open:** the fixed per-call cost is in the engine's region paint (probably a walk that is not culled to the region) and would help every host (Android, WinUI) if cut; and
tiles are still painted on the main thread - painting them on a worker needs the document's paint to be safe alongside the pointer pump's restyles, which was not established.

**Hardware-keyboard ordering on iOS.** A UIKeyCommand is delivered at once, while text arrives through `UIKeyInput.insertText` a few ms later, so a Space command pressed after
"ab" inserted its space *before* them (the field showed "helloworld" shifted right, the space leading). While a field is edited, Space and Return are therefore not key commands
(they come as text; Return as "\n", handled in `InsertText`), and the arrows run after the queued text. The simulator tool injects keys faster than a person can type, so an
arrow sent right after a burst of text can still overtake the end of it there; the engine's own log showed every received character applied in order.

**Sticky and fixed boxes were invisible (found by the user in the demo, `screen_sticky_header` and `screen_anchors`).** The viewport-attached layer was painted with
`PaintPageRegionAsync(page, canvas, region, ViewportAttached)`, and the region is a rectangle around what is in view. A stuck box is *painted* where the viewport is, but its
*layout* position is still where it would be if it scrolled with the page, so at scroll 600 a region around the visible part never contained the header's layout position and
culled it: the overlay was a blank bitmap (dumped to a PNG to see it) while the document reported 10 dirty regions and `ViewportLayersChanged` - so everything upstream was
right and the paint call was the one that returned nothing. `PeachBrackets.WinUI` paints that layer with `PaintPageAsync` (the whole page) for exactly this reason; the Apple
engine now does too. The content layer keeps the region. **`PeachBrackets.Android` paints the attached layer by region as well (`HtmlView.Painting.cs`, `PaintWindow`), so a
sticky header there is probably invisible in the same way; not changed or verified, no emulator here.** The macOS example now has `--showcase <slug> --scroll <pt> --screenshot
<png>`, which is how this was reproduced and checked.

**Not done / not verified.** Form controls, pickers, text input (IME), keyboard focus, and direct touch
on a Mac with a touch display are not implemented (see the accepted-gap file). The GPU-vs-CPU scroll benchmark was not run; the claim that scrolling does not
repaint rests on the layer design, not on a measurement. The iOS no-Metal path and a physical device are untested.
