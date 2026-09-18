# A float that follows inline content now shares its line, instead of starting a new one

Previously, a floated element written *after* some inline content in the same block (e.g. `XY
<span style="float:left">ZZZZ</span> more words`) was not placed beside that content: the float landed
on top of the inline content that preceded it (both drawn at the same position), and the content that
followed it was pushed onto a new line entirely, as if the float were an ordinary block-level sibling
splitting the paragraph in two. A float written *before* inline content already worked correctly.

The float now shares one inline formatting context with the surrounding inline content regardless of
source order, matching [CSS 2.1 §9.5](https://www.w3.org/TR/CSS21/visuren.html#floats) and
[§9.5.1 rule 6](https://www.w3.org/TR/CSS21/visuren.html#float-position): it is placed beside the
current line, and the content that follows it continues on that same line rather than starting a new
one. Two floats with nothing genuinely inline between them, and a float preceding inline content, are
unaffected — those already worked and still do.

One narrower gap remains, tracked separately: a float whose own content is taller than fits the
remaining page overflows that page rather than continuing its own content onto a later one (the same
limitation an `inline-block` holding real block-level content already has). This does not affect the
surrounding document's own pagination, which continues across page boundaries normally regardless of
a float's presence. See
[.claude/accepted-gaps/a-floats-own-content-taller-than-one-page-overflows.md](../accepted-gaps/a-floats-own-content-taller-than-one-page-overflows.md).
