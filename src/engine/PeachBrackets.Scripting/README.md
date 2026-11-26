# PeachBrackets.Scripting

Opt-in JavaScript for [PeachBrackets](https://www.nuget.org/packages/PeachBrackets/) and [PeachPDF](https://peachpdf.net/),
run by the [Jint](https://github.com/sebastienros/jint) interpreter. It is a pure .NET package with no browser and no external
process, and it is trimmable and Native AOT compatible.

PeachBrackets and PeachPDF ignore `<script>` elements. They do not depend on this package. Add it and set a script host to run
the scripts of a document while it loads:

```bash
dotnet add package PeachBrackets.Scripting
```

```csharp
using PeachBrackets.Scripting;

// PeachPDF
var config = new PdfGenerateConfig { ScriptHost = new JintScriptHost() };

// PeachBrackets
var options = new HtmlDocumentOptions { ScriptHost = new JintScriptHost() };
```

Scripts run once, after the HTML is parsed and before styles are applied, in document order. Timers and animation frames run on
a virtual clock until the document settles, so a page that builds itself with `setTimeout` or `requestAnimationFrame` is
complete when it is laid out. What the scripts leave in the document is what is styled, laid out and painted.

See [the guide](https://peachpdf.net/peachbrackets-scripting.html) for the supported DOM and its limits.

**Status:** pre-1.0, versioned in lockstep with PeachPDF. Targets .NET 10 and later.
