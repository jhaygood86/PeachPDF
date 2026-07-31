# The UA stylesheet's `[dir]` rule actually isolates instead of embedding

Closes [#554](https://github.com/jhaygood86/PeachPDF/issues/554). Files
[#575](https://github.com/jhaygood86/PeachPDF/issues/575)/[#576](https://github.com/jhaygood86/PeachPDF/issues/576)
for the two narrower gaps this uncovered — see
[`.claude/accepted-gaps/synthetic-isolate-chaining-deep-nesting-edge-cases.md`](../accepted-gaps/synthetic-isolate-chaining-deep-nesting-edge-cases.md).

## The load-bearing idea

The reported bug was surface-level: `CssDefaults.DefaultStyleSheet`
(`src/PeachPDF/Html/Core/CssDefaults.cs`) used the legacy CSS2.1 sample stylesheet's
`unicode-bidi: embed`/`bidi-override` for the `[dir=ltr]`/`[dir=rtl]` and `bdo[dir]` UA rules, instead
of the current HTML Standard's `isolate`/`isolate-override` (`<bdi>`'s own rule was already correct).
Changing the three keywords looked like the whole fix.

It wasn't. `BidiResolver` (`src/PeachPDF/Text/Bidi/BidiResolver.cs`) implements every CSS
`unicode-bidi` value as a synthetic push/pop onto an explicit-level stack
(`BidiIsolateOverride`/`BidiExplicitPush`) rather than by inserting real Unicode LRI/RLI/FSI/PDI
control characters into the paragraph text — deliberately, so string indices stay stable. But
`ComputeMatchingPdi`/`ComputeIsolatingRunSequences` (X10/BD13 "isolating run sequence" formation)
only ever looked for literal LRI/RLI/FSI/PDI `BidiClass` values *in the text* to decide which level
runs to chain together — which a synthetic push, by construction, never produces. The practical
effect: `unicode-bidi: isolate` and `embed` produced byte-for-byte identical level arrays for any
paragraph where neutral/EN-as-R resolution spans the box (UAX#9's N1 rule treats European numbers as
R for neutral-sandwiching purposes) — exactly issue #554's own repro,
`<p>1 <span dir="rtl">עברית</span> 2</p>`. Real Chromium renders that as `1 [hebrew] 2`; PeachPDF
rendered `1 2 [hebrew]` for **both** `isolate` and `embed`, before and after the keyword swap alone.

The actual fix adds a second chaining path to `ComputeIsolatingRunSequences`: a `syntheticChain` map
built from the isolate-initiating (`Lri`/`Rli`/`Fsi`) overrides, keyed by each override's own `Start`
(where the "before" run ends) to its `End` (where the "after" run starts) — mirroring real BD13
chaining, but located by index-adjacency instead of by an actual PDI character type. Two more bugs
surfaced once two overrides could interact, both caught by an independent review pass before this
landed: registering a chain entry whose `Start` never actually coincides with any run's end (two
adjacent same-direction isolates merge into one run with no boundary at all) would mark a run nothing
chains into as a "continuation," silently dropping it from every sequence — fixed by only registering
an entry when `runEndIndex` confirms a real run actually ends there. And two isolate scopes closing at
the identical index (an inner isolate that is the last content of its own enclosing isolate) would
both chain to the same "after" run, adding its positions twice and letting the second, wrong-context
`ResolveSequence` call silently overwrite the first's correct one — fixed with a `!visited[...]` guard
on every chain jump.

## What was found by running it, not by reading it

The keyword-swap-only version passed every existing test unchanged, because none of them mix a CSS
isolate with digit-sandwiching or nested/sibling isolates closing at the same index — the existing
`<bdi>` isolation test uses only strong-L content on both sides, which resolves identically whether
the box embeds or isolates. Rendering the issue's own repro through a real Chromium instance (browser
tool, before *and* after each change) was what exposed that the keyword swap alone changed nothing
observable, and later that the first version of the BD13 chaining fix had its own two bugs — an
independent review agent found both from reading the diff, and both were confirmed by temporarily
reverting each guard and watching the corresponding new test fail exactly as predicted.

## Evidence

Full `net8.0` suite: 7368 passing, 9 skipped, 0 failed (includes 2 new tests in
`PeachPDF.Tests/Text/Bidi/BidiResolverSyntheticIsolateTests.cs`, both confirmed to fail without their
respective guard and pass with it). Unicode's own ~92k-case `BidiCharacterTest.txt` conformance suite
(`BidiResolverConformanceTests`) still 100% passing — the fix only adds a second, purely additive
chaining path; it never touches the real-character matching path that suite exercises.
`dotnet build PeachPDF.slnx -t:Rebuild`: zero warnings. `diff-cover` against `origin/main`: 100%.
