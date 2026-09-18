# Text that exactly fits its line no longer wraps its last word

Previously, text whose natural width matched the available line width to within one floating-point
ULP could wrap its last word onto a second line, because the wrap test compared the running line
width against the limit with a strict `>` and the two sides can differ in the last bit. It bit
shrink-wrapped boxes, whose width is exactly their text's width: a flex-centred `16 / 9` label split
into `16 /` + `9`, `3 / 4` likewise, and an absolutely positioned `aspect-ratio` flex box wrapped
`abspos -> 192px wide` onto two lines. In the showcases this affected `aspect_ratio`, `flexbox`
(`also centered`), `full_bleed` (the cover's `edges`), `bengali_gujarati_tamil_use`, `cmyk_colors`
and `table_visibility_collapse`.

Now a word (or a whole `white-space: nowrap` run) counts as fitting when it overshoots the line's
limit by no more than 0.01 layout units, roughly the size of Chrome's 1/64px layout unit, so exact-fit
text stays on one line, matching Chrome. The same tolerance applies along the column axis of
`vertical-rl`/`vertical-lr` text.

Checked against the v0.9.18 release's showcase PDFs: v0.9.18 already wrapped `also centered` in the
`flexbox` showcase and `edges` in `full_bleed`, so those two are a fix relative to the last release;
the `aspect_ratio` label wrap is new since v0.9.18 (it kept `16 / 9` on one line there) and this
change removes it again.

Text that is genuinely wider than its line by more than 0.01 layout units still wraps exactly as
before.
