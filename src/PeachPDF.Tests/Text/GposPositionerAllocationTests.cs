using PeachPDF.Fonts.OpenType;
using PeachPDF.Text;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Xunit;

namespace PeachPDF.Tests.Text
{
    /// <summary>
    /// The GPOS positioner walks a lookup's subtables without allocating an enumerator per glyph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A lookup's <c>Subtables</c> is declared as <see cref="IReadOnlyList{T}"/>, so <c>foreach</c>
    /// over it boxes a fresh enumerator every time. The positioner runs that loop <b>once per glyph</b>
    /// — <see cref="GposPositioner.ApplyMarkToBase"/> calls straight into it for every glyph in the run
    /// — so the cost lands on every glyph of every text run in a document, whether or not the font has
    /// anything to attach.
    /// </para>
    /// <para>
    /// This is asserted at the positioner rather than through a rendered document on purpose. Measured
    /// end to end it is real but not separable: allocation over a document scales with the font a
    /// machine happens to resolve, so any threshold that catches it on one platform is either loose or
    /// wrong on another. Called directly, with a lookup that has no subtables at all, the only thing
    /// that can allocate is the loop itself — which makes the assertion exact and the same everywhere.
    /// </para>
    /// </remarks>
    public class GposPositionerAllocationTests
    {
        [Fact]
        public void WalkingALookupsSubtables_AllocatesNothingPerGlyph()
        {
            // One subtable whose coverage matches nothing, so the loop runs and finds no work: the
            // only thing that can allocate is the walk itself. The descriptor is never dereferenced on
            // this path, which is what lets it stay null.
            //
            // NOT an empty list, which was the first version of this and does not reproduce: List<T>
            // hands back a cached singleton enumerator when it is empty, so the very allocation under
            // test disappears. It needs at least one element.
            var subtable = new GposMarkAttachmentSubtable
            {
                MarkCoverage = MatchesNothing(),
                BaseCoverage = MatchesNothing(),
                MarkClassCount = 0,
                Marks = [],
                BaseAnchorsByClass = [],
            };

            var lookup = new GposMarkToBaseLookup { Subtables = new List<GposMarkAttachmentSubtable> { subtable } };

            var glyphs = new List<ShapedGlyph>();
            for (var i = 0; i < 200; i++) glyphs.Add(new ShapedGlyph(i, i, 1));

            // Warm: JIT the whole path before anything is counted.
            for (var i = 0; i < 3; i++) GposPositioner.ApplyMarkToBase(null!, lookup, glyphs, gdef: null);

            // Per THREAD, not process-wide: the suite runs collections in parallel, so
            // GC.GetTotalAllocatedBytes counts whatever every other test is allocating at the same
            // time and this reads over a megabyte of other people's work. This test body is
            // synchronous and never awaits, so it stays on the one thread throughout.
            const int passes = 50;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < passes; i++) GposPositioner.ApplyMarkToBase(null!, lookup, glyphs, gdef: null);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // 200 glyphs x 50 passes is 10,000 trips through the subtable loop. One boxed enumerator
            // each is ~320 KB; the indexed walk allocates nothing at all. A few hundred bytes of slack
            // rather than zero, so this states "not per glyph" without depending on the runtime never
            // allocating anything incidental.
            Assert.True(allocated < 4096,
                $"walking a lookup's subtables allocated {allocated} bytes over {passes * glyphs.Count:N0} "
                + "glyph visits. It should allocate nothing: the list is interface-typed, so a foreach "
                + "over it boxes an enumerator on every visit.");
        }

        /// <summary>
        /// A <see cref="CoverageTable"/> covering no glyph at all. Its constructor is private and its
        /// only public route in reads a real font, so this leaves both backing arrays null — the state
        /// <see cref="CoverageTable.IndexOfGlyph"/> already answers -1 for.
        /// </summary>
        private static CoverageTable MatchesNothing() =>
            (CoverageTable)RuntimeHelpers.GetUninitializedObject(typeof(CoverageTable));
    }
}
