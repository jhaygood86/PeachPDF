# `text-align` is now a real shorthand over `text-align-all`/`text-align-last`

Closes **#1027**, which tracked the accepted gap this fix removes
(`.claude/accepted-gaps/text-align-is-not-a-shorthand-of-text-align-all-and-last.md`, now deleted).

## What changed

`text-align` used to be an independent inherited longhand, playing the role of `text-align-all`
directly. It is now a real `ShorthandProperty` (`TextAlignProperty` in
`src/PeachPDF/CSS/StyleProperties/Text/TextAlignProperty.cs`) over a new `text-align-all` longhand
(`TextAlignAllProperty`, the renamed former `TextAlignProperty`) and the existing `text-align-last`:

- A plain value (`left`/`right`/`center`/`justify`/`start`/`end`) sets `text-align-all` and resets
  `text-align-last` to `auto` — this reset is the one behavior the accepted-gap file called out as
  the actual observable divergence from the spec.
- `justify-all` sets both longhands to `justify`.
- `match-parent` sets both longhands to `match-parent` — a real value on both `text-align-all` and
  `text-align-last` now, not just the shorthand.

## The render-layer rename

`CssBox`'s generated property backing `text-align`'s render-layer slot is renamed from `TextAlign`
to `TextAlignAll` (`css-properties.json`'s `propertyPath`), so a `text-align-all` CSS property isn't
confusingly backed by a C# property still called `TextAlign` while the CSS name `text-align` means
something else (the shorthand). Every call site that used to write/read `box.TextAlign` now uses
`box.TextAlignAll` (`DomParser.cs`'s `align=` handling, `CssLayoutEngine.cs`'s alignment resolution).
`MarginBoxRenderer.ResolveAlignment` was switched from `StyleDeclaration.TextAlign` (the shorthand,
whose serialization can legitimately be empty for a longhand combination it can't represent) to the
new `StyleDeclaration.TextAlignAll` — margin-box alignment never cared about `text-align-last`
anyway, and this sidesteps needing the shorthand's `Stringify` to be load-bearing there.

## The trap that would have silently broken the reset

`ShorthandProperty.Export` resets an omitted longhand by calling `property.TrySetValue(null)`,
which re-parses the **literal `initial` keyword** through that longhand's own `Converter`. This only
works if the longhand's converter actually recognizes `initial` as input (i.e. is `.OrDefault(...)`-
or `.OrGlobalValue()`-wrapped) — every existing shorthand-participating longhand this repo already
had (e.g. `MarginTopProperty`) has this wrapping, but `TextAlignLastProperty` never needed it before
(it was never a shorthand target) and didn't have it. Without adding
`.OrDefault(TextAlignLast.Auto)` to its converter, `TrySetValue(null)` would silently fail
(`Converters.TextAlignLastConverter` has no `initial` entry, `Convert` returns null, `TrySetValue`
returns false) and `text-align-last` would never actually reset — the entire point of this fix. This
was caught before it shipped, not by a test that then had to be fixed, but by tracing exactly what
`ShorthandProperty.Export`/`Property.TrySetValue` do with a null token value.

## `match-parent`'s resolution lives in `DerivedStyle`, not `CssLayoutEngine`

`match-parent` (css-writing-modes' definition, confirmed against the actual spec text rather than
the issue's own paraphrase, which undersold it — it's part of the *shorthand's* grammar too, not
just each longhand's) computes as if `inherit`, except an inherited `start`/`end` is resolved
against the **parent's own** `direction` rather than the box's own, recursing through a chain of
`match-parent` ancestors and computing to `start` at the root. This is a computed-value-time
concept, and `DerivedStyle.cs` is this repo's existing home for exactly that shape of thing — it
already had a near-identical precedent, `ActualNumericWeight`'s parent-recursive `bolder`/`lighter`
resolution, cached with no invalidation because both are "only ever read after the cascade has
finished assigning every box's own properties" (mid-layout style mutation isn't a thing here).

`DerivedStyle.ActualTextAlignAll`/`ActualTextAlignLast` do the recursive resolution once, lazily,
the first time either is read; `CssBox.ActualTextAlignAll`/`ActualTextAlignLast` are thin
forwarders (matching the existing `ActualNumericWeight` forwarder) that let the recursion write
`parent.ActualTextAlignAll` cleanly. The payoff: `match-parent` can never actually reach
`CssLayoutEngine.cs`'s `ResolveLogicalAlignment`/`ResolveLastLineAlignment` — both were changed to
read the `Actual*` accessor instead of the raw longhand value, and neither needed a new switch arm,
since by the time either runs, `match-parent` has already been fully resolved away. This also
sidesteps a false lead from earlier in this fix's own design: `match-parent` was initially assumed
to need the vertical-writing-mode-specific "inline-start orientation" the same way `start`/`end`
does for vertical text — it doesn't, since the spec's `match-parent` only ever consults the
`direction` property, never `writing-mode`.

## `match-parent` reaches `@page` margin boxes too

A post-change review pass (see CLAUDE.md's review convention) turned up a real gap `match-parent`'s
in-flow implementation didn't cover: `MarginBoxRenderer.ResolveAlignment` (`@page` margin-box text/
image alignment) has no box-tree/`DerivedStyle` chain to consult - it resolves `start`/`end` via
`IsRtl(style, pageStyle)`, treating the page context as a margin box's stand-in parent for direction
purposes. Before this change `text-align: match-parent` on a margin box was simply invalid CSS
(match-parent didn't exist yet), so it fell through to the position-inferred default; once
`match-parent` became a real keyword, it started parsing successfully but had no case in
`ResolveAlignment`'s switch, silently falling through to `BuildStringFormat`'s centered default
instead. Fixed by adding a `"match-parent"` arm resolved via a new `IsPageRtl(pageStyle)` - unlike
`IsRtl`, this never falls back to the margin box's own `direction`, since `match-parent` specifically
wants the *parent's* (here, the page's) direction, not the box's own. See
`MarginBoxRendererTextAlignmentTests.MatchParent_ResolvesAgainstThePageContextsOwnDirection_NotTheDefaultLeft`.

## Evidence

`dotnet build PeachPDF.slnx -t:Rebuild` — zero warnings. Full `dotnet test --framework net8.0` suite
green, including new coverage for the shorthand's reset/`justify-all`/`match-parent` parsing
(`TextProperty.cs`), the issue's own reported HTML reproduced as a regression test
(`TextAlignLastTests.cs`), and `match-parent`'s recursive resolution both at the `DerivedStyle` unit
level and through real paragraph layout (`TextAlignMatchParentTests.cs`). Diff coverage checked
against the 90% gate. The new TestHarness showcase was rasterized through both PDFium and MuPDF per
this repo's two-renderer paint-verification convention.
