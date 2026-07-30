# TryRestartAt re-appends a run instead of clearing its own latch

_Landed 2026-07-30._

**#390's stage 3** ([issue #516](https://github.com/jhaygood86/PeachPDF/issues/516)): "with the
parent holding the offset, `TryRestartAt` no longer has to re-place a box — it re-appends."
`TryRestartAt`'s own retraction step — a hand-written loop clearing `_earlyBreakTaken` on every box
from the run's head up to (but not including) the box that raised the restart — turned out to be
dead code. `BeginLayoutPass` already resets that same flag, unconditionally, on *every* entry to
`PerformLayoutImp`, and every box the rewound loop reaches from the head on gets exactly that: a
fresh `PerformLayout` call, indistinguishable from one an ordinary forward walk would make on a
child it had not reached yet. That is what "re-appends" means in practice — the loop does not
retract anything of its own first; it just resumes the same walk it already had, and the walk's own
per-entry reset does the rest. Traced rather than assumed: the for-loop that rewinds to `resumeFrom`
walks forward through every index up to and past `raisedAt` again, so even the box that raised the
decision gets a second `PerformLayoutImp` call and the same unconditional reset — which is also why
the old comment about that box "keeping its latch" never actually held; nothing read the stale value
before the reset overwrote it.

**One live worry going in, closed by reading rather than guessing.** The obvious alternative
reading — that "re-appends" meant giving `TryRestartAt` the *same* rollback the cross-pass rewind
uses (`PassRewind.RollBackTo`, which also clears `_prologueDone` and re-runs the prologue) — turned
out to be the wrong direction entirely. `_earlyBreakRetryTop`'s own retry (the sibling mechanism for
`decision.BeforeBox == this`) explicitly does *not* reset the prologue, and says why in its own
comment: doing so would register named strings and the named page a second time. The actual
correctness net for that case is `PerformLayoutEpilogue`'s own tail sync — `NamedStrings` and
`RegisteredNamedPageElement` are corrected in place against the box's final `Location.Y` on every
epilogue run, prologue or no — and `PrologueReentryRegistrationTests` already covers it for the
sibling retry. `TryRestartAt`'s boxes reach the same epilogue tail on their own re-entry, so they get
the same correction for free; forcing a prologue re-run onto them would have been a regression risk
for zero benefit, not a fix.

**`ResetChildrenForRefill` retired.** It was already a two-line wrapper around
`PassRewind.RollBackTo` (`CssLayoutEngineColumns`'s measurement-pass-to-real-fill handoff and its
own balance-retry loop), so both call sites now call `PassRewind.RollBackTo` directly. Byte-identical
behaviour — it is the same call with the wrapper's name removed — and it is what the invariants file
now calls three retraction mechanisms instead of five: `FragmentEmitter.InvalidateFrom`,
`DiscardLineBoxesFrom`, and `PassRewind.RollBackTo` (already shared by #355's pass re-entry and #371's
rewind, now also the columns engine's only name for it).

**What #516 leaves open.** `DiscardLineBoxesFrom` is untouched — it is inline layout's own
retraction, and every #390 stage states inline layout as out of scope. `FragmentEmitter.InvalidateFrom`
is untouched too: it retracts *emitted fragments*, a later, different layer than anything `TryRestartAt`
touches (confirmed by `HtmlContainerInt.InvalidateEmittedFragmentsFor`'s own gate, `HoldsFragmentsFor`,
which is false for a box still inside the pass that is filling it — nothing is frozen yet for
`TryRestartAt` to invalidate). Both stay as recorded reasons rather than silent gaps.

**Evidence.** Full net8.0 suite green (6982 passed, 0 failed, 9 skipped) — including the
`EarlyBreakLayoutIntegrationTests.PulledRun_*` family, which exercises exactly this restart path
end to end (interior-gap spacing, exactly-once word claims, the repeating-table replay case, the
already-restarted-this-pass guard) — and `dotnet build PeachPDF.slnx -t:Rebuild` at zero warnings.
Both touched call sites in `CssLayoutEngineColumns` show 285 and 118 hits in the coverage run, so the
inlined `PassRewind.RollBackTo` calls are exercised, not merely reachable. No new fixture was added:
every scenario this change could plausibly break already has a named test asserting it, which is the
existing coverage doing exactly what `EarlyBreakLayoutIntegrationTests`' own comments say it is for.
