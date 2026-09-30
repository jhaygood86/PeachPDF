# Micro-optimizations of the hinting arithmetic that did not measure

Tried and not shipped, against the cold benchmark (`-- --quick`, 3-4 alternating rounds against `origin/main`, the least of 40 runs per font, TrueType fonts loaded into an empty cache
so that the time is mostly the bytecode interpreter): the goldens passed with all of it, and none of it made the interpreter measurably faster.

- `FtCalc.Msb` as `BitOperations.Log2` (the same for zero, which is what `FT_MSB` gives): the TrueType interpreter hardly calls it (0 calls counted over every glyph of
  SourceSans3 and of Liberation Sans, under one a glyph in the opcode fixture, which runs every opcode; it is for the CFF darkening and the normalization of vectors).
- A 32-bit division in `FtCalc.MulDiv` and `DivFix` when the numerator fits in 32 bits (it does for nearly every call): counted per glyph, SourceSans3 makes 45.6 `MulDiv`, 5.0 `DivFix` and
  114.7 `MulFix` calls, Liberation Sans 4.2, 8.4 and 103.5. At about 10.6 microseconds a glyph for SourceSans3, a few nanoseconds saved on 45 divisions is about 2 %, which is what was measured
  (26.7 ms against 26.3 ms for the font, 0.79 against 0.77 ms for the opcode fixture: inside the noise between rounds).
- The data stack of the TrueType interpreter (`Stack`) read from a local in `RunIns` instead of the field: no measurable change either.

Left as they were, so the port stays as close to FreeType's shape as it can, and a future reader does not spend an afternoon on them again. What did move the cold path is allocation
(see the entries on the hinting caches, the TrueType loader and the CFF engine), and the hot path is the lock.
