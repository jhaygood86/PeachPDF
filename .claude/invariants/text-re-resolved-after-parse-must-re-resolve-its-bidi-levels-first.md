# Text re-resolved after parsing must re-resolve its bidi levels before `ParseToWords`

_CSS Writing Modes / UAX#9. Tracker: [#754](https://github.com/jhaygood86/PeachPDF/issues/754)._

`BidiLevels`, `CharScripts` and `JoiningForms` are indexed against the text the **last** resolution
saw. Any path that changes a box's `Text` after it was first parsed and then calls `ParseToWords`
must call `CssBidiParagraphResolver.ResolveOwnTextAsParagraph(box)` in between, or
`AppendWordsFromText` walks past the end of a stale array and throws `IndexOutOfRangeException`.

Five paths now do this: `ReapplyPseudoElementContent`, `ResolveTargetPageContent`,
`RunningElementLayout.RefreshPageCounterContent`, and — for both the author-`content` and the
*default* numbering path — `HtmlContainerInt.ReparseFootnoteText`.

**The measured symptom is a number gaining a digit**, because that is the cheapest way to change a
length: `"9"` becomes `"10"`. Every occurrence so far has been a counter, and every one shipped
because the fixtures stopped below ten — a running footer needs a ten-page document, a footnote needs
a page carrying ten notes or continuous numbering across pages. A counter feature exercised only on
single-digit values is not exercised.

The trap when adding the sixth path: it is easy to put the re-resolve inside whichever *new* branch
changed the text and miss that the pre-existing default branch changes it too. The footnote bridge
did exactly that, and the default path — untouched by the change, numbering a call `"10"` — was what
threw. Put the re-resolve at the single point that re-parses, not in the branches that write.
