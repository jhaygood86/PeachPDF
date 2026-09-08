# The document base is resolved once per tree, not once per reference

`<base href>` was looked up with a fresh `DomUtils.GetBoxByTagName(Root, "base")` at each of the three
places that need it — `CommonUtils.ResolveAgainstDocumentBase` (images, stylesheets),
`HtmlContainer.ResolveHref` (links, bookmark targets) and `PdfGenerator.HandleRunningElementLinks`
(a link inside a `content: element()` running element). All three ask **per reference**, not per
document, and that walk cannot short-circuit when the document declares no `<base>` at all: it visits
every box in the tree before returning null. A running element's own `<a href>` is re-resolved on
every page its element was selected onto, so the cost is `pages × links × boxes`.

`HtmlContainerInt.DocumentBaseUri` is now the single place the rule lives, memoized against the
tree's topmost box identity exactly like `_idIndex`.

## What running it showed, that reading it did not

A 188-page report (a real paginated document with one external link in its running footer) visits, in
`GetBoxByTagName` alone:

| | box visits |
|---|---|
| 188-page report, before | 5 525 506 |
| 188-page report, after | 79 849 |
| 7-page report, before | 19 127 |
| 7-page report, after | 4 815 |

**The sampled profile badly over-stated the time.** `dotnet-trace`'s sample profiler put
`GetBoxByTagName` at 18.0 % inclusive of the render's CPU (2.38 s of 13.20 s across three renders),
every bit of it under `HandleRunningElementLinks`. Removing it does not buy 18 %: it buys 5–7 %
(188-page report, three interleaved rounds of 8 renders per side — median wall 4.07 s → 3.86 s,
best-of-run CPU 3.77 s → 3.50 s). On a 1–7 page document the difference is below this machine's
run-to-run noise. The reason is worth remembering before trusting a sampled percentage again — this
function recurses to the depth of the box tree, so its stacks are the deepest in the whole render and
each sample taken inside it costs proportionally more to walk. A frame's sampled share is inflated by
its own stack depth. The box-visit count above is the honest measure of the work removed; the
timing is what the work was actually worth.

## Deliberately not done

- **`<base>` is still ignored for a `<link>`/`@import` stylesheet.** Those load from inside
  `DomParser.GenerateCssTree`, and `Root` is only assigned once `SetHtml` returns, so they reach
  `ResolveAgainstDocumentBase` with a null root and fall back to the adapter's base URI. That is
  long-standing behaviour and this change preserves it byte-for-byte. Whether the parse-time path
  *should* honour `<base>` is a separate question. Note this does **not** apply to `<img>`: images are
  resolved during layout (`CssBoxImage.MeasureWordsSize` → `ImageLoadHandler.SetImageFromPath`), by
  which point `Root` is assigned and `<base>` is honoured — `FileUriLoaderIntegrationTests`
  `.BaseHrefFileUri_UnderDefaultLoader_RoutesFileResourcesThroughAdapter` is the proof, since it
  reaches its `pic.png` only through `<base href>`. Do not read the null-root early return as
  "`<base>` for images is unimplemented".
- **The null-root early return is a fast path, not a correctness guard.** Removing it would store a
  null key, and the next read against a real tree would fail `ReferenceEquals` and re-walk. The key
  already handles it; `DomUtils.GetBoxByTagName` is null-safe too.
- **`RAdapter.BaseUri` is not frozen into the memo.** It is a cheap property on the adapter, not a
  tree walk, and re-reading it keeps the fallback live rather than fixed at first access. The
  *declared* base is memoized as an already-parsed `RUri`, so a document that has one does not
  re-parse it per reference either.
- No change to `new RUri(href)`'s exception behaviour for a malformed `<base href>`: it is still
  constructed at the same point relative to every caller's own work, and because the memo key is only
  committed afterwards, a malformed base throws on every read rather than just the first.

## Evidence

- `dotnet build PeachPDF.slnx -t:Rebuild` — 0 warnings.
- Full net8.0 suite: 10 258 passed. One pre-existing failure unrelated to this change,
  `FontSynthesisIntegrationTests.ObliqueWithExplicitAngle_RegularOnlyFamily_UsesDeclaredAngleNotFixedDefault`:
  it builds its expected substring with `Math.Sin(...).ToString("0.####")`, i.e. **current**-culture
  formatting, and looks for it in a PDF that writes numbers invariant, so it only passes on a
  period-decimal culture. It fails identically on unmodified `main` here and passes there under
  `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.
- Diff coverage: every executable changed line is hit — `DocumentBaseUriTests` covers the no-document,
  no-`<base>`, declared-`<base>`, blank-`<base>`, memoized-read and second-document paths, and
  `RunningElementLinksAndTaggingIntegrationTests.RunningElementLink_RelativeHref_ResolvesAgainstTheDocumentBase`
  asserts the written `/URI` action is base-resolved rather than raw, once per page.
- Each new test was checked for discrimination, not just coverage:
  `WithinOneTree_TheBaseIsResolvedOnlyOnce` fails when the memo is forced to always re-walk (verified
  by patching the key to `if (true)`), `ASecondDocumentReplacesTheFirstOnesBase` fails if it never
  invalidates, and `WithABlankBaseHref_FallsBackToTheAdapterBaseUri` fails if the whitespace check is
  dropped (`new RUri("   ")` throws). `WithNoDocumentAtAll_FallsBackToTheAdapterBaseUri` is coverage
  of the early return rather than proof of it, for the reason given above.
