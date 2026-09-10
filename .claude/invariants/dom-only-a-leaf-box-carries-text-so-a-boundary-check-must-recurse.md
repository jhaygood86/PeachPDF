# Only a leaf box carries `Text`, so a "what is just before this boundary" check must recurse

_A trap that has already shipped once, in the inline-boundary wrap-opportunity work._

`CssBox.Text` is set only on a **leaf** text box. A structural container's `Text` is `null` —
its content lives in `Boxes`, not in a string of its own. So reading `.Text` off a box handed
back by a tree walk is correct only when that box is *known* to be a leaf, and the boxes these
walks return generally are not.

`DomUtils.PrecedingBoxAcrossFirstChildChain` is the specific hazard: it returns the nearest
preceding *sibling*, at whatever nesting level the walk reached — an arbitrary subtree root, not
a text leaf. `HasInterElementWhitespaceBefore` (`CssLayoutEngine.cs`) originally did exactly
this and nothing more:

```csharp
DomUtils.PrecedingBoxAcrossFirstChildChain(word.OwnerBox)?.Text is { } text
    && HtmlUtils.IsNullOrCollapsibleWhitespace(text)
```

Measured symptom: in `<span>A</span><b><i> </i></b><span>B</span>` the walk returns `<b>`, whose
own `Text` is `null`, so the space nested two levels inside it is invisible and "AB" lays out as
one unbreakable token that overflows its line instead of wrapping. Nothing throws; the flat
one-level form `<span>A</span> <span>B</span>` keeps working, which is what makes it easy to
write the shallow version and believe it.

A correct check descends to the last text-bearing leaf. Two arms beyond the whitespace hit are
easy to miss and both matter:

- a child that contributed **real content** (`Text is not null || Words.Count > 0` — the latter
  catching a replaced element like `<img>`, which has words but no text) **ends** the search;
- a child that contributed **nothing at all** — an empty inline such as `<u></u>` — is skipped
  over to the sibling before it.

Two implementations of this descent now exist for the same reason:
`CssLayoutEngine.EndsWithCollapsibleWhitespace` and `CssBox.CountTrailingRegionalIndicatorsIn`
(regional-indicator parity). A third such check should copy their shape rather than the
one-level shortcut.

Pinned by `OverflowWrapIntegrationTests.Normal_WhitespaceNestedInsideAPrecedingInlineIsAWrapOpportunity`,
whose `nested`/`trailing` paragraphs must match the `flat` one's line count while `text` must not.
