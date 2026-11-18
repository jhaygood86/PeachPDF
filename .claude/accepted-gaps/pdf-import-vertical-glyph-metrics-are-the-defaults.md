# PDF import: vertical writing uses the default vertical metrics

A vertical CMap (`-V`, WMode 1) lays glyphs out with the defaults of ISO 32000-1 9.7.4.3: position vector (w0/2, 0.88 em) and vertical displacement -1 em. A CIDFont's `/DW2` and `/W2` arrays (per-CID overrides) are not read, so a font that tightens or loosens particular glyphs (punctuation, small kana) is spaced a little evenly. Proportional vertical fonts are rare; fixing it means parsing `/W2` into a per-CID (w1, vx, vy) table and using it in `ShowVertical`.
