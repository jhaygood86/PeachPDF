# Control characters are drawn

Before: a C0 control (U+0001..U+001F other than tab, line feed and carriage return), DEL (U+007F) in a font that maps it to a blank glyph, and the like vanished from the text, taking no room.
Now: each is drawn as the font's missing-glyph shape and takes that glyph's width, as css-text-3 requires ("control characters must be visible"). Text that carried stray control characters (copied from a terminal log, say) now shows a box for each. `PeachDrawing.Core`'s `TextMeasurement` measures them too (a carriage return is still dropped).
