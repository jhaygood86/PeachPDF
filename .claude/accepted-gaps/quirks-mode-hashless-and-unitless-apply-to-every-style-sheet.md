# The hashless color and the unitless length ignore where a style sheet came from

CSS Color 4 Appendix B and CSS Values 4 Appendix C allow the hashless hex color and the unitless length only in style sheets of the same origin as the document, and in `style` attributes. PeachPDF reads both quirks in every style sheet the document loads (`<style>`, `<link>`, `@import`) and every `style` attribute of a document in quirks mode, whatever its address. A sheet the host supplies (`UserStyleSheet`, `StyleSheet`, a declarative sheet) is never read with them. The origin check needs the document's address and the sheet's, and a document handed over as a string has no address to compare with.

The same limits: both quirks are written for a bare number or hex run in a declaration's top-level value, so one inside a function (`calc()`, `rgb()`) is not rewritten, as the appendices say, and `clip: rect(1, 2, 3, 4)` takes no bare numbers.

Part of [document modes](../../docs/html-css-support.md#document-modes-quirks-mode).
