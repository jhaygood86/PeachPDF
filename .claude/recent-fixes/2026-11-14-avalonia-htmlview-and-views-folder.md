# PeachBrackets.Avalonia, the `src/views/` folder and the shared HtmlView.Core sources

**What landed.** A cross-platform `PeachBrackets.Avalonia` `HtmlView` (Avalonia 12, drawn through `ISkiaSharpApiLeaseFeature` and `PeachDrawing.Skia`) at feature parity with the WinUI view, the
WinUI/Android/Apple views moved from `src/engine/` to `src/views/`, and the platform-free logic they share extracted to `src/views/PeachBrackets.HtmlView.Core/`
(internal sources imported through `PeachBrackets.HtmlView.Core.props`, not a package, so nothing becomes public API and no assembly needs an `InternalsVisibleTo` grant).

**Load-bearing ideas.**
- Avalonia renders on its own thread. The lease (`SKCanvas`, `GRContext`) is valid only inside `ICustomDrawOperation.Render`, so the view keeps the document, tiles and metrics behind
  `_documentLock` exactly as the Android view does, and posts every restyle/invalidation to the UI thread (`PostToUi`).
- The tile cache, caret math, picker values and animation clock are the same code as Android/Apple. They were moved, not copied; Android-only parts (`ImeEdits`, `TextTraversal`, pointer icon
  mapping) stayed in the Android project. WinUI keeps its own copies of what came from Android and Apple: not reconciled.
- Avalonia 12 only: Avalonia.Skia 12.x needs SkiaSharp 3.119.4, so the repo pins moved from 3.119.0 to 3.119.4 (and `HarfBuzzSharp.NativeAssets.Linux` to 8.3.1.3). Avalonia 11 uses SkiaSharp 2.88.

**Found by running it, not reading it.**
- `HeadlessUnitTestSession.Dispatch(Func<Task>, ...)` binds to `Dispatch<T>(Func<T>)` and returns the body's task unawaited. Every headless test passed vacuously (a deliberate `Assert.Fail`
  passed) until the harness used `Dispatch<T>(Func<Task<T>>)`. Seven real failures were hiding behind it; two were product bugs (a second click on a check box was swallowed as a word-selection
  double click; the field context menu took keyboard focus, so `OnLostFocus` cleared the document focus and Cut/Copy/Paste did nothing).
- A headless bitmap's channel order is the bitmap's own format, not always BGRA.
- A window needs a theme (`FluentTheme`) for popups: without one a `MenuFlyout` has no overlay layer and throws `Unable to create IPopupImpl`.
- Dispose-order and thread rules of `PeachDrawing.Skia` apply unchanged: never hold the lease outside `Render`.

**Not done on purpose.** See `.claude/accepted-gaps/avalonia-accessibility-patterns-missing.md` and `avalonia-no-system-caret-blink-time.md`.

**Evidence.** Avalonia.Tests 91+ pass in Debug; Skia (173), Android logic (184), Apple logic (137) and `PeachBrackets.Tests` (1309) pass on `--framework net10.0`; the solution rebuilds with 0 warnings
(the Blazor demo's cached `obj` once held a half-written static-web-assets file after an interrupted build: delete its `obj`).

## Review pass on the Avalonia view (lifecycle, render thread, fetches)

- **The render thread must read no Avalonia property.** `RenderTile` read `PageBackground` on the render thread; on a real GPU every frame threw
  "calling thread cannot access this object" and the window stayed transparent. Headless Skia has no `GRContext`, so the tile path never ran. `Render()` now
  captures a `PaintState` (scroll, scale, background) and the paint path reads only that; `TilesOnCpuForTests` + `CapturePaintForTests` run the real tile path
  into raster surfaces from a pool thread (`TheTilePathPaintsOnARenderThreadWithoutTouchingTheControl` fails if a property read comes back). A paint
  failure is reported once as `DocumentFailed`. Tiles are invalidated by a generation count (a flag cleared by the render thread lost updates).
- **A document swap ends everything that held the old one.** `ResetInteractionState` (fling, track repeat, thumb drag, press, blink and upgrade timers,
  flyouts, focus target) runs from `Adopt`, `ClearDocument` and detach. The pointer-state pump is deliberately not reset (its finally would clear the flag
  while a second pump ran); it re-reads the current document each round. Waiting work compares the document (or `_documentEpoch`) after each await and
  ends quietly when it was replaced.
- A changed `FontSet` makes the new context in the next load and swaps it in with the document (`Adopt`); the context is never null while attached.
- Sources and cursor images load through `HostResourceLoader` (`HostBytes`: size cap, cancellation per load version); the document's own calls on the UI
  thread outside `_documentLock` still race the render thread in principle (a `HtmlDocument` is not thread safe) - left as is, as in the Android view.
- Test harness: `HtmlView.IsIdle` replaces fixed settle loops; windows opened by a test are closed when it ends (per-test `AsyncLocal`).
