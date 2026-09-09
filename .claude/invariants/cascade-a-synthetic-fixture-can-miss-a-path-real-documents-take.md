# A synthetic fixture can miss a path real documents take

Twice the same wrong conclusion has been reached about the cascade's defaulting loop, in opposite
directions, and both times from a fixture written to exercise it.

- It was once recorded as **no longer reached** on `main`, from a fixture of 1,681 anonymous boxes
  where allocation measured 53.88 MB against 53.83 MB per render.
- Writing the fix, three fresh fixtures — anonymous blocks from mixed inline/block content, tables,
  flex/grid utility markup — again showed **no difference at all**.

Both were true of the fixture and false of the engine. The real corpus takes that branch **2,031
times** and the patch is worth **112 MB per pass**, 12% of everything `main` allocates.

**So: count the branch before concluding it is cold.** A counter behind the `if` and one run over
real documents is minutes of work and is the only thing that distinguishes "this path is not hot"
from "my fixture does not reach this path". An allocation delta of zero proves the second, not the
first.

## And count it before explaining *why* it is reached

The same counter, kept in place a little longer, would have caught a wrong explanation as well as a
wrong measurement. The fix's first justification said an anonymous box reaches the loop "for exactly
one reason: box generation assigned its display structurally". Instrumented over the corpus, **2,005
of the 2,031 still hold the initial display** — they are pseudo-elements (1,971 `::before`/`::after`,
34 `::marker`) that `CssData`'s synthesis forked by calling `InheritStyle` on them, out of band,
before their own cascade pass ran.

The fix was still correct, but for a broader reason than the one written down: it is a **full state
overwrite, equivalent to the old loop for any prior divergence**, not for one known cause. A
justification that names a single mechanism is a claim about every box that reaches the code, and
it is as measurable as the hit count is.
