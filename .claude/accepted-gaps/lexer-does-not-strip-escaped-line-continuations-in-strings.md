# The lexer does not strip escaped line continuations inside strings

Per CSS Syntax Level 3 [§4.3.7](https://www.w3.org/TR/css-syntax-3/#consume-string-token) ("consume a
string token"), a backslash immediately followed by a newline inside a quoted string is a line
continuation: it must be consumed and contribute nothing to the token's value. `Lexer`/`LexerBase` do
not implement this — for the source `"a\` + LF + `b"`, the tokenized string value comes out as `a` +
CR + LF + `b` (4 chars, with a spurious CR inserted alongside the un-stripped LF) instead of the
spec-correct `ab`.

This was found while migrating `Token.Data` from `string` to `ReadOnlySpan<char>`
(`.claude/recent-fixes/2026-09-12-token-data-becomes-span-only.md`): the existing regression test for
this case, `TokenDataSlicingTests.String_ContainingEscapedLineContinuation_MaterializesWithoutTheEscapedNewline`,
only ever asserted `token.Data == token.DataSpan.ToString()` — an equivalence between two accessors that
were equally wrong, never a check against the actual spec-correct value — so the bug predates that
migration and was never a regression from it. Confirmed directly against `Lexer`/`TextSource` on `main`
(commit `0f1fda01`), independent of the span migration. The test now pins today's actual (buggy) output
under the name `String_ContainingEscapedLineContinuation_CharacterizesCurrentLexerOutput` until this is
fixed.

Fixing it needs a special case for "backslash directly followed by a line terminator" that consumes the
pair and emits nothing, distinct from the general CRLF→LF normalization already applied correctly
elsewhere (e.g. inside comments — see `Comment_ContainingCrLfPair_NormalizesToLoneLineFeed`). Tracked as
[#1024](https://github.com/jhaygood86/PeachPDF/issues/1024).
