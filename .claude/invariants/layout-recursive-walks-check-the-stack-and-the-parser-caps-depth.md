# Recursive walks check the stack, and the parser caps element depth

A stack overflow ends the process and a host cannot catch it, so no recursion over the document may be left to run
out of stack. Measured on a 1 MB stack (Release, CLI): 200 nested `<div>` overflowed in `FragmentEmitter.BuildDraftCore`
(~156 levels; the issue's trace named `CreateLineBoxes`, which is only the first walk to run out), 155 nested SVG `<g>`,
and `color-mix()` nested ~465 deep. Layout recurses through async state machines, several KB per level.

- `StackGuard.Ensure()` (`PeachBrackets.Utilities`) throws `HtmlRenderException` (`Layout`) when
  `RuntimeHelpers.TryEnsureSufficientExecutionStack` says the stack is nearly gone. It is called at the entry of each
  recursive walk: `CssBox.LayoutBlockChild`, `FragmentEmitter.BuildDraftCore`, `FragmentPainter.PaintFragment`,
  `DomParser.CascadeApplyStyles`, `SvgTreeBuilder.BuildElement`, `SvgRenderer.RenderElement` and the CSS lexer's
  `NewFunction`. A new recursive walk over boxes, SVG elements or CSS tokens needs the same call.
- `HtmlParser.MaxElementDepth` (400) refuses a deeper document at parse time. It is only the backstop for walks
  that do not check, and it keeps a generated document of thousands of levels from reaching them; the guard is what
  sets the real limit, so it follows the host's stack (a document of 150 levels renders on an 8 MB stack and fails
  cleanly on 1 MB).
- `color-mix()` operands nest to `MaxColorMixDepth` (32); deeper is an invalid colour, not an exception.
- The CLI reports an `HtmlRenderException` as `error: <innermost message>` and exits 1 (layout wraps a failure once per
  box it passed through, so the outermost message is only "Exception in box layout").

Do not move the check to a big-stack thread: the pipeline is async (a real `await` resumes on a pool thread), and a
Blazor WebAssembly host has one thread. Test it on a small stack, as `DocumentDepthLimitTests` does
(`new Thread(..., maxStackSize: 512 * 1024)`), where an unguarded walk kills the test host.
