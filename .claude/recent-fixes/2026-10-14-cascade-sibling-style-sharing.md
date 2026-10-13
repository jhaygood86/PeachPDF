# An element takes an earlier sibling's computed style when no rule can tell them apart

The element half of [the text and anonymous box sharing](2026-10-14-cascade-inherited-only-styles.md). `invoices-l`: best of 8 in process, main
3.64/3.67/3.49 s, text boxes alone 3.74/3.47/3.43 s, with this 3.30/3.23/3.16 s; allocation 1,336 -> 1,229 MB. AOT CLI, interleaved: 4.87-4.91 s ->
4.43-4.52 s (that includes the text change), `table-l` 18.5 -> 17.6 s. Output is unchanged (benchmark documents, fuzz corpus, all 220 showcases).

`CascadeApplyStyles` takes a `SiblingStyles` from the loop over a parent's children: one list per parent, alive for that loop and no longer, of up
to eight entries by (tag, attributes, box type, the style the box came into the cascade with). The first element of a kind decides once whether
its kind may share (`CssData.CanShareStyleAmongSiblings`); if it may, the style it ends up with is kept, and the next element of that kind under
the same parent adopts it and skips matching and applying. 52% of the elements of `invoices-l` have such an earlier sibling, 71% of `table-l`'s.

Scoped to **one parent** because what matches an element depends on its ancestors (descendant and child combinators, `:lang()`, `@container`),
which are the same for siblings and not for cousins. Across parents nearly every element of these documents would hit, and it would be wrong the first
time a stylesheet had `tr.odd td`.

## What may not share (`SelectorSiblingStability`, an allowlist)

Only the *subject* of a selector (the last compound) is asked about the element: everything left of it is reached by walking up, and every sibling
finds the same elements there, so `li:first-child a` and `h1 + p span` are fine. A selector is stable when its subject asks about the tag, class,
id, attributes, `:lang()`, `:root`, `:link`, constant states (`:hover`, `:focus`: a PDF has none), or `:not()`/`:is()`/`:where()` of the same, and the
combinator in front of the subject is a descendant or child one. Everything else is not: `:first-child` and the other positional pseudo-classes,
`:nth-*`, `:only-*`, the column forms, `:empty`, `:placeholder-shown` (reads a textarea's text), `:has()`, `+` and `~` before the subject, and any
selector it does not recognise. A rule inside an `@container` also stops it: the matcher looks at surroundings it does not follow here.

Matching a compound that ends in `::before`, `::after`, `::marker` or `::first-letter` against an element **adds a box to it or sets
`MatchesFirstLetterSelector`** (`CssData.DoesSelectorMatch(CompoundSelector)`), so an element a sibling-shared style skipped would go without. Such a
rule stops sharing for the elements its subject (minus the pseudo-element) matches. The pseudo-element on its own only matches a box that is already
there; `p ::before` is not that case (it parses as `p *::before`, which does synthesize). After the leader's cascade the box count, the
display-contents shell list and the first-letter flag are checked again, and a leader that changed any of them is not kept.

Left out as for text boxes: a parent with custom properties, an animated document. Also foreign content (SVG, MathML: attribute names there are
case-sensitive, `HtmlTag` compares them without regard to case).

## Checking it

`DomParser.VerifySharedStyles` (a test hook) makes a hit run the cascade anyway and throw if the style, the box count, the shell list or the
first-letter flag differ. The whole suite, the benchmark documents (38,786 element hits on `invoices-l`), all 221 showcases (3,755 element hits)
and the 200 fuzz documents were run that way before the cache was trusted. `SiblingStyleSharingTests` does it for tables with zebra stripes and
first/last-child rules, sibling combinators, `:empty`, `:has()`, `:not()`/`:is()`, attribute selectors, descendant context, pseudo-elements,
`@container`/`@media`/`@layer`, `!important`/`revert`, inline styles, custom properties, `display: contents`, flex/grid items, floats, forms and
`:placeholder-shown`, SVG and math, bidi; and renders each document, and the math corpus, with sharing on and off, requiring identical content.
`SelectorSiblingStabilityTests` pins the judgement selector by selector.

**The check had to be structural.** The first version compared `ComputedStyle`s with `Equals`, and failed on the "Monolithic Content" showcase: two
equal `background` declarations had made two `List<CssImage>` (and gradients are parsed objects compared by reference), so equal styles were not
`Equals`. Sharing them is fine (a list of images is not changed after the cascade, `CssImage.Url` loads once per instance and its `Dispose` is safe to
repeat). The check now renders both styles through `CssUtils.SnapshotProperties`. A reflection-based comparison was tried first and removed: it
raised trim warnings, and a library that publishes AOT should not carry it.

## Not done

Sharing across parents. It would need the candidate rules to have no combinators at all (compound selectors only), which is rare in author CSS.
The next cost in the cascade is applying the declarations of the rules that do match (about a third of it), which is already memoized per
(style, property, value) by the transition cache.
