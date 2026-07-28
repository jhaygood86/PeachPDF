# A break token's equality is what stops a run that gets nowhere

_CSS Fragmentation Level 3 §4.3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

`HtmlContainerInt.LayoutDocument`'s only defence against a pass that resumes at the same point forever
is `if (next == token)` — the pass handed back the record it was given, so lay the remainder out
monolithically instead. **That is an equality test on a `record`**, and two things about it are easy
to break without any test noticing, because the failure mode is a slow run rather than a wrong one.

**A collection member is compared by reference.** The compiler's generated equality uses
`EqualityComparer<T>.Default` per member, so a `record` carrying an `IReadOnlyList<T>` compares two
structurally identical instances as unequal whenever they are different objects — which they always
are, since every pass builds its own. `TableBreakToken` carries three, and before it was given a
contents-based `Equals` a table that made no progress ran to the 100,000-pass cap: measured at 1m52s
for one fixture. Any new token type carrying a collection has to do the same.

**The comparison is between *consecutive* passes only**, so a cycle of length two or more slips
through however correct the equality is. That is not hypothetical: a continuation that forgot to carry
`TableBreakToken.FinishedCells` forward alternated between naming one finished cell and none, and the
two faults hid each other — fixing either alone left the run spinning, so each looked like it had done
nothing.

The symptom to recognize: a fixture that passes but takes tens of seconds, with
`HtmlContainerInt.FragmentainerPasses` in the thousands and `LastResortRelayouts` at **zero**. Those
two counters are the honest statement of "the backstop fired", and are what to assert on — never
elapsed time, which says only "slow" and rots into a flaky bound (see
[the reflow fixtures' platform sensitivity](testing-the-reflow-fixtures-are-platform-sensitive-by-design.md)
for the neighbouring lesson).
