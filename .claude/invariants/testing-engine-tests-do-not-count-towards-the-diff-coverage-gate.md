# Tests in `PeachBrackets.Engine.Tests` do not count towards the diff-coverage gate

A change to `PeachBrackets` (layout, cascade, paint) covered only by `PeachBrackets.Engine.Tests` fails the 90% diff-coverage gate. Measured on a float-placement change: the engine tests (a `clear` case, a margin case and others that all passed) left 7 changed lines of `CssLayoutEngine.cs` at 0 hits, 76% diff coverage, a red `coverage (…)` job and a red `test-gate`.

**Why.** Run with `--coverlet`, `PeachBrackets.Engine.Tests` writes a Cobertura report with no `PeachBrackets` package in it (its packages were `PeachDrawing.Core`, `PeachDrawing`, `PeachDrawing.PDF`, `PeachDrawing.Text.Data` and `PeachPDF`), so nothing it executes in the engine is attributed to the engine's files. `PeachPDF.Tests`' report does contain `PeachBrackets`, so that is where the gate sees engine lines run.

**What must hold.** A change in `src/engine/PeachBrackets/` needs a test in `PeachPDF.Tests` (or another project whose report carries `PeachBrackets`) that executes each changed line, written through `PdfGenerator` and read back from the PDF (`doc.Inspect()`; `Integration/FloatPlacementPdfTests.cs` reads filled rectangles from the content stream by fill colour). Keep the engine test as well: it asserts on the box tree, which the PDF test cannot.

**Check before pushing.** Run `PeachPDF.Tests` with `--coverlet --coverlet-output-format cobertura` (the command in CLAUDE.md) and compare the changed lines (`git diff origin/main...HEAD -U0`) with the `<line number hits>` entries of the engine file in the report; a line with `hits="0"` is what the gate counts as missing.
