# The `data:` URI bypass is per-target-framework, and it belongs in one predicate

`RUri` held a `data:` URI verbatim rather than constructing a `System.Uri`, but only in its two
single-string constructors. The two base-relative constructors — `RUri(RUri, string)` and
`RUri(RUri, RUri)` — had no `data:` handling at all, and those are the ones the resource pipeline
actually calls: `CommonUtils.ResolveAgainstDocumentBase` builds every `<img src>` through
`new RUri(baseUri, src)`. So the workaround was in place and the bug still shipped.

## What running it turned up

- **The first fix deleted the `#if NET10_0_OR_GREATER` gate, and that was a regression.** Collapsing
  both targets onto the string-holding path looks like a simplification, but .NET 10's `System.Uri`
  is strictly better for this: it round-trips a `data:` URI exactly, has no length ceiling
  (a 5,000,023-character payload constructs fine), and percent-escapes a non-base64 payload, which
  holding the raw string does not — `data:text/plain,Hello World!` comes back escaped there and
  verbatim before .NET 10. The gate is back, and
  `RUriTests.DataUri_NonBase64Payload_EscapesOnNet10AndIsVerbatimBefore` pins the difference on both
  targets so it cannot be flattened again unnoticed.
- **The gate belongs in one predicate, not in four constructors.** `MustBypassSystemUri` is
  constantly `false` on .NET 10 and every constructor asks it. Repeating an `#if` per constructor is
  precisely how the two base-relative ones ended up uncovered in the first place.
- **The 65,519 ceiling is pre-.NET-10 only.** An earlier version of this doc comment said "on every
  framework", which is wrong and would have left a future reader believing .NET 10 has a defect it
  demonstrably does not.
- **Gating the bypass creates a second per-target difference, in scheme casing.** RFC 3986 §3.1
  makes a scheme case-insensitive with lower case canonical, and `System.Uri` normalizes to it — so
  an authored `DATA:` comes back as `data:` on .NET 10 and survives verbatim before it. A test
  asserting byte-equality against the authored string therefore passes on net8.0 and fails on
  net10.0; CI caught exactly that. The fixture now asserts the invariant that holds on both (the
  payload survives, `Scheme` is `data`) and pins the casing difference per target.
- **The net10 path is simulatable on net8.** Forcing `MustBypassSystemUri` to `false` runs every
  `data:` fixture through `System.Uri` on net8.0, which is the .NET 10 code path minus the length
  ceiling: any failure that is not `UriFormatException: The Uri string is too long` is a real .NET 10
  failure. That is how the scheme-casing defect was isolated without a .NET 10 SDK to hand.
- **`RUri.Uri` is not covered by the bypass**, and reconstructing from the held string hits the same
  ceiling. No caller reaches it for a `data:` URI — the three that read it are selected by `Scheme`
  first — but it is now documented on the property, pointing a new caller at `AbsoluteUri` instead.

## Evidence

Five fixtures added to `RUriTests`, three of which fail against the merge base: both base-relative
constructors with a 100 KB payload, the case-insensitive scheme, an ordinary relative reference as
the contrast case (the short-circuit must not swallow it), and the per-TFM escaping difference.
Full suite green on net8.0, 0 build warnings, diff coverage 100% on the changed lines.

## Deliberately not done

No general resource-size cap. Removing an incidental ceiling on untrusted-HTML input deserves the
scrutiny, but this one was a `System.Uri` buffer artifact rather than a guard, and nothing else in
`Network`/`Html/Core/Utils` bounds resource size either — many small `data:` URIs already reach the
same place. Tracked separately as a configurable option rather than left as an implicit side effect
of a parser limit.
