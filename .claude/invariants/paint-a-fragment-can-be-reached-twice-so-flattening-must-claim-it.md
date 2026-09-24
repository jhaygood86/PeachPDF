# A fragment can be reached twice by PaintFragment, so anything decided there must claim it

`FragmentPainter` reaches a fragment twice when stacking order hoists it: once nested under its parent, once from the ancestor stacking
context that paints it in its own layer. The `_painted` set stops the second visit, but it is consulted in `PaintTagged`, *below*
`PaintFragment`. Anything `PaintFragment` decides before that point runs twice.

**Symptom, measured:** with `TransparencyPolicy.Flatten`, every flattened element was embedded twice (the PDF had two `Do` per region), and
after guarding only the flatten, the second visit fell through to `PaintWithOpacity` and composed an *empty* group, which itself hit the
transparency guard and rejected the document. **Rule:** a decision made in `PaintFragment` that consumes a fragment (flatten, a backdrop
repaint, a projective warp) must `_painted.Add` the fragment and its whole subtree (`MarkPainted`) and answer "already done" for a fragment
already in the set (`TryFlatten` returns true for it), rather than letting the visit fall into the ordinary paths.
