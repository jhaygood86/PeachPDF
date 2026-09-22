# A percentage inside calc()/min()/max()/clamp() is still a percentage for height-definiteness (#1236)

`CssLayoutEngine`'s height resolution (`GetBoxHeight`, `HasDefiniteHeight`, `ResolveDefiniteHeightValue`,
`ApplyHeight`'s max-height clamp, the min/max-height percentage-basis branches) and two predicates on
`CssBox` (`HasAutoBlockEndHeight`, the margin-collapse-through `heightIsAuto` check) all asked "is this
height a percentage" with `value.EndsWith('%')`. `calc(100% - 5px)` ends in `)`, so every one of them
answered false and treated it as an ordinary definite length.

## The load-bearing idea

The question these sites actually need answered is "does this value's *result* depend on the containing
block's size at all", and that has to be answered from the value's real grammar, not its last character.
`CssValueParser.DependsOnPercentage` parses a calc-family value with the same `CalcParser` layout already
uses to *evaluate* it (`CalcParser.Parse`), and walks the resulting `CalcNode` tree for a
`PercentageCalcNode` anywhere in it — under a `+`/`-`/`*`/`/` (`BinaryCalcNode`), a leading sign
(`UnaryCalcNode`), or nested inside `min()`/`max()`/`clamp()` (`CallCalcNode`). One parse, reused at every
call site, rather than a second textual scan that would also fire on an unrelated `%` inside a quoted
`content` value if it were ever reused there.

This is the same lesson as the two `<hr>` width fixes before it (percentage basis, border-box floor):
a question about a CSS value's grammar has exactly one parser to ask, and every caller asks it the same
way.

## Not an `<hr>` defect

The accepted-gap note this closes was filed against `<hr>`, because that was where a rule's early
height-resolution pass and the epilogue's second call could visibly disagree at two different values. Once
`<hr>` stopped resolving its own height twice (retiring `CssBoxHr`), the issue's own repro became internally
consistent — own height, parent height and the next sibling's offset all agree — which looked like the bug
was gone. Probing with a *content-bearing* parent (so the containing block's height is a real,
non-zero, wrong-if-picked-up number) showed the same defect on a plain `<div>`: `height: calc(100% - 5px)`
painted 36.25pt while the next block was placed as if the height were 0. It was never specific to the rule;
`<hr>`'s two-pass resolution just made it visible earlier.

## Scope

Only the height-percentage sites are touched. The width-percentage counterparts in `CssBox.cs` (the
shrink-to-fit walk, `GetMinMaxWidth`'s child measurement) keep `EndsWith('%')` — a `calc()`-dependent width
against an indefinite containing block is a different, unmeasured question, and folding it in here would
have widened the change past what was verified.

## Evidence

`PercentageDependentHeightTests` (20 cases: plain `%`, `calc()`, `min()`, `min-height`, `max-height`, both
against indefinite and definite containing blocks, on a `<div>` and an `<hr>`) fails on 9 cases before the
fix and passes on all of them after, on top of `fix/retire-cssboxhr-1247-1248`. Three `<hr>` cases in that
run still fail against a **pre-**`hr`-presentation-fix `main` because of the unrelated `1.1em` margin
substitution that PR fixes; they pass once rebased past it. Full suite green otherwise, 0 warnings, all
existing showcases pixel-identical to the pre-change baseline.
