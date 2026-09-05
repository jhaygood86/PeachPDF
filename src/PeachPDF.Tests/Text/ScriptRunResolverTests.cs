using PeachPDF.Text;
using System.Linq;
using Xunit;

namespace PeachPDF.Tests.Text
{
    /// <summary>Coverage for <see cref="ScriptRunResolver"/>'s Common/Inherited-to-surrounding-script
    /// resolution (UAX #24 §5.1).</summary>
    public class ScriptRunResolverTests
    {
        [Fact]
        public void AllRealScript_PassesThroughUnchanged()
        {
            // "Hi" - both Latin.
            var resolved = ScriptRunResolver.Resolve([0x0048, 0x0069]);

            Assert.Equal(["Latin", "Latin"], resolved);
        }

        [Fact]
        public void CommonCodepoint_MidRun_ResolvesToPrecedingScript()
        {
            // "A1B" - '1' is Common (a digit), sandwiched between two Latin letters.
            var resolved = ScriptRunResolver.Resolve([0x0041, 0x0031, 0x0042]);

            Assert.Equal(["Latin", "Latin", "Latin"], resolved);
        }

        [Fact]
        public void InheritedCombiningMark_ResolvesToBaseLetterScript()
        {
            // Arabic BEH followed by FATHATAN (Inherited combining mark).
            var resolved = ScriptRunResolver.Resolve([0x0628, 0x064B]);

            Assert.Equal(["Arabic", "Arabic"], resolved);
        }

        [Fact]
        public void LeadingCommonRun_BackwardFillsFromFirstRealScript()
        {
            // Opens with a space and exclamation mark (both Common) before any real-script letter.
            var resolved = ScriptRunResolver.Resolve([0x0020, 0x0021, 0x0628]);

            Assert.Equal(["Arabic", "Arabic", "Arabic"], resolved);
        }

        [Fact]
        public void CommonBetweenTwoDifferentScripts_ResolvesToPrecedingScript()
        {
            // Latin "A", a space (Common), Arabic BEH - the space takes the PRECEDING script (Latin),
            // not the following one, matching the forward-fill-first algorithm real shapers use.
            var resolved = ScriptRunResolver.Resolve([0x0041, 0x0020, 0x0628]);

            Assert.Equal(["Latin", "Latin", "Arabic"], resolved);
        }

        [Fact]
        public void AllCommon_StaysCommon_NothingToResolveAgainst()
        {
            // "1 2" - digits and a space, no real script anywhere.
            var resolved = ScriptRunResolver.Resolve([0x0031, 0x0020, 0x0032]);

            Assert.Equal(["Common", "Common", "Common"], resolved);
        }

        [Fact]
        public void AllInherited_CollapsesToCommon_NothingToInheritFrom()
        {
            // Two combining marks with no base letter at all - a degenerate, essentially unreachable
            // case in practice, but must not throw or return a nonsensical value.
            var resolved = ScriptRunResolver.Resolve([0x0300, 0x0301]);

            Assert.Equal(["Common", "Common"], resolved);
        }

        [Fact]
        public void Empty_ReturnsEmpty()
        {
            var resolved = ScriptRunResolver.Resolve([]);

            Assert.Empty(resolved);
        }

        [Fact]
        public void ResolveRaw_MatchesResolve_ForAlreadyLookedUpValues()
        {
            var codepoints = new[] { 0x0041, 0x0031, 0x0628 };
            var raw = codepoints.Select(ScriptTable.Of).ToArray();

            Assert.Equal(ScriptRunResolver.Resolve(codepoints), ScriptRunResolver.ResolveRaw(raw));
        }
    }
}
