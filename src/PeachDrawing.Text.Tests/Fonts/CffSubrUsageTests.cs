using System.Linq;
using PeachDrawing.Text.Internal.Fonts.OpenType;

namespace PeachDrawing.Text.Tests.Fonts
{
    public class CffSubrUsageTests
    {
        [Fact]
        public void Add_TracksLocalAndGlobalSubroutinesAcrossAllBiasRanges()
        {
            byte[][] shortSubrs = [[11]];
            var shortBias = new CffSubrUsage(shortSubrs, _ => shortSubrs);
            shortBias.Add([32, 10, 32, 29, 14], 0); // -107 + bias(1) = 0

            Assert.True(shortBias.Complete);
            Assert.Contains(0, shortBias.GlobalSubrs);
            Assert.Contains(0, shortBias.LocalSubrsForFd(0));

            byte[][] mediumSubrs = Enumerable.Repeat(new byte[] { 11 }, 1240).ToArray();
            var mediumBias = new CffSubrUsage(mediumSubrs, _ => mediumSubrs);
            mediumBias.Add([28, 0xFB, 0x95, 10, 14], 0); // -1131 + bias(1240) = 0

            Assert.True(mediumBias.Complete);
            Assert.Contains(0, mediumBias.LocalSubrsForFd(0));

            byte[][] largeSubrs = Enumerable.Repeat(new byte[] { 11 }, 33900).ToArray();
            var largeBias = new CffSubrUsage(largeSubrs, _ => largeSubrs);
            largeBias.Add([28, 0x80, 0x00, 29, 14], 0); // -32768 + bias(33900) = 0

            Assert.True(largeBias.Complete);
            Assert.Contains(0, largeBias.GlobalSubrs);
        }

        [Fact]
        public void Add_AbandonsWhenCallsHaveNoOperandOrNameAnInvalidSubroutine()
        {
            byte[][] noSubrs = [];
            var missingLocalOperand = new CffSubrUsage(noSubrs, _ => noSubrs);
            missingLocalOperand.Add([10], 0);
            Assert.False(missingLocalOperand.Complete);

            var missingGlobalOperand = new CffSubrUsage(noSubrs, _ => noSubrs);
            missingGlobalOperand.Add([29], 0);
            Assert.False(missingGlobalOperand.Complete);

            var invalidLocalIndex = new CffSubrUsage(noSubrs, _ => noSubrs);
            invalidLocalIndex.Add([32, 10], 0); // -107 + bias(0) is outside the empty INDEX
            Assert.False(invalidLocalIndex.Complete);
        }

        [Fact]
        public void Add_HandlesEscapesAndStopsAtTheMaximumSubroutineDepth()
        {
            var escaped = new CffSubrUsage([], _ => []);
            escaped.Add([12, 0, 14], 0);
            Assert.True(escaped.Complete);

            var truncatedEscape = new CffSubrUsage([], _ => []);
            truncatedEscape.Add([12], 0);
            Assert.False(truncatedEscape.Complete);

            byte[][] recursiveSubrs = Enumerable.Range(0, 11)
                .Select(index => index == 10
                    ? new byte[] { 11 }
                    : new byte[] { (byte)(33 + index), 10 })
                .ToArray(); // a chain longer than the scanner's maximum depth
            var recursive = new CffSubrUsage([], _ => recursiveSubrs);
            recursive.Add([32, 10], 0);
            Assert.False(recursive.Complete);

            // Once a scan is abandoned, later charstrings do not resume parsing.
            recursive.Add([14], 0);
            Assert.False(recursive.Complete);
        }

        [Fact]
        public void Add_ReachesEachFontDictionarysLocalSubroutineThroughASharedGlobalSubroutine()
        {
            // Global subr 0 calls local subr 0 of whichever font dictionary the glyph belongs to.
            byte[][] globalSubrs = [[32, 10, 11]]; // push -107, callsubr, return
            byte[][] fd0Subrs = [[11]];
            byte[][] fd1Subrs = [[11]];
            var usage = new CffSubrUsage(globalSubrs, fd => fd == 0 ? fd0Subrs : fd1Subrs);

            usage.Add([32, 29, 14], 0); // push -107, callgsubr, endchar
            usage.Add([32, 29, 14], 1);

            Assert.True(usage.Complete);
            Assert.Contains(0, usage.GlobalSubrs);
            Assert.Contains(0, usage.LocalSubrsForFd(0));
            Assert.Contains(0, usage.LocalSubrsForFd(1));
        }

        [Fact]
        public void Add_CountsTheHintsOfASubroutineWalkedForAnEarlierGlyph()
        {
            // Local subr 0 adds twelve hints (24 operands, hstemhm), so the hintmask after it takes two mask bytes.
            // Its second byte is a return opcode: the call to subr 1 after it is found only if the hint count
            // still includes subr 0's stems when the second glyph reaches the hintmask.
            byte[] stems = [.. Enumerable.Repeat((byte)139, 24), 18, 11];
            byte[][] localSubrs = [stems, [11]];
            var usage = new CffSubrUsage([], _ => localSubrs);

            usage.Add([32, 10, 14], 0); // callsubr 0, endchar
            usage.Add([32, 10, 19, 0, 11, 33, 10, 14], 0); // callsubr 0, hintmask, two mask bytes, callsubr 1, endchar

            Assert.True(usage.Complete);
            Assert.Contains(0, usage.LocalSubrsForFd(0));
            Assert.Contains(1, usage.LocalSubrsForFd(0));
        }
    }
}
