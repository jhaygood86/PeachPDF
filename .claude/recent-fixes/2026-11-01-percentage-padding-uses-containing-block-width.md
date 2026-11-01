# Percentage padding resolves against the containing block's width (#2001)

**Symptom:** `padding-left: 30%` (and the `%` term of a `calc()`) gave 0 padding on an ordinary block, flex item or
inline-block; `padding-top: 50%; height: 0` (the aspect-ratio idiom) produced no box.

**Cause:** `DerivedStyle.ActualPadding*` resolved the percentage against `Owner.Size.Width`, the box's *own* width, which
is still 0 while the box is being sized (CSS Box 3: it refers to the containing block's width). Margins already use
`ContainingBlock.AvailableWidth`.

**Fix:** a percentage padding now resolves against `Owner.ContainingBlock.AvailableWidth` (`ResolvePadding`), for all four
sides, and is not cached because the containing block may not have its width when first read.

**Trap:** reading the containing block's width for *every* padding recursed forever: `AvailableWidth` subtracts the
containing block's padding, which asked its own containing block, up to the root (its own containing block). Only a value
containing `%` takes that path; plain lengths resolve with no basis, exactly as before.

**Not done:** CSS Box 3 says the basis is the containing block's *logical* width (the inline size), so in a vertical
writing mode it is the containing block's height. Margins use the same physical-width basis in this engine, so this was left
consistent with them rather than changed for padding alone.

**Evidence:** `PercentagePaddingLayoutTests` (block, auto width, shorthand, calc, aspect-ratio, inline-block, flex item,
padded containing block); engine 16953/16953 and `PeachPDF.Tests` green.
