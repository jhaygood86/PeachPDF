# `:visited` never matches

`:visited` never matches, by design - there is no browsing history in a document renderer - so `:any-link`
selects exactly what `:link` does. `:hover`, `:active`, `:focus`, `:focus-visible` and `:focus-within` do
match, when a host tells the document where the pointer and focus are (see
[the reload gap](interaction-state-reloads-the-document-and-skips-edited-and-declarative-ones.md) for how a
change of them is applied); in a PDF they select nothing.
