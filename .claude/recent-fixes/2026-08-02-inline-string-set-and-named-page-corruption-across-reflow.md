# Wrong `string(name, first|last)` running headers: inline registration corruption plus a page-boundary matching flaw

## What was actually wrong

`css4.pub`'s Icelandic dictionary demo (`.chapter p b:first-child { string-set: term content() }`,
inside a `columns: 2` container) rendered wrong `string(term, first|last)` values in its `@top-left`/
`@top-right` running headers — e.g. page 5's top-right showed a term that actually belonged to page 6.
Two independent, additive bugs in `CssLayoutEngine.FlowBox` (`src/PeachPDF/Html/Core/Dom/CssLayoutEngine.cs`)
caused it, both stemming from the same root cause: unlike a block box, whose `string-set`/`page`
registration is guarded by `CssBox.PerformLayoutPrologue`'s `_prologueDone` gate (see
[.claude/invariants/fragmentation-a-boxs-prologue-runs-once-per-layout-so-a-re-entered-pass-does-not.md](../invariants/fragmentation-a-boxs-prologue-runs-once-per-layout-so-a-re-entered-pass-does-not.md)),
an *inline* box (`<b>`, `<span>`) is registered from inside `FlowBox` itself, which had neither of the
guarantees that guard provides:

1. **No unregister-before-register.** `CssNamedStringEngine.ApplyStringSet` always appends a fresh
   `NamedString` to `HtmlContainerInt._namedStrings` and `RegisterNamedPageElement` always appends to
   `_namedPageElements`, regardless of whether the same box already has an entry there. A block box's
   prologue withdraws its own previous registration first; `FlowBox`'s equivalent code (plain-inline
   entry ~1260, plain-inline exit ~1707, inline-flex ~1603/1620) did not. `CssLayoutEngineColumns.Layout`
   lays every child out at least twice per invocation — a "Phase 1" virtual single-tall-column measurement
   pass with breaking suppressed, then a real "Phase 2" fill, plus up to `MaxFillAttempts = 4` balance
   retries — so an inline `string-set`/`page` target inside multicol content accumulated one orphaned,
   stale-Y entry per re-layout.
2. **No `opensHere` gate.** Since [#321], a layout pass fills exactly one fragmentainer, so a pass
   resuming a box's flow into a later page/column re-enters `FlowBox` for the *whole* subtree from the
   top — already-placed words are skipped only by ordinal, further down. The string-set/named-page
   application wasn't gated on `opensHere` (the flag `FirstHostingLineBox`'s own assignment, two lines
   above it, already uses for exactly this reason), so a resumed pass that merely walks *past* an
   already-fully-placed `<b>` (to reach not-yet-placed content later in the same paragraph) re-ran
   `ApplyStringSet`/`RegisterNamedPageElement` using *that* pass's cursor — which sits at the resumed
   fragmentainer's own top, since the walk hadn't advanced past the box's words yet — overwriting the
   correct, first-opening position with the top of the next page. This is the one that actually produced
   the dictionary.html symptom: a paragraph starting near the bottom of one page's column, continuing onto
   the next, had its `string-set` value re-stamped to the *next* page's top, which corrupted attribution
   for **both** pages (a term that opened on page 5 got attributed to page 6, and `first`/`last`
   resolution on both pages shifted around it).

A third, independent bug in `MarginBoxRenderer.ResolveNamedString` (`src/PeachPDF/Html/Core/Dom/MarginBoxRenderer.cs`)
compounded the first two — this one pre-existing (from an earlier, incomplete pass at the same
dictionary.html symptom, `a61a82da` "Fix running headers across multicol page breaks") rather than
introduced by bugs 1/2 above:

3. **A symmetric page-boundary epsilon window admits the wrong page's content.** `ResolveNamedString`
   picked `first`/`last` by testing each candidate's raw `Y` against `[pageY - epsilon, pageY + pageHeight
   + epsilon)`, widened by `PageBoundaryEpsilon` (0.5) on *both* ends to absorb float drift between a
   `NamedString`'s `Y` and the page geometry's own accumulation path. But two boxes opening different
   columns on the *same* page land on the identical Y — both are "row 1" of their own column — and when
   that shared Y sits within a hairline of a page boundary (which it does *by construction* for a page's
   own first row), the widened window at the *previous* page's far end admits it too: a paragraph opening
   column 2 of page N could be picked as page N-1's "last" value, even though dozens of genuinely-page-N-1
   entries separate them in DOM order and only the epsilon coincidence pulls it in. Fixed by attributing
   every candidate to a single, unambiguous pagination slot via `HtmlContainerInt.SlotStartingAt` (the same
   top-edge, nudge-onto-the-later-page convention already used everywhere else a box's own Y needs a page
   index) rather than testing raw-Y window membership per page — removing the two-sided-window's inherent
   overlap without reintroducing the original hairline-exclusion bug the epsilon existed to fix.

## What was found by running it, not by reading it

Fixing only bug 1 (verified first, in isolation, via four synthetic multicol re-banding tests) was
*not* sufficient — confirmed by actually fetching `https://css4.pub/2015/icelandic/dictionary.html`
through the `peachpdf` CLI (which fetches over HTTP itself; no separate download step), rendering all
834 pages, and rasterizing pages 3–20 with PyMuPDF, extracting the top-margin band's words per page. With
only the unregister-before-register fix applied, page 5's `string(term, last)` still showed `af-góra` — a
term whose paragraph (confirmed against the raw HTML) visibly starts on page 6, not page 5 — while page
6's `string(term, first)` showed `af-feðrast`, whose paragraph visibly starts on page 5. Both entries'
recorded `Y` were byte-identical to page 6's fragmentainer-top coordinate, the unmistakable signature of
bug 2: a resumed pass stamping both with "wherever this pass's cursor sits," not their own true positions.
Adding the `opensHere` gate fixed both entries' `Y` to their real positions, and re-rendering showed every
adjacent page-boundary pair from page 3 through page 20 self-consistent (each page's `first` exactly
matches the previous page's `last`) — evidence a unit test alone (necessarily synthetic, and easy to
accidentally construct so it can't discriminate the real corruption path) would not have caught, since bug
2 only manifests when a *single paragraph's own content* — not just the container — straddles a real page
break, a shape none of the existing multicol test fixtures exercised.

Bug 3 surfaced the same way, later in the same document: the user reported page 831's `string(term, last)`
showing `ör-skreiðr`, a term whose paragraph actually opens column 2 of page 832. Diagnostic
instrumentation dumping every `term` `NamedString`'s `Y` alongside each page's own boundary (temporarily
added to `PdfGenerator.AddPdfPages`, removed before landing) showed `ör-skreiðr` and `ör-nafn` — the
*correct* page-832 "first" — sharing the exact same `Y`, both equal to page 832's own top: a real, correct
column-1/column-2 coincidence (bugs 1/2 were already fixed by this point; the `Y`s themselves were right),
not a registration bug. `ResolveNamedString`'s window-based matching was what mis-attributed one of the
two. After the `SlotStartingAt`-based fix, a global check dumping every one of the document's 14,644
`term` registrations and asserting their pagination-slot attribution is non-decreasing in document order
found zero violations — the same order-preservation property bugs 1/2's fixes established locally, now
confirmed to hold at full-document scale for bug 3's fix too.

## What was deliberately not done

- `CssNamedStringEngine.GetNamedStringValue` / `CssContentEngine.GetNamedStringValue` (the naive global
  first/last `string()` resolution used for regular in-flow `content`, with explicit `// TODO` comments
  for `start`/`first-except`) were left untouched — unrelated to `@page` margin boxes, which resolve
  through the already page-aware `MarginBoxRenderer.ResolveNamedString`.
- A pre-existing, narrower bug surfaced during review but not fixed here: `CssNamedStringEngine.ApplyStringSet`
  overwrites `cssBox.NamedStrings[currentName]` per comma-separated name in a single `string-set`
  declaration, so a *repeated* name within one declaration (`string-set: a x, a y`) orphans the first
  value's document-level entry (the box-level dict loses the reference needed to unregister it). This is
  independent of both bugs above (reachable via the unmodified block-level `CssBox.cs:2318` site too) and
  not implicated in the dictionary.html symptom.

## Evidence

- Full `net8.0` suite: 7536 passed, 0 failed, 9 skipped.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- Ten new regression tests, each individually confirmed (by temporarily reverting the relevant guard) to
  fail before its fix and pass after: four in `MulticolLayoutIntegrationTests.cs` covering bug 1
  (`NamedString_InlineTarget_DoesNotAccumulateStaleEntriesAcrossReflow` and its named-page/inline-flex
  variants), two in the new `ResumedInlineNamedStringLayoutIntegrationTests.cs` covering bug 2
  (`NamedString_InlineTarget_KeepsTruePositionAcrossResumedContinuation` and its named-page variant), and
  four covering bug 3 in `MarginBoxRendererNamedStringTests.cs`/`MarginBoxResolveNamedStringTests.cs`
  (`Last_ColumnTopsOfTheNextPageDoNotLeakOntoThisPagesLast` and
  `ResolveContent_StringFunction_DoesNotLeakAColumnTopFromTheNextPage`, the latter exercising the real
  `ResolveContent` call site rather than `ResolveNamedString` directly). `ResolveContent` was changed from
  `private` to `internal` for the latter's direct unit-test access, matching `ResolveNamedString`'s
  existing visibility.
- One existing test (`Last_ValueHairlineBelowPageEnd_StillResolvesAsLastOnPage`) had its expectation
  deliberately corrected rather than preserved: a value within epsilon of a page's *end* boundary is now
  consistently treated as opening the *next* page (matching `SlotStartingAt`'s established top-edge
  convention elsewhere), not as trailing off the current one — the old expectation encoded exactly the
  two-sided-window ambiguity bug 3's fix removes. A companion test confirms the same value now correctly
  resolves as the next page's own `first`.
- End-to-end: rendered the real `dictionary.html` (all 834 pages) via the `peachpdf` CLI before and after
  each fix. Pages 3–20 self-consistent (monotonic, adjacent-page-boundary matching) after bugs 1+2's fix;
  page 831/832 (the user-reported case) corrected after bug 3's fix, confirmed both by rasterized text
  extraction and a full-document scan asserting all 14,644 `term` registrations' pagination-slot
  attribution is non-decreasing in document order (zero violations).
