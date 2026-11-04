# `HtmlView` as a block in a virtualized list: auto height, synchronous layout, shared fonts

The ask came from a chat app that shows each message as an `HtmlView` in a recycled list. The view was a viewer (a `ScrollViewer` around the canvas, pages with an 8 DIP gap, wheel/touch/zoom owned by the inner scroller), so a row scrolled its own page, was clipped by `PageGap / 2` at the top, and had to be sized by hand from `GetElementBounds("e")`.

**What was load-bearing**

- `HtmlViewScrollMode.Disabled` removes the `ScrollViewer` from the tree instead of switching its scroll modes off, so there are no scrollbars, gutters or input handling to special-case, and the wheel is never handled by the view. `ScrollToElement`, link anchors and the accessibility `BringIntoView` go through `ScrollToY`, which asks the parent with `StartBringIntoView`.
- A touch that starts on a row must not `CapturePointer` in `Disabled` mode, or the ancestor list never sees the finger.
- `MeasureOverride` is where the document is loaded for the width it is given. The context therefore has to exist before `Loaded` (it is made lazily by `EnsureContext`), events (`DocumentLoaded`, `ContentHeightChanged`) are posted to the dispatcher instead of raised inside the measure, and an in-flight width is remembered so a second measure does not start a second load.
- **`HtmlDocument.ContentExtent.Height` was the whole 14000pt page, not the content (the widget was laid out as a paged document until the continuous-surface entry removed the page).** The initial containing block fills the page, and `ActualSize` accumulates its bottom. The real height is `GetContentHeight(page)`, read off the fragment tree (the lowest fragment bottom plus the bottom margin of a box's last fragment, minus the page's content-top offset). The box-based first attempt returned 255 DIP for a 60 DIP document because an anonymous text run's `ActualBottom` keeps whatever the last intrinsic-width measuring pass left in it. Fragments have no such stale state, and a fragment's `Rect` for `html`/`body` is in whole-box coordinates (it starts above the page on page 3), so only `Rect.Bottom` is meaningful, not `Rect.Y`.
- Synchronous layout is a promise about the document, enforced at the places the pipeline can really suspend (`AsyncGuard.Require` at every `GetResourceStream`, web/local font load and the default style sheet). It throws a private `SynchronousOnlyViolationException : InvalidOperationException`, and every `catch (Exception)` that wraps a failed resource into an `HtmlRenderException` or swallows it (image, stylesheet and SVG-image loaders, the box/flex/grid/columns layout wrappers, the painter and the DOM parser) filters it out, or the host sees `HtmlRenderException: Exception in box layout` instead. The table layout engine's `catch` only resets state and rethrows, so it needs no filter. `ValueTask.IsCompleted` is checked before the await; a pending one is abandoned with its fault observed, never blocked on.
- `FontSet` is shared by contexts, so every public entry takes its one lock (the resolver and its caches are plain dictionaries that lookups also write to) and `AddData` returns the existing family for the same bytes and description without parsing again. `Win2dRenderContext` registers the set's families when made; `SyncFonts()` picks up later additions (a context copies the family names, it does not read the set live).

**Found by running it**

- A view measured by a `StackPanel` with infinite height needed `availableSize.Height` ignored; only the width drives the layout.
- Even without `IsLayoutSynchronous` the first measure often finishes the load, because everything the load awaits has already completed. The switch is what makes that a guarantee and makes a violation an error.
- `ItemsRepeater` estimates the extent of rows it has not realized, so the total extent is within a few percent of the sum of the heights, never exact. Realized rows are exact, which is what the test asserts.
- A python `"\a"` in a patch script wrote BEL and form feed into a csproj path and a doc; a `perl -pi` with `\r` in the replacement ate the line break. Both showed up as the whole file diffing.

**Not done**

- DirectWrite-engine contexts keep per-context faces, so the shared `FontSet` does not apply to them.
- A wheel turn over a row is verified structurally (the row has no `ScrollViewer`) rather than by injecting input.

**Evidence**: `HtmlViewEmbeddingTests` and `HtmlViewListTests` (a hundred rows of mixed heights scrolled down and back up with exact heights, painted visible rows, selection per row and view reuse), `SynchronousOnlyTests`, `ContentHeightTests`, `SharedFontSetTests` (Text, Skia, Win2D); the pre-existing suites (PeachPDF 1586, Engine 17945, Text 6597, PDF 1536, WinUI UI tests 54) pass with the `ValueTask` conversion.
