# A complexity guard asserts a count, not the clock

_A trap this repo has paid for at least once._

A test that exists to catch accidental O(n²) behaviour must assert on something countable — boxes
visited, calls made, passes run — and never on `Stopwatch.ElapsedMilliseconds`. A wall-clock bound has
two failure modes and both were measured on the float-scan guard
(`FloatLayoutRegressionTests`, issue #482):

- **It fails without a defect.** CI runs both target frameworks' test passes concurrently in one
  two-core job, so the contention factor is unbounded and no bound is high enough. The guard tripped at
  16,383 ms against its 15,000 ms bound on `windows-latest` while the *same job's* net10.0 leg passed
  6,815/6,815 on identical source.
- **It passes with the defect.** Raising the bound to absorb that noise is what destroys its
  sensitivity. Measured by deleting the `HasFloatedBoxes` short-circuit and re-running: the float scan
  went from **0** boxes visited to **6,000,869** on the 40-section fixture — the full O(document size)
  walk per box, growing 16× for a 4× document — and the render still finished in **1.9–3.6 s**, i.e.
  the 15,000 ms bound would have passed the exact regression it was named for.

The counter version fails by six orders of magnitude on that same experiment and reads identically on a
loaded machine and an idle one. When a countable proxy does not exist, add one (`FloatScanCalls` /
`FloatScanBoxVisits` on `HtmlContainerInt` are a field increment each) rather than reaching for a timer.

Two corollaries:

- **Count the calls as well as the work**, and assert the call count is non-zero. "Visited no boxes" is
  otherwise indistinguishable from "nobody asked" — the same shape as the ungated-mechanism trap.
  Pair it with a case that makes the counter move (a document that *does* have a float), so the guard
  cannot rot into an assertion about nothing.
- **A timeout is still fine as a hang guard.** `Fact(Timeout = …)` on the CSS parser's
  `…DoesNotTakeForever` tests asserts termination, not speed, with four orders of magnitude of headroom.
  The rule is about bounds that stand in for a complexity claim.
