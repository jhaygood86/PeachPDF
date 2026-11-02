# `GetAccessibleNodes` is replaced by `GetAccessibilityTree`

**Before:** `HtmlDocument.GetAccessibleNodes()` returned a flat list of `AccessibleNode`s built from the text runs - one for each block of text, with a child per link - and needed `RecordTextRuns`. It had four roles (`Text`, `Heading`, `ListItem`, `Link`), no names other than the text, no state and nothing for images, controls, tables or landmarks.

**Now:** `HtmlDocument.GetAccessibilityTree()` returns an `AccessibilityTree`: a hierarchy of `AccessibilityNode`s with the full set of WAI-ARIA roles, the name and description each has, state, language, focusability, links and the rectangle on every page they are on. It does not need `RecordTextRuns`. `GetAccessibilityNodeAt` finds the innermost node at a point. `AccessibleNode`, `AccessibleRole` and `GetAccessibleNodes` are removed.

**Moving over:** where you had a block's text, read `node.ReadingText`; where you had its links, read `node.ReadingChildren`; `Role`/`HeadingLevel` become `Role`/`Level`; a node that crosses a page break is one node with a rectangle for each page (`Bounds`) instead of one node for each page; `ParentIndex` becomes `Parent`.

**Why:** a screen reader needs more than text: what a control is, what it is called, whether it is checked, where the landmarks are. The old list could not say any of that.
