# Face matching narrows by width, then style, then weight; fractional weights; installed variable fonts

`FontResolver.TryFindNearestFace` narrows by width, then style, then weight (CSS Fonts 4 section 5.2); among faces that declare oblique
ranges the requested angle chooses; `font-weight` (and every query below it) carries a `double`; installed variable fonts are registered
with the ranges of their axes. Closes the accepted gap that was tracked as issue #1444, and the "not done" items of
[the ranges entry](2026-09-26-font-face-ranges-and-percentage-font-stretch.md).

## What the load-bearing idea was

**Each step keeps the faces that hold the value it settled on**, and the next step looks only at those. Width is the old clamp trick
(`Ranges.Width.Clamp(request)` measures a face at the end of its range nearest the request, `PickNearestStretch` picks the value, faces
measured at it stay), then `NarrowByStyle`, then the weight step unchanged. The old "exact match" short-circuit was dropped because the
three steps subsume it: a face that holds all three values survives all three.

**The style step distinguishes what the specification says the `@font-face` descriptor must distinguish.** A face is *italic* (declared
`font-style: italic`, `Italic` with no oblique range) or *oblique* (an oblique range, declared or read from a `slnt` axis). An `italic`
request takes italic faces, then the oblique range nearest 11 degrees (the spec maps italic to 11deg); an `oblique <angle>` request takes
the oblique ranges nearest the angle, then italic faces. `NearestOblique` follows the spec's order for the two cases (11 degrees or more:
above ascending then below descending; smaller: below descending then above ascending, positive angles only, then the angles at or below
0), and a negative angle is mirrored. `PreferStrictSlant` still decides upright requests, so a face that is upright itself beats an
oblique range that merely includes 0.

**The angle is now an input to which face is chosen, so it is in the keys.** `FaceRequest.ObliqueAngle` comes from
`TypefaceQuery.ObliqueAngle`, and `FontResolvingOptions.ComputeTypefaceKey` writes it (`tk:f/i@20/400/5`) for italic requests, otherwise
two boxes that differ only in the angle would be served the first one's typeface (the integration test
`ADifferentAngle_IsADifferentTypeface_WhereTheAngleChoosesTheFace` fails without it). The weight is written with the invariant culture
and `R`, so 350.5 and 350 are different keys and a whole weight reads as it always did (`tk:f/n/400/5`). See
[the cache-key invariant](../invariants/fonts-every-input-that-sets-an-axis-is-in-the-font-cache-keys.md).

**Fractional weights follow the whole path.** The `font-weight` property is `CssKeywordOrValue<FontWeightKeyword, double>` (generator
`valueType: "number"`, `CssValueParser.TryParseNumber`), Layer A's `ToWeightNumber` replaces the integer converter (also in the `font`
shorthand, and a plain-number `calc()` is folded without rounding), `ActualNumericWeight` and every `GetFont` overload down to
`FaceRequest`/`TypefaceQuery.Weight` are `double`, and the font caches are keyed on the double. `FontFaceDescriptorResolver` already read
fractions, so a declared range can now be matched by an equally fractional request.

**Installed variable fonts** get their `Declared` ranges when `ParseSystemFonts` reads the description: `TtfFontDescription` reads the
`fvar` table it walks past anyway (16-byte header, 20-byte axis records, at most 64 axes, bounded by the table length, and a malformed table
gives no ranges instead of failing the font, which stays usable at its default as before).

## What running it found rather than reading

- The tests that already existed did not change meaning: none of them had a family that differed in two axes, which is exactly why the
  order stayed wrong. Only the public API snapshot changed (`TypefaceQuery.Weight`, an `int` becoming a `double`).
- xUnit will not bind an `int` `InlineData` to a `double?` parameter (`Object of type 'System.Int32' cannot be converted to
  'Nullable<Double>'`); the oblique theory writes `5.0`.
- `TryParseNumber` has to clear `out result` when it rejects a value: `double.TryParse` leaves `NaN`/`Infinity` in it.
- The angle reaches the engine as `asin(sin(angle))`, which is not exact (10 degrees comes back as 10.000001), and it is compared
  with the exact ends of the ranges faces declare, so `oblique 10deg` missed a range that starts at 10 and `oblique 11deg` fell on the wrong
  side of the threshold. `PdfSharpAdapter.ObliqueAngleOf` rounds to four decimals (the angle passes through a single-precision radian on the way); the integration test has both boundary angles. The tests
  still compare the slant axis with a tolerance.
- A NaN or infinite weight, width or angle made the nearest-face search throw `InvalidOperationException` ("no matching element"), because
  `AxisRange.Contains` treats NaN as inside and `==` does not. The public entry points (`TypefaceFamily.TryMatch`, `FontSet.MatchOrFallback`)
  now refuse them with `ArgumentException`; the CSS path never produces one.

## Deliberately not done

- `bolder`/`lighter` still use the CSS 2.1 table (thresholds at 400/500/700), not the CSS Fonts 4 one (350/550/750), which fractional
  weights make easier to reach: [the accepted gap](../accepted-gaps/font-weight-bolder-lighter-follow-the-css-2-1-table.md).
- The oblique fall-through after the positive ranges, and `oblique 0deg` against upright faces:
  [the accepted gap](../accepted-gaps/face-matching-oblique-fall-through-and-oblique-zero.md).
- A bare `oblique` request (no angle) is `italic` to the engine: `RFontStyle` has no third state, so the oblique-first order applies only
  to `oblique <angle>`.
- `font-synthesis-style` and the `ital`-axis handling of an `oblique` request are as they were.

## Evidence

`PeachDrawing.Text.Tests` (`FontResolverFaceRangesTests`, `VariableFaceRangesTests`, `InstalledVariableFontRangesTests`: the width-first,
style-before-weight, nearest-oblique (both declaration orders, mirrored angles), italic-versus-oblique, fractional-weight, cache-key and
`fvar`-reader (malformed tables) and non-finite-query cases), `PeachPDF.Tests` (`FontFaceMatchingOrderIntegrationTests`: through the real cascade with bundled fonts
in `@font-face`; the order and oblique tests fail against the previous resolver, checked by restoring it; `FontWeightResolverTests`) and
`PeachPDF.SourceGenerators.Tests` (the `number` value type).
