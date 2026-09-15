# One decoration line becomes N segments: atomic inlines (#1066) and skip-ink (#1064)

Two issues, one mechanism. [#1066](https://github.com/jhaygood86/PeachPDF/issues/1066) (a decoration
line runs through an atomic inline, which css-text-decor-3 §2.4 says is not decorated) and
[#1064](https://github.com/jhaygood86/PeachPDF/issues/1064) (`text-decoration-skip-ink` unimplemented,
css-text-decor-4 §2.5) both amount to *subtracting ranges from a line*, which is why they landed
together rather than each growing a subtraction of its own. #1066's own gap note said as much and was
right.

## The load-bearing idea

`DecorationSegments.Subtract(span, exclusions)` is the whole shared part: sort, merge, walk, drop
slivers. Everything else is producing exclusions in the span's coordinate space. Atomic inlines
produce them from geometry; skip-ink produces them from glyph outlines. Neither knows about the
other, and `PaintDecoration` composes them.

The one thing that is *not* shared is when they are computed. Atomic-inline exclusions are a fact
about the line's content, so they are resolved once. Ink exclusions depend on where the line sits —
an underline and an overline cross different parts of the same glyphs — so they are resolved inside
the per-keyword loop, against that keyword's own band. Getting this backwards would give both lines
the same gaps, which is what `UnderlineOverline_EachMeasuresItsOwnBand` exists to catch.

## Found by running it, not by reading it

- **An inherited property in `TextDecorationArea` does not inherit.** `CssBox.InheritStyle`
  whole-adopts `Font`/`Text`/`Table`/`List`/`Pagination` from the parent; `TextDecorationArea` is
  copied only in the `everything: true` branch, for a structural duplicate of the same element. Every
  other `text-decoration-*` longhand is `Inherited: false`, so the area had never needed to be in the
  inherited set. `text-decoration-skip-ink` is the one that inherits, so it lives in `TextArea`
  instead — the JSON entry says why. A test asserting an ancestor's `none` reaching the decorated text
  is what caught it; nothing else would have.
- **A real overline never crosses ink.** It is drawn at `rectangle.Top`, which is the font's ascent
  line — above every glyph in a well-behaved font. Source Sans 3 has nothing that reaches it, not
  `l`, `f`, `k`, `Å`, `Ķ` or `ẞ`; a tighter `line-height` does not help either, because an inline
  box's rectangle height is the font's content area and does not follow `line-height`. So the
  overline half of skip-ink is correct but inert in practice, and the only honest way to test it is
  scripted ink (`InkAwareRecordingGraphics.ScriptedInk`). A test that cannot fail is worse than none.
- **A band sampled exactly at its top edge finds nothing.** `GlyphInkScanner`'s crossing test is
  half-open, so an edge *ending* on the scanline does not count — deliberately, since counting a
  vertex from both its edges sums to a winding of zero and punches a hole through a stem. The
  consequence is that the topmost sample has to be taken a hair inside the band
  (`TopSampleInset`), or a glyph whose ink stops right at the band's top reports no crossing at all.
- **A `g` produces two crossings, not one.** A band through the open loop of a descender cuts both
  its walls, and nonzero winding correctly reports both. Adapter tests that assumed one crossing per
  descender were wrong, not the scanner.
- **The clearance must not key off the line's thickness.** The first version used
  `max(fontSize * 0.06, thickness * 0.5)`, and a 3px underline came out looking dashed: a thicker
  line already meets more ink (its band is taller and reaches the bottoms of round letters), so
  widening the gap as well compounded it. The reasoning behind the floor was wrong anyway — a
  vertically thicker line does not close a horizontal gap. It is now font-size-proportional only.

## Two traps in the geometry

- **The exclusion is the atomic inline's margin box; every rectangle layout records is a border box.**
  `RecordExclusions` adds the margins back. `BlockUnderline_ExcludesTheAtomicInlinesMarginBoxNotItsBorderBox`
  pins it.
- **An atomic inline is recognized by its display type, never by its fragment's shape.** An
  `inline-block` whose content is inlines-only reaches paint through the ordinary inline path, and one
  whose content is block-level through `CssLayoutEngine.FlowAtomicBlockContentChild`. Both are atomic
  inlines. `DomUtils.IsAtomicInline` reuses `MonolithicContent.IsReplaced` for the replaced half so
  the two can never name different sets.

## The walk had to stop stopping

`CollectDecorationSpans` used to return as soon as it found a line-hosted fragment, because its
rectangle already covers its content. That is still true for *spans* — but an atomic inline nested
inside a line-hosted inline box (`<span>xx <img> yy</span>`) is still one the line must break around,
and returning there hid it completely. The walk now continues with `contributesSpan` cleared:
rectangles stop accumulating, exclusions and words keep being found. It moved to
`DecorationContent` in the process, since it now gathers three things rather than one.

## Deliberately not done

- **Vertical writing mode.** `SkipsInk` excludes it outright: its decoration wants a vertical band and
  every coordinate in `AddInkExclusions` is horizontal. Adjacent to the existing vertical-decoration
  gap, not part of this.
- **CFF outlines and per-codepoint font fallback** contribute no ink, so nothing is skipped in either.
  Recorded in
  [skip-ink-finds-no-ink-under-cff-outlines-or-font-fallback.md](../accepted-gaps/skip-ink-finds-no-ink-under-cff-outlines-or-font-fallback.md),
  tracked as [#1074](https://github.com/jhaygood86/PeachPDF/issues/1074).
- **`GetInkCrossings` is virtual, not abstract**, returning null by default, so every existing
  `RGraphics` test mock needed no edit and degrades to "skip nothing". That is also what made the
  first green full-suite run meaningless as evidence — the mocks answer null, so nothing skipped.
  `InkAwareRecordingGraphics` (delegating to a real, `sealed` `GraphicsAdapter`) is what makes the
  feature observable in a test at all.

## Something this turned up that is not fixed here

**An empty or inlines-only `inline-block` ignores its declared `width`.**
`<span style="display:inline-block; width:54pt"></span>` gets `ActualBoxSizingWidth == 0` and a
near-zero rectangle, so its background paints ~0 wide — while the line still advances past it. The
same box with a block-level child (`<div></div>`) is sized correctly. This is pre-existing and
entirely separate from decoration, but it surfaced here because the showcase's first draft used such
a chip and the decoration gap dutifully matched the wrong rectangle. The showcase now uses fixtures
layout sizes correctly. Worth filing on its own.

## Evidence

- 35 new tests: `TextDecorationAtomicInlineTests` (21, including `DecorationSegments`'s own
  arithmetic), `TextDecorationSkipInkTests` (13), `GlyphInkScannerTests` (13),
  `GraphicsAdapterInkCrossingsTests` (9).
- Full suite on net8.0: 11651 passed, 0 failed (11616 before).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- New `text_decoration_skipping` showcase, rasterized through **both** PDFium and MuPDF at 150-400
  dpi, in agreement: descender gaps present under `auto`, absent under `none`, `line-through`
  untouched, and a real gap around an inline-block, an invisible inline-block and an `<img>`.
