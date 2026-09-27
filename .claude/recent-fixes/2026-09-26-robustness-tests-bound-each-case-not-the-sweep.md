# The hinting robustness tests bound each case, not the whole sweep

`HintingCff2RobustnessTests.EveryByteOfATableChangedIsAnsweredWithoutAnUnexpectedException` asserted that its whole sweep (every byte of a CFF2 table, three masks, four glyphs
at two sizes: about 1,900 damaged fonts) took under 120 s. It passed in 1 s in Release and 3 s in Debug on a development machine and failed once on macOS CI (net11, under coverage)
at 140 s, with no case having gone wrong. Measured here, the same test takes **16 s under coverage** on the development machine (16 times its Release time), so a runner that is
eight times slower again is over the budget without any bug. A budget on a sweep is a budget on the number of cases times the speed of the runner.

## What replaced it

- `WorkBounds.Case` (`PeachDrawing.Text.Tests/WorkBounds.cs`) runs **one case** (a damaged font, a hostile program, a glyph) and asserts that it took under 30 s and allocated under
  256 MB on its thread. The slowest single case takes a fraction of a second, and a case that a budget of the engine did not stop would take minutes to hours or never end, so the bound
  tells the two apart on any machine; the allocation bound does not depend on the speed of the machine at all. Every sweep still visits every case (every byte of the table, every
  iteration of the fuzz).
- Converted: the sweeps (`EveryByteOfATableChanged...`, `RandomDamageToAVariableFont...`, `ScrambledCffTables...`, `ScrambledFonts...`, `RandomBytesAsEveryProgram...`,
  and `FlippingAnyByteOfTheFixturesCff2Table...` of the CFF2 outline reader), and the single-scenario limits of 5 to 20 s of the hostile-program tests, which had the same weakness to a
  smaller degree (a hostile composite that takes 0.16 s in Debug is 7 s on a machine 47 times slower, over its limit of 5 s). The sweeps draw their random numbers in the same order as before, so
  each fuzz visits the same fonts.
- What the tests prove is unchanged: the expected exception types, the counts of hinted and refused glyphs, and that a program that would run for ever is stopped (`Assert.Throws<HintingException>`).
  Mutation check: with the bound set to one tick, 21 of the 109 tests of these classes fail.

## Not done

A deterministic counter of the interpreters' own steps (the instruction and loop budgets) to assert against: it would need a counter in production code, or a test hook in each of the two
interpreters, for a property (a budget stops a program) that `Assert.Throws<HintingException>` already shows.
