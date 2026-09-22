# The `hidden` paint guards must not be deleted as dead branches

Several paint sites guard on a style being `hidden` even though ordinary, cascade-settled paint
cannot reach them with that value — the used width is already 0 by then, and the callers skip a
zero-width edge. They therefore look like dead branches. **They are defence-in-depth against a defect
that has actually shipped, and deleting them re-arms it.**

One of the two is *not* merely theoretical, and that is the part worth reading before trusting the
word "unreachable": the column-rule guard has a real, non-contrived route in, and a test that takes
it.

## `FragmentPainter.PaintColumnRules`

```csharp
if (box.ColumnRuleStyle.Value is LineStyle.None or LineStyle.Hidden) return;
```

Unreachable through ordinary paint, because `DerivedStyle.ActualColumnRuleWidth` zeroes both styles
and the call site only paints when that width is `> 0`.

**But reachable in the one way that matters.** `_actualColumnRuleWidth` has no invalidator, so
priming the cache from a drawn rule and *then* writing `column-rule-style` leaves a stale non-zero
width behind: the call site waves it through, and only this guard stops a solid 16pt line reaching
the page. `AStaleColumnRuleWidth_IsStoppedByThePaintGuard` does exactly that and fails without the
guard, which is why the line sits at 100% branch coverage rather than 75%. The guard is not
speculative — it is the only thing standing between a documented cache hazard and a visible
regression.

It is stated anyway because the *inference* is fragile in two ways. The dash-style switch below it
ends in `_ => RDashStyle.Solid`, so any width that ever reaches this method non-zero paints a solid
rule — and that is not hypothetical: it is exactly what `column-rule: 16pt hidden` did before the
width was zeroed, painting precisely what `column-rule: 16pt solid` painted. And the width it would
be inferring from is `_actualColumnRuleWidth`, which has **no invalidator at all** (see
[hidden is a zero used width…](css-hidden-is-a-zero-used-width-so-the-style-longhand-must-invalidate-the-width-cache.md)),
so a style written after a width read leaves a stale non-zero value behind with nothing to clear it.
The guard states the rule instead of deriving it from a cache that cannot be trusted to have been
refreshed.

## `OutlineDrawHandler`'s three `OutlineStyle.Hidden` guards

`OutwardReach`, the paint predicate, and `TryResolveRing` each test
`OutlineStyle.None or OutlineStyle.Hidden`. (`ToLineStyle`'s
`OutlineStyle.Hidden => LineStyle.Hidden` arm is a fourth reference, but it is a map entry rather
than a guard.)

`OutlineStyle.Hidden` is unreachable **by parsing**: `Map.OutlineStyles` deliberately omits the
keyword, because [css-ui-4 §3.3](https://www.w3.org/TR/css-ui-4/#outline-style) defines
`outline-style` as `auto | <'border-style'>` *excluding* `hidden`. The enum member itself remains.

Those three guards are what keep `DerivedStyle.ActualOutlineWidth` — still the one width getter that
zeroes for `None` only, not for `Hidden` — harmless. If the keyword ever became reachable again
(a new map entry, a `customSetter` that skips `Validate_`, a code path constructing the enum value
directly) the guards are the only thing standing between that and a silently suppressed outline.

## The rule

Do not delete a `hidden` branch because coverage reports it unexercised, and do not "simplify" one
into the width check it duplicates. A zero-hit branch here is the intended state, not evidence of
dead code. If one genuinely must go, the thing to remove first is the *reason* it is unreachable —
and then it is no longer unreachable.

Note the asymmetry, so the outline guards are not assumed to be equally testable: the column-rule
guard has a test because the missing invalidator hands it a route in. The outline guards have none,
because `Map.OutlineStyles` omits the keyword outright and there is no comparable stale-state path —
the only way to exercise them would be to reintroduce the defect they guard against.

Measured symptom if they go: a `column-rule: 16pt hidden` paints a solid 16pt line, and an
`outline-style: hidden` silently suppresses an outline that the cascade should have kept.
