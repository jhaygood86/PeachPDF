# `@page` size correction no longer re-parses the whole document

Follow-up to `.claude/recent-fixes/2026-07-31-emission-no-longer-rewalks-the-whole-tree-per-page.md`,
which flagged this as its own issue (#582): `PdfGenerator.AddPdfPages` called `SetContent` — full HTML
parse, full CSS parse, the full per-box cascade, ~8 box-tree correction passes — once with the
caller-configured page size, and then, only after that entire call returned, checked whether the
document's own base `@page { size: ... }` disagreed with it. If it did, it threw the whole parse away
and called `SetContent` a **second time**. Measured on the css4.pub Icelandic Dictionary: ~14s per
`SetContent` call, ~28s total, a fixed cost paid by *any* document whose `@page` size differs from the
configured one regardless of page count — dominant for small documents.

## The load-bearing idea

`DomParser.GenerateCssTree` already resolves `CssPageSize` early, inside `CascadeApplyPageStyles` —
*before* `CascadeApplyStyles` (the expensive per-box cascade) and every correction pass even start. The
mismatch was never actually unknown until late; it was known ~30 lines into a ~90-line method, and
`PdfGenerator` was reacting to it one whole call stack up by discarding everything below that point.
Correcting `htmlContainer.PageSize` right where the mismatch is discovered, and re-resolving the (cheap)
page-styles pass once more in place, needed nothing from `PdfGenerator` at all — `CascadeApplyPageStyles`
now does this itself, split into a thin outer method (resolve, compare, correct-and-retry) and an inner
`ApplyPageStylesOnce` (the original body, now parameterized on `pixelsPerPoint` instead of computing it).
`PdfGenerator.AddPdfPages` and `SetContent` needed small adjustments to keep reading the corrected size
from the right place afterward, but they no longer call `SetHtml` a second time — this is now genuinely
a single HTML parse, a single CSS parse, a single cascade, per render.

## Why the retry is bounded to exactly one, and safe

`ParsePageSizeToPdfPoints`/`ParseSizeDimensionToPdfPoints` resolve `CssPageSize` using only
`PageLengthContext.EmPt`/`RemPt` (the font-size basis) for `em`/`rem` — **never**
`HundredPercentPt` (the page-width basis; `%` is illegal for `size` itself per css-page-3 §7.1). So
`CssPageSize`'s own value is independent of `htmlContainer.PageSize` and resolves identically whether
`ApplyPageStylesOnce` runs once or twice. Only the **margins** resolved in the same pass (`%`/`em`
`@page` margins, via `HundredPercentPt`) actually depend on the page width being correct — so a single
retry, once `CssPageSize` is known, is sufficient. There is no scenario where a second retry could
produce a different answer than the first, since nothing the second call reads changes between the two
calls except `htmlContainer.PageSize` itself (already corrected) and `CssPageSize` (already fixed).

## The trap: two unit spaces for "page size"

`HtmlContainerInt.PageSize` is `RSize`, in `PixelsPerPoint`-scaled internal pixel space (usually == true
points, but diverges under `ShrinkToFit`/`ScaleToPageSize` or a non-72 `PixelsPerInch`).
`HtmlContainerInt.CssPageSize` is `XSize`, always true, unscaled PDF points (its own doc comment says
so explicitly). The old code's comparison lived in `PdfGenerator.AddPdfPages`, comparing `CssPageSize`
against the method's own `orgPageSize` local (always true points, by construction) — implicitly
unit-consistent because it never touched `PageSize` directly. Moving the comparison down into
`CascadeApplyPageStyles` meant it now has to compare against `htmlContainer.PageSize` (`RSize`)
directly, so both directions go through `PeachPDF.Utilities.Utils.Convert(XSize/RSize, pixelsPerPoint)`
explicitly. A second, smaller trap: `Utils` is ambiguous unqualified here — `DomParser.cs`'s namespace
(`PeachPDF.Html.Core.Parse`) nests under `PeachPDF.Html.Core`, whose sibling `PeachPDF.Html.Core.Utils`
*namespace* wins C#'s enclosing-namespace-before-using lookup order over the `PeachPDF.Utilities.Utils`
*class* even with a `using PeachPDF.Utilities;` in scope (CS0234, "does not exist in the namespace
...Utils"). Fully qualifying `PeachPDF.Utilities.Utils.Convert(...)` avoids the CS0234 shadowing
outright, so the `using` was dropped rather than kept unused.

## Downstream: `PdfGenerator` no longer owns the correction, but still needs the corrected value

`AddPdfPages` used to update its own `orgPageSize` local only inside the now-removed second-`SetContent`
branch; that local still feeds the measure/rescale pass, the `ShrinkToFit`/`ScaleToPageSize` third
`SetContent` call, and every page's `page.Width`/`Height`/`MarginBoxRenderer`/`HandleLinks` call further
down the method — so it still needs to be set unconditionally to `container.CssPageSize` when present,
just without a second `SetContent` call to trigger on. `SetContent`'s own tail (which subtracts margins
from the page size to get the final content-box `PageSize`) used to read its `orgPageSize` *parameter*
directly — safe under the old two-call scheme, since the second call was invoked with the already-
corrected size as that parameter. With only one call now, that tail reads `container.CssPageSize ??
orgPageSize` instead, or it would silently clobber `CascadeApplyPageStyles`'s in-place correction with
the stale, originally-configured size.

## What was deliberately not done

Directions 2 ("re-run only what depends on the page size") and 3 ("skip the re-parse when nothing
observed the old size") from issue #582 were not pursued — Direction 1 fully eliminates the redundant
pass with a smaller, more local change (one method split, no new state to track across the DOM), and was
the issue's own recommended direction.

## Evidence

- Full `net8.0` suite green: 7378 passed, 0 failed, 9 skipped (up from 7376 passed pre-fix — two new
  regression tests).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- `diff-cover` against `origin/main`: 100% on the two changed files' lines.
- New regression tests (`PageSizeCorrectionMarginBasisIntegrationTests`) drive the actual mismatch
  through `PdfGeneratorLayoutHarness.LayoutAsync` (the production page-size-resolution path) with a `%`
  and an `em` `@page` margin — the fixture shape the issue's own "Verification" section called for, and
  one no pre-existing test exercised (the existing `@page`-size/margin suites all pre-set
  `HtmlContainerInt.PageSize` correctly before a single direct `SetHtml` call, never triggering the
  mismatch/correction branch at all).
- Not re-run: the multi-minute `dictionary.mhtml` stress benchmark from issue #582/PR #584. The fix is a
  direct, provable elimination of a whole second `HtmlParser.ParseDocument`/`CascadeApplyStyles`/
  correction-pass sequence (verified by code inspection: `PdfGenerator.AddPdfPages` now calls
  `SetContent` exactly once), not a probabilistic win that needs re-measuring to confirm.
