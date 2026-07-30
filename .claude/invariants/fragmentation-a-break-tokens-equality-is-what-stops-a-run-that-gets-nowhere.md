# A break token's equality is what stops a run that gets nowhere

_CSS Fragmentation Level 3 §4.3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

`HtmlContainerInt.LayoutDocument`'s defence against a pass that resumes at the same point forever is
`if (next == token || HasAlreadyBeenEntered(next))` — the run has arrived at a record it has already
been handed, so lay the remainder out monolithically instead. **That is an equality test on a
`record`**, and it is easy to break without any test noticing, because the failure mode is a slow run
rather than a wrong one.

**A collection member is compared by reference.** The compiler's generated equality uses
`EqualityComparer<T>.Default` per member, so a `record` carrying an `IReadOnlyList<T>` compares two
structurally identical instances as unequal whenever they are different objects — which they always
are, since every pass builds its own. `TableBreakToken` carries three, and before it was given a
contents-based `Equals` a table that made no progress ran to the 100,000-pass cap: measured at 1m52s
for one fixture. `InlineBreakToken` carries one — `ResumePath` — and had the same defect latently: its
single construction site passes an empty collection expression, which the compiler serves from a cached
singleton, so the two instances happened to compare equal. **Any new token type carrying a collection
has to hand-write `Equals`**, and must not be excused by an accident of how today's callers build it.

**The question is about a *pair*, not about two consecutive passes.** A pass is a function of the
fragmentainer it fills and the record it resumes from, so a run that arrives at a `(slot, token)` pair
it has been entered with before cannot advance, whatever the distance back to that pass. Comparing
only against the pass just run recognizes a cycle exactly one pass long and misses every longer one —
which is not hypothetical: a continuation that forgot to carry `TableBreakToken.FinishedCells` forward
alternated between naming one finished cell and none, and the two faults hid each other, so fixing
either alone left the run spinning. The driver therefore remembers every pair (`_passEntrySet`), and
the consecutive test is kept only as the cheap special case that avoids running the offending pass
once more.

**A pass re-entry must forget the entries it replaces.** The keep-with-next run pull deliberately goes
back to an earlier pass with the pair it was first entered with, and rolls the box tree back so that
pass produces something different; `widows` re-enters with a rebuilt record. The entries from the
re-entry point on describe passes that are being *replaced*, not repeated, so `TruncatePassEntries`
drops them — leave them behind and a legitimate rewind is indistinguishable from a stall, and the
document silently degrades to §4.3's last resort.

The symptom to recognize: a fixture that passes but takes tens of seconds, with
`HtmlContainerInt.FragmentainerPasses` in the thousands. Before
[#422](https://github.com/jhaygood86/PeachPDF/issues/422), it was also not only slow but silently
*wrong*: the run left the loop at `MaxFragmentainers` having emitted nothing after the point it got
stuck at, reported as a successful render, with `LastResortRelayouts` at **zero** — the honest statement
that the backstop never fired at all. Measured on a two-pass cycle: two of the three words after the
stall were missing. `LayoutDocument`'s loop now treats running out of the budget as a no-progress
condition in its own right, routing it through the same monolithic recovery a detected cycle already
uses, so a broken equality test degrades to *slow-but-complete* rather than *silently truncated* —
`LastResortRelayouts` reads 1, not 0, once the budget is spent. Still worth fixing the equality rather
than relying on the fallback: the recovery pays for the whole 100,000-pass budget before it fires, and
`LastResortRelayouts` is still what to assert on — never elapsed time, which says only "slow" and rots
into a flaky bound (see
[the reflow fixtures' platform sensitivity](testing-the-reflow-fixtures-are-platform-sensitive-by-design.md)
for the neighbouring lesson).
