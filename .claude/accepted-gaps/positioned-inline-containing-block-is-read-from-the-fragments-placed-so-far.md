# A positioned inline's containing block is read from the fragments placed so far (#1336)

**Gap:** an absolutely positioned box whose nearest positioned ancestor is an inline gets its containing
block from that inline's first and last fragments (CSS Positioned Layout 3 §2.1, CSS 2.1 §10.1 (4.1))
via `DomUtils.InlineContainingBlockOf`. When the flow holding the inline stops at a page break
(`CssLayoutEngine.CreateLineBoxes`'s `coordinates.Break` branch), the boxes set aside so far are laid out
by `LayoutAbsolutelyPositioned(g, coordinates, stopped.ResumeWordIndex)` on that same pass, before the
fragments on the later page exist. Their `bottom`/`right` therefore resolve against the last fragment *on
that page*, not the inline's true last fragment. `top`/`left` come from the first fragment, which is on
the page being filled, so they are right.

What was closed by [#1304's fix](../recent-fixes/2026-09-24-abspos-nested-in-a-float-defers-to-the-inline-flow.md):
a box nested in a float or a block-content inline-block *inside* the positioned inline used to be laid out
before any fragment existed and landed at the sheet origin. It is now handed to the flow that owns the
inline and laid out with the directly nested ones, so it shares this remaining edge and nothing worse.

**Why out of scope:** closing it needs the box's layout deferred until the block's flow has finished across
every fragmentainer, or re-resolved once the last fragment exists. A box in the `Break` branch is also what
lets its content be distributed over the pages it reaches, so deferring it changes which pass owns it.
Tracked as [#1336](https://github.com/jhaygood86/PeachPDF/issues/1336).
