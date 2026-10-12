# Structural selectors answer from a per-parent sibling index

## The load-bearing idea

`:nth-child`/`-last-child`/`-of-type`/`-last-of-type`, the `+` and `~` combinators and `:has(+ S)`/`:has(~ S)`
each located the element among its siblings by scanning `parent.Children`, and the `:nth-*` family also copied the
siblings into a fresh list (`Where(...).ToList().IndexOf(node)`) for every element tested. Asked of every row of an
N-row table, that is O(N²) time and N-element allocations per row. `tr:nth-child(even)` zebra striping is the single
most common report-table idiom.

`SiblingIndex` (`Html/Core/SiblingIndex.cs`) indexes a parent's element children once: element list, node → position,
and (lazily, only for `-of-type`) position and count per tag. Every lookup above now takes it first and falls back to
the old scan when it has no answer, so the old code remains the definition of correct behaviour.

## Why the index proves itself on every use instead of being invalidated

`CssBox.Boxes` is a plain `List<CssBox>` mutated from everywhere (Add/Insert/Remove, the pseudo-element synthesis in
`CssData` among them), so there is no choke point to bump a version from. The index is keyed by that list in a
`ConditionalWeakTable` (no leak, no size bound to manage) and is trusted only while the child count is unchanged and
the child it answers about still sits in the slot it was indexed at. A grown list rebuilds it; a same-size reshuffle is
caught by the slot check and sends the caller to the scan. Only a swap of one element for another that leaves every
other position alone could go unnoticed, and nothing in the cascade does that.

## What was left alone, deliberately

- **Parents under 16 children are not indexed.** Scanning a handful is cheaper than building a map and keeps the
  common case allocation-free.
- **SVG nodes are not indexed.** `SvgCssBoxDomNode.Children`/`SvgXmlDomNode.Children` build a new list on every
  access, so there is no stable list to key on, and SVG sibling runs are short.
- **`:nth-child(An+B of S)` keeps the walk.** The filter changes what is counted, so it cannot use the unfiltered index.

## Evidence

- `SiblingIndexTests`: 17 selector tests on a 36-child mixed parent (text nodes between elements, three tags) against a
  naive oracle, tests of the index's own staleness handling, and a cost test comparing 3,000 and 24,000 rows. With the
  index forced off the cost test fails (1.2s → 59.5s, ~50x for 8x the rows); with it on, linear.
- Real documents (Release CLI, matched generated table, same machine, PDF bytes identical):
  `table-m` (3,000 rows, 80pp) 6.2s → 4.8s; `table-l` (30,000 rows, 790pp) **298s → 30.7s**, peak working set
  3.66GB → 2.90GB.
- Full suite on net8.0 in Release green (16,388 passed), whole solution `-t:Rebuild` with 0 warnings, changed lines
  covered.

## What this did not fix

`table-l` is still ~3x Typst's time. That remainder is not selector cost; it needs its own profile. Prose and
multi-column documents were never affected by this (their cost is the stale-slot catch-up in `FragmentEmitter`).
