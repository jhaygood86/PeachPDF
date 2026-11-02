# Android: reading a node by character, word and paragraph

`TextTraversal` (in `Logic/`, so it is tested without a device) steps through a string by text element, word or paragraph, forward and back; the provider calls it for `ACTION_NEXT_AT_MOVEMENT_GRANULARITY`/`PREVIOUS` and keeps the position per node (`_textCursor`, cleared when the tree is rebuilt).

- **A character is a text element**, not a UTF-16 unit: `StringInfo.GetNextTextElementLength` forward, and the element starts from `ParseCombiningCharacters` backwards. Stepping a unit at a time splits an emoji and reads half of it.
- **A word keeps an apostrophe inside it** (`Don't`, `believin'`), so TalkBack does not read "Don" and "t".
- **Lines and pages are not offered**, because the node does not know where its lines break; `MovementGranularities` lists only what is answered.
- **`AccessibilityEvent.SetAction` takes a `GlobalAction` in this binding** and not the view action, so the traversed event carries `FromIndex`, `ToIndex` and the granularity and no action, which is what TalkBack reads.
- **Not run on a device in this change** (builds with the 10.0.4xx SDK; logic is unit tested).
