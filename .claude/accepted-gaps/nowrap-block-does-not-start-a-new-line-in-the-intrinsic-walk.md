# A `white-space: nowrap` block does not start a new line in the intrinsic-width walk

Tracked as **#1017**. Found during the issue #1014 review; pre-existing, not introduced by it.

`CssBox.StartsNewLine` — the predicate `GetMinMaxSumWords` resets its running line total on — refuses
to reset for any box with `white-space: nowrap`:

```csharp
box.DerivedStyle.ActualDisplay != Keywords.Inline
&& box.DerivedStyle.ActualDisplay != Keywords.TableCell
&& box.WhiteSpace.Value != Whitespace.NoWrap;
```

`white-space` says whether a box's content wraps *within* a line; it says nothing about whether the
box begins one. A block-level box always does ([CSS 2.1
§9.4.1](https://www.w3.org/TR/CSS21/visuren.html#block-formatting)), so a `nowrap` block-level sibling
has its line **added** to the previous sibling's instead of competing with it for "widest line wins".

Float widths at `font: 16px monospace` (one character advances 6.5977pt), against Chromium 148:

| markup | PeachPDF | Chromium |
|---|---|---|
| `<div>AB CD</div><div>EF</div>` | 32.9883 | 32.9883 |
| `<div>AB CD</div><div style="white-space:nowrap">EF</div>` | **46.1836** | 32.9883 |
| …with a third `<div>GH</div>` sibling, all under `nowrap` | **59.3789** | 32.9883 |

The error is one whole sibling line each time, so it grows linearly with the sibling count. Only the
*later* sibling needs `nowrap`. `paddingSum` loses its per-line scoping the same way, since
`oldPaddingSum` is saved in the same branch — two `border-left: 10pt` siblings give 66.1836 against
Chromium's 42.7383, which is the Acid2 `#eyes-a`/`#eyes-b`/`#eyes-c` regression the `oldPaddingSum`
comment describes, reachable again whenever the siblings are `nowrap`.

## Why deleting the clause is not the fix

Measured, not assumed: deleting it fixes every row above exactly, and makes two other cases **worse**,
because `StartsNewLine` is wrong in a second way the clause currently masks — it also returns `true`
for *inline-level* boxes (`inline-block`, `inline-flex`, `inline-table`, `inline-grid`), which do
share the line. An inherited `nowrap` accidentally forces them back onto it today.

| markup (under `white-space: nowrap`) | today | clause deleted | Chromium |
|---|---|---|---|
| `AB <span style="display:inline-flex">CD</span>` | 32.9883 | **19.7930** | 32.9883 |
| `AB <span style="display:inline-block;width:50pt"></span>` | 69.7930 | **50.0000** | 69.7852 |

Those two are exactly the fixtures behind
`AFlexRowLandingOnTheLineAfterASpace_DoesNotSwallowIt` and
`AnExplicitChildWidthLandingOnTheLineAfterASpace_DoesNotSwallowIt` in
`IntrinsicWidthWalkTests` — the only two tests a clause deletion breaks, which is how the second
defect surfaced at all.

So the predicate has to be made to mean what its name says, *is this box block-level in flow*, rather
than having one wrong answer partially compensate for another. Dropping the clause **and** excluding
the inline-level display types puts all six measurements on Chromium's numbers and passes the full
suite (10 844 / 0, net8.0). That is a measured starting point, not a finished patch: `StartsNewLine`
feeds three call sites that must agree (the block reset, the inline-child-margin addition, the
explicit-width fold), and the showcase corpus has not been rasterized against it.

## Why it was out of scope for #1014

#1014 was about hanging a line's trailing white space, and is unaffected by this either way — it
strictly improves this shape (the later sibling's trailing space is now hung rather than added on
top) without going near the cause. Correcting `StartsNewLine` changes which content shares a line at
all, across every shrink-to-fit consumer of the walk, and belongs in a change whose subject that is.

## Provenance

Not from this codebase: `box.WhiteSpace != CssConstants.NoWrap` is in the walk's block-reset in the
original HtmlRenderer import (`2beb0d7f`), the same lineage as the phantom `ActualWordSpacing` term
issue #1011 removed. #937 extracted the condition into `StartsNewLine` unchanged.

Already correct and not part of this: a `<br>` under `nowrap` (`AB CD<br>EF` → 32.9883, matching
Chromium), since the `IsLineBreak` branch does not consult `StartsNewLine`. See
[.claude/invariants/intrinsic-a-trailing-space-may-only-hang-where-a-line-is-known-to-end.md](../invariants/intrinsic-a-trailing-space-may-only-hang-where-a-line-is-known-to-end.md),
whose invariant deliberately does not rest on any claim about how many lines `maxSum` holds —
precisely because this gap means it can hold more than one.
