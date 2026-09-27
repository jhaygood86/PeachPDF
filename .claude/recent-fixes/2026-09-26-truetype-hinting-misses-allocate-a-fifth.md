# A TrueType hinting miss allocates a fifth of what it did

Measured on the cold benchmark (every glyph of a font into an empty cache; `PeachDrawing.Text.Benchmarks`, with `-- --quick` for the A/B below, baseline in
[the benchmarks entry](2026-09-26-hinting-benchmarks-and-baseline.md)); base is `origin/main` before this change, same machine and session, three alternating rounds
each, timings indicative (other work was loading the machine), allocations exact.

| Font (glyphs) | Allocated, before | Allocated, after | Least time, before | Least time, after |
| --- | --- | --- | --- | --- |
| LiberationSans (2620) | 25,765 KB | 4,725 KB | 16.0-18.3 ms | 7.5-7.9 ms |
| LiberationSerif (2602) | 29,075 KB | 5,402 KB | 12.9-15.2 ms | 7.7-8.8 ms |
| SourceSans3 (2478) | 26,138 KB | 4,943 KB | 39.8-46.9 ms | 36.2-38.3 ms |
| HintingOpcodes (391) | 2,607 KB | 453 KB | 1.25-1.31 ms | 0.95-1.01 ms |
| HintingCff, HintingCffCid | unchanged (the CFF engine is a separate change) | | | |

Per glyph that is about 10.0 KB to 1.8 KB for LiberationSans (the loader alone, without the outline it makes: 4.4 KB to 0.5 KB). The gen0 collections of the
benchmark are gone. SourceSans3 is bound by the interpreter, not by allocation, so it gains least in time.

## What it was

- **A loader per glyph, and everything it works in.** `TtGlyphLoader` was new for each glyph, with two `Outline`s (four arrays each), the composite path, four
  scratch arrays for the zone, and then a zone copy, an instruction array and a component list for a composite, and finally a copy of the result into four fresh
  arrays. That is now one loader per thread, kept by the thread's `TtExecContext` (`Loader`), reset by `Init`, with a stack of components (`_subglyphs`, a composite
  that is a component reads its own after its parent's) and a byte buffer for instructions (`RentInstructionBuffer`; the interpreter reads by `_codeSize`, and a program
  cannot define a function in a glyph program, so nothing refers to the buffer after the glyph is hinted).
- **The engine makes the outline from the loader's own arrays** (`TtGlyphLoader.Load<TState, TResult>` hands a reader a `TtGlyphView` of spans; the state parameter
  keeps the reader a cached static delegate, no closure). `Load(size, glyph)` still copies, which is what the FreeType goldens compare, so the fused path is checked against
  it by `TrueTypeLoaderScratchTests` (all glyphs of four fonts at three sizes: the engine's outline equals a conversion of the copy written out again in the test).
- **`BuildContour` was a large share of a miss.** It copied every contour's points into a second list before walking them and let the list of segments double up from
  empty (about 56 bytes a segment: a contour of 30 grew arrays of 4, 8, 16 and 32). It walks the points in place now and counts the segments first
  (`OutlineContour(start, segments)`), and the engine keeps one list of raw points per thread. A randomized test compares it with the old algorithm (written out in the
  test), on contours of every shape, and another that the segment list ends up exactly as big as it is.
- Smaller: the twilight zone copy no longer clears the room it is about to fill (`TtGlyphZone.CopyFrom`), the blend coordinates `GETVARIATION` returns are made once for
  the face (`TtFace.BlendCoordinates`), the list of an outline's contours is presized.

## Traps

- **A thread-static that keeps capacity keeps a hostile font's capacity.** A font that declares 60,000 twilight points and stack elements would leave megabytes in every
  thread that ever hinted a glyph of it. `TtExecContext.Return` drops what is above 4,096 elements (16,384 for the stack, 16 KiB of instructions, 512 components, 4,096 raw
  points) and `Release` lets go of the font, size and context (a loader in a thread-static must not keep the last font alive). `WhatAHostileFontAsksForIsNotKeptByTheThread`
  fails with 150,532 retained elements when the trim is disabled.
- **Reused arrays are not zeroed.** What the loader reads was already all written before it is read (tags, coordinates, phantom points), and the interpreter never looks at
  `.Length` of a zone array; both were checked by reading, and `GlyphsLoadedInAnyOrderAreTheSameWhateverTheScratchHeldBefore` loads composites, simple glyphs and refusals
  through the same scratch in random order against each glyph loaded on a thread of its own.
- **A `ref` into the component stack is stale after a component loads** (the stack may be resized by the nested composite), so `ProcessCompositeComponent` takes the entry
  by `in` fetched again after the load, not one taken before it. `ApplyCompositeDeltas` (variable fonts) is tested through the API for the first time
  (`TheComponentOffsetsOfACompositeAreMovedByTheVariationDeltasOfAnInstance`; fails when the deltas are not applied). A hinted composite rounds its offsets to the grid when the
  component says so, so the fitted accent is up to half a pixel from the scaled design.

## Not done

Pooling the five `TtGlyphZone` placeholder objects `Return` makes (one object per glyph, under 100 bytes: not worth the risk of a zone mutated through a shared
instance). `ApplySimpleDeltas`/`ApplyCompositeDeltas`' arrays of doubles: they are the variable-font instance path, which is not the common case and is in flux, so it was left as it is.
