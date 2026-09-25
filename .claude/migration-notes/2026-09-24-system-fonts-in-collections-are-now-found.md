# System fonts inside `.ttc`/`.otc` collections are now discovered

**Before:** only `.ttf`/`.otf` files were read, so any family a platform ships only inside a font collection was
"not installed" to PeachPDF and requests for it fell back to the default font - Windows' Cambria Math and MS Gothic,
macOS's Helvetica, Times, Menlo and Courier, the Noto CJK fonts on Linux.

**Now:** every face of a `.ttc`/`.otc` is discovered as its own font. A document naming one of those families
(`font-family: Menlo` on macOS, `monospace` where it maps there, `math` on Windows) now renders in it, and system
fallback for a character no listed font covers can now pick a font from a collection (CJK, for instance). Text that
previously fell back to the default font because its family lived in a collection changes appearance and metrics.
A `.ttc` passed to `AddFontFromStream` registers its first face.
