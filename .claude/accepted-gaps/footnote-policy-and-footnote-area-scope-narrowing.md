# footnote-policy and @footnote: narrower-than-full-spec behaviors

Issue #1080 added `footnote-display`, `footnote-policy`, and the `@footnote` area rule. Places
where the implementation is deliberately (or currently unavoidably) narrower than a literal
reading of css-gcpm-3 would ask for:

- **`float`/`column-span` declared on `@footnote` are parsed and ignored.** The spec's own default UA
  stylesheet declares both (`float: bottom`, `column-span: all`), and PeachPDF's note area already
  behaves that way unconditionally - it is always at the bottom of the page and always spans the full
  content width - so the declared defaults are matched. Any *other* value has no code path to drive:
  there is no mechanism to put the note area anywhere but the page bottom, and half-honouring
  `float: top` (or narrowing the area for `column-span: none`) would be worse than ignoring it.
  `height` and `counter-increment`, the other two the spec declares there, are now both supported.

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

None of these are believed to be common in practice; each is left here rather than fixed because a
real fix for any of them touches either the row-packing loop's own accounting, the widows epilogue
(shared, invariant-documented fragmentation code), or the ordering between two independently-owned
convergence loops in `PerformLayout` - higher-risk changes than the value of closing a narrow gap
in the same change that introduced it.
