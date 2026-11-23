# A word split by font fallback wraps with the fragments a line may not end before

**Symptom (css-text WPT, `i18n/css3-text-line-break-opclns-*`, 150 tests):** `中中中⦘文` in a 95px box left the `⦘` hanging past the edge of line 1. UAX #14 forbids a break before a closing character, so `中⦘` must wrap as a pair.

**Cause, found by printing the words, not by reading:** the test font has no U+2998, so `AddWord` cuts the word `中⦘` into two fragments, one per font. Fragments set `SuppressWrapBefore`. `FlowBox` fit-checks one word at a time and only wraps at a word with an ordinary wrap opportunity before it, so `中` fit, the glued `⦘` then did not, and having no opportunity before it, stayed and overflowed. The word the line may end before never knew what was glued after it.

**Fix:** `FlowBox` (horizontal path) asks `GluedRunWidthAfter` for the width of the words after the current one that a line may not end before (a `SuppressWrapBefore` fragment, or a word `HasOrdinaryWrapOpportunityBefore` rejects) and wraps when the word plus that run overflows. The vertical-column path in `CssLayoutEngine` still checks one word at a time.

**Traps:**
- A first version that only looked at `UnicodeBreakBefore == false` did nothing: the fragments have it `null`. A second that stopped the run at `SuppressWrapBefore` also did nothing: that flag marks glue, it does not end a run.
- Skip the check for hanging spaces (`PreservedSpacesHang`) and for words with `HungEndWidth`, and require a non-empty run, or `line-break: anywhere` + `pre-wrap` and `hanging-punctuation` tests regress.
- Same-box words only; a glued run that crosses an inline element boundary is still not moved as a unit (see [2026-09-26-line-break-follows-the-language-of-the-text.md](2026-09-26-line-break-follows-the-language-of-the-text.md)).

**Evidence:** css-text 666 -> 821 passes on a Mac (+155); the floor goes 640 -> 795 by that delta only, because CI's fonts differ from this Mac's (it sits one test under the css-lists floor with or without this change). The other 15 modules are unchanged. One test regressed: `text-spacing-trim/text-spacing-trim-end-span-001` passed only because a `）` used to overflow; it needs `text-spacing-trim`, which is not implemented. `PeachBrackets.Engine.Tests` 20,425 pass on net10.0; the new `ClosingCharacterFromAFallbackFont_...` test fails without the fix.
