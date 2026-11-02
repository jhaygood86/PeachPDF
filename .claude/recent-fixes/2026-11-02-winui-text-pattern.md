# WinUI: the Text pattern

`HtmlViewAutomationPeer` and a text field's peer now answer `PatternInterface.Text` with `HtmlTextProvider`/`HtmlTextRange` (`HtmlViewText.cs`),
so Narrator can read a page by character, word, line and paragraph.

- The window-free part is `HtmlTextModel`: the reading text of the accessibility tree as one string, a block per node ending in a line break, and the
  range arithmetic UIA asks for (expand, move, move an endpoint, find). It is unit-tested on its own; the pattern itself is tested in a real window.
- A line is a block. The engine lays text into lines only while painting, and the tree keeps no line boxes, so "line" and "paragraph" are the same
  unit. Bounding rectangles are the rectangles of the blocks a range touches, not of its characters.
- Not done: the selection and caret through the pattern (`SupportedTextSelection.None`), text attributes (`GetAttributeValue`/`FindAttribute` find
  nothing), `Select`. Each needs a map from a text offset to the engine's text positions.
- Trap: `ExpandToEnclosingUnit` on a range that is not empty keeps the whole range (it only widens to cover whole units), as UIA specifies; a test
  that expands the document range gets the whole document. Collapse it first.
- Trap: the WinRT projection of `ITextRangeProvider.GetBoundingRectangles` is `void (out double[])`, and `GetText` returns the string.
- `HtmlViewText.cs` is window-bound, so it is excluded from the unit-test coverage run like `HtmlViewAutomation.cs`; the window tests cover it.
