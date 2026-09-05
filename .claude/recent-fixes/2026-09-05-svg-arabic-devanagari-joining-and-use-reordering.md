# Port Arabic-family joining / Devanagari USE reordering to SVG `<text>`/`<tspan>`

Closes the SVG side of issue #533's own accepted-gap bullet ("SVG `<text>`/`<tspan>`/`<textPath>` does
not resolve script, joining forms, or USE categories at all") left open when HTML's own Arabic-family
joining and Devanagari USE support landed (see
[2026-09-04-arabic-rlig-logical-order-shaping.md](2026-09-04-arabic-rlig-logical-order-shaping.md) and
[2026-09-05-devanagari-use-syllable-reordering.md](2026-09-05-devanagari-use-syllable-reordering.md)).
Narrowed to `<text>`/`<tspan>` (not `<textPath>`) under `horizontal-tb` only - see the accepted-gaps
file for the precise remaining scope and why each was cut.

## The core problem, and why a straight port of the HTML approach doesn't fit

HTML's own model treats a **word** (`CssRectWord`) as the atomic shaping/reorder/paint unit: joining
forms/USE categories are resolved once over the whole paragraph
(`CssBidiParagraphResolver.ResolveScriptsAndJoining`), sliced per word, and a word's own `Text` is
*never* mutated for display - only `DisplayOrderReversed` flips, and
`TextShapingFeatures.ReverseForDisplay` tells the shaper to reverse the resulting *glyph list* (not the
source text) as its very last step, so a font's own contextual `rlig` rules (lam-alef) still see true
logical adjacency.

SVG has no word concept at all - `SvgRenderer.FlattenRun` produces exactly one `GlyphInfo` per `Rune`,
addressable individually via `x`/`y`/`dx`/`dy`/`rotate` attribute lists, and `ApplyBidiReordering`
reorders/mirrors that list **character by character**, running *before* any shaping happens. Naively
requesting joining forms/USE categories per character and continuing to reorder per character would
reproduce the exact bug HTML had before the rlig fix: shaping would see already-visually-reversed text,
and a font's contextual joining rules would never match.

**The fix**: introduce a *shaping run* as SVG's own equivalent of a word - a maximal contiguous stretch
of `GlyphInfo`s (in true document/logical order) that participate in the same kind of complex shaping
(Arabic-family joining, via `ArabicShapingTable.Of(codepoint) != ArabicJoiningType.U`, or Devanagari USE,
via the character's `ScriptRunResolver`-resolved script), share one `SvgTextElement` (`<tspan>`), carry
zero `letter-spacing`, and have no explicit per-character `x`/`y`/`dx`/`dy`/`rotate` on any but the
run's own first character. `GlyphInfo.ShapingRunFirst` (null for the overwhelming common case) points
every member of a run at its own first glyph, which carries the run's shared state (`RunText`,
`RunJoiningForms`/`RunUseCategories`, `RunScriptTag`, `RunMeasuredWidth`, `RunReverseForDisplay`).

- **`LayoutGlyphs`** measures a run once, as a whole shaped string (`RunText`), on its first glyph;
  every other member gets `Advance` 0 (no independently addressable position, mirroring SVG 2 §11.5's
  own rule that a multi-character glyph's component characters have no addressable position of their
  own) - correctly reproducing e.g. lam-alef's merged one-glyph advance, which the old per-character
  isolated-form measurement never could.
- **`ApplyBidiReordering`**'s RTL branch now walks the bidi run backward *treating a whole shaping run as
  one atomic block* (scan to the block's start via `ShapingRunFirst` reference equality, reflect the
  block's position using its total measured width, emit its members in ascending/logical order) instead
  of reversing/mirroring each character - the same "reorder the word as a unit, keep its internal text
  order intact" idea as `CssLayoutEngine.MirrorWordTextIfNeeded`, just generalized from HTML's
  word-granularity to SVG's run-granularity. `RunReverseForDisplay` is set true here (only for an
  Arabic-family run - USE never display-reverses, matching `CssRectWord.DisplayOrderReversed`'s own
  `EffectiveJoiningForms`-only gating) and consumed by `ResolveShapingFeatures` to build the run's
  `TextShapingFeatures` at paint time.
- **`PaintGlyphs`**'s existing batching loop (which already merges consecutive same-run, unrotated,
  in-flow characters into one `DrawString` call) needed exactly one more break condition -
  `ReferenceEquals(gc.ShapingRunFirst, start.ShapingRunFirst)` - to stop it merging two different
  shaping runs (or a run and plain text) under one `TextShapingFeatures`. Everything else (batching,
  `LogicalGlyph`/ToUnicode-extraction plumbing, decoration spans) needed no change at all: a shaping
  run's glyphs already satisfy the batching predicate the same way ordinary same-run text does, and a
  decoration span can never start/end strictly inside a run (a decorator's scope is a `<tspan>`, and a
  run never crosses a `<tspan>` boundary by construction).

## What was found by running, not by reading

- **Joining forms are resolved over the whole flattened stream, then sliced per run - never
  re-resolved per run.** An explicit `dx` mid-word splits how a word *paints* (two separate `DrawString`
  batches) but a character's true joining form still reflects its real neighbors from the *unbroken*
  word - confirmed by a test (`ExplicitDxMidWord_SplitsIntoSeparatePaintRunsButKeepsTheWordsTrueJoiningForms`)
  that initially asserted the wrong (re-resolved-as-if-isolated) forms and had to be corrected once the
  actual output showed the real ones. This mirrors `CssBidiParagraphResolver`'s own paragraph-wide
  resolution exactly, and is arguably more correct than re-resolving per run would have been.
- **A 3-letter Arabic word with no `direction` attribute at all still visually reverses**, because
  Arabic's own strong-RTL Unicode bidi class triggers UAX#9 reordering independent of the paragraph's
  base direction - this cost a wrong test assertion (assumed document order for `DrawStringCalls`) before
  the actual rendered output made it obvious. Real, correct, and consistent with how HTML's own
  Arabic-family joining behaves regardless of `direction: ltr`.
- Real font shaping (see Evidence below) confirmed the whole pipeline end-to-end: joined glyph forms
  differ from isolated ones, lam-alef genuinely ligates via `rlig`, Aref Ruqaa's cursive attachment
  produces a plausible (non-collapsed) total advance, and Devanagari's `cjct`/matra reorder genuinely
  fuses/reorders glyphs rather than shaping 4 independent nominal glyphs.

## What was deliberately not done, and why

- **`<textPath>` gets no joining/USE support at all** - `RenderTextPath` paints each glyph individually,
  positioned/rotated along the path's own tangent at its own arc-length point; a shaped multi-character
  run has no single point to lay along a curve at without inventing a whole second "one run = one
  positioned/rotated unit consuming one arc-length span" mechanism, which wasn't attempted here.
- **Vertical writing modes are entirely out of scope** - `ResolveComplexScriptRuns` is only called when
  `!isVertical`. Neither Arabic nor Devanagari is ever vertically typeset in practice, and no bundled
  test font/showcase exercises the combination.
- **A run never crosses a `<tspan>` boundary**, even between two sibling `<tspan>`s sharing the same
  script - this was a scoping choice, not a hard technical requirement, but it comes for free from
  requiring every run member to share the same `Run` reference (itself needed regardless, since
  `unicode-bidi`/position lists are keyed per-`<tspan>`), and an author splitting one Arabic/Devanagari
  word across sibling `<tspan>`s is an uncommon authoring pattern (styling seams rarely fall mid-word).
- **No attempt to expose per-glyph cluster-level advances through `RGraphics`** to give a run's interior
  characters their own precise positions (the way a real shaper's `ClusterStart`/`ClusterLength` would
  allow) - SVG 2 §11.5 itself says a multi-character glyph's component characters have no independently
  addressable position, so "no addressable position" (zero advance, position pinned to the run's end) is
  the spec-correct answer, not an approximation standing in for a more precise one.

## Evidence

- New tests: `SvgTextArabicJoiningTests`/`SvgTextDevanagariUseTests` (9 + 6 cases - mock-recorded
  `TextShapingFeatures` reaching `RGraphics.DrawString`, covering script tag/joining-form/USE-category
  resolution, `ReverseForDisplay` gating, run-splitting on explicit `dx`/non-zero `letter-spacing`/
  non-zero `rotate`, and mixed-script/mixed-kind text) and
  `SvgTextArabicJoiningCharacterizationTests`/`SvgTextDevanagariUseCharacterizationTests` (5 + 3 cases -
  the *exact* `(text, TextShapingFeatures)` pair SVG's own pipeline computed, re-shaped through a real
  `OpenTypeDescriptor` against the bundled Noto Sans Arabic/Aref Ruqaa/Noto Sans Devanagari subsets,
  proving real GSUB/GPOS substitution - joined forms differ from isolated ones, `rlig` lam-alef fires,
  cursive attachment produces a plausible total advance, `ReverseForDisplay` genuinely changes glyph
  order, and Devanagari's conjunct+matra reorder produces fewer/differently-ordered glyphs than
  unshaped nominal text).
- New TestHarness showcase `svg_arabic_devanagari_shaping` (`src/PeachPDF.TestHarness/Program.cs`),
  rendered to a real PDF and rasterized with both PDFium and MuPDF - both renderers agree pixel-for-pixel:
  Beh-Yeh-Teh renders with real joined connecting strokes (not three disconnected isolated letters),
  lam-alef renders as one connected ligature glyph, the Aref Ruqaa sample shows a continuous flowing
  cursive baseline, and the three Devanagari samples show the pre-base matra/conjunct/reph correctly
  repositioned - matching the equivalent HTML showcases' own known-correct output.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` (full suite): 9749 passed, 9
  pre-existing platform-specific skips, 0 failed.
- Diff coverage (`diff-cover` against the pre-existing HTML-side Arabic/USE commit): 99% (135 changed
  lines, 1 missing - a defensive `glyphs.Count == 0` early-return in `ResolveComplexScriptRuns` that its
  only caller already guards against reaching).
