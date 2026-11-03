# Document modes, `<template>`, and the remaining in-body rules

Third of the parser series (after [the adoption agency](2026-11-03-adoption-agency.md) and [the table modes](2026-11-03-table-insertion-modes.md)).

- **Document phases are stored, the rest still read off the stack.** `ParserState.Phase` holds _initial_ through _after after body_ because those modes are not a function of the
  stack (before `<html>` the stack is empty). Once it is `InBody` the table, cell and select modes come from `ModeOf` as before. `ParseDocument(html, document: true)` is the whole-document
  entry (`DomParser.GenerateCssTree`); a fragment (`GenerateFragmentCssTree`, `<noscript>` content, the tests that call `ParseDocument(html)`) starts in `InBody` and gets no skeleton.
  `EnsureHtmlAndBody` is gone: `FinishDocument` supplies what an unfinished source lacks at its end (an empty source is `html > head, body`).
- **`</body>` and `</html>` move the phase, they do not pop.** The stack keeps `html` and `body`, so content after `</body>` goes into the body. In a fragment the two end tags still close the
  nearest open element of that name, because there is no skeleton to keep open.
- **A repeated `<html>`/`<body>` merges attributes** (`MergeMissingAttributes`) instead of nesting. `<head>` outside the head is ignored in a document and inserted in a fragment (the tests parse
  whole documents as fragments).
- **Head content after the head** goes into it: the head pointer is put back on the stack for the token and taken off again (a list, so removal from the middle works).
- **Raw text elements** (`title`, `style`, `script`, `textarea`, `xmp`, `iframe`, `noembed`, `noframes`, `plaintext`) take their text whatever the phase or mode, and a line feed that starts
  `pre`/`listing`/`textarea` is dropped (`IgnoreNextNewline`, cleared by the next token).
- **Template.** Each open `template` has an entry on `TemplateModes`; "in template" reads the top and switches it on the first start tag (`tr` makes it _in table body_, `td` _in row_...), then
  dispatches to those handlers, whose context sets already include `template`. `AfterPop` trims the mode stack when a template leaves the stack, in the same place the formatting markers
  are cleared. The phase is untouched while a template is open (the standard "resets the insertion mode" afterwards, which comes to the same phase).
- **Foreign content.** A start tag from the standard's breakout list ends an open `<svg>`/`<math>` back to an integration point or an HTML element; SVG element names are given their mixed case
  while a `CssBoxSvg` is open. Attribute names are not fixed (see [the accepted gap](../accepted-gaps/parser-tokenizer-framesets-and-foreign-attribute-fixups.md)).
- **Also:** `image` is `img`; `rb`/`rtc`/`rp`/`rt` close their implied predecessors when a `ruby` is in scope.
- **Evidence:** engine suite (17,5xx tests), `PeachBrackets.Tests`, `PeachPDF.Tests`, `PeachDrawing.Skia.Tests` and `PeachPDF.Cli.Tests` pass; the only existing test that changed is the one that
  expected a `<b>` to stay inside an `<svg>`.
- **Not done:** the frameset modes, SVG/MathML attribute fixups, tokenizer states (all in the accepted gap, issue 2148), quirks-mode `<table>` (issue 2141).
