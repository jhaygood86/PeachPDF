# A `border-collapse: collapse` table's borders keep their declared width

**Before:** a collapsed table's grid lines could render at half — or a quarter, or an eighth — of the
width the document declared, with the cells shrinking to match, depending on how many times the layout
engine happened to re-enter that table. A `border: 12px` collapsed cell in one document painted a 12px
line and in another an otherwise identical table painted 6px. Nothing about the table itself predicted
which: the trigger was elsewhere in the document, in whatever caused a second layout pass (a
shrink-to-fit measurement, a CSS Fragmentation 3 §4.3 relocation, a per-page-width reflow). A
`border-collapse: separate` table was never affected, and neither was a single-pass document, which is
why the two models could disagree about the same declaration in the same stylesheet.

**Now:** CSS 2.1 §17.6.2 conflict resolution always reads each participant's *declared*
`border-*-width`, so a collapsed grid line is the width the document asked for on every pass, and the
table and its cells size to match.

**What an author may need to change:** nothing is invalid that was valid before, but a collapsed table
that was being compensated for — a width nudged, a padding added, a border declared at twice its
intended thickness to survive the halving — now lays out at its declared size and those compensations
will over-shoot. Compare against a browser rather than against the previous output.
