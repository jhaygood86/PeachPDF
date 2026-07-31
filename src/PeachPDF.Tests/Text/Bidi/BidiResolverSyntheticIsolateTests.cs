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
    }
}
