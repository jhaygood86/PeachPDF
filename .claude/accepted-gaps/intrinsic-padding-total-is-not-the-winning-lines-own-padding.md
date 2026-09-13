# The intrinsic walk's padding total is neither an outer width nor the winning line's own padding

Tracked as **#1040**. Pre-existing; found while fixing #1033, and deliberately untouched by it.

`CssBox.GetMinMaxSumWords` accumulates border/padding in `paddingSum`, a running total kept
**separate** from `maxSum` and combined across boxes by `Math.Max` rather than by addition;
`GetMinMaxWidth` returns `paddingSum + Math.Max(maxSum, widestLine)`. That number is wrong in three
independent ways, and **the three cancel in some shapes and compound in others**, which is why none of
them may be fixed alone.

1. **A containment chain's paddings are maxed, not added.** A child's border box has to fit inside its
   parent's content box, so both count. `<div style="position:absolute; padding:0 2pt"><div
   style="padding:0 10pt">ABCD</div></div>` resolves 20pt of padding where 24pt has to fit. The
   `Math.Max` is right for *siblings* (different lines, widest wins — the Acid2
   `#eyes-a`/`-b`/`-c` case it was added for) and wrong for nesting; one save/restore serves both.
2. **The padding added is not the winning line's own.** `maxSum` and `paddingSum` are chosen
   independently, so one line's decoration can be added to a different line's width. The
   `writing_mode` showcase's flex item measures 171.81pt where `max(vbox outer 165, label text
   156.81)` = 165: the label's line wins, and the `.vbox`'s 15pt is added to it. **Fixing (1) alone
   makes this worse** (177.81pt) — measured, not predicted.
3. **Consumers disagree about what the number is.** The table engine's column widths and
   `GetBoxWidth`'s float branch treat it as an **outer** width (the float branch subtracts
   `ActualBoxSizeIncludedWidth` to get a content width); `GetBoxWidth`'s `position: absolute`
   shrink-to-fit branch treats it as a **content** width, so such a box is its own decoration too
   wide — `<div style="position:absolute; padding:4pt 12pt; border:2.5pt solid">PAID</div>` comes out
   29pt wider than its content. **Fixing only this, without (1), turns that over-count into an
   under-count** whenever a descendant is padded — also measured.

The shape of the fix is to stop keeping a separate accumulator: fold each box's decoration into
`maxSum`/`min`/`widestLine` as its line is measured, so a line's width is complete when the epilogue's
`Math.Max` compares it against another and the result is unambiguously an outer width. That touches
every consumer of the walk (table column sizing, float and absolute shrink-to-fit, flex item
hypothetical sizes) and wants its own change with its own showcase sweep.

## What #1033 does about it: nothing, on purpose

#1033's float branch measures a float in isolation instead of descending into it, which means it also
has to decide where the float's own decoration goes. It splits it back out of the returned widths
(`GetMinMaxWidth`'s `decoration` out-parameter exists only for this) and folds it in by `Math.Max`,
**reproducing the existing accounting exactly** — so that branch changes only *where the float's
content lands*, and nothing about padding. All 115 showcases are byte-identical, which is the evidence
that it really is unchanged.

Both alternatives were built and measured first, and both shipped one of the defects above in a new
shape: adding the float's full outer width to the line makes an absolute box one decoration too wide
(Acid2's `.smile div div`, 108pt against its real 90pt, drawn over the mouth beside it), and fixing
(1) and (3) together to make that legitimate widened (2) on two more showcases.

The visible cost of leaving it: two floats that each declare padding lose one of the two paddings from
the line they share.

```html
<div style="float:left; border:1pt solid; padding:4pt">
  <span style="float:left; padding:0 2pt">AAA</span><span style="float:left; padding:0 2pt">BBB</span>
</div>
```

measures 8pt short of what it draws, and the second float wraps. Without the padding the same markup
is correct — see
[.claude/recent-fixes/2026-09-13-a-float-contributes-to-the-line-it-sits-beside.md](../recent-fixes/2026-09-13-a-float-contributes-to-the-line-it-sits-beside.md).
