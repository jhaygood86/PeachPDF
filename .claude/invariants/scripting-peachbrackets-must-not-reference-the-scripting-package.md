# PeachBrackets and PeachPDF must not reference PeachBrackets.Scripting (or Jint)

Scripting is opt-in. The seam (`IScriptHost`, `ScriptDocument`, `ScriptNode`, `HtmlDocumentOptions.ScriptHost`, `PdfGenerateConfig.ScriptHost`) lives in
`PeachBrackets`; the interpreter lives in `PeachBrackets.Scripting`. Only the `PeachPDF.Cli`, the WPT runner and tests reference the latter. A reference
from `PeachBrackets.csproj` or `PeachPDF.csproj` would make every consumer carry Jint and would blur "off by default".

## Traps

- The tree the script changed is **written back as HTML and parsed again** (`HtmlContainerInt.RunScriptsAsync`), only when `ScriptDocument.Version > 0`.
  Measured: serializing every scripted page, even unchanged ones, dropped css-transforms from 542 to 300 passing WPT tests (SVG/foreign content does not
  round-trip). Keep the "unchanged keeps its source" rule, and add a mutation to `NoteChanged` whenever a new `ScriptNode` operation changes the tree.
- `SetHtml` re-runs for container-query passes and restyles with the *scripted* source (`_scriptedSource`); do not run the host again for it.
- The `(scripting)` media feature must keep `.Option()` in its converter so the boolean form `@media (scripting)` parses; without it the UA rule hiding
  `<noscript>` never matched even when scripting was on.
- Jint's `ClrFunction` cannot be called with `new`: constructors are made by `Js.Constructor` as script functions wrapping a native one.
