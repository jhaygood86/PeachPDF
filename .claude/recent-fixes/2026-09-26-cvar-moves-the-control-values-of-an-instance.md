# `cvar` moves the control values of a variable font's instance

A TrueType instance from `Typeface.WithAxes` is hinted with the control values its location gives: `TtFace` (the hinting port's per-face tables)
adds the deltas of the font's `cvar` table to its `cvt` array before any size scales it. This closes the last item of the variable-font gap
issue; what remained of variable-font hinting, its exactness with FreeType, was closed later (see the 2026-09-27 entry on it).

## What the load-bearing idea was

- **`cvar` is `gvar` without the geometry.** The header is `1.0`, a tuple count (`0x8000` = shared point numbers) and an offset to the
  serialized data; a tuple has the same header as a `gvar` one, and its data is point numbers followed by **one** delta per point (the
  number of a control value) instead of an x and a y. `CvarTable` reuses `GvarTable`'s packed-number readers and its tuple scalar (they
  were made `internal`), and differs in three ways: every tuple must carry its own peak (there are no shared tuples in `cvar`; a tuple
  without one makes the table unusable), a control value a tuple names no point for is left alone (there is no outline to interpolate
  along, so nothing like IUP), and the deltas are per control value in font units.
- **Where it plugs in.** `TtFace` already receives the instance's `VariationCoordinates`; after it reads `cvt` (each value times 64, the
  26.6 form `TtSize` later scales with `FtCalc.MulFix(value / 64, scale)`) it adds `Round(delta * 64)`. `TtSize`, the interpreter and the
  hinting cache did not change: a `HintingEngine` is per descriptor, so per instance, and it builds its `TtFace` from that instance's location,
  which is why the invariant about every axis input being in the cache keys needed nothing new.
- **Hinting and exports agree.** An instance embedded in a PDF (`TypefaceExporter.ExportSubset`) writes each glyph afresh with no instructions,
  so it drops `cvt`, `fpgm`, `prep` and does not carry `cvar`; nothing in it refers to a table that is not there. The default typeface's export is the
  path it always was (`AnInstance_IsEmbeddedWithoutControlValuesOrInstructions_AndTheDefaultKeepsThem`).

## What running it showed

- **`MIAP[1]` cannot show a `cvar` effect.** The first fixture moved the glyph's top edge with `MIAP[1]`, which honours the control value cut-in
  (a control value more than about a pixel away from the point's original position is ignored), so at 1000 pixels to the em an instance whose
  control value moved by 32 units was hinted exactly like the default. `MIAP[0]` (no rounding, no cut-in) takes the whole value. A test that a
  control value reaches the fitted outline has to use it.
- **fontTools' instancer rounds each control value to a whole number** (`setCvarDeltas`: `cvt[i] += otRound(delta)`), and this reader keeps the
  fraction (in 26.6), so the tests allow one font unit, exact at the locations where every scalar is 0 or 1 (default, each end of each axis).
- **varLib writes explicit zero deltas** for control values a tuple does not change; the generator turns them into `None` so the tuples are sparse and
  the packed point numbers, the shared-points flag and the "left alone" rule are exercised by a real table, not only by the hand-written ones.
- **The size of the table does not bound the work.** A tuple's data is found by the sizes in the headers, and every size may be 0, so 4,095
  tuples can each read the same 65,535 deltas (a hostile table of a few dozen kilobytes; a review found the first bound, tuples times control values,
  missed the case of a private point list that is longer than the `cvt`). The reader now counts the deltas each applied tuple reads (all the control
  values, or its point count) and refuses a location whose count passes 2^24; a hundred full tuples (6.5 million) are within it. `TtFace` already refuses
  a `cvt` of more than 65,535 entries.
- **The fraction is dropped when a size scales the values, on purpose.** `TtFace` keeps `cvt + delta` in 26.6, but `TtSize` scales `cvt / 64` (integer
  division), which is what FreeType does (its comment says the division must be applied to the multiplicand), so a control value of 729.67 is fitted as
  729 and the instancer's rounding says 730. The test at weight 850 allows either. The sum is clamped to the int range so that a hostile table cannot wrap it.

## Deliberately not done

- FreeType's arithmetic (16.16 deltas scaled with `FT_MulFix`, one `FT_fixedToFdot6` at the end) is not reproduced: the deltas are doubles rounded once to
  26.6. That belongs with the exactness work recorded in the accepted-gap file, together with its goldens.
- `cvar` does not touch anything outside hinting (nothing else reads `cvt`), and an instance's exported font is not hinted.

## Evidence

`VariableCvarTests`: the control values at eleven locations against the instancer, unchanged entries, the fitted top of the glyph at three weights
(700, 732 and 685 at 1000 pixels to the em), a byte-flip sweep over every byte of the fixture's `cvar` table (four values each) that reads the
control values and a hinted outline of every instance, and hand-written tables for private and shared points, all-points, an intermediate region,
summing tuples, a tuple with no peak, truncations at every length and the work bound.
