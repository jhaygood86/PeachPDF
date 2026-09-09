# Content dropped from a re-emitted page comes back

A page whose content a later relocation disturbed is emitted again once layout settles. That second
emission could skip a run of leading children in a container, and whatever they held was then drawn
on no page at all — leaving a visible blank where the content had been laid out.

**Before.** On a long document, the first few blocks of a container could be missing from the
output entirely, with a gap of exactly their height in their place. Measured on a fifteen-page
document: 660 characters absent and 420pt of blank at the top of page 1.

**Now.** They are drawn.

**When a document author would notice.** Only in a document long enough for a page to be finalised
and then reopened — several pages at least, with something later in the flow (a `break-inside:
avoid` box, a widows/orphans push, a table moved off a boundary) moving content that had already
been placed. Nothing needs changing in a document; output that was silently missing text now has it.
