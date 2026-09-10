using PeachPDF;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using PeachPDF.Fonts;
using PeachPDF.Text;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    public class FontResolverCodepointTests
    {
        private static List<RuneRange> R(int start, int end) => [new RuneRange(new Rune(start), new Rune(end))];

        [Fact]
        public void ResolveTypeface_WithCodepoint_FiltersToTheCoveringFace()
        {
            var upperName = TtfFontDescription.LoadDescription(BundledFonts.Ttf).FontNameInvariantCulture; // Source Sans 3
            var lowerName = TtfFontDescription.LoadDescription(BundledFonts.Otf).FontNameInvariantCulture; // Source Code Pro

            var resolver = new FontResolver { NullIfFontNotFound = true };
            resolver.AddFont(File.OpenRead(BundledFonts.Ttf), "Combo", null, null, null, R(0x41, 0x5A)); // A-Z
            resolver.AddFont(File.OpenRead(BundledFonts.Otf), "Combo", null, null, null, R(0x61, 0x7A)); // a-z

            Assert.True(resolver.HasExplicitRanges("Combo"));

            // Each codepoint resolves to the face whose declared unicode-range covers it - even though both
            // fonts physically contain both cases.
            Assert.Equal(upperName, resolver.ResolveTypeface("Combo", 400, false, 5, new Rune('A')).FaceName);
            Assert.Equal(upperName, resolver.ResolveTypeface("Combo", 400, false, 5, new Rune('Z')).FaceName);
            Assert.Equal(lowerName, resolver.ResolveTypeface("Combo", 400, false, 5, new Rune('a')).FaceName);

            // A codepoint covered by no face's range reports "no covering face" so the caller can fall back.
            Assert.Null(resolver.ResolveTypeface("Combo", 400, false, 5, new Rune('0')));
        }

        [Fact]
        public void ResolveTypeface_CodepointLess_Ignores_Ranges_And_Never_Returns_Null()
        {
            // The existing (codepoint-less) overloads must keep working regardless of ranges: no coverage
            // filter, always a result (box/metrics resolution).
            var resolver = new FontResolver { NullIfFontNotFound = true };
            resolver.AddFont(File.OpenRead(BundledFonts.Ttf), "Ranged", null, null, null, R(0x41, 0x5A));

            Assert.NotNull(resolver.ResolveTypeface("Ranged", 400, false));
            Assert.NotNull(resolver.ResolveTypeface("Ranged", 400, false, 5));
        }

        [Fact]
        public void CMapCoverage_Extract_ReportsCoveredCodepoints_ForRangelessFont()
        {
            var fontSource = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Ttf));
            var coverage = CMapCoverage.Extract(fontSource.Fontface.cmap.cmap4);

            Assert.NotEmpty(coverage);
            Assert.True(CMapCoverage.Contains(coverage, new Rune('A')));
            Assert.True(CMapCoverage.Contains(coverage, new Rune('z')));

            var otf = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Otf));
            var otfCoverage = CMapCoverage.Extract(otf.Fontface.cmap.cmap4);
            Assert.True(CMapCoverage.Contains(otfCoverage, new Rune('a')));
        }

        // U+12000 (CUNEIFORM SIGN A) is used below as the "nothing in the declared stack covers this"
        // probe for FindFamilyCoveringCodepoint (issue #172's last-resort scan): its Unicode Script is
        // the real, verified-against-Scripts.txt value "Cuneiform" (see ScriptTableTests), and no
        // mainstream OS ships a Cuneiform-covering system font, so registering it as an explicit
        // unicode-range on an ordinary bundled font keeps these tests deterministic across the
        // Windows/Linux/macOS CI matrix (macOS in particular bundles system fonts for every modern
        // living script - Arabic/Hebrew/Devanagari/CJK/etc. - which would make a real-script choice here
        // flaky) without needing a real Cuneiform-glyph font asset.
        private const int CuneiformCodepoint = 0x12000;

        /// <summary>
        /// Every range <see cref="ScriptTable"/> itself reports for the Cuneiform script. Declaring a
        /// test candidate's coverage from this directly (rather than a hand-picked span) makes its
        /// overlap score PROVABLY unbeatable - not just larger than some other test font, but bounded
        /// by the same table <see cref="FontResolver.FindFamilyCoveringCodepoint"/> itself scores
        /// against, so no real font on any host (declared or system) can ever exceed it, only tie it.
        /// Real Cuneiform-covering system fonts turned out to exist in this repo's own CI matrix (macOS
        /// ships "Noto Sans Cuneiform"; Windows ships "Segoe UI Historic") - a fixed hex range that
        /// merely "looked broad" was not actually safe against them.
        /// </summary>
        private static List<RuneRange> FullCuneiformRanges() => ScriptTable.RangesForScript("Cuneiform").ToList();

        [Fact]
        public void FindFamilyCoveringCodepoint_FindsRegisteredFamily_NotJustTheDeclaredStack()
        {
            var resolver = new FontResolver { NullIfFontNotFound = true };

            // Even Cuneiform turned out not to be safe from a REAL system font: Windows bundles "Segoe UI
            // Historic", which genuinely covers it - discovered by this test actually failing against it.
            // Declaring the family's own coverage as the FULL Cuneiform block and prefixing its name with
            // '!' (which sorts before any real font family's name under ordinal comparison) means its
            // overlap score can at worst TIE a real font that also fully covers the script, and the '!'
            // prefix wins that tie - so the assertion holds no matter what fonts the host happens to have.
            resolver.AddFont(File.OpenRead(BundledFonts.Ttf), "!CuneiformFallback", null, null, null, R(0x12000, 0x12543));

            Assert.Equal("!cuneiformfallback", resolver.FindFamilyCoveringCodepoint(new Rune(CuneiformCodepoint)));
        }

        [Fact]
        public void FindFamilyCoveringCodepoint_ReturnsNull_WhenNothingRegisteredCoversTheCodepoint()
        {
            var resolver = new FontResolver { NullIfFontNotFound = true };

            // U+10FFFF (the very last Unicode codepoint) is a permanently-guaranteed-unassigned
            // noncharacter, in the supplementary plane specifically - not the BMP, where GNU Unifont
            // (bundled on Ubuntu CI runners) turned out to genuinely map a "here's an undefined
            // codepoint" glyph even to a BMP noncharacter like U+FDD0, defeating that as a "nothing
            // covers this" probe. Unifont's own astral coverage (the separate "Unifont Upper" font) is
            // not present on that image, so this stays safe there too.
            Assert.Null(resolver.FindFamilyCoveringCodepoint(new Rune(0x10FFFF)));
        }

        [Fact]
        public void FindFamilyCoveringCodepoint_CachesTheWinner_AcrossLaterRegistrations()
        {
            var resolver = new FontResolver { NullIfFontNotFound = true };
            var codepoint = new Rune(CuneiformCodepoint);
            resolver.AddFont(File.OpenRead(BundledFonts.Ttf), "!First", null, null, null, FullCuneiformRanges());

            var first = resolver.FindFamilyCoveringCodepoint(codepoint);
            Assert.Equal("!first", first);

            // A second, even more alphabetically-dominant AND equally-broad family registered AFTER the
            // first lookup must not change the cached answer for a codepoint already resolved.
            resolver.AddFont(File.OpenRead(BundledFonts.Otf), "!!EvenEarlier", null, null, null, FullCuneiformRanges());
            Assert.Equal(first, resolver.FindFamilyCoveringCodepoint(codepoint));
        }

        [Fact]
        public void FindFamilyCoveringCodepoint_PrefersBroaderScriptCoverage_OverAlphabeticalOrder()
        {
            var resolver = new FontResolver { NullIfFontNotFound = true };
            var codepoint = new Rune(CuneiformCodepoint);

            // Both names are '!'-prefixed so they dominate any real font the host also happens to have
            // (e.g. Windows' "Segoe UI Historic", macOS' "Noto Sans Cuneiform" - both genuinely cover
            // Cuneiform); between the two, alphabetical order alone would prefer "!AAANarrow", whose
            // declared unicode-range covers only the single requested codepoint - a "found this one
            // glyph incidentally" font. "!ZZZBroad" declares the whole Cuneiform script block instead
            // (see FullCuneiformRanges), so it can only win if script-aware scoring genuinely runs, not
            // merely by alphabetical tie-break.
            resolver.AddFont(File.OpenRead(BundledFonts.Ttf), "!AAANarrow", null, null, null, R(CuneiformCodepoint, CuneiformCodepoint));
            resolver.AddFont(File.OpenRead(BundledFonts.Otf), "!ZZZBroad", null, null, null, FullCuneiformRanges());

            Assert.Equal("!zzzbroad", resolver.FindFamilyCoveringCodepoint(codepoint));
        }

        [Fact]
        public void FindFamilyCoveringCodepoint_ScoresCorrectly_WhenExplicitRangesAreDeclaredOutOfOrder()
        {
            // unicode-range/AddFont's explicit range list is not required to be sorted (a descriptor like
            // "U+12480-12543, U+12000-12399" - later range first - is perfectly valid CSS), so the
            // overlap-scoring sweep must sort defensively rather than assume declaration order is
            // ascending. A naive two-pointer sweep over the list as-declared stops advancing through the
            // first (out-of-order) candidate's ranges too early and silently undercounts its real overlap
            // - reversing the FULL (provably-unbeatable, see FullCuneiformRanges) range list makes this a
            // real regression this test would have caught: the undercounted total lost even to
            // "!SortedRival"'s much narrower, trivially-sorted single range on the actual CI run that
            // caught this bug.
            var resolver = new FontResolver { NullIfFontNotFound = true };
            var codepoint = new Rune(CuneiformCodepoint);

            var reversedFullRange = FullCuneiformRanges();
            reversedFullRange.Reverse();
            resolver.AddFont(File.OpenRead(BundledFonts.Ttf), "!UnsortedBroad", null, null, null, reversedFullRange);
            resolver.AddFont(File.OpenRead(BundledFonts.Otf), "!SortedRival", null, null, null, R(0x12000, 0x12399));

            Assert.Equal("!unsortedbroad", resolver.FindFamilyCoveringCodepoint(codepoint));
        }

        [Fact]
        public void FindFamilyCoveringCodepoint_FallsBackToAlphabeticalOrder_ForCommonScriptCodepoint()
        {
            var resolver = new FontResolver { NullIfFontNotFound = true };
            var codepoint = new Rune('0'); // DIGIT ZERO - Unicode Script "Common", nothing to score against

            // '!' sorts before any letter/digit under ordinal comparison, so these stay first/last
            // regardless of what real fonts the host running this test also happens to have installed.
            resolver.AddFont(File.OpenRead(BundledFonts.Ttf), "!AAAFirst", null, null, null, R('0', '0'));
            resolver.AddFont(File.OpenRead(BundledFonts.Otf), "!ZZZSecond", null, null, null, R('0', '0'));

            Assert.Equal("!aaafirst", resolver.FindFamilyCoveringCodepoint(codepoint));
        }
    }
}
