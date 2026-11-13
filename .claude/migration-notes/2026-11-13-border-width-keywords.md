# `border-width: thin | medium | thick` are 1px, 3px, 5px

Before: `thin`, `medium` and `thick` were 1, 2 and 4 points (1.33px, 2.67px, 5.33px). A border with a style and no width (`border: 1px`-less `border: solid black`, `border-style: solid` alone) is `medium`.
Now: 1px, 3px and 5px (0.75pt, 2.25pt, 3.75pt), as in every browser. An unstyled-width border is 2.25pt where it was 2pt, so the box is 0.25pt larger on each side.
