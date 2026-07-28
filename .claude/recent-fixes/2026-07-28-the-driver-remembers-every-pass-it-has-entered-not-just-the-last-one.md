# The driver remembers every pass it has entered, not just the last one

_Landed 2026-07-28._

[Issue #403](https://github.com/jhaygood86/PeachPDF/issues/403). `HtmlContainerInt.LayoutDocument`'s
no-progress backstop asked whether the pass that just ran handed back the record it was given. That
recognizes a cycle exactly **one** pass long. A cycle of two or more slipped through it entirely — and
what the driver then does is not recover but run to `MaxFragmentainers` (100,000) and leave the loop
having emitted nothing after the point it got stuck at. **The document is truncated and the render
reports success.** The backstop now asks the general question: a pass is a function of the
fragmentainer it fills and the record it resumes from, so arriving at a `(slot, token)` pair the run
has already been entered with is what "this cannot advance" means, and the consecutive test is the
special case of it kept only to avoid running the offending pass one more time.

**The load-bearing half is the truncation, not the slowness.** The invariant file already said a
defeated backstop shows up as a slow run; it did not say that content goes missing, and that is what
makes it a defect rather than a performance note. Measured, not reasoned: with the fix neutered,
`ACycleTwoPassesLong_KeepsTheDocumentsContent` renders `["alpha"]` where the document holds `alpha`,
`beta`, `gamma` — the two paragraphs after the stall are simply absent, and
`LastResortRelayouts` is **0**, so nothing anywhere says so.

**A pass re-entry has to forget the entries it replaces**, which is the one thing that makes the
general test safe. `TryRewindForRunPull` deliberately goes back to an earlier pass *with the pair that
pass was first entered with*, having rolled the box tree back so it produces something different. Left
in the set, that legitimate rewind is indistinguishable from a stall and every document that takes one
degrades to §4.3's last resort. `TruncatePassEntries` mirrors the `_passEntries` truncation that was
already there for exactly the same reason; the set is rebuilt from the list rather than maintained
separately, because two structures that can disagree about which passes exist is the bug this is
guarding against. `widows` needs nothing: it re-enters with a *rebuilt* record, which is a different
pair by construction.

**`InlineBreakToken` had the collection-equality defect the invariant file warns about, latently.** It
carries `IReadOnlyList<int> ResumePath` with no `Equals` override, so the compiler compares it by
reference — and it survived only because its single construction site passes an empty collection
expression, which Roslyn serves from a cached singleton. The accident, not the code, was doing the
work; the moment a non-empty path is ever built (which is the field's documented purpose) any inline
resumption becomes invisible to the backstop. It now compares by contents like `TableBreakToken`, and
the test deliberately builds two `List<int>`s rather than two collection expressions, since the latter
would pass against the unfixed code.

**Deliberately not done: routing the §4.3 movers' page-index questions through `SlotStartingAt`.**
`EarlyBreak.Discover` asks for its `Slot` with `SlotStartingAt` (a top-edge convention, tolerant by
`PageBoundaryEpsilon`) and for `ownPageTop`/`destinationBand` with raw `PageIndexOf` on the same class
of coordinate, and `CssBox.PerformLayoutEpilogue`'s `avoid` and `widows` movers do the same. That reads
like a violation of
[one membership question, one tolerance](../invariants/fragmentation-one-membership-question-is-asked-with-one-tolerance.md),
and it was the leading hypothesis here. It does not survive contact: raw `PageIndexOf` never reports a
*later* slot than the true one for a top edge, so the mover's target is always at or below the box's
own top, and the disagreement window is only the half-point where the two conventions are meant to
differ. Swept and found nothing (below). Changing it would be untested churn in the most
regression-prone code in the engine, so it is left exactly as it is — with this note, so the next
reader does not re-run the hypothesis.

**What the sweep says, so it is not run an eighth time.** #403 is alignment-sensitive, and every prior
sweep varied whole lines. This one varied *sub-line* offsets: 4,001 filler heights in 0.1pt steps
across two and a half bands, times four shapes — the reported three declarations, `avoid` alone, an
`<h2>` above it, and content after it — inside an `<article>`, on the 200pt/20pt fixture grid. 16,004
layouts, asserting both `LastResortRelayouts == 0` and a pass-count bound of 12. **Zero** stalls and
zero walks. That closes the sub-point-alignment axis the way the earlier six sweeps closed the
declaration-combination axis, and it is also the negative evidence for the `FitsInFragmentainer`
over-measure (`ActualBottom - Location.Y` on a *completing* straddler includes the inter-band gap, so
it can answer "does not fit" for a box that would): a walk down the document is what that would cause,
and the pass-count bound would have caught one.

**The reporter's own document was not re-run**, and this note should not be read as saying it was:
`repro.html`/`contract.css` lived in a scratchpad and were never attached to the issue. What is fixed
is the class the issue's title names — a driver that cannot advance past a fragmentainer boundary —
for cycles of any length, and with content kept rather than silently dropped.

Tests: `NoProgressBackstopTests` (8, two new: the two-pass cycle is recognized and its content
survives — both fail with the change neutered, the second by losing two of three words) and
`BreakTokenTests` (9, four new for `InlineBreakToken`'s equality). Full net8.0 suite green (6848 passed,
9 skipped), CLI green (96), `dotnet build PeachPDF.slnx -t:Rebuild` **0 warnings**, **100% diff
coverage**, and **70 of 70 showcases byte-identical** to `main` once the two PDFsharp header timestamps
and the four in-object ones are normalized — which is the real statement that the general test fires
on nothing that was previously working.
