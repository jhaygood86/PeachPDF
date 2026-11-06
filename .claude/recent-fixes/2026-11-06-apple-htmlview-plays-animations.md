# The Apple HtmlView plays animations

The WinUI view got `AnimationsEnabled` with the engine's animation clock (`HtmlDocument.AdvanceAnimationsAsync`); the Apple views now host it too, through `HtmlViewEngine.Animation.cs` so both share one loop.

- **The loop** starts when a document that `HasRunningAnimations` is adopted, or when `AnimationsWoke` fires (the control API restarted a clock, possibly from another thread, so it hops to the main one), and stops when a frame says `!IsRunning`. The host supplies the clock: `CADisplayLink` in the common modes on iOS; on macOS a 60 Hz `NSTimer` in the common modes, since a view cannot get the display's refresh before macOS 14. Both pass `CACurrentMediaTime`, and the time stepped to leaves out the idle time between a stop and a restart, as WinUI does.
- **A frame and a hover must not overlap.** A step that changes layout is async and a pointer-state change restyles the same document, so `_stepping` makes the pointer pump wait and the frame handler skip; the finished step starts the pump again if a pointer change queued meanwhile.
- **A layout step can change the content size**, which hover restyles never did, so the hosts got `ContentResized` (AppKit resizes the document view; UIKit does it only at zoom 1, since a zoomed `UIScrollView` owns the page's frame).
- **Reduced motion:** `NSWorkspace.AccessibilityDisplayShouldReduceMotion` / `UIAccessibility.IsReduceMotionEnabled` turn playing off and set `prefers-reduced-motion`. On macOS the property must be read on the main thread (the AppKit thread check throws), which the app tests learned the hard way.
- On iOS the property is `new bool AnimationsEnabled`: `UIView` has a static of that name for UIKit's own view animations. Same name as the other views was worth the hiding.

**Not done:** retained GPU layers for animated `transform`/`opacity` (the engine has `Canvas.CreateRetainedLayer`; only Win2D implements it, CoreGraphics falls back to repainting the tiles a frame touches). No iOS UI test of the display link; the clock is the same code path as the macOS one past `StartFrameClock`.

**Evidence:** `PeachBrackets.Apple.AppTests` has three tests (an animated colour changes the painted pixels, nothing plays with the switch off, events arrive on the main thread); both TFMs build with no warnings.
