# The declared-width advance in FlowBox's child loop serves both inline-block paths

`CssLayoutEngine.FlowBox`'s per-child loop reserves an `inline-block`'s declared content width on the
line *after* the `if`/`else if` dispatch, so it runs for **both** ways an inline-block is placed:

- the inline-flow path (inlines-only content), where `FinalizeFlowBoxExit` has already put the cursor
  in the same place — making the reservation a harmless `Math.Max` no-op;
- `FlowAtomicBlockContentChild` (block-level content), where it is **load-bearing**.

`FlowAtomicBlockContentChild` advances to `b.ClientRight`, and the caller then adds the box's trailing
margin/border/padding. That comes out one *leading* padding short of the declared content box, so
without the reservation the box reserves less room than it declares. Measured on the `cascade_layers`
showcase's `.card` (`width: 150px; padding: 16px; margin: 0 12px 12px 0`): each card advanced the line
**121.5pt** instead of **145.5pt** — exactly its 24pt of padding missing.

Two things make this easy to break and hard to notice:

- The **whole test suite stays green.** No fixture covers a declared-width inline-block holding
  block-level content on a line whose total width matters.
- The symptom does not look local. The document came out narrower, which stopped triggering
  shrink-to-fit, which rendered *the entire page* at a different scale — every glyph on it moved. The
  signal was showcase content-stream parity, not a failing assertion.

So: if the reservation is ever moved inside the dispatch, or dropped because the inline-flow path
already does it, `FlowAtomicBlockContentChild`'s own advance has to be corrected in the same change.
Note that it also overwrites `CssBox.Size` for the box (`b.ActualRight = …`), so the reservation
cannot read the width back off the box afterwards — `ResolveAtomicInlineDeclaredWidth` returns it to
the caller for exactly that reason.
