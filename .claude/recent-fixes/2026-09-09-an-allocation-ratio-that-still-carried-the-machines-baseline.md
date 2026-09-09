# An allocation ratio that still carried the machine's baseline

`AnonymousBoxDefaultingTests` compared one list document against one plain document and asserted the
ratio. That is self-calibrating only for what the two documents share, and they shared less than the
comment claimed: the fixed cost of rendering anything at all — document setup, font loading, the page
machinery — sat in both numerator and denominator, so the ratio moved with whatever that costs on a
given machine.

It measured **1.71x** here against a bound of 2.05, and **2.06x** on a reviewer's machine, repeatably,
across four runs. Same build, same direction, right on the bound — a merged test that fails for one
of the two people who run it.

It now takes each shape at two sizes and asserts the ratio of the **slopes**. The fixed baseline drops
out of each slope, and dividing the list's by a plain block's cancels the per-item text work as well,
leaving the list machinery itself.

| | list | plain | ratio |
| --- | --- | --- | --- |
| with the fast path | 102.3 KB/item | 54.4 KB/item | **1.88** |
| without | 157.3 KB/item | 54.8 KB/item | **2.87** |

Repeatable to three decimal places, and the window is 53% wide against the old form's 41% — of which
the reviewer's machine had already eaten most.

## What this is really an instance of

A ratio is only self-calibrating for the terms that actually cancel. Both documents rendering "the
same text" is not enough when the thing being measured is a per-item cost and the baseline is not:
the baseline is a constant added to both, and a constant added to both does **not** cancel in a
ratio — it drags it toward 1 by however much the machine charges for it. Taking a slope removes it;
that is the whole difference between the two forms.

Nothing about the fix under test changed. This is only the instrument.
