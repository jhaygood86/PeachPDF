# A block-content list item does not settle its `::marker` inside a multi-column container

`<li><p>…</p></li>` is numbered correctly on the page grid, but inside a **multi-column** container its
marker can land in a later column fragment than the one the item begins in, and in a minority of cases
be claimed twice or not at all. [CSS 2.1 §12.5.1](https://www.w3.org/TR/CSS21/generate.html#lists) and
[CSS Lists Level 3 §3.1](https://www.w3.org/TR/css-lists-3/#marker-position) put the marker beside the
item's **first** line box, so this is a real deviation. Tracked as
[#483](https://github.com/jhaygood86/PeachPDF/issues/483).

**Measured**, over 162 documents (`column-fill: auto|balance` × `column-count: 1|2|3` × 3|5|8 items ×
1|2|4 block children × page heights 120/200/300pt):

| item content | `column-count: 1` | `column-count: 2\|3` |
|---|---|---|
| block children, no words | 0 late, 0 bad | 55 late, 3 claimed twice |
| block children with text | 0 late, 0 bad | 6 late, 18 claimed twice, 6 claimed **zero** times |

The page grid is clean in both rows, and **inline** item content is clean everywhere — that is the
660-document sweep [#468](https://github.com/jhaygood86/PeachPDF/issues/468) was closed against.

**Why it is not the marker rule's doing.** An outside marker is positioned by the pass that *places*
its item and left alone afterwards
([the invariant](../invariants/fragmentation-an-outside-marker-is-positioned-by-the-pass-that-places-its-item.md)),
which is correct wherever the item's own fragmentation is. Here the item's fragmentation is what is
unsettled: a block-content item flows through `CssBox.LayoutBlockChildren`, so nothing calls
`CssBox.AwaitPlacement` over its subtree the way `CssLayoutEngine.CreateLineBoxes` does for an
inline-content item (#433's defect class, still open for this shape), and the loop's column arms can
re-decide which column a child belongs to after the item has been placed.
`CssBox.TakeBackTheMarkerOfAnItemThisPassKeptNothingOf` covers only the case where the item kept
*nothing*, not the case where it kept content in a column it is later moved out of.

**Measured, not assumed, to sit upstream of the take-back:** disabling that method entirely leaves the
same counts (55 late / 3 bad on the wordless row), and a words-only "kept nothing" test — the obvious
alternative — is much worse, turning 55 late markers into 52 claimed zero or twice. A marker in the
wrong column is visible; one claimed zero times is invisible, which is #444's symptom.

**Reachable only because [#467](https://github.com/jhaygood86/PeachPDF/issues/467) closed.** The same
sweep on the build before it reports every one of these markers missing outright, and some items
producing no fragment at all — so this is a residual of a strictly improved state, not a regression.
Closing it means settling how the columns engine fragments a block-level list item, which is a larger
change than the marker rule.

**A second attempt (2026-09-07) found the actual mechanism, and it is not the column-rejection arms.**
The plan for this attempt was "take the marker back wherever `CssBox.LayoutBlockChildren`'s own
column-rejection arms (§3.1 forced/avoid-column-break, column-overflow, column-span:all, the orphans
retry) push an already-placed child to the next column wholesale" — mirroring
`TakeBackTheMarkerOfAnItemThisPassKeptNothingOf`'s shape but unconditional, since the whole subtree
moves. Implemented at all four arms and measured against the same 162-combination sweep: **the late/bad
counts (21 late, 14 bad on the current build's own parameter grid) were bit-for-bit identical with and
without the fix.** The four arms are real, but every one of them hands the rejected child a fresh
`BlockBreakToken` with `ChildToken: null` — and `MarkerBelongsToTheFragmentainerBeingFilled` already
treats a `null` resume as "reposition it" unconditionally, regardless of whether `AwaitPlacement` was
ever called. The take-back is not wrong, but it is inert: nothing in the sweep ever needed it.

**The real mechanism, traced from one sweep failure (`li5`, `column-count:2;column-fill:auto`, 8 items,
2 `<p>` children, page height 120):** the item's *marker word* and the item's own final `Location` came
back pointing at two different columns entirely — `Location.X` at the column the item's content actually
settled in, the marker's own word rect still at the column an *earlier, abandoned* attempt had placed it
in. The seam is `CssBox.ResumeInTheNextFragmentainer` (`CssBox.cs`, called from `DriveBlockChildPass`
whenever a box resumes with a non-null token): its own doc comment states the design plainly — "**only
this box moves, not its subtree**... its already-placed descendants belong to the fragmentainer being
left and keep the geometry that one's own fragment was built from." That is correct for ordinary content
(a `<p>`'s first half genuinely does stay in the fragmentainer it was placed in), but the *marker* is not
"content behind in the fragmentainer being left" the way a placed line is — it names a position derived
from the item's own border box, and `ResumeInTheNextFragmentainer` moves that border box (`Location`)
without moving the one child (the marker) whose position is supposed to describe it. Compounding this:
the failing items' own fragments (both, in `li5`'s case) ended up on a pagination slot neither of the two
logged `LayoutPassContents` visits ever named, meaning at least one further whole-container relayout
attempt is involved that a simple per-visit trace does not capture — consistent with the file's own
"settling how the columns engine fragments a block-level list item" framing above, not a narrow gap in
one method.

**Status: still open, scope confirmed larger than a marker-only fix.** A real fix needs
`ResumeInTheNextFragmentainer` (or whatever relays the item across that further whole-container retry)
to either move the marker's word along with the box, or explicitly take it back so a later pass
repositions it — and the second, currently-untraced relayout path needs identifying before either change
can be verified against the sweep. Left as this file's own open gap rather than shipped as a fix that
measurably does nothing.
