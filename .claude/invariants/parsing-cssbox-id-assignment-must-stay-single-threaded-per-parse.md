# CssBox.Id assignment must stay single-threaded within one DomParser.GenerateCssTree call

`CssBox.Id` is assigned from `AsyncLocal<StrongBox<uint>>` counter state (`CssBox._idCounter`, see
[.claude/recent-fixes/2026-08-08-cssbox-id-counter-cross-test-race.md](../recent-fixes/2026-08-08-cssbox-id-counter-cross-test-race.md)
while that file still exists), scoped so that concurrently-running, *independent* parses (different
`HtmlContainerInt`/test/request) never see or perturb each other's sequence -
`HtmlContainerInt.PerformLayout`'s `@container` convergence loop depends on the *same* document
producing the *same* Id sequence across its own two back-to-back re-parses.

That isolation is between independent async flows, established by `AsyncLocal.Value`'s copy-on-write
semantics. It does **not** protect against parallelism *within* one flow: `IdCounterBox`'s lazily-
created `StrongBox<uint>` is shared by reference across any branches that fork from the same
`AsyncLocal` context without an intervening `Set`, and the constructor's `++IdCounterBox.Value` is a
plain non-atomic read-modify-write.

**Don't parallelize `CssBox` tree construction (or any anonymous/pseudo-box creation) within a single
`DomParser.GenerateCssTree` call** - a `Parallel.ForEach`/`Task.WhenAll` over sibling node
construction before any branch has incremented the counter would have every branch share the same
boxed `uint` and race on the increment, silently producing duplicate or skipped Ids. That reintroduces
the exact class of bug the AsyncLocal migration fixed, in a subtler form: the by-Id container-size
lookup would fail unpredictably depending on which duplicate/missing Id landed where, and a
counter-only unit test (which only checks Id *values*, not concurrent-construction behavior) wouldn't
catch it. If tree construction is ever parallelized for performance, `CssBox.Id` assignment needs its
own fix first (e.g. `Interlocked.Increment`, or moving to an explicit per-parse counter threaded
through construction instead of ambient `AsyncLocal` state).
