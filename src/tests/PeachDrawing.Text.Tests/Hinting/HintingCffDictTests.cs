using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Internal.Hinting.FreeType;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>The DICT operand parser of the CFF port: the number encodings, the operators it stores, and what FreeType refuses.</summary>
    public class HintingCffDictTests
    {
        private const byte Int0 = 139; // the one-byte integer 0
        private const byte OpFontMatrix0 = 12, OpFontMatrix1 = 7;

        private static byte One(int value)
        {
            Assert.InRange(value, -107, 107);
            return (byte)(value + 139);
        }

        // a real number: byte 30 and then its nibbles (0-9 digits, a the point, b E, c E-, e the minus sign), padded and terminated with f
        private static byte[] Real(string nibbles)
        {
            var text = nibbles.Length % 2 == 0 ? nibbles + "ff" : nibbles + "f";
            var bytes = new List<byte> { 30 };
            for (int i = 0; i < text.Length; i += 2)
                bytes.Add(Convert.ToByte(text.Substring(i, 2), 16));
            return bytes.ToArray();
        }

        private static byte[] Cat(params object[] parts)
        {
            var bytes = new List<byte>();
            foreach (var part in parts)
            {
                switch (part)
                {
                    case byte b: bytes.Add(b); break;
                    case int i: bytes.Add((byte)i); break;
                    case byte[] array: bytes.AddRange(array); break;
                    default: throw new InvalidOperationException();
                }
            }

            return bytes.ToArray();
        }

        private static CffFontDict Top(byte[] dict)
        {
            var top = new CffFontDict();
            CffParser.Run(dict, 0, dict.Length, CffParser.Kind.Top, top, null);
            return top;
        }

        private static CffPrivate Private(byte[] dict)
        {
            var priv = new CffPrivate();
            CffParser.Run(dict, 0, dict.Length, CffParser.Kind.Private, null, priv);
            return priv;
        }

        private static byte[] Matrix(byte[] xx, byte[] yy) => Cat(xx, Int0, Int0, yy, Int0, Int0, OpFontMatrix0, OpFontMatrix1);

        [Fact]
        public void AFontMatrixOfSmallRealsIsNormalisedToTheUnitsPerEmTheyImply()
        {
            var thousand = Top(Matrix(Real("0a001"), Real("0a001")));
            Assert.True(thousand.HasFontMatrix);
            Assert.Equal(1000u, thousand.UnitsPerEm);
            Assert.Equal(0x10000, thousand.MatrixXx);
            Assert.Equal(0x10000, thousand.MatrixYy);

            var twoThousand = Top(Matrix(Real("0a0005"), Real("0a0005"))); // 0.0005 is 5 in units of a ten-thousandth
            Assert.Equal(10000u, twoThousand.UnitsPerEm);
            Assert.Equal(twoThousand.MatrixXx, twoThousand.MatrixYy);
        }

        [Fact]
        public void AFontMatrixInScientificNotationMeansTheSameAsTheDecimalOne()
        {
            var top = Top(Matrix(Real("1c3"), Real("1c3")));
            Assert.Equal(1000u, top.UnitsPerEm);
            Assert.Equal(0x10000, top.MatrixXx);
            Assert.Equal(0x10000, top.MatrixYy);
        }

        [Fact]
        public void AFontMatrixWithMixedMagnitudesKeepsTheSmallerElementsInProportion()
        {
            // xx 0.001, yx 0.0005 (the skew of half a unit per unit), yy 0.001
            var dict = Cat(Real("0a001"), Real("0a0005"), Int0, Real("0a001"), Int0, Int0, OpFontMatrix0, OpFontMatrix1);
            var top = Top(dict);
            Assert.Equal(1000u, top.UnitsPerEm);
            Assert.Equal(0x10000, top.MatrixXx);
            Assert.Equal(0x8000, top.MatrixYx);
        }

        [Fact]
        public void AFontMatrixOfNegativeElementsKeepsTheirSign()
        {
            var dict = Cat(Real("0a001"), Int0, Real("e0a0005"), Real("0a001"), Int0, Int0, OpFontMatrix0, OpFontMatrix1);
            var top = Top(dict);
            Assert.Equal(1000u, top.UnitsPerEm);
            Assert.Equal(-0x8000, top.MatrixXy);
        }

        [Fact]
        public void AnIntegerFontMatrixIsKeptAsItIs()
        {
            // an integer matrix is kept as it is, with a unit of one
            var big = Cat(new byte[] { 28, 0x03, 0xE8 }, Int0, Int0, new byte[] { 28, 0x03, 0xE8 }, Int0, Int0, OpFontMatrix0, OpFontMatrix1);
            var top = Top(big);
            Assert.Equal(1u, top.UnitsPerEm);
            Assert.Equal(1000 << 16, top.MatrixXx);
            Assert.Equal(0, top.MatrixYx);

            // elements many orders of magnitude apart
            var apart = Cat(Real("0a001"), Real("0a00000000001"), Int0, Real("0a001"), Int0, Int0, OpFontMatrix0, OpFontMatrix1);
            Assert.True(Top(apart).HasFontMatrix);
        }

        [Fact]
        public void AFontMatrixThatCannotBeInvertedIsTheDefaultOne()
        {
            var singular = Cat(Int0, Int0, Int0, Int0, Int0, Int0, OpFontMatrix0, OpFontMatrix1);
            var top = Top(singular);
            Assert.True(top.HasFontMatrix);
            Assert.Equal(0x10000, top.MatrixXx);
            Assert.Equal(0x10000, top.MatrixYy);
        }

        [Fact]
        public void TheOffsetsAndTypesOfATopDictAreStored()
        {
            var top = Top(Cat(
                new byte[] { 29, 0x00, 0x01, 0x00, 0x00 }, 15, // charset 65536
                new byte[] { 28, 0x12, 0x34 }, 17, // CharStrings 0x1234
                One(1), 12, 6, // CharstringType 1
                One(50), new byte[] { 28, 0x00, 0x80 }, 18, // Private: size 50 at 128
                new byte[] { 28, 0x01, 0x00 }, 12, 36, // FDArray
                new byte[] { 28, 0x02, 0x00 }, 12, 37, // FDSelect
                One(5), 16, // Encoding (ignored)
                One(0), One(0), One(0), 12, 30, // ROS
                One(9), 20)); // an operator a Top DICT does not use

            Assert.Equal(65536u, top.CharsetOffset);
            Assert.Equal(0x1234u, top.CharstringsOffset);
            Assert.Equal(1u, top.CharstringType);
            Assert.Equal(50u, top.PrivateSize);
            Assert.Equal(128u, top.PrivateOffset);
            Assert.Equal(256u, top.FdArrayOffset);
            Assert.Equal(512u, top.FdSelectOffset);
            Assert.Equal(0u, top.CidRegistry);
        }

        [Fact]
        public void APrivateDictStoresItsBluesAsCumulativeFixedNumbersAndItsScalarsAsIntegers()
        {
            var priv = Private(Cat(
                One(-15), One(15), new byte[] { 29, 0x00, 0x00, 0x01, 0xE6 }, One(12), 6, // BlueValues: -15 0 486 498, as deltas
                One(-20), One(0), 7, // OtherBlues
                One(-15), One(15), 8, // FamilyBlues
                One(-20), One(0), 9, // FamilyOtherBlues
                One(10), 10, // StdHW
                One(11), 11, // StdVW
                One(5), 12, 10, // BlueShift
                One(3), 12, 11, // BlueFuzz
                One(1), 12, 17, // LanguageGroup
                new byte[] { 28, 0x00, 0x2A }, 12, 19, // initialRandomSeed
                new byte[] { 29, 0x00, 0x00, 0x01, 0x00 }, 19, // Subrs
                One(20), 20, // defaultWidthX
                new byte[] { 28, 0x01, 0x90 }, 21)); // nominalWidthX

            Assert.Equal(4, priv.NumBlueValues);
            Assert.Equal(-15 << 16, priv.BlueValues[0]);
            Assert.Equal(0, priv.BlueValues[1]);
            Assert.Equal(486 << 16, priv.BlueValues[2]);
            Assert.Equal(498 << 16, priv.BlueValues[3]);
            Assert.Equal(2, priv.NumOtherBlues);
            Assert.Equal(-20 << 16, priv.OtherBlues[0]);
            Assert.Equal(2, priv.NumFamilyBlues);
            Assert.Equal(2, priv.NumFamilyOtherBlues);
            Assert.Equal(10, priv.StandardWidth);
            Assert.Equal(11, priv.StandardHeight);
            Assert.Equal(5, priv.BlueShift);
            Assert.Equal(3, priv.BlueFuzz);
            Assert.Equal(1, priv.LanguageGroup);
            Assert.Equal(42, priv.InitialRandomSeed);
            Assert.Equal(256u, priv.LocalSubrsOffset);
            Assert.Equal(20, priv.DefaultWidth);
            Assert.Equal(400, priv.NominalWidth);
        }

        [Fact]
        public void TooManyBlueValuesAreCutToTheCapacityOfTheArray()
        {
            var bytes = new List<byte>();
            for (int i = 0; i < 20; i++)
                bytes.Add(One(1));
            bytes.Add(6);

            var priv = Private(bytes.ToArray());
            Assert.Equal(CffPrivate.MaxBlueValues, priv.NumBlueValues);
            Assert.Equal(CffPrivate.MaxBlueValues << 16, priv.BlueValues[CffPrivate.MaxBlueValues - 1]);
        }

        [Fact]
        public void RealsAsIntegerOperandsAreTruncatedToTheirWholePart()
        {
            var priv = Private(Cat(Real("12a5"), 10, Real("1b2"), 11, Real("e3a7"), 20));
            Assert.Equal(12, priv.StandardWidth);
            Assert.Equal(100, priv.StandardHeight);
            Assert.InRange(priv.DefaultWidth, -4, -3);
        }

        [Fact]
        public void TheBlueScaleIsAFixedNumberScaledByAThousand()
        {
            // 0.039625 as a 16.16 number, scaled by the 1000 of the operator: 2596864
            var priv = Private(Cat(Real("0a039625"), 12, 9));
            Assert.InRange(priv.BlueScale, 2596000, 2598000);

            // a scale written as an integer and one written with a large exponent
            var integer = Private(Cat(One(1), 12, 9));
            Assert.InRange(integer.BlueScale, 0x10000 - 1, 0x10000 * 1000 + 1);
            Assert.NotEqual(0, Private(Cat(Real("1b3"), 12, 9)).BlueScale);
            Assert.Equal(0, Private(Cat(Real("1c9"), 12, 9)).BlueScale); // too small for a fixed number
            Assert.NotEqual(0, Private(Cat(Real("12345a6789"), 12, 9)).BlueScale);
        }

        [Fact]
        public void RealsWithDigitsBeyondWhatAFixedNumberHoldsStillParse()
        {
            foreach (var real in new[] { "1234567890a1", "0a00000000001", "99999999999", "1b30", "1c30", "0a1b40", "123456a789012", "e5b30", "9a9c9" })
            {
                var priv = Private(Cat(Real(real), 12, 9));
                _ = priv.BlueScale; // no exception: an operand FreeType cannot represent saturates or reads as zero
            }
        }

        [Fact]
        public void AnUnterminatedRealAtTheEndOfADictIsHarmless()
        {
            var priv = Private(new byte[] { One(3), 10, 30, 0x12 });
            Assert.Equal(3, priv.StandardWidth);
        }

        [Fact]
        public void TheThreeAndFiveByteIntegersAreDecoded()
        {
            var priv = new CffPrivate();
            CffParser.Run(new byte[] { 29, 0x00, 0x00, 0x01, 0x00, 10 }, 0, 6, CffParser.Kind.Private, null, priv);
            Assert.Equal(256, priv.StandardWidth);

            var cut = new CffPrivate();
            CffParser.Run([28, 0x01, 0x02, 10, 29, 0, 0, 0, 5, 11], 0, 10, CffParser.Kind.Private, null, cut);
            Assert.Equal(258, cut.StandardWidth);
            Assert.Equal(5, cut.StandardHeight);
        }

        [Fact]
        public void TheLongerIntegersOfOneAndTwoBytesAreDecoded()
        {
            var priv = Private(Cat(new byte[] { 247, 0x05 }, 10, new byte[] { 251, 0x05 }, 11, new byte[] { 254, 0xFF }, 20, new byte[] { 250, 0xFF }, 21));
            Assert.Equal(113, priv.StandardWidth);
            Assert.Equal(-113, priv.StandardHeight);
            Assert.Equal(-(3 * 256) - 255 - 108, priv.DefaultWidth);
            Assert.Equal(3 * 256 + 255 + 108, priv.NominalWidth);
        }

        [Fact]
        public void ADictWithTooManyOperandsIsRefused()
        {
            var bytes = new List<byte>();
            for (int i = 0; i < 100; i++)
                bytes.Add(One(1));
            bytes.Add(10);
            Assert.Throws<HintingException>(() => Private(bytes.ToArray()));

            // an operator needs a stack slot of its own: one operand fewer than the stack holds still runs
            var full = new List<byte>();
            for (int i = 0; i < 95; i++)
                full.Add(One(1));
            full.Add(6);
            Assert.Equal(CffPrivate.MaxBlueValues, Private(full.ToArray()).NumBlueValues);
        }

        [Fact]
        public void ATwoByteOperatorThatTheDictEndsInsideOfIsRefused()
        {
            Assert.Throws<HintingException>(() => Private([One(1), 12]));
        }

        [Fact]
        public void OperatorsWithoutTheirOperandsAreRefused()
        {
            Assert.Throws<HintingException>(() => Top([15]));
            Assert.Throws<HintingException>(() => Top([17]));
            Assert.Throws<HintingException>(() => Top([16]));
            Assert.Throws<HintingException>(() => Top([12, 6]));
            Assert.Throws<HintingException>(() => Top([12, 36]));
            Assert.Throws<HintingException>(() => Top([12, 37]));
            Assert.Throws<HintingException>(() => Private([10]));
            Assert.Throws<HintingException>(() => Private([12, 9]));
            Assert.Throws<HintingException>(() => Private([12, 19]));
            Assert.Throws<HintingException>(() => Private([19]));
            Assert.Throws<HintingException>(() => Private([20]));
            Assert.Throws<HintingException>(() => Private([21]));
            Assert.Throws<HintingException>(() => Private([12, 10]));
            Assert.Throws<HintingException>(() => Private([12, 11]));
            Assert.Throws<HintingException>(() => Private([12, 17]));
            Assert.Throws<HintingException>(() => Private([11]));
        }

        [Fact]
        public void APrivateOperatorNeedsItsSizeAndOffsetAndBothMustBeNonNegative()
        {
            Assert.Throws<HintingException>(() => Top([One(5), 18]));
            Assert.Throws<HintingException>(() => Top([One(-5), One(5), 18]));
            Assert.Throws<HintingException>(() => Top([One(5), One(-5), 18]));
        }

        [Fact]
        public void AMalformedRosOrFontMatrixIsRefused()
        {
            Assert.Throws<HintingException>(() => Top([One(1), One(2), 12, 30]));
            Assert.Throws<HintingException>(() => Top([One(1), One(0), One(0), One(1), One(0), 12, 7]));
        }

        [Fact]
        public void OperatorsOfTheOtherKindOfDictAreIgnored()
        {
            var priv = new CffPrivate();
            CffParser.Run([One(9), 17, One(3), 10], 0, 4, CffParser.Kind.Private, null, priv);
            Assert.Equal(3, priv.StandardWidth);

            var top = new CffFontDict();
            CffParser.Run([One(9), 10, One(4), 17], 0, 4, CffParser.Kind.Top, top, null);
            Assert.Equal(4u, top.CharstringsOffset);

            // and a DICT run with nothing to store into reads nothing and fails nothing
            CffParser.Run([One(9), 17], 0, 2, CffParser.Kind.Top, null, null);
        }

        [Fact]
        public void TheLegacyOperatorsAndReservedBytesAreNotNumbers()
        {
            // byte 31 is an operator with no meaning here, and clears the operands before it
            var priv = Private(Cat(One(9), 31, One(3), 10));
            Assert.Equal(3, priv.StandardWidth);
        }
    }
}
