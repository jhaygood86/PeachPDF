# PeachBrackets.WinUI

A WinUI 3 `HtmlView` control for HTML documents laid out by PeachBrackets.

Besides a viewer that scrolls and zooms the document itself, it can be a block inside other layout - a row of a virtualized list - that is as tall
as its document, starts at its top-left corner and leaves the wheel and touch to the list (`ScrollMode`, `ContentHeight`, `IsLayoutSynchronous`).
Fonts added once to `HtmlViewFonts` are shared by every view. The package depends on only the parts of the Windows App SDK it compiles against, not
the all-in-one package. See https://peachpdf.net/peachbrackets-winui.html.
