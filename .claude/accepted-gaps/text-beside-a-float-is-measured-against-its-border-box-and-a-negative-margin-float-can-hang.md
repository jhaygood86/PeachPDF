# Text beside a float is measured against its border box, and a negative-margin float can hang layout

Two float-placement defects are left alone by the change that lets a tall float continue with its text, because
fixing them changes where lines and floats land for documents that have nothing to do with tall floats:

- **Lines beside a float ignore its margin.** The line collision test uses the float's border box, so with
  `margin: 6pt` on a `float: left; width: 100pt` box a line beside it can start 6pt inside the margin
  (x = page margin + 100 rather than + 112). Chrome starts it clear of the margin box.
- **A `float: left` with a negative `margin-top` that has to drop below an earlier left float never finishes
  layout.** The drop is placed at the earlier float's border-box bottom plus the float's own margin, which for a
  negative margin lands back inside the blocker, so the next collision scan finds it again. Repro: a 300pt x 200pt
  page with 20pt margins, `<div style="float:left;width:134pt">a</div><div style="float:left;width:144pt;margin-top:-3pt"></div>`.
  Existing behaviour, present before the continuation change.

A fix for both was written and measured (place the drop below the margin box, compare outer tops so a negative
margin cannot re-collide, and start lines to the right of the margin box). It is not bundled because on generated
corpora it moved a line that cannot fit beside a float in a way that dropped words the current placement keeps:
the line-placement policy for a first word that does not fit beside a float has to be settled (shift the line below
the float) in the same change. That policy is now settled (an empty line that a float leaves no room on is shifted
below it, see `ShiftEmptyLineBelowCrowdingFloats`), so the margin-box fix can be retried against it.

Tracking issue for the hang: #1512.
