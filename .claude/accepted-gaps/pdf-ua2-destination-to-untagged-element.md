# PDF/UA-2: a destination to an element with no structure element stays a page destination

Tracked in issue 2098. A link or outline entry gets a PDF 2.0 structure destination only when its target element has a structure element (`PagePosition.Target` resolves to one through the tag recorder). A target with none - an `id` on a `display: none` box, or one whose tagging is suppressed - keeps its page destination, and veraPDF's UA-2 profile (rule 8.8) rejects that link.

**Why not:** the nearest tagged ancestor is a different, coarser place than the one the author named; pointing at it silently moves where a reader lands. It is rare, and the validator names the link, so the author can give the target a tagged element.
