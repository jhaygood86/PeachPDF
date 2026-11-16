# An `img` or `input` with no image is not turned into its alt text in quirks mode

HTML 15.4.2 says an `img` or `input` that represents no image is drawn as a replaced element with its alt text as content when the document is in quirks mode and the element already has natural dimensions; in the other modes, only when it has no `alt` or the image is expected to load. PeachPDF draws a missing image by its own rules in every mode, and does not tell the modes apart here.

Part of [document modes](../../docs/html-css-support.md#document-modes-quirks-mode).
