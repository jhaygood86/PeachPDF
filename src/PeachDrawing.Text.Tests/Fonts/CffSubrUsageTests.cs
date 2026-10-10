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
    }
}
