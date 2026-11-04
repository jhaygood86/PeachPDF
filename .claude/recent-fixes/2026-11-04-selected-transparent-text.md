# Selected `background-clip: text` text vanished

`background-clip: text; color: transparent` (gradient text) disappeared when selected: the selection fill (opaque) covers the clipped
background the glyphs normally show through, and the glyphs themselves were still the element's transparent colour.

Fix: `FragmentPainter.Selection.cs` `VisibleForeground` - when a highlight has a visible fill, sets no colour of its own, and the word's own
colour has alpha 0, the highlight gets black or white (by fill luminance) as its foreground. Visible text colours and explicit `::selection`
/ `SelectionForeground` colours are untouched.

Tests: `SelectionStylingTests` (`SelectedTransparentText_WithBackgroundClipText_StaysReadable`, plus the unselected gradient regression).
