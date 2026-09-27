# Two oblique face-matching edge cases now follow CSS Fonts 4 §5.2 exactly

Both changes only affect a family with faces the matching algorithm previously skipped past; a family with only an
upright/italic pair and no declared oblique ranges is unaffected.

**An `oblique <angle>` request of 0 degrees or more can now reach a declared `italic` face it previously skipped.**
Before, once a family had *any* face declaring an oblique range - even one leaning the opposite way from the
requested angle - `font-style: oblique <angle>` (`@font-face`'s `font-style: oblique <min> <max>` descriptor, or a
variable font's `slnt` axis) would go straight to that range and never consider a declared `italic` face in the same
family, even when every declared oblique range in the family leaned the other way (e.g. every range at or below 0
degrees for a request above 0). It now tries the italic face before crossing over to a range on the other side, per
CSS Fonts 4 §5.2's specified order (oblique ranges on the requested side, then italic, then oblique ranges on the
other side).

**An explicit `oblique 0deg` request now prefers a genuinely upright face.** Before, `font-style: oblique 0deg` was
matched purely against the family's declared oblique ranges and never considered an upright face, even though upright
is oblique 0 on the specification's own scale. It now prefers an upright face over any oblique range - one that
happens to include 0 as much as one that excludes it - the same preference `font-style: normal` already gives.

Both are rare in practice (an author has to declare oblique ranges that all lean one way plus a separate italic face,
or explicitly request `oblique 0deg` rather than `normal`), so most documents render unchanged.
