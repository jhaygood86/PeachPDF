using PeachDrawing.Text.Internal.Hinting.FreeType;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>The operand stack of the CFF engine, and the fixed-point helpers the CFF port added to the arithmetic of the interpreter.</summary>
    public class HintingCffStackTests
    {
        private static (Cf2Stack Stack, Cf2Error Error) Make(int size = 4)
        {
            var error = new Cf2Error();
            return (new Cf2Stack(error, size), error);
        }

        [Fact]
        public void ANumberIsReadBackInWhicheverKindItIsAskedFor()
        {
            var (stack, error) = Make();
            stack.PushInt(3);
            stack.PushFixed(0x18000); // 1.5
            Assert.Equal(2, stack.Count);

            Assert.Equal(3 << 16, stack.GetReal(0));
            Assert.Equal(0x18000, stack.GetReal(1));
            Assert.Equal(0x18000, stack.PopFixed());
            Assert.Equal(3, stack.PopInt());
            Assert.Equal(0, error.Value);
        }

        [Fact]
        public void PushingBeyondTheCapacityIsAnOverflowAndStoresNothing()
        {
            var (stack, error) = Make(1);
            stack.PushInt(1);
            Assert.Equal(0, error.Value);

            stack.PushFixed(2);
            Assert.Equal(Cf2Error.StackOverflow, error.Value);
            Assert.Equal(1, stack.Count);

            var (other, otherError) = Make(1);
            other.PushInt(1);
            other.PushInt(2);
            Assert.Equal(Cf2Error.StackOverflow, otherError.Value);
            Assert.Equal(1, other.Count);
        }

        [Fact]
        public void PoppingAnEmptyStackIsAnUnderflowThatReadsAsZero()
        {
            var (stack, error) = Make();
            Assert.Equal(0, stack.PopInt());
            Assert.Equal(Cf2Error.StackUnderflow, error.Value);

            var (second, secondError) = Make();
            Assert.Equal(0, second.PopFixed());
            Assert.Equal(Cf2Error.StackUnderflow, secondError.Value);
        }

        [Fact]
        public void APoppedIntegerMustBeAnInteger()
        {
            var (stack, error) = Make();
            stack.PushFixed(0x10000);
            Assert.Equal(0, stack.PopInt());
            Assert.Equal(Cf2Error.SyntaxError, error.Value);
            Assert.Equal(1, stack.Count);
        }

        [Fact]
        public void AReadOutsideTheStackIsAnErrorThatReadsAsZero()
        {
            var (stack, error) = Make();
            stack.PushInt(7);
            Assert.Equal(0, stack.GetReal(1));
            Assert.Equal(Cf2Error.StackOverflow, error.Value);
        }

        [Fact]
        public void ARandomAccessWriteReplacesTheValueAndIsBoundedByTheCount()
        {
            var (stack, error) = Make(2);
            stack.PushInt(7);
            stack.SetReal(0, 0x28000);
            Assert.Equal(0x28000, stack.GetReal(0));
            Assert.Equal(0, error.Value);

            // the first free slot may be written, though it does not count as a value
            stack.SetReal(1, 5);
            Assert.Equal(0, error.Value);

            // the slot after the last of a full buffer is not written, and is not an error either
            stack.PushInt(8);
            stack.SetReal(2, 5);
            Assert.Equal(0, error.Value);

            // but nothing further is
            stack.SetReal(3, 5);
            Assert.Equal(Cf2Error.StackOverflow, error.Value);
        }
        [Fact]
        public void PoppingMoreThanTheStackHoldsIsAnUnderflow()
        {
            var (stack, error) = Make();
            stack.PushInt(1);
            stack.PushInt(2);
            stack.Pop(1);
            Assert.Equal(1, stack.Count);
            Assert.Equal(0, error.Value);

            stack.Pop(2);
            Assert.Equal(Cf2Error.StackUnderflow, error.Value);
            Assert.Equal(1, stack.Count);
        }

        [Fact]
        public void RollingMovesValuesByTheShiftAndRefusesMoreThanThereAre()
        {
            var (stack, error) = Make(8);
            for (int i = 0; i < 5; i++)
                stack.PushInt(i);

            stack.Roll(0, 1); // nothing to do
            stack.Roll(1, 1);
            stack.Roll(5, 0);
            Assert.Equal(0, error.Value);

            stack.Roll(5, 1); // 0 1 2 3 4 -> 4 0 1 2 3, the top on the right
            Assert.Equal(new[] { 4, 0, 1, 2, 3 }, Enumerable.Range(0, 5).Select(i => stack.GetReal(i) >> 16));

            stack.Roll(5, -1); // and back
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, Enumerable.Range(0, 5).Select(i => stack.GetReal(i) >> 16));

            stack.Roll(4, 2); // two chains of two
            Assert.Equal(new[] { 2, 3, 0, 1, 4 }, Enumerable.Range(0, 5).Select(i => stack.GetReal(i) >> 16));

            stack.Roll(6, 1);
            Assert.Equal(Cf2Error.StackOverflow, error.Value);

            stack.Clear();
            Assert.Equal(0, stack.Count);
        }

        [Fact]
        public void FixedPointRoundingHelpersRoundHalfAwayFromZero()
        {
            Assert.Equal(0, FtCalc.PixRound(31));
            Assert.Equal(64, FtCalc.PixRound(32));
            Assert.Equal(64, FtCalc.PixCeil(1));
            Assert.Equal(64, FtCalc.PixCeil(64));
            Assert.Equal(4, FtCalc.PadRound(5, 4));
            Assert.Equal(8, FtCalc.PadRound(6, 4));
            Assert.Equal(0x20000, FtCalc.RoundFix(0x18000));
            Assert.Equal(-0x20000, FtCalc.RoundFix(-0x18000));
            Assert.Equal(0x10000, FtCalc.RoundFix(0x8000));
            Assert.Equal(0, FtCalc.RoundFix(0x7FFF));
        }

        [Fact]
        public void TheFixedPointSquareRootIsExactForSquaresAndRoundedOtherwise()
        {
            Assert.Equal(0u, FtCalc.SqrtFixed(0));
            Assert.Equal(1u << 16, FtCalc.SqrtFixed(1u << 16));
            Assert.Equal(2u << 16, FtCalc.SqrtFixed(4u << 16));
            Assert.InRange(FtCalc.SqrtFixed(2u << 16), 0x16A09u, 0x16A0Bu);
        }

        [Fact]
        public void ScaledMatrixAndVectorProductsDivideByTheScaling()
        {
            const int one = 0x10000;

            int bxx = 2 * one, bxy = 0, byx = 0, byy = 3 * one;
            FtCalc.MatrixMultiplyScaled(one, 0, 0, one, ref bxx, ref bxy, ref byx, ref byy, 1);
            Assert.Equal((2 * one, 0, 0, 3 * one), (bxx, bxy, byx, byy));

            FtCalc.MatrixMultiplyScaled(one, 0, 0, one, ref bxx, ref bxy, ref byx, ref byy, 2);
            Assert.Equal((one, 0, 0, 3 * one / 2), (bxx, bxy, byx, byy));

            int x = 10, y = 20;
            FtCalc.VectorTransformScaled(ref x, ref y, 2 * one, 0, 0, 2 * one, 1);
            Assert.Equal((20, 40), (x, y));

            FtCalc.VectorTransformScaled(ref x, ref y, one, 0, 0, one, 2);
            Assert.Equal((10, 20), (x, y));
        }

        [Fact]
        public void OnlyAnInvertibleMatrixOfRepresentableValuesPassesTheMatrixCheck()
        {
            const int one = 0x10000;
            Assert.True(FtCalc.MatrixCheck(one, 0, 0, one));
            Assert.True(FtCalc.MatrixCheck(one, one / 2, 0, one));
            Assert.False(FtCalc.MatrixCheck(0, 0, 0, 0));
            Assert.False(FtCalc.MatrixCheck(one, one, one, one));
        }
    }
}
