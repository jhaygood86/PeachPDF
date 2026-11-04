# Fixed background no longer lags in the WinUI HtmlView

Symptom: on a `background-attachment: fixed` document the picture scrolled with the content and snapped back after the repaint (the same compositor-first lag the sticky bar had).

Cause: the `ScrollViewer` moves the painted canvas before `ViewChanged` can repaint, and a fixed image belongs under its box's text and over its ancestors' backgrounds, so unlike a fixed box it cannot go in the top overlay (it would cover the text) or a bottom underlay (ancestor backgrounds would cover it).

Fix: `HtmlDocument.HasViewportBackgrounds`; when true in `ScrollMode.Auto` the view enters viewport mode (`ApplyRenderMode`): the scrolled canvas clears transparent and only gives the scroller its extent (all pointer handlers stay on it), and the non-scrolling overlay paints the visible region of the whole page (`DocumentPaintLayer.All`) on every `ViewChanged` and invalidation. Documents without one keep the cheaper path. Android repaints per scroll anyway.

Trap: the overlay is hit-test invisible, so input must stay on `_canvas`; the canvas and overlay `ClearColor`s swap (the opaque one is the overlay) or LCD text loses its opaque backdrop.

Evidence: PeachBrackets.Tests ViewportTests, WinUI UITests `HtmlViewViewportModeTests`.
