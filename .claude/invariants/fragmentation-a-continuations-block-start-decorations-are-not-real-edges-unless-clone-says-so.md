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

**Every site that begins a fragmentainer's content has to agree, and there is more than one.** A
column's content is begun both by the box that *continues* into it (`ResumeInTheNextFragmentainer`)
and by the child laid out *afresh* at its head (`ColumnTopForTheChildThisFillBeginsAt`); a chain two
or more levels deep goes through both. Correcting one is worse than correcting neither: the continuing
block is then placed at its containing block's re-inserted content edge while its own children are
placed at the fragmentainer's, so its fragment rectangle sits **below the content it contains** and its
background and border paint outside their own content. Measured at a fragment rect of y=22.00 against
a column content top of 6.00 with rows at 6.00. **Assert this on the emitted `BoxFragment`, not on the
box** — every box-level assertion about the content still passes while it is broken.

**The bound on the sum is the fragmentation context, not the DOM root.** `ClonedBlockStart` walks
ancestors, and the boxes a boundary re-opens are those it falls *inside* — so a column break stops at
the multi-column container, which is not fragmented by its own columns (its border and padding wrap all
of them at once, and `ResumeContentTop` is already inside them). Unbounded, a container that itself
sets `clone` contributes its own block-start spacing to content inside it: measured at 14pt of spurious
indent, and on `slice` content too, since `HasCloneDecorations` is a document-wide flag.

**Name the fragmentation context, not the caller.** The tempting test is "is this containing block the
box whose child loop is running" — but a child's `ParentBox` always *is* that box, so such a test is
false only when the loop's box is not a block container by `CssBox.ContainingBlock`'s walk, and the
containing block is then an ancestor *higher in the same continuing chain*. "It did not resume here"
does not follow from it. Asking instead whether the containing block is the context root answers the
question §6.2 actually turns on, and it collapses into the walk's `stopAt` rather than needing a branch.

The inline path (`CssLayoutEngine.CreateLineBoxes`) has had this right since §6.2 landed. When a block
path needs the same answer, make it call the same shape rather than deriving a second one — the two
getting different answers inside one feature is what this cost.
