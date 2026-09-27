# A CFF hinting miss allocates an eighth of what it did

Measured on the cold benchmark (every glyph of a font into an empty cache, `-- --quick` of `PeachDrawing.Text.Benchmarks`; base is `origin/main` before this change,
four alternating rounds each on the same machine and session, allocations exact, timings the least of 40 runs and indicative because other work loaded the machine).

| Font (glyphs) | Allocated, before | Allocated, after | Least time, before | Least time, after |
| --- | --- | --- | --- | --- |
| SourceCodePro (1568, a real CFF font) | 29,672 KB | 3,596 KB | 4.7-7.7 ms | 2.8-3.0 ms |
| HintingCff (300, 62 of them refused) | 6,552 KB | 909 KB | 2.7-3.1 ms | 2.5-2.6 ms |
| HintingCffCid (150) | 3,197 KB | 431 KB | 1.0-1.8 ms | 0.8-1.5 ms |

About 19 KB a glyph became 2.3 KB for SourceCodePro. HintingCff spends most of its time on the exceptions of its 62 refused glyphs, which is why its time hardly moves.

## What it was, found by sampling and not by reading

`GCAllocationTick` events (an `EventListener` in a throwaway test) give allocation by type. The review that proposed this change said the hint maps were about 70 % of it, and they were:
`Cf2GlyphPath` made three `Cf2HintMap`s, each with a 192-element array of 20-byte edges (about 3.8 KB), for every charstring run, and the counter mask a fourth; then a
temporary `Cf2HintMask` (with a `Cf2Error` of its own) for every map that was built. Next, in order: the list of an outline's segments, grown by doubling from empty (56 bytes a
segment), the seventeen `Cf2Buffer` objects of the subroutine stack, the outline arrays and the copy of them, `Cf2Blues` (made twice, the first thrown away), the stem hint and
operand stacks.

## What was done, and in what order it paid

1. **`Cf2Pool` (`Internal/Hinting/`, this package's own code): hint maps and masks kept by the thread.** 6,552 KB to 2,617 KB by itself. `Cf2HintMap.Init` no longer clears its edges and
   `CopyFrom` copies the `Count` that count: nothing reads an edge at or past `Count` (every access was read for that, and `AdjustHints`' `Edge[j + 1]` is behind `j < Count - 1`), so
   an object that is used again holds what the last glyph left. `Cf2GlyphPath.ReleaseMaps` and the `Exit:` of `Interpret` give them back; a load that throws leaves them to the collector.
2. **The outline is the thread's and is read in place.** `CffGlyphLoader.Load<TState, TResult>` hands a reader a `CffGlyphView` of spans over the pooled `Cf2Outline`, with the font matrix and
   offset applied in place, and the engine makes its `GlyphOutline` from it: no copy of the points, tags and contour ends. `Load(size, glyph)` still copies, which is what the goldens compare.
   `Cf2Outline.Prepare` resets what a new one has (`_pathBegun` included: `Reset` does not, on purpose, between the two runs of one glyph).
   The segment list of every contour is counted first (`CountSegments`, which walks the tags as the conversion does) and made as big as it is. 6,552 to 1,677 KB with 2.
3. **`Cf2InterpBuffers`: the interpreter's own arrays.** Storage, subroutine stack, both stem hint stacks, the hint-moves stack and the operand stack are one pooled object per run (a `seac`
   accent rents a second while the first is held; there are at most two). `Cf2ArrStack.Reset` zeroes struct elements (a new stack's are zero) and keeps object elements, which are all set when
   pushed; `Cf2Stack.Reset` keeps the buffer and moves a `_capacity`, because the overflow check was against `_buffer.Length`. 1,677 to 925 KB.
4. `Cf2Font.Blues` was made twice (the field initializer, then `Setup`); `DarkenParams` was cloned for each glyph and only ever read; the Font DICT lookup was done twice.

## Traps

- **A pooled object that "starts clean" is a reset of every field.** The tests that found the ones I would have missed: the FreeType goldens and the new `GlyphsLoadedInAnyOrderAreTheSameWhateverThe
  ThreadsPoolsHeldBefore` (every glyph of five fonts on a thread that has loaded nothing, against the same glyphs in random order through one thread's pools) both fail when the interpreter's
  storage is not cleared between glyphs (59 failures). `WhatAHostileCffFontAsksForIsNotKeptByTheThread` fails when the outline's arrays are not dropped after a glyph of 40,000 points.
- **A thread-static must not keep the font.** `Cf2Buffer` objects point into the font's data (a subroutine or a charstring), so `Cf2InterpBuffers.Release` points them at nothing;
  `Cf2HintMap.Release` lets go of the font, the initial map and the moves; `Cf2Outline.Release` of the decoder and the error. Capacity above 4,096 elements is dropped.
- The interpreter's operand stack of a CFF2 font is as big as its `maxstack` (513 in most fonts, up to 65,535 declared), so the retention limit for stacks is 4,096 and not the 512
  first written: a limit under 513 would allocate a stack for every glyph of an ordinary CFF2 font and lose most of the gain.

## Not done

Caching the hint-independent parts of `Cf2Blues` for each (size, subfont): what is left of `Cf2Blues` and its `Cf2Blue` array is about 12 % of the allocation that is left (about 0.3 KB a glyph),
and the cached object would be shared between threads and hold `EmBox*Edge` values that `Build` takes by `ref`. `Cf2Buffer` as a struct in an `[InlineArray(17)]`: the pooled objects give the same
saving with no change to the code that holds a `Cf2Buffer` in a local. Neither is worth its risk against what they would save.
