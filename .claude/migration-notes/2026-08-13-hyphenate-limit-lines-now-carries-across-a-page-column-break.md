# `hyphenate-limit-lines` now carries its consecutive-hyphenated-line count across a page/column break

Previously: `hyphenate-limit-lines` correctly capped how many consecutive lines could end in a hyphen
*within* a single fragmentainer (page or column) pass, but the count restarted at 0 the moment a
paragraph resumed on the next page or column, regardless of how many consecutive hyphenated lines it had
already produced right before the break. A paragraph with `hyphenate-limit-lines: 2` whose fragment
happened to end on 2 consecutive hyphenated lines let the next page hyphenate its own first 2 lines
again, producing 4 consecutive hyphenated lines across the boundary where the author asked for at most 2.

Now: the count carries across the boundary the same way `text-indent: each-line`'s own resumed-line state
already does. Any existing document whose `hyphenate-limit-lines`-constrained paragraphs happen to break
across a page or column right after a run of hyphenated lines will now see fewer hyphens immediately after
that break — some lines that previously hyphenated at the top of the new page/column no longer do,
matching what the same content would have produced laid out on a single unbroken page — tracked as
[#724](https://github.com/jhaygood86/PeachPDF/issues/724).
