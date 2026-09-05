# SVG CSS Text/Fonts parity with HTML text

Closes the SVG-specific slice of issue #533: `letter-spacing`, `word-spacing`, `text-transform`,
`font-variant-ligatures`/`-caps`/`-numeric`/`-east-asian`, `font-feature-settings`, `font-kerning`,
`font-stretch`, and `text-decoration-line`/`-style`/`-color` now all work on SVG
`<text>`/`<tspan>`/`<tref>`/`<textPath>` the same way they already did on HTML text.

## Load-bearing ideas

- **Reused HTML's CSS-grammar parsers instead of writing a second copy.** `TextShapingFeatureResolver`
  (`Html/Core/Utils/`) and `TextTransformer` are extracted from `DerivedStyle`'s
  `ActualFontVariant*`/`ActualFontFeatureSettings`/`ActualFontKerning` properties and `CssBox.ApplyTextTransform`
  respectively - pure string-in/typed-out functions, with `DerivedStyle` itself refactored to thin
  wrappers calling them (a pure extract-method refactor, verified behavior-preserving by the existing
  430-test HTML font-variant/text-transform/shaping suite passing unchanged). `SvgTreeBuilder.ComputeFontContext`
  calls the exact same functions against its own presentation-attribute/style strings - this repo's own
  "don't write two parsers for the same CSS grammar across layers" convention, applied to a grammar that
  hadn't been factored out yet.
- **`FontContext` (SVG's inherited font-property carrier) grew nine fields, not a parallel struct.**
  `Stretch`/`LetterSpacing`/`WordSpacing`/`TextTransform`/`Ligatures`/`CapsRequested`/`Numeric`/
  `EastAsian`/`FeatureSettings`/`Kerning` join the existing `Family`/`Size`/`Bold`/`Italic`, each
  resolved in `ComputeFontContext` with the same null-or-`inherit`-falls-back-to-inherited pattern the
  existing four fields already used - `font-variant-caps`'s capability gating (via
  `RFont.SupportsFontVariantCaps`) still happens after font resolution in `BuildTextRun`, same as
  HTML's own `DerivedStyle.ActualFontVariantCaps`, since gating needs a resolved font to check against.
- **`letter-spacing`/`word-spacing`'s `em`/`ex` resolve against the CURRENT element's own font-size,
  not the parent's** - the opposite of `font-size`'s own `em`, which resolves against the parent's used
  size. This is a genuine per-property CSS rule difference, not an inconsistency: `ResolveSpacingLength`
  is deliberately a separate method from `ResolveFontSize`, taking the just-computed `size` (not
  `inherited.Size`) as its relative-unit base.
- **`word-spacing` has no per-character mid-string paint primitive to reuse** - `RGraphics.DrawString`'s
  own `letterSpacing` parameter applies uniformly between every glyph pair in one call, with no way to
  request a single, different, extra gap at one specific position. `PaintGlyphs`' batching loop instead
  forces a fresh `DrawString` call to start immediately after a word-spaced whitespace glyph; the next
  batch's own `Px` (already correctly offset by `LayoutGlyphs`' pen-advance, which *does* fold
  word-spacing into `GlyphInfo.Advance` per-glyph) reproduces the gap as the visual distance between two
  independently-positioned paint calls. A no-word-spacing run never takes this break, so default batching
  is unchanged.
- **`text-decoration` needed a new, SVG-native painter, not a reuse of `FragmentPainter.Decorations.cs`** -
  that file is tightly coupled to `CssBox`/line-box rectangles/`box-decoration-break`, none of which
  exist in SVG's per-glyph pen model. What *is* reused is the underline/overline/line-through *offset
  formulas* (`Top + UnderlineOffset`, `Top + Height/2`, `Top`) and the dash-style keyword mapping,
  translated from that file's line-box-top-relative terms into `Py - Ascent` (the same baseline
  convention every other SVG glyph-paint call site already uses). Decoration ownership is tracked via a
  new `GlyphInfo.Decorators` list (lazily allocated, one entry per ancestor run that requested a
  decoration) built in `FlattenRun` - it survives `ApplyBidiReordering`'s physical list reordering for
  free, since that moves `GlyphInfo` instances themselves, not indices into a separate array.
- **`text-decoration` is genuinely not inherited (CSS Text Decoration 3), but still "flows across" a
  plain descendant** - a `<tspan>` with no `text-decoration` of its own still shows an ancestor's line
  underneath it, because `PaintTextDecorations` groups maximal *contiguous* glyph runs per decorator
  (not per `SvgTextElement.TextDecorationLine` value), so a descendant contributing no decorator of its
  own simply doesn't break an ancestor's already-contiguous span.

## What was found by running it, not by reading it

- The first version of `TextTransformer.ApplyCapitalize` added an `else { atWordStart = false; }` branch
  meant to "complete" the original `CssBox.ApplyTextTransform`'s capitalize switch - and silently broke
  it: the original logic deliberately leaves `atWordStart` `true` through a non-letter run (so
  `"123abc"` capitalizes to `"123Abc"`, not `"123abc"`), and the added branch would have consumed that
  flag on the first digit instead. Caught by re-deriving the exact original semantics character-by-
  character before trusting the "obviously equivalent" refactor, not by a test (none existed yet for
  this exact input shape) - added `Capitalize_WordStartsWithNonLetter_CapitalizesFirstActualLetter` once
  found, to pin it going forward.
- `SvgTextTransformTests`'s first `Capitalize_CrossesTspanBoundary_MidWord` assertion assumed the
  trailing space after `"hello "` would stay attached to that fragment's own collapsed output
  (`"Hello "`). Running it showed the real behavior: `TextWhitespaceState.Collapse`'s pending-space
  mechanism attaches a trailing space to the *front* of the next fragment instead (`"Hello"` / `" Wor"`
  / `"ld"`), which is correct, pre-existing SVG cross-run whitespace-collapsing behavior this feature
  doesn't change - the test's expected values were wrong, not the implementation.
- Font-stretch's resolved value has no way to be asserted back out of `SvgTextElement`/`RFont` short of
  adding test-only state or real multi-width font fixtures - `SvgTextFontStretchTests` settled for
  proving every keyword resolves a non-null font without throwing (following every code path) rather
  than the exact numeric class reaching `GetFont`, once the more precise approach (extending
  `TestGraphicsAdapter`'s `TestFont` to record it) hit `TestGraphicsAdapter`'s own `GetFont` always
  returning null (no font family is ever registered on a bare instance - `AddFontFromStream`/
  `AddLocalFont` are both no-op stubs there). `Html.Core.Utils.FontStretchResolverTests` already covers
  the keyword-to-numeric-scale mapping in isolation.

## What was deliberately not done, and why

- No small-caps *synthesis* fallback for SVG (the scaled-lowercase-glyph substitute `CssBox.AddWord`
  provides for HTML when a font lacks real `smcp`/`c2sc`) - real GSUB substitution only, silently inert
  otherwise. A smaller, explicitly scoped gap, recorded in a new
  `.claude/accepted-gaps/svg-text-decoration-v1-scope.md` alongside this same PR's other v1 scope calls
  (which also needs a tracking issue filed - not yet done).
- `text-decoration` doesn't paint at all for vertical-writing-mode `<text>` or `<textPath>` glyphs (a
  real per-glyph-rotated/along-a-curve decoration line is a materially different geometry problem than
  the straight horizontal segment this cut implements) - same accepted-gap file.
- No per-element `xml:lang`/`lang`-driven GSUB language-system selection for SVG (`CssBox.Language`'s
  HTML-side equivalent has no SVG counterpart) - out of scope, small and additive if wanted later.

## Evidence

- Full suite (`dotnet test --framework net8.0`): 9557 passed, 0 failed, 9 skipped (pre-existing
  platform-specific skips) - no regressions, including the full pre-existing 590-test SVG suite and
  430-test HTML font-variant/text-transform/shaping suite (proving the extract-method refactors are
  behavior-preserving).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors, across all target frameworks/projects.
- Diff coverage against `main` (`diff-cover`): 93%, above the 90% gate.
- New test files: `SvgTextLetterWordSpacingTests`, `SvgTextTransformTests`, `SvgTextFontVariantTests`,
  `SvgTextFontStretchTests`, `SvgTextDecorationTests` - all asserting on structural facts (recorded
  `DrawString`/`DrawLine` call fields: text content, letter-spacing value, resolved
  `TextShapingFeatures`, line color/position/dash-style), not content-stream substrings.
  `SvgTextDecorationTests` is additionally backed by a manual PDFium+MuPDF rasterization check (both
  renderers agree - underline, a red dashed overline+line-through+underline combination, and
  letter/word-spacing all visually correct) per this repo's own stated distrust of self-consistent paint
  tests; not encoded as an automated xUnit test since PDFium/MuPDF rasterization here is a Python-tooling
  verification step, not part of the C# test infrastructure.
- New showcase row added to the existing `svg` TestHarness showcase's "19 — Text, tspan & tref"
  section (`letter-spacing`/`word-spacing`, `text-transform`, `font-variant-caps: small-caps`,
  `text-decoration-line/-style/-color`), rendered and visually confirmed via the same PDFium
  rasterization pass.

## Post-implementation review pass (same day)

An 8-angle review of the diff turned up four real correctness bugs the "9557 passed" run above didn't
catch, because nothing exercised the specific shapes that trigger them:

- **`<tref>` text-transform resolved one scope too high** (`SvgTreeBuilder.BuildTextRun`'s `"tref"`
  case): line 1326 recomputed the tref's font context against the *tspan's own inherited* value
  (`fontContext`) instead of the tspan's own *resolved* value (`childFontContext`, already used
  correctly two lines earlier to build the run itself) - so `<tspan text-transform="uppercase"><tref
  .../></tspan>` silently rendered the referenced text without the uppercase transform. One-line fix;
  `childFontContext` was already sitting right there, just not used at this second call site.
- **Word-spacing silently disappeared whenever a run boundary landed exactly on the space glyph**
  (`SvgRenderer.PaintGlyphs`): the batch-break check only tested glyphs appended *inside* the inner
  loop, never the batch's own starting glyph - `<text>AB<tspan word-spacing="10"> CD</tspan></text>`
  starts a fresh batch at the tspan's leading space (a run boundary), and since that glyph is `start`,
  not `gc`, nothing ever broke the batch there. Fixed by checking `start` itself before entering the
  loop at all.
- **Gradient/pattern/stroke bounding box ignored letter-spacing** (`SvgRenderer.PaintTextGlyphs`):
  `RGraphics.MeasureString` has no `letterSpacing` parameter (unlike `DrawString`/`GetTextOutline`,
  which both do), so `textBounds` - the `objectBoundingBox` reference gradients/patterns/strokes
  resolve against - came out narrower than the outline actually painted two lines later, for any
  letter-spaced text with a non-solid fill or a stroke. Fixed the same way HTML's own
  `CssBox`/`CssLayoutEngine` already handle exactly this gap for their own letter-spacing-aware
  measurements: `g.CountShapedGlyphs(text, font, features) * letterSpacing` added on top of the
  unspaced `MeasureString` width - an existing, already-abstract `RGraphics` primitive, not a new one.
- **`font-variant-*`/`font-kerning` SVG attributes were case-sensitive** while the adjacent, same-PR
  `text-transform` resolution three lines above them already normalized case: a presentation attribute
  like `font-kerning="NONE"` (raw XML text, no CSS-cascade tokenization to lowercase it first) silently
  failed to match `Keywords.None` and left kerning enabled. Fixed by lowercasing before resolving -
  deliberately *not* applied to `font-feature-settings`, whose quoted OpenType tag literal is genuinely
  case-sensitive per spec and would have been corrupted by the same fix.

Also fixed: `PaintTextDecorations`'s O(N·D) rescan (dedup via `List.Contains` plus one full glyph-list
walk per distinct decorator) rewritten as a single forward pass tracking every decorator's open span at
once, bounded by per-glyph nesting depth rather than total distinct-decorator count; the text-decoration
dash-style keyword mapping extracted into a shared `TextDecorationStyleMapper.ToDashStyle` used by both
`SvgRenderer` and `FragmentPainter.Decorations.cs` instead of two independently-maintained copies; a
`<tref>` carrying its own (spec-invalid) content nodes no longer leaks whitespace-collapse/capitalize
state into text that follows it (new `TextWhitespaceState.Snapshot`/`Restore`, used around the
tref case's now-discarded speculative walk); the font-stretch test suite gained
`DifferentStretchKeywords_ResolveDistinctCachedFontInstances`, which proves the `stretch:` argument
reaches font resolution by asserting on `FontsHandler`'s cache-key-driven instance identity rather than
only "resolves without throwing" (closing a real CLAUDE.md-flagged no-op-detection gap); and the
`docs/html-css-support.md` SVG-parity sentence was corrected to call out that SVG text still never
resolves a per-element `lang`-driven `LangSys` the way HTML does (already listed as a gap in
`svg-text-decoration-v1-scope.md`, just not cross-referenced from the parity claim itself).

Each of the four correctness bugs got a dedicated regression test (none of the original ~50 new PR1
tests happened to construct the exact shape that triggers any of them): `Uppercase_
AppliesToTrefContent_UsingTheTrefsOwnResolvedContext`, `WordSpacing_RunBoundaryLandsOnTheSpaceGlyph_
GapStillRenders`, `GradientFillText_LetterSpacing_WidensTheObjectBoundingBoxToMatchTheOutline` (required
extending `TestBrush`/`DrawPathCall` in `TestGraphicsAdapter.cs` to capture linear-gradient endpoints,
previously discarded entirely), and the font-stretch instance-identity test above. Full suite after all
fixes: 9561 passed, 0 failed, 9 skipped, `dotnet build -t:Rebuild` still zero warnings.
