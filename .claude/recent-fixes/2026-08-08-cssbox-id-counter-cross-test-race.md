# `CssBox.Id`'s counter was a process-wide static, racing across concurrent parses

Root-caused the intermittent `ContainerQueryLayoutIntegrationTests` failures noted (but not
root-caused) in [.claude/recent-fixes](.claude/recent-fixes) history and previously chalked up to
unrelated pre-existing flakiness.

## The load-bearing idea

`HtmlContainerInt.PerformLayout`'s `@container` size-query convergence loop re-parses the *same*
document from scratch between passes and matches each size container's resolved size across passes
by `CssBox.Id` (`ContainerQuerySizes`'s own remarks document this explicitly: "a real element's `Id`
is assigned purely during HTML parsing... stable across the repeated re-parses of identical HTML").
That guarantee depended on `CssBox._idCounter` being a plain `static uint`, incremented with a
non-atomic `++_idCounter` and reset via `ClearCounter()` on every `SetHtml`/`DomParser
.GenerateCssTree` call.

That's process-wide, not per-parse - and xUnit runs test classes in parallel by default, with each
test's own `HtmlContainerInt` instance independently thread-safe to use concurrently with another
instance's (CLAUDE.md's "Thread safety" section documents that per-instance contract). A totally
unrelated concurrently-running test's `SetHtml` call could reset or perturb *this* test's counter
mid-parse or between its own two convergence-loop passes, desynchronizing the Id sequence the second
pass produces from the first pass's - so `ContainerQuerySizes.TryGet` would silently miss the
container's size, the query condition would stay evaluated as "no data yet" (false), and the test
would see the pre-fix (never-applies) color instead of the expected one. Nondeterministic, and
specifically most visible in this one test class because it's the only consumer that depends on
`Id` stability *across two sequential calls within one test*, not just uniqueness within one parse.

Fixed by scoping the counter to `AsyncLocal<StrongBox<uint>>` instead of a plain `static uint` -
each call's own logical async flow (each test method's own call chain) gets its own counter value
that other, independently-running async flows on other threads never see or perturb, while
`ClearCounter()`/the increment still behave like ordinary sequential mutable state within one flow's
own pass-after-pass calls.

## What didn't need to change

`ContainerQuerySizes`'s Id-based matching design itself was correct - the bug was purely in the
counter's storage lifetime, not the matching strategy. No other `CssBox` state was found to have the
same problem (this was the only plain-`static`-with-a-process-wide-reset field in the class).

## Evidence

- 5 consecutive full `net8.0` suite runs (no `--filter`, ordinary parallel xUnit execution): 8315
  passed, 0 failed, 9 skipped, every run - the exact scenario that previously reproduced the
  `ContainerQueryLayoutIntegrationTests.SizeFeature_MatchesAgainstTheInlineSizeContainersOwnWidth`
  failure intermittently (reproduced once in 2 runs earlier the same session, before this fix).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- `diff-cover` against `main`: 100% diff coverage.
