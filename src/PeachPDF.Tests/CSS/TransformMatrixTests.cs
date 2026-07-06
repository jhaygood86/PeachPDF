namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using System;
    using Xunit;

    public class TransformMatrixTests
    {
        [Fact]
        public void Zero_HasAllZeroComponents()
        {
            Assert.Equal(0f, TransformMatrix.Zero.Tx);
            Assert.Equal(0f, TransformMatrix.Zero.Ty);
            Assert.Equal(0f, TransformMatrix.Zero.Tz);
        }

        [Fact]
        public void One_IsIdentityLikeMatrix()
        {
            Assert.Equal(0f, TransformMatrix.One.Tx);
            Assert.Equal(0f, TransformMatrix.One.Ty);
            Assert.Equal(0f, TransformMatrix.One.Tz);
        }

        [Fact]
        public void Constructor_16Values_SetsTranslationComponents()
        {
            var values = new float[16];
            // Column-major 4x4: translation lives in the 4th column (indices 12,13,14).
            values[12] = 1f;
            values[13] = 2f;
            values[14] = 3f;
            values[15] = 1f;

            var matrix = new TransformMatrix(values);

            Assert.Equal(1f, matrix.Tx);
            Assert.Equal(2f, matrix.Ty);
            Assert.Equal(3f, matrix.Tz);
        }

        [Fact]
        public void Constructor_16Values_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new TransformMatrix(null));
        }

        [Fact]
        public void Constructor_16Values_WrongLength_Throws()
        {
            Assert.Throws<ArgumentException>(() => new TransformMatrix(new float[4]));
        }

        [Fact]
        public void Constructor_Explicit_SetsTranslationComponents()
        {
            var matrix = new TransformMatrix(
                1, 0, 0,
                0, 1, 0,
                0, 0, 1,
                10, 20, 30,
                0, 0, 0);

            Assert.Equal(10f, matrix.Tx);
            Assert.Equal(20f, matrix.Ty);
            Assert.Equal(30f, matrix.Tz);
        }

        [Fact]
        public void Equals_SameComponents_AreEqual()
        {
            var a = new TransformMatrix(1, 0, 0, 0, 1, 0, 0, 0, 1, 5, 6, 7, 0, 0, 0);
            var b = new TransformMatrix(1, 0, 0, 0, 1, 0, 0, 0, 1, 5, 6, 7, 0, 0, 0);

            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
        }

        [Fact]
        public void Equals_DifferentComponents_AreNotEqual()
        {
            var a = new TransformMatrix(1, 0, 0, 0, 1, 0, 0, 0, 1, 5, 6, 7, 0, 0, 0);
            var b = new TransformMatrix(1, 0, 0, 0, 1, 0, 0, 0, 1, 5, 6, 8, 0, 0, 0);

            Assert.False(a.Equals(b));
            Assert.False(a.Equals((object)"not a matrix"));
        }

        [Fact]
        public void GetHashCode_SameForEqualMatrices()
        {
            var a = new TransformMatrix(1, 0, 0, 0, 1, 0, 0, 0, 1, 5, 6, 7, 0, 0, 0);
            var b = new TransformMatrix(1, 0, 0, 0, 1, 0, 0, 0, 1, 5, 6, 7, 0, 0, 0);

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }
    }
}
