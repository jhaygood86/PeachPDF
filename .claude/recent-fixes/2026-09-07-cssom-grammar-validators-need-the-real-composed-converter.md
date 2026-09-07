# `cssom-grammar` validators must call the real composed `Property.Converter`, not a bare shared grammar helper

Part of the issue #909 cssom-allocation-reduction work (a new `DataTypeKind.CssOmGrammar` in the source
generator, routing specific `css-properties.json` properties away from the full `cssom` round trip by
calling an existing Layer A grammar directly against `CssValueParser.GetCssTokens(...)`). Two mistakes
made during the first wiring pass, both caught only by the full `PeachPDF.Tests` run (not by the
generator's own tests, not by a partial/filtered test run) — worth reading before adding more
`cssom-grammar` entries in a later phase of the same work.

## Mistake 1: `GetCssTokens` strips whitespace; several real grammars need it

`CssValueParser.GetCssTokens` drops every `Whitespace` token by design (most existing callers just want
a single length/color/keyword's tokens). But `ValueExtensions.ToItems()` — the method behind
`OneOrMoreValueConverter`/`.Many()` and used throughout the `Converters.*` composition layer — splits an
item list **on whitespace tokens**. Feed it a whitespace-stripped token stream and every space-separated
multi-value grammar (`text-decoration-line: underline overline`, `font-variant-numeric: oldstyle-nums
tabular-nums`, a gradient's `to right`/`to top left` direction syntax) collapses into a single run of
adjacent tokens that no longs matches any real grammar, and the whole declaration silently gets rejected.

Fix: `GetCssTokens` gained a third parameter, `preserveWhitespace` (default `false`, existing callers
unaffected). `ValidatorExpressionBuilder.BuildCssOmGrammarClause` (the `CssOmGrammar` codegen) always
passes `preserveWhitespace: true` — every grammar the mechanism calls into either needs whitespace
(`.Many()`/`.FromList()`-composed converters, gradient direction) or is indifferent to it (grammar
classes like `BasicShapeGrammar`/`BoxShadowGrammar`/`GridTrackListGrammar` filter whitespace themselves
internally as their first step, so passing it through is harmless for them).

**Symptom if this regresses**: a space-separated multi-keyword property that validated fine for a single
keyword starts silently rejecting any value with two or more keywords, falling back to its initial value.
Caught by `FontVariantNumericIntegrationTests.MultiAxisCombination_CombinesFlags`,
`FontVariantEastAsianIntegrationTests.MultiAxisCombination_CombinesFlags`,
`TextDecorationPaintIntegrationTests.CombinedLines_UnderlineOverline_DrawBothAtTheirOwnPositions`,
`CssUtilsTests.FontVariant_CombinesCapsLigaturesAndNumericAxesInOneDeclaration`.

## Mistake 2: a comma-list property's real grammar lives one level above the shared "per-item" class

Several properties documented (and initially wired) as "already has a shared grammar class, e.g.
`BackgroundPositionGrammar`/`BackgroundSizeGrammar`" turned out to have that class as only the **per-item**
grammar — `background-position`/`background-size` are `<value>#` (comma-separated per layer), and the
real `Property.Converter` (`BackgroundPositionProperty.cs`/`BackgroundSizeProperty.cs`) wraps the item
converter in `.FromList()` at the property level:

```csharp
private static readonly IValueConverter ListConverter = Converters.PointConverter.FromList().OrDefault(Point.Center);
```

`BackgroundPositionGrammar.TryParse(tokens)` (called directly, the initial mistake) has no comma-handling
at all — it's designed to run once per already-comma-split layer (that's exactly what
`BackgroundPositionValueConverter`/`BackgroundLayerResolver` do with it). Calling it on the whole
multi-layer token stream hits a `Comma` token where it expects a length/keyword and returns null,
rejecting any declaration with more than one layer while a single-layer declaration validates fine —
easy to miss if the property isn't tested with 2+ comma-separated values.

Fix: exposed each property's real, list-wrapped grammar as a small `internal static readonly` field
(`BackgroundPositionProperty.ValueGrammar` / `BackgroundSizeProperty.ValueGrammar`, one line each, next
to the existing `.OrDefault()`-wrapped private field) and pointed the JSON's `member` at that instead of
at the bare grammar class. `object-position` (a single `<position>`, not a list) correctly stays on the
already-public `Converters.PointConverter` directly, no `.FromList()` needed — the distinction matters
per-property, not per-grammar-class.

**Symptom if this regresses**: a single-value declaration of the property validates and works; a
multi-layer (comma-separated) declaration of the *same* property is silently rejected and falls back to
the initial value, dropping every layer but effectively becoming invisible in the common single-layer
test case. Caught by `BackgroundPositionSizeIntegrationTests.MultipleLayers_BackgroundPosition_CyclesPerLayer`,
`BackgroundShorthandIntegrationTests.BackgroundShorthand_MultipleLayers_RepeatValuesApplyPerLayer`,
`BackgroundImageSvgIntegrationTests.SvgLayerMixedWithGradientLayer_BothRender` (this last one is a
`background-image` case failing purely from mistake 1's whitespace stripping breaking gradient direction
syntax, not from a list-wrapping issue — `background-image`'s member,
`Converters.MultipleImageSourceConverter.Convert`, was already the correctly-list-wrapped real converter).

## The actual verification method that caught both

Neither mistake was caught by the generator's own unit/golden-file tests (those only check the *emitted
C# text*, not runtime behavior against real grammars) or by a `--filter`-scoped test run targeting the
properties being changed. Both were caught only by running the **full** `PeachPDF.Tests` suite
(`dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`, no filter) after wiring a batch of
properties — pre-existing integration tests for unrelated-looking areas (background shorthand parsing,
font-variant combination, text-decoration paint) were the actual regression net. **Before adding any more
`cssom-grammar` entries in a later phase, read the real `Property.Converter` definition for that property
(not just the shared grammar class doc comment) to confirm: (a) no `.Many()`/`.FromList()` composition
sits above the class being referenced, and (b) if one does, expose and reference that composed field
instead — then run the full untargeted suite, not a filtered one, before trusting the result.**
