# A continuation's block-start decorations are not real edges unless `clone` says so

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

`ClientTop` folds in a box's top border and padding, which is right for the fragment the box
**started** in and wrong for every later one: §6.2's `slice` renders the box as though it were never
broken and cuts it at the break, so nothing is inserted there. Any site that positions resumed content
against a containing block's `ClientTop` therefore has to ask `box-decoration-break` first — the
amount is `HasCloneDecorations ? DomUtils.ClonedBlockStart(...) : 0` above the fragmentainer's own
content edge, never the containing block's edge unconditionally.

**The symptom is silent, because the painter is right.** `FragmentEmitter` clears the top edge of a
fragment that resumes an earlier one, so a wrongly-inserted inset is not a doubled border — it is a
strip of blank space at the head of every continuation with nothing drawn in it, and `slice` and
`clone` render **identically**, which is the tell. Measured at 16pt with `padding-top: 10pt;
border-top: 6pt`.

**Ask which box resumed, not where it sits.** A continuation and a box that genuinely begins at the
fragmentainer's top share a coordinate, so no coordinate test separates them. What separates them is
that a resumption record naming a box is exactly what makes its child loop a resumed one — so inside
such a loop, *that* box is continuing and any other containing block is not.

The inline path (`CssLayoutEngine.CreateLineBoxes`) has had this right since §6.2 landed. When a block
path needs the same answer, make it call the same shape rather than deriving a second one — the two
getting different answers inside one feature is what this cost.
