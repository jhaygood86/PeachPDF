# Emission no longer re-walks the whole box tree for every page

`FragmentEmitter.EmitSlot` built each page's draft tree by recursing from the document root with
no pruning at all. Measured on the css4.pub Icelandic dictionary (31 chapters, ~14,650 paragraphs,
36,222 `<b>`, ~255,000 boxes, `.chapter { columns: 2 }`): **~255,000 `BuildDraft` calls per page,
flat**, whether emitting page 1 or page 40. Fragment emission cost **84.7s** against **6.5s** for
all the layout it describes. Introduced by the fragment-tree rewrite, which is why the same file
rendered in 53.8s at `c047bb90` and 9m24s afterwards.

## What "may I skip this subtree?" actually needs

"The emitter walked it and it produced nothing" is **not** sufficient on its own, and this is the
trap three earlier attempts died in. `EmitPass(from, through)` freezes a whole *range* of slots in
one go, **after** the pass that filled them has already flowed content into every one, with no
layout in between. So at any slot below the frontier, "nothing here" equally describes content that
is simply further down — and no write will ever come along to correct it.

Two separately-justified cases, never merged:

- **Behind the frontier** — the box is already in `_frozen`, so it has had its fragments and they
  were contiguous. Empty now means empty later.
- **Ahead of the frontier** — nothing has ever written to the box this generation
  (`CssBox.NeverTouchedThisLayout`), so it holds no positioned content anywhere. The write that
  first positions it lands before the pass placing it ends, hence before the slot needing it is
  frozen.

Excluded is the box *in between* — reached, laid out, simply not here — whose content may be in a
slot the same `EmitPass` range is about to freeze.

## What was measured, not reasoned

- **Per-visit facts must not propagate to ancestors.** A multi-column container's children are each
  visited once per column, so none can be observed individually — but letting that poison the
  *container* made every multicol container unprunable, which on this document is the entire
  optimization. Only `ContentStaysInOneRun` (out-of-flow/fixed/proxy/spacing/marker) travels up.
- **`InvalidateFrom` must bump the observation epoch only past its own early return.** It is
  reached on every block-axis reposition of a box holding fragments — constant during a pass — so
  bumping unconditionally retired observations as fast as they were made. Diagnosed by counting
  skips per slot (`skips=0` on nearly every slot while ~195k marks were being re-made).
- **Never derive the observation from live geometry.** Engines rewrite a box's extent well after
  its own layout pass finishes, and the multi-column engine keeps each column's geometry in its own
  `BoxGeometrySnapshot`, so a box's own fields do not describe every fragment it has. This is what
  killed attempt 2 (476 failures). Two write choke points had to be created instead: `CssBox.Size`
  (through which every `ActualBottom`/`ActualRight` assignment passes — guarded on value change,
  it is on the hot path) and `CssRect.Top` (the only place a word's position is settled).

## The verification is the deliverable as much as the change is

Three previous attempts each broke 460–480 tests and each was caught only by running the whole
suite — never by review. So the oracle was built first: `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1` makes
every slot build its draft tree **twice**, pruned and unpruned, and throws unless the two agree on
all nineteen `Draft` members, on `hasPrintableContent`, and — the part that matters most — on the
set of boxes holding fragments.

That last one is not a detail. Per
[.claude/invariants/fragmentation-which-drafts-exist-decides-whether-a-frozen-slot-is-emitted-again.md](../invariants/fragmentation-which-drafts-exist-decides-whether-a-frozen-slot-is-emitted-again.md),
`_frozen` is the only gate on whether an already-frozen slot is emitted again, so a pruning bug can
leave every draft identical and still change the whole document's emission order. `_frozen` is
snapshotted and rolled back between the two builds (safe — `BuildDraft`'s only emitter mutation is
the idempotent `_frozen.Add`).

It works: the first run under it produced **22** failures, not the ~470 of the blind attempts, each
naming the diverging box and member. Both were real — a rewound keep-with-next pass whose
observations were not retired, and inline content whose words move with no box-level write.

## Where it stands

9m24s → **7m38s**, suite green with and without the harness, solution rebuilds warning-free.

The remaining gap is understood and worth a follow-up rather than a guess: instrumentation shows
observations are still discarded roughly every pass (~195k boxes re-marked per slot), so most slots
still take the full walk and only a few prune hard (18k, 8k, 1.5k calls). Something in the
per-pass write traffic — the columns fill/retry loop's `PassRewind.RollBackTo` over its children is
the prime suspect — clears far more than it needs to. Find it by counting `DiscardEmittedNothing`
calls per call site over one pass; the harness already guarantees any fix stays honest.

Separately, ~28s of this document's time is **two** full HTML parses + CSS cascades, because
`PdfGenerator` re-parses when CSS `@page size` differs from the configured page size. Independent,
simpler, and worth its own issue.
