# `rgb()`, `rgba()`, `hsl()`, `hsla()` and hex colors follow the CSS grammar

**Before.** The color parser accepted spellings the grammar rejects and rejected ones it allows. `rgb(255, 0, 0 / 1)`, `rgb(1, 2, 3,)`, `rgba(100%, 0, 0, 1)` (a mix of percentages and numbers with commas), `rgb(0 0 0 0.5)` (an alpha without the slash), `hsl(0, 100%, 50% / 1)` and an `rgb()` with extra components were read as colors. `#rgba` and `#rrggbbaa` (4 and 8 hex digits) and whitespace inside the parentheses (`rgba(  0 , 255 , 0 , 1 )`) were not. A negative alpha was read as opaque, a negative saturation was not clamped and a hue past a few turns (`hsl(360120, ...)`) came out black.

**Now.** Exactly the CSS Color 4 grammar: the legacy comma form (three numbers or three percentages, `hsl()` with percentages, an optional alpha) or the modern space form with the alpha after a slash, never both. An invalid color is ignored where it appears, as any invalid value is: a declaration `color: rgb(255, 0, 0 / 1)` no longer paints red. Hex of 4 and 8 digits works. An alpha outside 0 to 1 is clamped (`-2` is transparent), saturation and lightness are clamped, and a hue goes round any number of turns.

**Why.** The canvas `fillStyle` tests (and the CSS ones) check both directions, and the parser is the one CSS and canvas share. css-color gains 14 passing reftests and css-backgrounds 29 from it.
