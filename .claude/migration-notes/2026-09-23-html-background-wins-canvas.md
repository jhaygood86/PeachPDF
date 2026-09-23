# `<html>`'s own background now owns the canvas; `<body>`'s no longer overrides it

**Before:** when both `<html>` and `<body>` declared a background, `<body>`'s filled the whole page canvas and `<html>`'s painted over its own (smaller) box on top of it — `html { background: blue } body { background: red }` gave a red page with blue only where `<html>`'s box reached.

**Now:** per CSS Backgrounds 3 §2.11.2 the root element's own background is the canvas background, and `<body>`'s is propagated to the canvas only when `<html>` has no background (transparent color and no image). The same document now gives a blue page with red painted only over `<body>`'s own box. Documents that set a background on `<body>` alone, or on `<html>` alone, are unchanged.
