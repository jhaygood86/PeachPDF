# Flex item max-content counts inline-box margin, border and padding

An auto-width flex item holding only inline content was sized by `CssLayoutEngineFlex.LineContentWidth`, the sum of its laid-out words' widths. A word's width never includes the margin, border or padding of the inline box it sits in (the line flow adds those to the cursor), so `<h1><img style="margin-right:8pt">Invoice request</h1>` measured 8pt short and its last word wrapped when the item was laid out at that width (issue 2456). `CssBox.GetMinMaxSumWords` already counts them (css-sizing-3 §5.2: contributions are outer sizes).

Fix: the word-sum fast path is taken only when `HasInlineHorizontalDecoration` finds none on any descendant; otherwise the item goes through the intrinsic walk like a block child.

Not done: the fast path itself is kept for plain text items (cheaper, and its trailing-space handling is pinned by existing tests).

Evidence: `FlexboxIntegrationTests.AFlexItemWithDecoratedInlineContent_KeepsItsTextOnOneLine` (image repro, margin, padding, border) failed before and passes; PeachBrackets.Engine.Tests 20480/20480 on net10.0.
