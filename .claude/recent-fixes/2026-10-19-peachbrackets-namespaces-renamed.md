# The core's namespaces are `PeachBrackets.*`

Phase 3b, step one: the move into `src/PeachBrackets/` kept the old names; this renames them, mechanically and in one commit so the diff reads as a rename and nothing else.
`PeachPDF.CSS` -> `PeachBrackets.Css`, `PeachPDF.Html.Core[.X]` -> `PeachBrackets.Html[.X]`, and `PeachPDF.Svg`/`MathML`/`Utilities` -> `PeachBrackets.*`. The generator's hard-coded type names, its golden
tests, the tests, the TestHarness, the CLI and the docs follow. The folder names (`Html/Core/`) are unchanged. `PeachPDF.HtmlRenderException`/`HtmlRenderErrorType` keep the `PeachPDF` namespace on purpose:
they are forwarded from PeachPDF.dll, and a forward needs the same full name.

- **What a textual rename misses.** A fully qualified name is easy; the damage is in *relative* references that used to resolve through the enclosing `namespace PeachPDF...`: `CSS.Length`, `Html.Core.Parse.X`,
  `Utilities.Utils` in a file nested under `PeachPDF`, `cref="Html.Core.Dom.CssBox"`, and the core's own use of `HtmlRenderException`, which it used to find because it sat in a parent namespace (those files now
  `using PeachPDF;`). They show up as `CS0246`/`CS0103`, and as `CS1574` for a doc `cref`; the second kind compiles silently and only warns, so a warning-free rebuild is the check.
- **Evidence:** `dotnet build PeachPDF.slnx -t:Rebuild` clean, `PeachPDF.Tests` 17444, `PeachDrawing.PDF.Tests` 959, Cli 213 and the source-generator tests (golden files included) pass.
