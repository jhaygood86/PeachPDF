# SVG text position lists count `<textPath>` characters

**Before:** in `<text x="10 30 50 70">A<textPath href="#p">BC</textPath>D</text>` the `x`/`y`/`dx`/`dy`/`rotate`
lists were indexed over the straight characters only, so `D` took the second entry (30).

**Now:** the characters inside the `<textPath>` are addressable characters of the `<text>` (SVG 2 §11.5), so `D`
takes the fourth entry (70). Text without a `<textPath>` is unchanged.

A nested `<tspan rotate>` still replaces, rather than adds to, an outer `rotate`: SVG 2 gives the nearest ancestor
precedence, so that part of the old gap was not a deviation.
