# Lexer emitted `Environment.NewLine` instead of nothing for an escaped line continuation

## What was wrong

Per CSS Syntax Level 3 §4.3.7, a backslash immediately followed by a newline inside a quoted string (or
unquoted `url()` content) is a "line continuation": it must be fully consumed and contribute zero
characters to the token's value. `Lexer.AppendLineContinuation()`
(`src/PeachPDF/CSS/Parser/Lexer.cs`) instead called `StringBuffer.AppendLine()` with no arguments, which
appends `Environment.NewLine` (`"\r\n"` on Windows, `"\n"` on Linux/macOS) — not an empty string. Called
from four sites (`StringDoubleQuote`, `StringSingleQuote`, `UrlDoubleQuote`, `UrlSingleQuote`), so every
one of those token kinds got a platform-dependent newline inserted instead of the escaped newline being
dropped.

This is a pre-existing bug (confirmed present on `main` independent of the `Token.Data` span migration
that surfaced it — see
[2026-09-12-token-data-becomes-span-only.md](2026-09-12-token-data-becomes-span-only.md)), tracked as
[issue #1024](https://github.com/jhaygood86/PeachPDF/issues/1024) and an accepted gap. It stopped being
something that could be deferred once it started failing CI on two of three platforms: the regression
test that pinned the (buggy) current behavior asserted a Windows-observed value, which doesn't hold on
Linux/macOS — the bug is platform-*dependent*, so a test asserting "today's behavior" on one platform is
never going to be true everywhere.

## The fix

Deleted the `StringBuffer.AppendLine()` call in `AppendLineContinuation()`, keeping only
`_mustMaterialize = true` (still required: the run's length now diverges from a literal source slice,
since bytes were consumed from `Source` but nothing was added to the buffer, so `EndContent()` must keep
returning the accumulated buffer rather than attempting a slice). No other code needed to change —
`AppendEscape`/`IsValidEscape` (the `\41`-style hex-escape path) are a separate, mutually-exclusive
branch, and CRLF/lone-CR→LF normalization already happens correctly and platform-independently in
`LexerBase.NormalizeForward` before this method is ever reached, so no extra newline-form handling was
needed here.

Restored `TokenDataSlicingTests.cs`'s test to its pre-migration name and spec-correct assertion
(`String_ContainingEscapedLineContinuation_MaterializesWithoutTheEscapedNewline`, asserting `"ab"`), and
fixed three more tests in `Tokenization.cs`
(`StringSingleQuote_EscapedLineContinuation_InsertsThePlatformLineTerminator`,
`UrlDoubleQuote_EscapedLineContinuation_InsertsThePlatformLineTerminator`,
`UrlSingleQuote_EscapedLineContinuation_InsertsThePlatformLineTerminator`) that had independently pinned
the same buggy behavior by name — renamed to `..._MaterializesWithoutTheEscapedNewline` and flipped to
assert `"ab"`/`"aAb"`-style spec-correct content instead of `"a" + Environment.NewLine + "b"`. Deleted
the accepted-gap file and closed #1024.

## Evidence

`dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`: 11025 passed, 0 failed, 9 skipped
— all four previously-platform-dependent tests now assert (and pass with) the same spec-correct value
regardless of OS, which is the actual fix being verified here (this can't be fully confirmed on a single
Windows dev machine alone — the real proof is CI going green on ubuntu-latest and macos-latest, which
were the two platforms actually failing).
