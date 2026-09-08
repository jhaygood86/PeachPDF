# HTML names match ASCII case-insensitively, not by Unicode case folding

The selector matcher, the tag/class/id indexes, `IsBrElement`, the font caches and the entity tables
all compared names with `InvariantCultureIgnoreCase`. HTML matches element and attribute names
**ASCII** case-insensitively; invariant culture applies full Unicode case folding.

Under that folding **U+212A KELVIN SIGN equals ASCII `k`**, so `class="K"` matched the selector `.k`
and a `K` element matched `k`. The comment above `NameComparison` already said the rule was ASCII
case-insensitivity — the code just did not express it.

## What running it turned up

- **The correctness case is demonstrable and the performance case is not, on this machine.** This
  started as a profile-driven change (`System.Globalization` was a fifth of thread CPU on a real
  corpus, and throughput rose ~5%). I could not reproduce a trustworthy number here: nine interleaved
  renders of a synthetic class-heavy document gave a 175ms median before and 177ms after, with a
  164–225ms spread — the variance swamps the effect. So this is offered as the spec fix it also is,
  with a deterministic repro, and the throughput left unquantified rather than quoted from a machine
  that cannot show it.
- **Ordinal is the cheaper comparison as well as the correct one**, which is why both readings point
  the same way: the matcher runs one name against every candidate rule for every box, and a
  culture-aware comparison routes each through ICU.
- **A blanket `sed` to strip our internal comment tag ate the comment marker with it**, turning a
  `// TAG: Ordinal, not InvariantCulture` line into bare prose and producing 19 syntax errors.
  Replace the tag with `// `, never delete it.

- **Two call sites were worse than the ones the first sweep found, and it missed them.**
  `DomUtils.FindParent` (the parser's implied-tag-closing rules — a `<table>` closes an open `<p>`)
  and `DomParser.CascadeParseStyles` (the `link`/`style` tag names and the `stylesheet` relation)
  used `CurrentCultureIgnoreCase`: full Unicode folding **and** locale-dependent, so the same
  document could parse differently on two machines. Grepping for the comparison the fix replaced
  found neither; the sweep has to be for every culture-aware comparison, not for one enum value.
- **`FontResolver` keys its installed-font table on `.ToLower()`**, which is also the current
  culture. That is a cross-machine determinism bug in its own right — a Turkish locale lower-cases
  `I` to a dotless `ı` — so all four sites are `ToLowerInvariant()`. It does NOT fix the folding
  (`char.ToLowerInvariant('K')` is still `k`), and font-family matching is not the spec-mandated
  ASCII rule element names are, so that half is left alone deliberately.

## Evidence

`AsciiNameMatchingTests`, five fixtures: a Kelvin-sign class does not match `.k` (fails against
`main`); an ASCII class still matches case-insensitively in both cases; an uppercase `DIV` still
matches `div`; implied tag closing still matches `P` to `p`; and an uppercase `STYLE` element is
still honoured. The last four are contrast cases — the point is to narrow the comparison to ASCII,
not to make it case-sensitive. Full suite green on net8.0
(10,348), 0 new build warnings.
