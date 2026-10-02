# Content overflowing a column into one that holds nothing of the box was drawn nowhere (seeds 235, 216, 101, 70, 190, 112)

Each column of a multi-column container claims the words whose line lies in its region (its own x extent and band). A box
wider than its column (a table, a row of cells, a long word) puts its content past the column's edge, and two places
had nobody to claim it: past the last column that was filled, and inside the extent of a column that does not hold the
box at all (a row only the first column placed, whose cell text starts in the second column's extent). The words were
laid out, present in the box tree, and in no fragment, so never painted.

Two changes in `FragmentEmitter`:

- the last instance of a row (same parent, same band top, no instance further right) has its region open on the right
  (`OpenAtTheRightIfLastInItsRow`), so overflow past the last filled column belongs to it;
- a word that no holder of its box claims, by the emitter's own line-level test (`ClaimsWordIn`), belongs to the first
  holder whose band it lies in (`IsOverflowNoColumnClaims`).

Traps found by running it:

- the second change first used a word-rectangle test for "someone else claims it". A line is claimed as a whole by
  its aggregate rectangle, so a word outside every region could still be claimed by its line, and both claimed it: 13
  existing "exactly once" tests failed. It must ask the same question the claim does (`ClaimsWordIn`).
- without the vertical check it claimed words laid out in a later fragmentainer's band (the snapshot of a continuing
  box holds every line).
- a container filled under two outer columns holds the first fill's geometry in the second, at stale positions. The
  fallback stays out when a box is held outside its own row (seed 112 drew a word twice, at one position).

Evidence: corpus (300 documents, page-edge visibility): lost words 38 to 13, doubled unchanged at 6; seeds 70, 101, 190,
216, 235 and 112 improve, none worsens. net8.0 suite green. Both new tests fail on main and pass here.

Not done: `w112_66` (a float wider than its column inside nested columns) is still lost. Seeds 40, 214, 248 and 252
keep the words they lost; they were not looked at.