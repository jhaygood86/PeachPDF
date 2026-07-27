# The float-scan regression guard counts the work instead of timing it

`FloatLayoutRegressionTests.ManyNestedBlocksWithoutFloats_RendersWithinASaneTimeBound` asserted
`sw.ElapsedMilliseconds < 15000` around a 40-section float-free render. It failed CI on `windows-latest`
at 16,383 ms (issue #482) while the same job's net10.0 leg passed 6,815/6,815 on identical source — the
two TFM passes run concurrently on one two-core runner, both under coverage instrumentation. The bound
had already been raised once for that, and the test's own comment said so.

## What replaced it

Two counters on `HtmlContainerInt`, reset per `PerformLayout`, incremented from
`DomUtils.GetFirstIntersectingFloatBox`:

- `FloatScanCalls` — incremented **before** the `HasFloatedBoxes` short-circuit is consulted, so it says
  how often layout asked the question;
- `FloatScanBoxVisits` — the boxes the walk actually examined, threaded out of the recursive
  `GetNextIntersectingFloatBox` as a `ref int` (no per-node container lookup, one increment per node).

The recursive walk moved into a private `FindIntersectingFloatBox`, purely so the public method has one
exit point at which to record the count.

Four tests replace the one:

- `ManyNestedBlocksWithoutFloats_FloatScanVisitsNoBoxes` — 40 sections: calls > 0 (with a floor tied to
  the box count), visits **exactly 0**;
- `FloatFreeDocument_FloatScanWorkPerBoxDoesNotGrowWithDocumentSize` — 10/20/40 sections: `visits ≤
  boxCount` at every size, `calls ≤ 4 × boxCount`, and calls-per-box at 40 sections no more than 2× the
  rate at 10 (flat is 1.0, quadratic is 4.0);
- `FloatScanCounters_CountTheWalkTheyGuard_WhenAFloatIsPresent` — a floated document must move **both**
  counters, so the two guards above cannot decay into assertions about nothing;
- `ManyNestedBlocksWithoutFloats_StillRendersEveryPage` — the end-to-end `PdfGenerator` render the old
  test also performed, keeping the output assertion and dropping the clock.

## What running it found, and did not find by reading it

Measured on `main`'s algorithm: boxes 606/1206/2406 for 10/20/40 sections, calls 1253/2503/5003, visits
**0** at every size. Calls per box: 2.068 / 2.075 / 2.079 — flat to three digits, which is why the ratio
assertion can be tight without being brittle.

Then the degradation was reintroduced — the `HasFloatedBoxes` short-circuit deleted, leaving the
original O(document size) walk — and the suite re-run:

- visits went 0 → **376,439** at 10 sections and **6,000,869** at 40. That is 16× for a 4× document:
  quadratic, exactly. Both new tests failed, loudly and with the numbers in the message.
- **The same degraded build rendered the 40-section document in 1,873–3,563 ms**, three runs. The
  15,000 ms bound would have *passed* it. So the old guard had already lost the ability to detect the
  regression it was named for; it could only still detect runner contention. That is the load-bearing
  finding of this change and the reason it is an invariant file
  ([testing-a-complexity-guard-asserts-a-count-not-the-clock.md](../invariants/testing-a-complexity-guard-asserts-a-count-not-the-clock.md))
  rather than only a test edit.

## Survey of the rest of the suite

`Stopwatch`/`ElapsedMilliseconds` appear nowhere else in either test project after this change. The
remaining wall-clock constructs are all **hang guards**, which assert termination rather than speed and
are the permitted last-resort form: three `[Fact(Timeout = 10000)]` CSS-parser tests in `CSS/Sheet.cs`
(each parse is sub-millisecond — a comment there now says the timeout is not a performance bound) and
`StylesheetRelativeUrlResolutionTests.CircularImport_DoesNotHang_AndStillRendersRemainingStyles`, whose
15 s `Task.WhenAny` race is against an in-memory loader. None of them stands in for a complexity claim,
so none was converted and no follow-up issue was filed for them.

## Deliberately not done

- **No growth-ratio assertion on the visit count.** With the short-circuit it is 0 at every size, and a
  ratio of zeros says nothing; the linear *bound* (`visits ≤ boxCount`) is the statement that survives
  both cases, and it is what the degraded build fails.
- **No ratio assertion on wall-clock time at two sizes** (issue #482's option 2). It is still a clock:
  a runner that de-schedules one sample and not the other distorts the ratio too.
- **No counter on a floated document's growth.** With floats present the walk is genuinely
  O(document) per lookup by design — that is the algorithm, not a regression — so asserting linearity
  there would fail on correct code.

Evidence: full net8.0 suite green, zero warnings on `dotnet build PeachPDF.slnx -t:Rebuild`. Production
change is two counters and a refactor with no behavioural effect; showcases unchanged.
