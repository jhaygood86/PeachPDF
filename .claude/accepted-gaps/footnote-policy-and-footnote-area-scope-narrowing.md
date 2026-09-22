# footnote-policy and @footnote: four narrower-than-full-spec behaviors

Issue #1080 added `footnote-display`, `footnote-policy`, and the `@footnote` area rule. Four
places where the implementation is deliberately (or currently unavoidably) narrower than a
literal reading of css-gcpm-3 would ask for:

- **`@footnote` doesn't support `counter-increment`/`float`/`column-span`/`height`** - the spec's
  own default UA stylesheet declares all four on `@footnote`; `HtmlContainerInt.ResolveFootnoteAreaBoxModel`
  only reads `border-top`/`margin-top`/`padding-top`/`max-height`. Three of the four are moot in
  PeachPDF's model (numbering already resets per page without `counter-increment`; the note area
  is always page-bottom/full-width already, matching `float: bottom`/`column-span: all`) but a
  fixed `height` (as opposed to `max-height`'s upper bound) has no equivalent. Filed as
  [issue #1259](https://github.com/jhaygood86/PeachPDF/issues/1259).

- **`footnote-policy`'s "doesn't fit" check is a whole-slot aggregate, not per-footnote** - when a
  page's *combined* note-area height exceeds the limit, every `block`/`line` call landing on that
  page is forced onward together, even one whose own body is small and not itself responsible for
  the overflow (`HtmlContainerInt.ResolveFootnotesForThisAttempt`'s `doesntFit` check, computed
  once per slot from `totalHeight`). This is a deliberate simplification: attributing "whose
  addition tipped it over" precisely would need per-call incremental accounting through the
  footnote-display row-packing loop, materially more bookkeeping for a case (multiple
  differently-policied footnotes overflowing the same page together) that's already narrow.
  Documented in `docs/html-css-support.md`'s "Footnotes" section as "Both values only react to the
  note area's fit on the *current* page."

- **`footnote-policy: line`'s widows/orphans interaction is unverified for a paragraph that
  already spans a page break before reaching the footnote call.** The forced break itself reuses
  the natural `InlineBreakToken` machinery, which orphans (`CssBox.KeepsFewerLinesThanOrphans`)
  correctly reads off the same token. Widows, however, is decided later and separately
  (`CssBox`'s epilogue, `TryKeepFewerLinesForWidows`), computing its own boundary from
  `BlockConstraint.For(this).AbsoluteBandBottom` - the box's own *first* page boundary. For a
  paragraph that hasn't yet crossed a page before the footnote-policy: line call, that's the same
  boundary the forced break just created, so widows correction applies correctly (covered by
  `FootnoteIntegrationTests.FootnotePolicyLine_NoteAreaExceedsMaxHeight_ForcesAnExtraPageComparedToAuto`).
  Whether it still lines up for a paragraph that already spans 2+ pages *before* the line carrying
  the footnote call is untested and not traced through - worth a targeted test (or a fix, if it
  turns out wrong) before relying on it for that shape of document.

- **`ResolveFootnoteAreaBoxModel`'s two call sites (`ResolveFootnotesForThisAttempt` and
  `AttachFootnoteAreas`) can in principle resolve a different `@footnote` rule for the same slot**
  if a named-page transition's active name changes between them - which can happen because the
  `target-counter(_, page)`/`leader()` convergence loop (`PerformLayout`, not gated on
  `HasFootnotes`) runs a further `LayoutDocument` pass, rebuilding `_namedPageElements`, in between
  the footnote convergence loop finishing and `AttachFootnoteAreas` running. This needs a document
  combining named pages, footnotes, *and* `target-counter`/`leader()` content whose resolved text
  width is itself pagination-sensitive - narrow enough that no reproduction has been attempted yet.

None of these are believed to be common in practice; each is left here rather than fixed because a
real fix for any of them touches either the row-packing loop's own accounting, the widows epilogue
(shared, invariant-documented fragmentation code), or the ordering between two independently-owned
convergence loops in `PerformLayout` - higher-risk changes than the value of closing a narrow gap
in the same change that introduced it.
