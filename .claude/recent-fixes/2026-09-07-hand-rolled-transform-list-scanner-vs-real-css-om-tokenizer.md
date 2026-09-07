# A hand-rolled "does this look like a transform-list" scanner needs two non-obvious concessions to match the real tokenizer

Part of the issue #909 cssom-allocation-reduction work (Phase 5: `transform`'s permissive
`cssDataType: "transform-list"`, `CssValueParser.IsSyntacticallyValidTransformList`, a span-based
sibling to the real `Converters.TransformConverter.Many()` grammar). Writing an equivalence test
(`CssValueParserIsSyntacticallyValidTransformListTests.AgreesWithRealCssOmRoundTrip`, comparing the
new scanner against the literal `PropertyFactory.Create("transform")` + `StylesheetParser.Default.
ParseValue` + `TrySetValue` round trip it replaces) caught two real discrepancies neither manual
review nor the full test suite would have found on their own — both are worth knowing before writing
another hand-rolled "looks like this grammar" scanner anywhere else in this codebase.

## 1. CSS Syntax Level 3 closes an unterminated construct at EOF — a naive balanced-paren counter doesn't

`transform: translate(10px` (missing the closing `)`) is **accepted** by the real pipeline today: the
CSS tokenizer's own error-recovery rule closes an unterminated function/string/url at end-of-input
rather than treating it as a hard parse failure, so `StylesheetParser.Default.ParseValue` produces a
normal `FunctionToken` for `translate` with argument `10px` as if it had been written correctly. A
depth-counting scanner that requires depth to return to exactly 0 by the end of the string (the
"obviously correct" balanced-parens check) rejects this — which is a real, if narrow, behavior
regression relative to today.

Fix: when the scanner's char-index reaches end-of-string while still inside a function's argument
list (depth > 0), that's *not* an error — just stop scanning normally, exactly like reaching EOF while
depth is 0. There's nothing to additionally reject.

## 2. An argument-shape mismatch inside a recognized function is not a rejection either — but for a different reason

`transform: translate()` (zero arguments; `TranslateTransformConverter` requires at least one via
`LengthOrPercentConverter.Required()`) is **rejected** by the real per-function grammar. A permissive
scanner that deliberately doesn't validate argument shape (the entire point of "permissive" here, per
the accepted-gap file's requirement that an unimplemented function like `perspective()` must not
invalidate a whole value) will accept it anyway — a genuine, intentional divergence, not a bug, but
one that has to be *decided*, not discovered by accident.

The reason it's safe: `BuildFunctionMatrix` (the paint-time consumer, `CssValueParser.cs`) already
defaults a missing argument to `0` via its own `LengthArg`/`AngleArg` helpers (`index < args.Count ? ... :
0f`), so a malformed `translate()` renders as an identity transform rather than throwing — the same
"tolerate it, don't apply it" contract the accepted-gap file already established for an unrecognized
function name. Verify this exact claim (read the real paint-time consumer's argument-handling, don't
assume it) before relying on it to justify skipping argument validation in a new permissive scanner
elsewhere.

## The methodology that mattered

Both of these were found only because the equivalence test called the *actual* production objects
(`PropertyFactory.Instance.Create`, `StylesheetParser.Default.ParseValue`, `Property.TrySetValue`)
side-by-side with the new scanner over a table of deliberately adversarial inputs — not because of
code review, not because of the full `PeachPDF.Tests` suite (which has no existing coverage for
`translate(10px` or `translate()` specifically), and not because the design was reasoned through
carefully beforehand (it was, and still missed both). When adding another span-based "permissive
syntactic check" DataTypeKind for a property currently on `cssom`, write this kind of equivalence
test *before* trusting the new validator, over inputs that specifically probe: unterminated brackets/
quotes/functions, empty or too-few arguments, mixed-case keywords, and whitespace variations (none,
extra, tabs) — not just the "obviously valid" and "obviously invalid" cases that come to mind first.
