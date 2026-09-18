# `background-clip: text` no longer falls back for an upright run on a real-vertical-metrics font

Previously, `background-clip: text` on a `vertical-rl`/`vertical-lr` box with an **upright** run (each
character stacked top-to-bottom, its own reading orientation preserved) fell back to a plain `border-box`
clip whenever the run's font carried real OpenType vertical metrics (`vhea`/`vmtx`, or a real `VORG`
table) - the same fallback an outline-less font gets. This mattered only for a real vertical CJK font
(most fonts have no `vhea`/`vmtx` at all and were unaffected) under `text-orientation: upright` (or
`mixed`'s own per-character upright classification): the background layer filled the whole box rather
than being cut to the shape of the text.

It now clips to the actual glyph-outline union, matching every other `background-clip: text` case. Each
upright character's own outline is additionally clipped to its own reserved cell - the same cell
`PaintUprightVerticalRun` already confines that character's *paint* to, since a real `vmtx` advance is
routinely narrower than the font's own line height - before being added to the union, so the clip shape
can never disagree with what is actually painted.

A font with no decodable glyph outlines at all (a CID-keyed CFF or bitmap font), and
`writing-mode: sideways-rl`/`sideways-lr` (a different, unrelated writing-mode value), are unaffected and
still fall back to `border-box`.
