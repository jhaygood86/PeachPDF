# A relocated table left its abandoned headers behind

A table with `break-inside: avoid` (or the `page-break-inside` alias) that does not fit in the rest
of a page is laid out again from the start on the next one, per
[css-break-3 §4.2](https://www.w3.org/TR/css-break-3/#break-within). If it repeats a `<thead>`, the
abandoned run's headers were still drawn.

`CssLayoutEngineTable.RestoreStructureFromAnyPreviousRun` already undid the visible half — the
proxies the last run put in the table's child list. But a proxy is not what paint reads: laying one
out also records a `CapturedInstance` with `FragmentEmitter`, and nothing dropped those. One call to
`ClearCapturedInstances(_tableBox)` on a fresh pass, exactly as `CssLayoutEngineColumns` has always
made for the same reason, fixes it.

## What measuring it turned up

- **The header was drawn three times, not twice.** Once stranded on the page the table had *left*,
  at the position the abandoned run gave it — a header floating alone above the bottom margin with
  no table under it — and twice on top of itself at the top of the page it moved to, where the
  abandoned run's page break had put the repeat. Traced by printing every `CreateHeaderProxy` call
  site: the first run produced two (the in-flow one and the break's), the second run one, and all
  three reached paint.
- **The box tree was already right, which is how it survived.** `RepeatingTableRelayoutTests`
  counts `DisplayMode.TableHeaderGroup` boxes under the root and asserts 1–2. That count was
  correct — the restore does remove the proxies — while the output was wrong. Instrumenting the
  restore confirmed it: proxies before 2, after 0, and the run that followed added exactly one.
  So the new test counts words per fragmentainer in `FragmentTree` instead, which is the only place
  the defect is visible. That is now written down as an invariant.
- **Only a *relocated* table is affected.** The same table allowed to split across the boundary
  repeats its header once per page and always did. That case is the second test here, so a fix that
  suppressed repetition outright rather than dropping stale records would fail it.
- **Chrome agrees exactly.** The repro rendered against Chrome 152 gives one header, on the page the
  table lands on, at the same coordinates this now produces (39.9 vs 39.8pt, the usual rounding).

## Evidence

`RelocatedTableHeaderInstanceTests`: three filler heights that each push the table over the
boundary, asserting nothing on the page it left and exactly one where it landed, plus the
split-table control. All three relocation cases fail against `main` (0 expected / 1 actual on the
abandoned page, 1 / 2 on the landing page); the control passes there and here. Full suite green on
net8.0 (10,391), 0 new build warnings.
