# The cascade re-defaulted every list item's anonymous box

The cascade's defaulting step walks `CssDefaults.InitialValues` and sets each property back to its
initial value. It is skipped for a box still on the shared `ComputedStyle.Default`, which is most of
them — but an **anonymous** box that anything has already forked off `Default` took the full walk to
reconstruct a state it was one assignment away from.

Re-pointing at `ComputedStyle.Default` and restoring the structural display is that same state,
directly. Every area is copy-on-write, so the display assignment forks it straight back.

## What measuring it turned up

- **The reason they are forked is not what this first claimed.** The original justification said box
  generation had assigned the display structurally. Instrumented over the corpus, 2,005 of the 2,031
  hits still hold the *initial* display: they are pseudo-elements (1,971 `::before`/`::after`, 34
  `::marker`) forked by `CssData`'s synthesis calling `InheritStyle` on them before their own cascade
  ran. The fix is safe for a broader reason — it is a full state overwrite, equivalent to the old
  loop for **any** prior divergence, and `InheritStyle` re-runs immediately after it either way.
  Caught in review, and only because the reviewer instrumented the branch rather than reading the
  comment.
- **The shape that hits it is a list, and nothing else I tried.** Counting the branch over eight
  markup shapes — anonymous blocks from mixed inline/block content, a well-formed table, a table
  missing its rows, loose text inside a table, `display: table-cell` outside a row, floats — every
  one took it **once**. A hundred `<li>` took it **101 times**. It is one forked anonymous box per
  list item, and `list-style: none` does not avoid it.
- **On real documents that is the bulk of the cascade's allocation.** Across a 26-document corpus
  the branch is taken **2,031 times** against 3 for element-backed boxes, and the patch takes the
  corpus from **930 MB to 818 MB** of allocation per pass — 12% of everything, reproduced across
  four alternating measurements (929.85/929.92 against 818.40/818.35).
- **A synthetic fixture nearly missed it entirely.** The first three I wrote — anonymous blocks,
  tables, flex/grid utility markup — showed no difference at all, because none of them generates a
  *forked* anonymous box. Had I stopped there I would have concluded the path was cold, which is
  the opposite of what the corpus says.
- **The premise is already tested.** `ComputedStyleTests.CascadeDefaultingLoop_OnAFreshBox_OnlyKnownExceptionsAreNotNoOps`
  establishes that setting a property to its own initial value leaves a box on `Default`. That is
  exactly why re-pointing at `Default` reconstructs what the loop reconstructs.

## Evidence

`AnonymousBoxDefaultingTests` compares 400 list items against the same text in plain `<div>`s,
measured in the same run so the comparison is self-calibrating and says the same thing whatever font
a machine resolves: **1.72x with the fast path, 2.41x without**, bound at 2.05x. The item text is one
character deliberately — the saving is fixed per item, so longer text dilutes it (at 200 items of a
sentence the window narrows to 1.48 against 1.60, too tight to gate across platforms).

The measurement is **per-thread and synchronous**. The first version used
`GC.GetTotalAllocatedBytes`, which is process-wide, in a suite that runs collections in parallel:
stable in isolation, and reported by a reviewer swinging between 2.05x and 4.59x with the suite
running. Reproduced here — 1.50x, 4.89x, 2.26x over three full-suite runs, against 1.71x twice with
the per-thread counter.

Full suite green on net8.0, 0 new build warnings.
