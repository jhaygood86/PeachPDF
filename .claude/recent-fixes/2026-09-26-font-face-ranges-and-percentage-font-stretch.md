# `@font-face` ranges and `font-stretch` percentages

`@font-face` accepts `font-weight: 100 900`, `font-stretch: 75% 125%` and `font-style: oblique 0deg 14deg`; the `font-stretch` property
accepts a percentage; a variable face covers its whole range in nearest-face matching and the request sets `wght`/`wdth`/`slnt` inside
it. Closes the accepted gap that was tracked as issue #1410.

## What the load-bearing idea was

**A face is a range, and a request is measured at the value of the range nearest to it.** `FontFaceEntry.Ranges` (`FaceRanges`: weight,
width in percent, optional oblique angles) is what matching reads. A face is measured at `range.Clamp(request)`, which is the request
itself when the range holds it and the nearest end when not, so the specification's search orders (`PickNearestWeight`,
`PickNearestStretch`) run unchanged over those measured values, and a face with a single value is the old behaviour exactly. The same
clamped value is what `VariableMatching` sets the axis to, and what decides `mustSimulateBold`, so matching, axis and synthesis cannot
disagree about where inside the range a request landed.

**Undeclared means the file's axes.** `FontResolver.AddFont` reads the `fvar` of the added font (`ReadAxisRanges`, tolerant of a font it
cannot read) when a descriptor is missing: a variable file with no descriptors covers `wght`, `wdth` and (as oblique `-max..-min`) `slnt`.
A declared `font-style: normal` or `italic` does not get the axis-derived oblique range; only a missing one does.

**The width became a percentage end to end.** `RAdapter.GetFont` and every font cache key (`FontsHandler`) carry `double stretch` in
percent (100 = normal) instead of the OpenType class; `TypefaceQuery.WidthPercent` is the engine's name for it and `Width` (the class)
stays for callers that have one. The cache-key rule of [the fonts cache invariant](../invariants/fonts-a-cached-font-is-identified-by-its-size-and-its-scale.md)
holds by construction: every input that sets an axis (weight, width percent, oblique angle, the encoded variation settings) is in the
key of the font caches, and the engine's own typeface key carries the percentage when it is not a class percentage (`w93.5`).

## What running it found rather than reading

- **The `@font-face` descriptors were parsed with the cascaded properties' grammars**, which take one value, so `font-weight: 100 900`
  never reached `FontFaceDescriptorResolver` as text at all. The rule now registers dedicated `FontFaceDescriptorProperty` entries
  (raw text, read by the resolver, which treats what it cannot read as `auto`, so an invalid descriptor is ignored as the specification
  says). The `font-stretch` property itself became `CssKeywordOrValue<FontStretch, double>` (new generator `valueType: "percentage"`).
- **`PickNearestWeight` threw** when the only weight on offer equalled the target and the target was below 400 (it skipped the exact
  value). It was unreachable while every face was a point, because the exact-match step caught it first; with ranges the exact step can
  miss on the width or slant while the weight matches. It now returns an equal weight when there is one.
- **A declared single weight is a range of one.** `@font-face { font-weight: 700 }` on a variable file now holds the axis at 700 for a
  weight-400 request (browsers do the same); before, the request drove the axis regardless of the declaration.
- **Two registrations of one file share a face name** (the internal name), so the per-codepoint typeface key (`cp/<face>/...`) now
  carries the ranges; without it the second registration's range would be served from the first's cached typeface.
- **The `-15..0` slant axis is `0..15` degrees of oblique**, so a face with a `slnt` axis and no descriptor serves upright and italic text
  alike; `font-style: oblique <angle>` is passed as `TypefaceQuery.ObliqueAngle` instead of as a `slnt` axis setting, otherwise the
  explicit setting would win over the declared range's clamp.
- **A test that asserts the absence of `" 2 Tr"` can never fail**: the PDF writes the operator at the start of a line (`\n2 Tr`), so the
  leading space is never there. `VariableFontIntegrationTests.ABoldInstance_IsNotAlsoFakedBold` had exactly that; it and the new tests
  look for `2 Tr`, and the new one checks the positive case (a range that cannot reach bold does fake it) so the pattern is known to match.

- **A range that includes 0 must not beat a face that is upright itself.** An oblique-range face serves upright text (`SlantMatches`), and
  "last declared wins" would have chosen it over an upright face declared before it; `PreferStrictSlant` ranks faces of the requested
  slant above range-only matches in both the exact and the nearest step. The synthesis decision for a variable face is finished in
  `VariableMatching` from what the axes can draw (a declared range wider than the axis must still fake the bold the axis cannot reach,
  and a variable face with neither `ital` nor `slnt` still fakes italic).

## Deliberately not done

- The order the axes are narrowed in (style before width, the specification says width first) and choosing between several oblique
  ranges by the requested angle: both are recorded in [the accepted gap](../accepted-gaps/font-face-matching-narrows-style-before-width.md).
- Fractional `font-weight` values in a query (`ActualNumericWeight` is an integer): a range may hold fractions (`350.5 400`), the
  request cannot.
- Per-face ranges for **installed** variable fonts: a system font registers at its own default like any installed face.
- `font-style: oblique` on the `ital` axis: an italic request sets `ital` to 1 as before; the oblique range only drives `slnt`.

## Evidence

`PeachDrawing.Text.Tests` (76 new: `VariableFaceRangesTests`, `FontResolverFaceRangesTests`, `AxisRangeTests`) and `PeachPDF.Tests`
(`FontFaceRangesIntegrationTests`: box-level axis assertions through the real cascade, PDF-level embedding and faux-bold checks, a
layout test that text width follows `font-stretch: 75% < 100% < 125%`; the resolver, stretch and generator tests). The showcase
`variable_font_ranges` was rasterized with PDFium and MuPDF and agrees.
