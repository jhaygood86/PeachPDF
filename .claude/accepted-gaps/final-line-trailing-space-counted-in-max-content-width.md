# A block's own final line counts its trailing space in max-content width

Tracked as **#1014**. Surfaced by the issue #1011 review; pre-existing.

[css-text-3 §4.1.2](https://www.w3.org/TR/css-text-3/#white-space-phase-2) hangs a line's trailing
white space, so it does not contribute to the line's width. `CssBox.GetMinMaxSumWords` applies this at
a `<br>` — the one point in its walk where a line is known to have ended — but not at the end of a
block, so a block whose last line ends in white space measures one space wider than it draws. Every
shrink-to-fit consumer of that measurement (a table column, a float, an inline-block) inherits the
extra space.

The obvious repair is wrong and was checked: the walk carries **one** running `maxSum` across a whole
subtree, so the last word of any given box is not the last word of the line whenever a sibling's
content follows it there. In `<span>AB </span><span>CD</span>` that trailing space is an ordinary
inter-word gap, and subtracting it would undercount the line by a space. Closing this properly means
knowing where the line really ends during the intrinsic-size walk, which that walk is not structured
to answer.

Until issue #1011 this was masked in one direction: a `!HasSpaceAfter`-guarded subtraction sat at the
end of that branch, put there to cancel the phantom word space `CssRect.ActualWordSpacing` used to
give every `IsImage` word. With that term gone the guard selects only words whose spacing is provably
zero, so the subtraction was removed — see the note left in its place in `GetMinMaxSumWords`.
