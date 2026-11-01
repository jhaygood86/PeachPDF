# A space between empty inlines is a wrap opportunity before bare text

Before: in `aaaaaaaaaaaa<span></span> <span></span>bbbbbbbbbbbbbbbbbbbb` the space was not used as a line-break opportunity when the text after the empty inlines sat directly in the paragraph, so the long word stayed on the first line and overflowed.

Now: an inline box that contributes nothing no longer hides the collapsible space before it (CSS Text 3 section 5.5: inline box boundaries add or remove no wrap opportunity), so the word wraps like it does with no empty inlines present.
