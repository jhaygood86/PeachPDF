# `text-overflow: ellipsis`, fully spec-complete across writing modes/directions (#694)

## Load-bearing idea

`text-overflow` is purely a paint-time concern: `CssLayoutEngine`'s existing word-placement loop
already lays a nowrap (or single-unbreakable-token) line's words out past the container's edge without
wrapping - nothing in layout needed to change. `FragmentPainter.PaintWords` now truncates per **line**
(`fragment.Lines`, i.e. `CssLineBox`), not per box gated on `white-space: nowrap` - a wrapping block
whose one line happens to contain an unbreakable overflowing token gets that single line truncated,
every other line of the same box untouched. The truncation-edge/walk-direction math
(`FragmentPainter.TextOverflow.cs`) is one algorithm parametrized by `(isVertical, isRtl)`, not four
separate ones: `Forward(coordinate, isRtl)` negates for RTL so "does it still fit" is one comparison
in all four writing-mode/direction combinations, and a line's words are always re-sorted into true
*physical* visual order (never trusted from document/list order) since bidi reordering
(`CssLayoutEngine.ApplyBidiReordering`/`ApplyVerticalBidiReordering`) repositions a word's
`Left`/`Top` without ever permuting `CssLineBox.Words`' own list order - a mixed-direction line (a
Latin/digit run inside RTL Hebrew) would silently truncate the wrong end otherwise.

## Five traps that cost real debugging time

1. **The word-owning box is (almost) never the box `text-overflow`/`overflow` were declared on.**
   `<div style="overflow:hidden;text-overflow:ellipsis">text</div>` does not store `text`'s `CssRect`
   on the div itself - `CssBox.ParseToWords` puts it on an anonymous inline child box, confirmed
   empirically (a debug dump showed the div's own `fragment.Words.Count == 0`, the anonymous child's
   `== 1`). Reading `box.TextOverflow.Value`/`box.Overflow.Value` off the box actually holding the
   words is *always* wrong for this reason - `PaintWords` now reads both off `box.ContainingBlock`
   instead, exactly mirroring how `RenderUtils.OverflowClipOf`/`TryPushOverflowClip` already resolve
   an ancestor's `overflow: hidden` for a box that doesn't have it itself. This also sidesteps needing
   the containing block's *own* fragment for boundary geometry: `BoxFragment.OverflowClip` (already
   resolved on the word-owning box's own fragment, by that same `ContainingBlock` walk) turns out to
   already equal the containing block's padding-edge rect, fragment-local and pagination-safe, for
   free - once the gate is narrowed to `Overflow.Hidden` specifically (next point), the two walks are
   provably the same walk over the same node with the same first check, so it's never null when needed.

2. **`Overflow.Auto`/`Overflow.Scroll` don't get a real clip in this renderer at all** -
   `OverflowClipOf` only special-cases `Overflow.Hidden`, consistent with `overflow`'s own
   `css-properties.json` comment (no interactive scrolling in a PDF, so `auto`/`scroll` already render
   unclipped here). The ellipsis gate was narrowed to match (`== Overflow.Hidden`, not `!=
   Overflow.Visible` as CSS Overflow 3's literal text would suggest) specifically so it doesn't produce
   the confusing half-effect of an ellipsis appended over content that isn't actually being clipped -
   and so point 1's `fragment.OverflowClip` reuse is sound (it would be `null` for `auto`/`scroll`).

3. **A genuine, pre-existing cascade bug, found only because `text-overflow` tripped over it**:
   `CssBox.InheritStyle` treats `TextArea` as "100% inheritable" and adopts the whole area by reference
   from the parent for performance (copy-on-write area sharing) - `unicode-bidi`/`vertical-align`
   already needed a manual "restore this box's own pre-inherit value" step afterward because they're
   the two non-inherited properties living in that otherwise-fully-inherited area. `text-overflow` is a
   *third* one, and without adding it to that same restore step, `TextOverflow.Ellipsis` silently
   "inherited" onto every descendant of a `text-overflow: ellipsis` box (confirmed via the same debug
   dump: an anonymous child born under the `ellipsis` div reported `TextOverflow.Value == Ellipsis`
   despite the property being `inherited: false` in `css-properties.json` and correctly *not*
   inheriting `Overflow.Hidden` the same way) - a real bug independent of anything else in this change,
   now fixed by extending `InheritStyle`'s existing restore call with `TextOverflow` alongside
   `UnicodeBidi`/`VerticalAlign`. Anyone adding a **new non-inherited property to an area that is
   otherwise 100% inherited** needs the same treatment, or it will silently cascade-inherit.

4. **The ellipsis glyph is never one of the cut word's own codepoints, so it can't reuse the cut
   word's own resolved font** - `CssBox.ResolveWordFont(word, styleSource)` resolves per-codepoint
   fallback for `word`'s *own* text only (`word.UsesPerCodepointFont`, decided at layout time from
   that word's own characters); it was never asked about U+2026, so a first pass that reused it to
   draw the ellipsis silently produced *nothing visible* whenever the cut word's own script (Hebrew,
   CJK) resolved to a narrow embedded subset font that doesn't happen to include a "…" glyph - a
   real, easy-to-hit case for any non-Latin-script content in a `font-family` fallback stack, not an
   exotic corner. Caught only by rasterizing the showcase and looking (a passing content-stream text
   *extraction* actually found "…" at the right position throughout - the glyph reference was there,
   it just pointed at a font missing that glyph, which text extraction can't detect but a rendered
   pixel can - exactly the "a token can be present while the composed result is broken" trap this
   repo's own testing conventions warn about). Fixed by resolving the ellipsis's own font
   independently via `CssBox.ActualFontForCodepoint` (the same per-codepoint fallback primitive
   `ResolveWordFont` itself calls, just applied to the ellipsis's own rune) and threading it through
   `DrawWordGlyphs`'s new `fontOverride` parameter instead of letting that method re-resolve
   `word`'s own font a second time.

5. **A truncated line spanning more than one sibling `CssBox` (plain text next to a `<b>`/`<span>`/
   inline image) drew a duplicate, misplaced ellipsis** - found independently by two review-agent
   angles. `FragmentPainter.PaintWords` is called once per *box*, but one `CssLineBox` is shared by
   every sibling box that contributes words to it (`CssLineBox.WordsOf(box)` filters to just one
   box's own subset). The original design let each box independently rediscover "none of my own
   words fit" once content ran past the truncation point, and each drew its own ellipsis anchored at
   its own local start - producing several overlapping "…" glyphs instead of one. Fixed with
   `FragmentPainter._linesAlreadyTruncated` (`HashSet<CssLineBox>`, instance-scoped - one
   `FragmentPainter` already owns exactly one page's paint, confirmed via
   `HtmlContainerInt.PerformPaint`'s own comment): the first box on a line to find and paint the cut
   records the line; every later sibling box on that same line then paints nothing further for it.
   This works without needing any sibling box's own fragment-local word rects (which would otherwise
   require reading geometry outside the paint-per-box contract) because paint order already follows
   visual order for plain inline content, so "first to truncate wins" is equivalent to "the box that
   actually contains the cut point wins." Regression test:
   `Horizontal_Ltr_Nowrap_TextSplitAcrossSiblingBoxes_OnlyOneEllipsisDrawn` (confirmed via a direct
   `CssLineBox.Words` dump that "short" and "bold" - two different owner boxes - really do share one
   line before asserting on it).

Two smaller correctness bugs surfaced by the same review pass, both in
`FragmentPainter.TextOverflow.cs`'s character-fit loop: **letter-spacing was omitted** from the
horizontal `MeasureRunExtent` branch (`g.MeasureString` never includes it - every other width
computation in the codebase adds `CountShapedGlyphs(...) * ActualLetterSpacing` afterward, this one
didn't), which under-counted a growing candidate's true width and could let the kept run spill past
the clip edge under `letter-spacing`; and **the character-growing loop indexed by raw UTF-16 `char`
count**, which can land exactly inside a surrogate pair (an emoji, an astral-plane CJK ideograph) and
produce a malformed lone-surrogate candidate. Both fixed alongside a third, efficiency-only finding
in the same loop (a real shaped `g.MeasureString` call is not O(1) - re-measuring every growing
prefix/suffix from scratch is quadratic in the word's own length): the loop now walks rune (not
char) boundaries and binary-searches the candidate count instead of growing it one rune at a time,
since longer text is monotonically no narrower than a leading/trailing sub-run of it under any real
font's shaping.

Also found (angle B, "removed-behavior auditor"): the truncated word's kept run and the ellipsis
glyph are drawn via `DrawWordGlyphs` directly, bypassing the issue-#113 visibility-clip-epsilon guard
`PaintWordSequence` applies to every ordinary word (a box relocated to the next page's content top can
leave a near-zero clip intersection on the page it left, producing an invisible-but-in-the-content-
stream duplicate). Fixed by adding the identical check (`IsVisible`) to both draw sites.

## Deliberately not done

- **`ApplyCenterAlignment`/`ApplyJustifyAlignment`'s own overflow-guard shape** (the same
  `if (!(diff > 0)) return;` pattern `ApplyRightAlignment` had, fixed here to mirror
  `ApplyVerticalFlushAlignment`'s already-shipped #797 fix) was *not* touched - only
  `ApplyRightAlignment` directly blocks a spec-correct RTL/`text-align:right` ellipsis. Filed as
  [#840](https://github.com/jhaygood86/PeachPDF/issues/840) rather than silently expanding this
  change's scope.
- **`sideways-rl`/`sideways-lr`**: not distinct writing modes anywhere in this codebase
  (`WritingModeFrame.IsVertical` only recognizes `VerticalRl`/`VerticalLr`) - nothing to do here.
- **A sideways-rotated vertical run is truncated atomically (fits whole or drops whole), never
  mid-run** - unlike a horizontal word or an upright vertical run, which both get real character-level
  truncation. Slicing *inside* one rotated run correctly would need re-deriving its rotated footprint
  from a re-measured (not the original, full-word) natural width, and the practical case (an
  unbreakable single sideways run alone filling an entire overflowing column) is rare enough that the
  added complexity wasn't worth it here - documented as a code comment, not a formal accepted-gap file.
- The non-standard two-value edge syntax, `<string>` replacement, and `fade()` (CSS Overflow 3 doesn't
  define any of them - confirmed by fetching the actual W3C spec text and MDN directly rather than
  assuming from memory; MDN documents all three as experimental/non-standard with inconsistent browser
  support) are out of scope, not gaps against the ratified spec.
- **A separate, pre-existing `white-space: nowrap` bug, found while building a showcase/test fixture**:
  `nowrap` content split across more than one sibling inline box (even a plain `<span>`, not just
  `<b>`) still wraps onto a second line box - confirmed to reproduce identically with `overflow:hidden`
  entirely absent, so it's a `CssLayoutEngine`/`FlowBox` wrap-boundary bug, not anything to do with
  text-overflow's own paint-time logic. Not fixed here (out of scope, and not well enough understood
  yet to fix safely) - filed as
  [#841](https://github.com/jhaygood86/PeachPDF/issues/841). The multi-box regression test above still
  exercises the intended scenario despite this - confirmed directly (a `CssLineBox.Words` dump) that
  the two sibling boxes whose duplicate-ellipsis bug it targets really do share one line even though a
  *third* box's content lands on an unexpected second line.

## What running it (not just reading it) confirmed

- `RecordingGraphics.MeasureString` returns a fixed `(0, 12)` regardless of content - useless for
  verifying exact horizontal sub-character truncation width. Paint tests for horizontal LTR/RTL are
  scoped to word-level assertions (which line got an ellipsis, that nothing paints after it) rather
  than exact character counts. Vertical-upright truncation is the one case that stayed fully precise
  under this mock: its per-character extent comes from real `RFont.GetVerticalAdvance`/`Height`, not
  `g.MeasureString`, and each kept character paints as its own `DrawString` call
  (`PaintUprightVerticalRun`), so `TextOverflowPaintIntegrationTests`'s vertical tests assert exact
  kept-character counts and confirm the kept run is a genuine prefix (LTR) of the source text.
- The `ApplyRightAlignment` fix does not regress any existing RTL/`text-indent`/justify test
  (`TextIndentLayoutIntegrationTests`, 13/13; a broader `Rtl|Bidi|TextAlign|Alignment` filter sweep,
  183/183) - the scenario its own comments warn about (indent double-reservation producing a
  non-overflow negative `diff`) doesn't actually occur in the current, already-fixed wrap-boundary-
  narrowing design, only genuine overflow does.
- Full suite: `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` - 9264 passed, 9
  pre-existing platform-gated skips, 0 failed (final run, after the full review-driven fix pass).
- The new `text_overflow` `TestHarness` showcase, rasterized through both PDFium and MuPDF (per this
  repo's two-renderer paint-verification convention) is what actually caught trap 4 above - both
  renderers agreed the ellipsis was invisible before the font-override fix and agreed it renders
  correctly (LTR right edge, RTL left edge, vertical-rl/vertical-lr bottom edge) after it. Passing
  `dotnet test` alone would not have caught this, since every automated assertion about the ellipsis
  checks that a `DrawString("…", ...)` call happened, not whether the resolved font could actually
  render that glyph.
- Fixing trap 5 (multi-box duplicate ellipsis) surfaced a real gap in the existing
  `RecordingGraphics` test mock: its `IsVisible`-guard-triggering interaction with the new #113-style
  visibility check (an ellipsis/kept-run rect measured as exactly zero-width under the mock's fixed
  `(0, 12)` `MeasureString` reads as "clipped away" and gets silently skipped) broke every horizontal
  test that asserted an ellipsis was drawn - passing before the guard was added, failing after, for a
  reason that had nothing to do with the guard's own correctness. Fixed by adding
  `RecordingGraphics.MeasureStringOverride` (an optional `Func`, null by default so all ~47 existing
  consumers of the shared mock are unaffected) and setting a deterministic length-proportional
  measurement in this file's own tests - the kind of "extend the shared mock, don't route around it"
  fix this repo's own conventions ask for.
