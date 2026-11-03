# Table and select insertion modes, foster parenting, and a stack of open elements

Second of the parser series (after [the adoption agency note](2026-11-03-adoption-agency.md)).

- **The stack is a list now.** The first change derived the stack from the chain of parents. Foster parenting breaks that: an element inserted in front of a
  table is pushed on the stack above the table without being inside it. `ParserState.Stack` is the stack; `Current` is its top (or the root). Every pop goes
  through `Pop`/`PopThrough`, which also clear the markers of closed elements from the formatting list, so nothing needs a per-token sweep.
- **The insertion mode is read off the stack, not stored** (`ModeOf`): the innermost table, section, row, cell, caption, column group or select. The standard
  switches mode exactly when it pushes or pops those elements, so the two agree, and there is no variable to forget to reset (`reset the insertion mode
  appropriately` is the whole function). The one place the equivalence needed care is a fostered `<div>` on top of the table: it is not a table part, so the
  mode stays "in table" and `<tr>` clears the stack back to the table, popping the `<div>`, as the standard does.
- **Implied parts change the box tree.** `<table><tr>` now has a `<tbody>`, `<col>` a `<colgroup>`. Layout already handled explicit ones. Tests that read a
  bare row under `table` (`CollapsedBorder*`, `TableGrid`) now find rows through descendants, and the two `TableGrid` tests of a row with no group build it
  from `display: table-row` boxes, since the parser can no longer produce one. A fixture that put a `<div>` straight inside a `<table>` (the flex-table test)
  now uses `<tbody>` items: a browser moves the `<div>` out of the table.
- **Foster parenting** is a flag on the state (`FosterParenting`) that "in table" sets while it hands a token to the "in body" rules; `AppropriatePlace`
  returns the parent and the sibling to insert before. It applies to elements, text, reformatting clones and the adoption agency's re-insertion. Text in a
  table part is held to the standard's "pending table character tokens" rule per `Data` token: whitespace stays in the table, anything else is fostered.
  MimeKit may split one run of text across tokens; a token that is whitespace followed by a token that is not would be fostered whole by the standard.
- **Form element pointer.** `<form>` while one is open is ignored; `</form>` takes the form off the stack without closing what is open inside it (the stack
  is a list, so that is `Remove`).
- **Select.** Options and option groups close each other; anything else inside a `<select>` is dropped; a table part inside a select in a table closes the
  select. Customizable select (richer content in a `<select>`) is not modelled.
- **Removed:** `HtmlUtils.CanEndTagBeOmitted` and the `ShouldTagClose*` helpers it used; the modes replace every case.
- **Evidence:** engine suite 17,4xx tests, `PeachBrackets.Tests` and `PeachPDF.Tests` green; 38 new tree-construction tests. The expected trees are the
  standard's results, traced by hand; they were not compared against a browser.
- **Not done:** quirks-mode `<table>` ([accepted gap](../accepted-gaps/parser-table-closes-paragraph-in-quirks-mode.md)), `<template>`, the document-level
  modes (`<html>`/`<head>`/`<body>`, `</body>`), frameset, tokenizer states.
