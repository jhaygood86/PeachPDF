# Text after an absolutely positioned inline box stays in its column (#2247)

`DomParser.JoinsAsOutOfFlow` refused an absolutely positioned child of a multi-column container, so
`aaa <span style="position:absolute"></span> bbb` was split into two anonymous blocks and `bbb` started
the next column. The refusal is gone: the abs box joins the inline run like anywhere else (#1299).

**Trap:** a container holding *only* absolutely positioned children must not be wrapped for columns.
Joined to the run, it counted as inline-only content, was wrapped, and the abs child laid out at width 0
(`ContainerHoldingOnlyAnAbsolutelyPositionedChild_LaysItOut`). `wrapsInlineRunForColumns` therefore
requires a non-floated, non-absolute run member.

Evidence: `AbsolutelyPositionedChild_InAMultiColumnContainer_DoesNotMoveTheTextAfterItToTheNextColumn`
fails without the change; the Multicol*/Absolute* classes pass.
