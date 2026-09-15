# Thick underlines stay below the alphabetic baseline

Previously, every underline shared one fixed center position. Increasing
`text-decoration-thickness` therefore expanded half of the stroke upward into the text, often causing
`text-decoration-skip-ink: auto` to break a heavy underline around ordinary glyphs.

Automatically positioned underlines now keep their top edge below the alphabetic baseline, with a gap
that grows with the stroke thickness. Thick underlines consequently sit lower and remain continuous
unless they genuinely intersect descending ink.
