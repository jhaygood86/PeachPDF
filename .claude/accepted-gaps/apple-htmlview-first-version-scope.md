# PeachBrackets.Apple's first version leaves out the interactive layers

The first `PeachBrackets.Apple.HtmlView` does loading, GPU-composited painting, scroll and zoom, pointer state, cursor, links, selection and copy. It does **not**
yet do: in-place composition (marked text) through `UITextInput` on iOS, VoiceOver table/set/range semantics and text-granularity navigation (the tree itself is exposed), or direct touch on a Mac with a touch display (the Mac view reports a fine pointer
only). `PeachBrackets.WinUI` and `PeachBrackets.Android` have all of these; the Apple view is meant to reach the same set, built on `HtmlDocument`'s edit,
focus and accessibility APIs and the Android `Logic/` helpers (`CaretMoves`, `PickerValues`, `TextTraversal`, `ImeEdits`) already in this repository.

Listed in docs/peachbrackets-apple.md under Limits. Delete this file when the last of them lands.
