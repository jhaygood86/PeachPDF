# An inline-block reads its own vertical-align for its inner lines

_CSS 2.1 §10.8.1 (`vertical-align` places a box within the line box it sits on). Tracker:
[#1439](https://github.com/jhaygood86/PeachPDF/issues/1439)._

`CssLayoutEngine.EffectiveVerticalAlignOf` walks from an anonymous text box up to the nearest element to read
its `vertical-align`. On an inline-block's own inner lines, that element is the inline-block: the box that owns
those lines, whose value describes how it sits in its parent's line.

With top padding, its text is aligned against its padded rectangle instead of its line box.

| `vertical-align` | Where the text lands |
|---|---|
| `top`, `text-top`, `text-bottom` | over the padding, at the border edge (y=20 against 50 with 30pt padding) |
| `middle` | halfway (y=35) |
| `bottom`, `baseline` | correct |

The words then sit above their own line's `FlowTop`, which `FragmentEmitter.ClaimsLine` guards against by
falling back to the ink's page (see
[the fix](../recent-fixes/2026-09-26-a-line-is-claimed-by-the-page-its-line-box-is-on.md)).

Not fixed with it, because the fix was measured to lose content.
- Not reading the owner's value on its own lines puts the words where Chrome does (y=50.3 against 50.2).
- But it moves the lines off the page boundaries they happened to line up with. A tall inline-block is laid
  out in one piece and sliced, and its lines that then straddle a slice boundary are lost (#1328's shape: 53
  and 54 of 59 words through the CLI).
- It needs to land together with slicing a tall inline-block without losing its boundary lines.

The documented workaround (padding on a block inside the inline-block) was measured: the text lands at 50.3.
