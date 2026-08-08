using PeachPDF.Text.Bidi;
using System.Collections.Generic;
using Xunit;

namespace PeachPDF.Tests.Text.Bidi
{
    /// <summary>
    /// Regression tests for the X10/BD13 "isolating run sequence" chaining <see cref="BidiResolver"/> does
    /// for a CSS <c>unicode-bidi: isolate</c>/<c>isolate-override</c> box's synthetic push (see
    /// <see cref="BidiIsolateOverride"/>), as distinct from a real Unicode LRI/RLI/FSI/PDI control
    /// character. A synthetic push never occupies an index of its own, so the chain has to be located by
    /// index-adjacency (an override's own Start/End) rather than by an actual PDI character type - two
    /// failure modes that surfaced only once two overrides interact are covered here directly, since
    /// <see cref="CssLayoutEngineBidiTests"/>'s single-span case can't exercise them.
    /// </summary>
    public class BidiResolverSyntheticIsolateTests
    {
        [Fact]
        public void NestedIsolate_FlushAgainstEnclosingIsolateClose_DoesNotDoubleResolveTrailingContent()
        {
            // "A שלום ש" with an outer isolate over "שלום" and an inner isolate over its own last two
            // characters ("ום") - the inner isolate's own End coincides exactly with the outer isolate's
            // End (nothing follows "ום" inside the outer isolate), so both overrides' Start->End chain
            // entries land on the very same "after" run. Without the !visited guard in
            // ComputeIsolatingRunSequences, both the outer isolate's own chain (from the "A " run) and the
            // inner isolate's chain (from the "של" run) would jump to that same trailing run and add its
            // positions twice - the second (spurious) ResolveSequence call would resolve the trailing
            // space+Hebrew-letter using the INNER isolate's own (matched R/R neighbor) context instead of
            // the correct outer/paragraph-level context, silently overwriting the first, correct result.
            const string text = "A שלום ש"; // "A " + "שלום" + " " + "ש"

            var outer = new BidiIsolateOverride(Start: 2, Length: 4, Push: BidiExplicitPush.Rli); // "שלום"
            var inner = new BidiIsolateOverride(Start: 4, Length: 2, Push: BidiExplicitPush.Rli); // "ום" (flush against outer's End)

            var result = BidiResolver.Resolve(text, BidiParagraphDirection.Ltr, [outer, inner]);

            // The space right after both isolates close belongs only to the outer isolate's own (correct)
            // sequence, resolved against "A"(L) before and the trailing "ש"(R) after - a mismatch, so N2's
            // embedding direction (L, since the paragraph is level 0/even) applies and it stays at level 0.
            // The inner isolate's spurious duplicate resolution would instead see R/R neighbors (matched,
            // via N1) and push it to level 1 - so this assertion fails if the duplicate-visit bug regresses.
            Assert.Equal(0, result.Levels[6]);

            // The trailing Hebrew letter itself is an ordinary strong-R character at the paragraph's base
            // (even) level - I1/I2 must bump it to level 1 exactly once, regardless of how many isolate
            // chains reach it.
            Assert.Equal(1, result.Levels[7]);
        }

        [Fact]
        public void AdjacentIsolates_WithNoGapBetweenThem_StillResolveTrailingContent()
        {
            // Two sibling dir="rtl" boxes back to back ("שת" then "בג", no text between them) push to the
            // identical level from the identical starting level, so their content merges into ONE level run
            // with no boundary at all at the point where the second box's own override starts - that Start
            // never coincides with any run's End. Registering it in the synthetic chain map regardless
            // would still mark the run after it (the trailing "ש") as a continuation nothing ever actually
            // chains into, permanently excluding it from every sequence - so its level would never advance
            // past whatever ResolveExplicitLevels assigned it pre-resolution, instead of the level I1/I2
            // implicit resolution actually requires for a strong-R character at the paragraph's base level.
            const string text = "Aשתבגש"; // "A" + "שת" + "בג" + "ש"

            var span1 = new BidiIsolateOverride(Start: 1, Length: 2, Push: BidiExplicitPush.Rli); // "שת"
            var span2 = new BidiIsolateOverride(Start: 3, Length: 2, Push: BidiExplicitPush.Rli); // "בג"

            var result = BidiResolver.Resolve(text, BidiParagraphDirection.Ltr, [span1, span2]);

            // The trailing "ש" is a strong-R character sitting at the paragraph's base (even) level once
            // both isolates close - I1/I2 must still bump it to level 1. A dropped-positions regression
            // leaves it un-resolved at its raw pre-implicit-level value (0) instead.
            Assert.Equal(1, result.Levels[5]);
        }

        [Fact]
        public void SyntheticFsiPush_OverContentStartingWithStrongRtlCharacter_ResolvesIsolateAsRtl()
        {
            // A synthetic Fsi push (CSS unicode-bidi: plaintext - see CssUnicodeBidiMapping.MapToPushes)
            // must apply real UAX#9 P2/P3 first-strong-character detection over its own [Start, End) text
            // range, exactly as a real U+2068 FIRST STRONG ISOLATE character would over its own
            // "matching PDI" range - not fall back to always behaving like Lri (issue #552).
            const string text = "A שלום B"; // "A " + "שלום" (leading strong-R) + " B"

            var fsi = new BidiIsolateOverride(Start: 2, Length: 4, Push: BidiExplicitPush.Fsi); // "שלום"

            var result = BidiResolver.Resolve(text, BidiParagraphDirection.Ltr, [fsi]);

            // The isolate's own content is detected RTL, so I1/I2 keeps its strong-R characters at an odd
            // (level 1) embedding level - a wrongly-Lri-treated isolate would instead resolve this at the
            // even paragraph level (0) since Lri's own new level is the *next even* level up from the
            // paragraph's, not RTL.
            Assert.Equal(1, result.Levels[2]);
            Assert.Equal(1, result.Levels[5]);
        }

        [Fact]
        public void SyntheticFsiPush_WithLeadingNestedIsolateOverride_SkipsItRatherThanReadingItsFirstCharacter()
        {
            // UAX#9 P2 skips characters between an isolate initiator and its matching PDI when hunting for
            // the first strong character - and that applies to a CSS-synthetic nested isolate override
            // (e.g. a dir="rtl" span, mapped to its own Rli push) exactly as it would a real Unicode
            // LRI/RLI/FSI...PDI run, even though the nested override never occupies a character position
            // of its own in the flattened text for a naive per-character scan to notice. A synthetic
            // Fsi's own detection has to consult the override list, not just originalTypes, or this nested
            // span's leading strong-R character ('ש') would be read directly instead of skipped.
            const string text = "שלום Hello"; // nested-isolate "שלום" (strong-R) + " " + "Hello" (strong-L)

            // Outer plaintext box wraps the whole thing; inner is a plain dir="rtl" span (unicode-bidi:
            // isolate, not plaintext) over just "שלום" - added outer-before-inner, matching how
            // CssBidiParagraphResolver.Flatten always appends a box's own override before recursing into
            // any nested box's.
            var outerFsi = new BidiIsolateOverride(Start: 0, Length: 10, Push: BidiExplicitPush.Fsi);
            var innerRli = new BidiIsolateOverride(Start: 0, Length: 4, Push: BidiExplicitPush.Rli); // "שלום"

            var result = BidiResolver.Resolve(text, BidiParagraphDirection.Ltr, [outerFsi, innerRli]);

            // A strong character's own final level self-corrects to match its own type via I1/I2
            // regardless of which (right or wrong) explicit level the enclosing isolate picked, so 'H'..'o'
            // land at the same final level either way and can't tell a fixed detection apart from a buggy
            // one on their own. The space right after the nested isolate closes (index 4) is the
            // discriminating character: as a neutral, N0-N2 resolves it from its *sos* (start-of-sequence)
            // type, which - unlike a strong character's own final level - is read directly from the
            // outer isolate's actual (uncorrected) explicit level. Correctly detected LTR (even explicit
            // level) gives this space an L-matching sos, resolving it (via N1) to the same level as the
            // 'H' that follows it. A version that (wrongly) read 'ש' directly instead of skipping the
            // nested isolate would detect RTL (odd explicit level) instead, giving the space a
            // mismatched, one-level-lower R sos/N2 resolution as a result - even though 'H' itself still
            // self-corrects back to the "right" level regardless.
            Assert.Equal(result.Levels[5], result.Levels[4]);

            // The inner dir="rtl" span keeps its own explicit (not detected) RTL direction regardless -
            // one level above the outer isolate's own (correctly LTR/even) level.
            Assert.Equal(3, result.Levels[0]); // 'ש'
            Assert.Equal(3, result.Levels[3]); // 'ם'
        }

        [Fact]
        public void AutoParagraphDirection_WithLeadingRealIsolateCharacter_SkipsItRatherThanReadingItsFirstCharacter()
        {
            // The real-Unicode-character counterpart of
            // SyntheticFsiPush_WithLeadingNestedIsolateOverride_..._: P2's "skip between an isolate
            // initiator and its matching PDI" applies just the same when the isolate initiator is an
            // actual U+2067 RIGHT-TO-LEFT ISOLATE character in the text (not a CSS-synthetic override) -
            // this is the pre-existing isolateDepth-tracking loop in ComputeAutoParagraphLevel that the
            // CSS-override-skip feature (added alongside this test) had to compose with, not replace.
            const string text = "⁧שלום⁩ Hello"; // RLI "שלום" PDI + " Hello"

            var result = BidiResolver.Resolve(text, BidiParagraphDirection.Auto);

            // First strong character outside any isolate is 'H' (L) - paragraph resolves LTR (level 0) -
            // reading the isolated 'ש' directly instead would have resolved RTL (level 1).
            Assert.Equal(0, result.ParagraphLevel);
        }
    }
}
