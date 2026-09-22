# `border-style: hidden` was painted like `none` but sized like a real border

Found from the `border_style` showcase: the `hidden` swatch rendered as a *larger* grey rectangle
than its `none` neighbour in the "All CSS1 border-style keywords" row. Nothing blue was drawn, so
paint was right; the box itself was 32pt wider and taller than the 200pt × 48pt it should have been.

## The load-bearing idea

Every consumer of a border's *used* width goes through one place — `DerivedStyle`'s
`ActualBorder*Width` / `NaturalBorder*Width` / `ActualColumnRuleWidth` — and each of them zeroed the
width only for `LineStyle.None`. Every *border* paint site already spelt the pair out
(`BordersDrawHandler`, `BoxEdgesDrawHandler`, `MarginBoxRenderer`, `FormFieldChrome` all test
`is LineStyle.None or LineStyle.Hidden`), which is why the border half of the bug was invisible as a
stray blue stroke and visible only as geometry. Fix is nine `== LineStyle.None` → `is LineStyle.None
or LineStyle.Hidden` in that one file; `CssValueParser.GetActualBorderWidth` has no other call sites,
so there was nowhere else for the rule to leak from.

**The column rule was the exception, and that is the part worth remembering.**
`FragmentPainter.PaintColumnRules` had no `None`/`Hidden` guard at all — the *only* thing keeping a
rule off the page was `ActualColumnRuleWidth > 0` at its call site. So on the column-rule side the
same missing `Hidden` did not merely mis-size anything: `column-rule: 16pt hidden` **painted a solid
16pt line**, because the dash-style switch folds every style it does not name into
`_ => RDashStyle.Solid`. Zeroing the width fixes it, but inferring "don't paint" from a cached
number is the arrangement that let this happen, so `PaintColumnRules` now states the rule itself as
well. Verified both ways: with only the `DerivedStyle` change reverted the paint test fails (the
hidden rule is drawn), and with only the guard restored it passes — the guard is sufficient on its
own, which is what makes it real defence and not decoration.

**The guard looks redundant and is not.** Once the width is zeroed, the call site's own
`ActualColumnRuleWidth > 0` check keeps `none` and `hidden` out of `PaintColumnRules` entirely, so
through ordinary cascade-settled paint the guard's taken-`return` arms never run — it first measured
75% branch coverage (3/4) for exactly that reason, with full line coverage.

That made it look like speculative defence. It is not, and the proof is the hazard in the next
paragraph: because `_actualColumnRuleWidth` has **no invalidator**, priming the cache from a drawn
rule and then writing `column-rule-style` leaves a stale non-zero width that the call site waves
straight through. `AStaleColumnRuleWidth_IsStoppedByThePaintGuard` takes that route, fails without
the guard, and lifts the line to 100% (4/4). So the guard is the only thing standing between a
documented cache hazard and a visible regression — deleting it to satisfy a coverage report re-arms
a defect that shipped once already.

The `OutlineDrawHandler` guards below are the same idea **without** that escape hatch: there is no
comparable stale-state path, so they stay genuinely unexercised and have to be justified rather than
tested.

Note the cache it is defending against has **no invalidator**: neither `column-rule-style` nor
`column-rule-width` carries an `invalidates` entry in `css-properties.json`, and unlike the four
border widths there is no `InvalidateColumnRuleWidth` to call. `_actualColumnRuleWidth` is computed
once and kept. Left that way deliberately — the paint guard above removes the consequence, and adding
an invalidator means a new `DerivedStyle` method plus two JSON hooks in a subsystem this change was
not otherwise touching.

The four **border** widths and **`ActualOutlineWidth`** had the same hazard in a milder form and it
*was* closed here: the width longhands carried an `invalidates` hook but the style longhands did not,
so reading a width and then writing the style left the stale value behind. Pre-existing for `none`;
`hidden` simply gave it a second style that changes the answer. One line each in
`css-properties.json` (`border-<side>-style` → `InvalidateBorder<side>Width`, `outline-style` →
`InvalidateOutlineWidth`) fixes it;
`WritingTheStyle_InvalidatesAnAlreadyReadWidth` and
`WritingTheOutlineStyle_InvalidatesAnAlreadyReadOutlineWidth` pin all five caches in both directions.
The logical longhands need no hook: they are read-once scratch space that writes through to the
physical setter.

Outline was nearly missed, and the reason is worth recording: it was not in the same file as the
border widths in my head, so the first pass "finished" the inventory without it and both this note
and the invariant then *stated* an incomplete list as if it were complete. A note that enumerates
what is left over is read as exhaustive — so the inventory now lives as a table in
[the invariant](../invariants/css-hidden-is-a-zero-used-width-so-the-style-longhand-must-invalidate-the-width-cache.md),
where the empty cells are visible rather than inferred from prose. `column-rule-width` is the only
remaining hole, and it is the one that genuinely costs more than a JSON line.

Spec: [css-backgrounds-3 §3.3](https://www.w3.org/TR/css-backgrounds-3/#the-border-width) —
computed value "absolute length, or 0 if the border style is `none` or `hidden`". Same rule for
`column-rule-width` in [css-multicol-1 §4.4](https://www.w3.org/TR/css-multicol-1/#crw).

## Durable rules extracted from this

Three things here are rules a *future* change must not break, so they live in
[.claude/invariants/](../invariants/) rather than expiring with this file in 30 days:

- [`hidden` is a zero used width, so the collapsed resolver must read style before width](../invariants/css-hidden-is-a-zero-used-width-so-the-style-longhand-must-invalidate-the-width-cache.md)
  — why zeroing the width does not cost `hidden` its CSS 2.1 §17.6.2 clause-1 win, and the
  `invalidates` hooks the cached width depends on.
- [The `hidden` paint guards must not be deleted as dead branches](../invariants/paint-the-hidden-style-guards-must-not-be-deleted-as-dead-branches.md)
  — `PaintColumnRules` and `OutlineDrawHandler`'s guards read as dead branches, and
  `_actualColumnRuleWidth` has no invalidator at all, which is both why the rule is stated rather
  than inferred *and* how the column-rule guard turns out to be reachable and testable after all.
- [A border side's cached colour](../invariants/dom-a-border-sides-cached-actual-colour-is-invalidated-only-by-its-own-longhand.md)
  — updated here, since `border-*-style` now carries an `invalidates` hook that clears the width
  cache but **not** the colour one.

## `outline-style: hidden` — a second, pre-existing defect the first rationale hid

The first draft of this note claimed outline needed no fix because `OutlineStyle` has no `Hidden`
member. That was simply wrong — it has one, `Map.OutlineStyles` mapped the keyword, and
`OutlineDrawHandler.ToLineStyle` mapped it through. Writing down the *correct* rationale (that the
parser was more permissive than css-ui-4) is what made the real defect visible:

css-ui-4 §3.3 defines `outline-style` as `auto | <'border-style'>` **excluding** `hidden` — in the
spec's own words, "`<outline-line-style>` accepts the same values as `<line-style>` with the same
meaning, except that `hidden` is not a legal outline style." So
`outline: 2px solid red; outline-style: hidden` must drop the second declaration as invalid and keep
painting the solid red ring. PeachPDF instead accepted it, let it win, and then painted nothing,
because `TryResolveRing` bails on `OutlineStyle.Hidden`. Pre-existing, not introduced here, and it
takes a **two-declaration cascade** to observe — with only `outline-style: hidden` present, dropping
the declaration leaves the initial value `none`, which paints nothing either way.

Closed by removing the one `Map.OutlineStyles` entry, rather than recorded as an accepted gap.

**Why removing one dictionary entry is sufficient**, which is not obvious and was initially got
wrong here: `Map.OutlineStyles` is the *single gate* for this property, consulted on every path a
value can arrive by. The CSS-OM converter (`Converters.OutlineStyleConverter`) is built from it, and
the generated registry's `Validate_OutlineStyle` is emitted as `Map.OutlineStyles.ContainsKey(value)`
(`ValidatorExpressionBuilder`, `DataTypeKind.EnumKeyword`) and runs *before* the assignment —
`RegistryEmitter` emits `Set_` as `if (!Validate_...) return false;` then the assign. So deleting the
entry rejects the keyword at both layers.

That matters because `CssProperty<T>.FromCssText` — which the registry's assignment then calls —
**fails open**: on an unrecognized keyword it returns `FromValue(value, fallback)`, applying
`OutlineStyle.None` rather than rejecting. Had an unvalidated value ever reached it, removing the map
entry would have changed nothing observable ("paints nothing because `Hidden`" would have become
"paints nothing because the fallback is `None`"). It cannot, because `Validate_` gates it — which
also means `css-properties.json`'s `"fallback": "OutlineStyle.None"` is dead for any authored value
on this property. Do not "harden" that path: there is no hole in it, and a value that fails
validation never reaches `box.OutlineStyle` at all.

Measured, not read: `CssUtils.SetPropertyValue(box, "outline-style", …)` leaves an existing `solid`
untouched for both `hidden` and a nonsense keyword, while `dashed` really does change it — so
`hidden` is rejected, not silently coerced. End to end,
`outline: 2pt solid red; outline-style: hidden` now resolves to `solid`, matching what
`outline-style: no` already did, and `outline-style: hidden` alone leaves the initial `none`.

`OutlineStyle.Hidden` stays, along with everything that reads it in `OutlineDrawHandler`: three
`is (not) OutlineStyle.None or OutlineStyle.Hidden` guards (`OutwardReach`, the paint predicate,
`TryResolveRing`) plus `ToLineStyle`'s `OutlineStyle.Hidden => LineStyle.Hidden` arm, which is a map
entry rather than a guard. The member is now unreachable by parsing, and those three guards are what
keep `DerivedStyle.ActualOutlineWidth` — still the one width getter that zeroes for `None` only —
harmless. Deleting either would re-arm this same
defect if the keyword ever became reachable again.

**So do not let a coverage sweep delete them.** Being unparseable is exactly what makes those guards
read as dead branches in branch coverage. That is intended defence-in-depth, not unreachable code:
the deletion a coverage report would suggest here is precisely the one that re-opens the defect.

## Evidence

- `HiddenBorderWidthTests` (25 cases: per-shorthand, per-side, the `NaturalBorder*` accessors,
  border- and outline-style cache invalidation, column-rule used width, column-rule *paint*, plus
  `none`/`solid` controls throughout). Stashing only `DerivedStyle.cs` fails 9 and passes the control
  cases, so each assertion is pinned to the defect rather than to layout in general.
- **The column-rule case needed a paint assertion, not a width one.** The width getter is not
  user-visible; what changed for a document author is that a hidden rule stopped being drawn.
  `AHiddenColumnRule_IsNotPainted` records `DrawLine` calls through `TestRecordingGraphics` and
  asserts on the rule's own colour, with a `solid` control so an empty log reads as a broken harness
  rather than as the feature working. A width-only test passes whether or not anything reaches the
  page — which is exactly the trap this repo's paint convention names.
- **The box under test states its own `width`.** The first draft left it `auto` inside a 200pt
  parent, which silently made the inline axis unassertable: an auto-width block absorbs its inline
  borders into the width it takes from its containing block, so the border box measured 200pt
  whichever way left/right resolved. Reverting *only* `ActualBorderRightWidth`/`ActualBorderLeftWidth`
  left all 13 tests of that draft — and the whole suite — green. Diff coverage did not catch it either: the lines
  executed, nothing asserted on them. With a stated width the same revert fails 5 tests. A covered
  line is not a pinned line.
- Full suite `--framework net8.0`: 13112 passed, 0 failed, 9 skipped. Diff coverage: all nine
  changed `DerivedStyle` lines and `PaintColumnRules`' new guard at 100% line *and* branch.
- **The guard's last branch is reached through the hazard it defends against, not through a
  contrivance.** It first measured 75% (3/4): once the width is zeroed the call site's own
  `ActualColumnRuleWidth > 0` check keeps a suppressed rule out of the method entirely.
  `AStaleColumnRuleWidth_IsStoppedByThePaintGuard` gets in by priming the width cache from a drawn
  rule and *then* writing `column-rule-style` — legal precisely because `_actualColumnRuleWidth` has
  no invalidator — so the call site waves a stale 16pt width through and only the guard stops a solid
  line. It fails without the guard, and it asserts the staleness itself, so it starts failing the day
  an invalidator is added rather than silently ceasing to exercise anything.
- Cache invalidation: `WritingTheStyle_InvalidatesAnAlreadyReadWidth` (4 cases, both directions, all
  four border sides) and `WritingTheOutlineStyle_InvalidatesAnAlreadyReadOutlineWidth` (2 cases, both
  directions). Stashing only `css-properties.json` fails all 6.
- Column-rule paint: `AHiddenColumnRule_IsNotPainted` asserts exact `DrawLine` counts rather than the
  cached width, per this repo's paint-testing convention. Stashing `DerivedStyle.cs` *and* the paint
  guard fails 3; either one alone is sufficient to keep it green, which is what proves the guard is
  independent defence rather than decoration.
- `border_style` showcase re-rendered and rasterized through **both** PDFium and MuPDF: the `hidden`
  swatch is now pixel-identical in size to `none`, in both.
- For the outline half, four tests across all three layers the keyword can arrive by:
  `CssOutlineStyleHiddenIllegal`/`CssOutlineShorthandHiddenIllegal` (CSS-OM parse),
  `CssUtilsTests.OutlineStyleHidden_IsRejectedByTheRegistry_ButAcceptedForBorderTopStyle` (the
  generated registry — the one that pins `Validate_` gating the assign, so `FromCssText`'s fail-open
  fallback stays unreachable), and
  `OutlineStyleHidden_IsInvalidAndDropped_SoAnEarlierOutlineStillPaints` (paint, via the draw-call
  recorder). Each carries a control that fails if the harness rather than the cascade is broken: an
  `outline-style: none` case for the paint test, and a `border-top-style: hidden` assertion for the
  registry test (`hidden` is valid *there*, so it proves the setter is reaching the box at all).
  Stashing only `Map.cs` fails exactly those 4 and nothing else.
