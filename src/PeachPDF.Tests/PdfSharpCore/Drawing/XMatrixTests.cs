using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing
{
    public class XMatrixTests
    {
        [Fact]
        public void Identity_IsIdentity()
        {
            var m = XMatrix.Identity;

            Assert.True(m.IsIdentity);
            Assert.Equal(new double[] { 1, 0, 0, 1, 0, 0 }, m.GetElements());
        }

        [Fact]
        public void Constructor_SetsElementsAndIsNotIdentity()
        {
            var m = new XMatrix(2, 0, 0, 2, 5, 6);

            Assert.False(m.IsIdentity);
            Assert.Equal(new double[] { 2, 0, 0, 2, 5, 6 }, m.GetElements());
        }

        [Fact]
        public void SetIdentity_ResetsMatrixToIdentity()
        {
            var m = new XMatrix(2, 0, 0, 2, 5, 6);
            m.SetIdentity();

            Assert.True(m.IsIdentity);
        }

        [Fact]
        public void Multiply_CombinesTwoMatrices()
        {
            var scale = XMatrix.Identity;
            scale.ScaleAppend(2, 2);
            var translate = XMatrix.Identity;
            translate.TranslateAppend(5, 5);

            var combined = scale * translate;
            var point = combined.Transform(new XPoint(1, 1));

            // Scale first, then translate (matches Multiply/operator* semantics).
            Assert.Equal(7, point.X, precision: 8);
            Assert.Equal(7, point.Y, precision: 8);
            Assert.Equal(combined, XMatrix.Multiply(scale, translate));
        }

        [Fact]
        public void Append_And_Prepend_ProduceDifferentOrder()
        {
            var translation = new XMatrix(1, 0, 0, 1, 5, 0);

            var m1 = XMatrix.Identity;
            m1.ScaleAppend(2, 2);
            m1.Append(translation);

            var m2 = XMatrix.Identity;
            m2.ScaleAppend(2, 2);
            m2.Prepend(translation);

            var p1 = m1.Transform(new XPoint(1, 0));
            var p2 = m2.Transform(new XPoint(1, 0));

            // Append applies the scale first, then the translation: (1,0) -> (2,0) -> (7,0).
            Assert.Equal(7, p1.X, precision: 8);
            // Prepend applies the translation first, then the scale: (1,0) -> (6,0) -> (12,0).
            Assert.Equal(12, p2.X, precision: 8);
        }

        [Fact]
        public void TranslateAppend_MovesPoints()
        {
            var m = XMatrix.Identity;
            m.TranslateAppend(3, 4);

            var p = m.Transform(new XPoint(1, 1));

            Assert.Equal(4, p.X, precision: 8);
            Assert.Equal(5, p.Y, precision: 8);
        }

        [Fact]
        public void TranslatePrepend_MovesPointsBeforeOtherTransforms()
        {
            var m = XMatrix.Identity;
            m.ScaleAppend(2, 2);
            m.TranslatePrepend(3, 4);

            var p = m.Transform(new XPoint(0, 0));

            Assert.Equal(6, p.X, precision: 8);
            Assert.Equal(8, p.Y, precision: 8);
        }

        [Fact]
        public void Translate_WithOrderParameter()
        {
            var append = XMatrix.Identity;
            append.Translate(3, 4, XMatrixOrder.Append);

            var prepend = XMatrix.Identity;
            prepend.Translate(3, 4, XMatrixOrder.Prepend);

            Assert.Equal(new XPoint(4, 5), append.Transform(new XPoint(1, 1)));
            Assert.Equal(new XPoint(4, 5), prepend.Transform(new XPoint(1, 1)));
        }

        [Fact]
        public void ScaleAppend_ScalesPoints()
        {
            var m = XMatrix.Identity;
            m.ScaleAppend(2, 3);

            var p = m.Transform(new XPoint(2, 2));

            Assert.Equal(4, p.X, precision: 8);
            Assert.Equal(6, p.Y, precision: 8);
        }

        [Fact]
        public void ScaleAppend_UniformScalar()
        {
            var m = XMatrix.Identity;
            m.ScaleAppend(2.0);

            var p = m.Transform(new XPoint(2, 3));

            Assert.Equal(4, p.X, precision: 8);
            Assert.Equal(6, p.Y, precision: 8);
        }

        [Fact]
        public void Scale_WithOrderParameter()
        {
            var m = XMatrix.Identity;
            m.Scale(2, 3, XMatrixOrder.Append);

            var p = m.Transform(new XPoint(2, 2));

            Assert.Equal(4, p.X, precision: 8);
            Assert.Equal(6, p.Y, precision: 8);
        }

        [Fact]
        public void ScaleAtAppend_ScalesAboutCenterPoint()
        {
            var m = XMatrix.Identity;
            m.ScaleAtAppend(2, 2, 1, 1);

            var p = m.Transform(new XPoint(1, 1));

            // The center point should remain fixed under the scale.
            Assert.Equal(1, p.X, precision: 8);
            Assert.Equal(1, p.Y, precision: 8);
        }

        [Fact]
        public void RotateAppend_Rotates90Degrees()
        {
            var m = XMatrix.Identity;
            m.RotateAppend(90);

            var p = m.Transform(new XPoint(1, 0));

            Assert.Equal(0, p.X, precision: 8);
            Assert.Equal(1, p.Y, precision: 8);
        }

        [Fact]
        public void Rotate_WithOrderParameter()
        {
            var m = XMatrix.Identity;
            m.Rotate(90, XMatrixOrder.Append);

            var p = m.Transform(new XPoint(1, 0));

            Assert.Equal(0, p.X, precision: 8);
            Assert.Equal(1, p.Y, precision: 8);
        }

        [Fact]
        public void RotateAtAppend_RotatesAboutPoint()
        {
            var m = XMatrix.Identity;
            m.RotateAtAppend(90, 1, 1);

            var p = m.Transform(new XPoint(1, 1));

            // Rotating about the same point leaves it fixed.
            Assert.Equal(1, p.X, precision: 8);
            Assert.Equal(1, p.Y, precision: 8);
        }

        [Fact]
        public void RotateAtAppend_WithXPointOverload()
        {
            var m = XMatrix.Identity;
            m.RotateAtAppend(90, new XPoint(1, 1));

            var p = m.Transform(new XPoint(1, 1));

            Assert.Equal(1, p.X, precision: 8);
            Assert.Equal(1, p.Y, precision: 8);
        }

        [Fact]
        public void RotateAtPrepend_WithXPointOverload()
        {
            var m = XMatrix.Identity;
            m.RotateAtPrepend(90, new XPoint(0, 0));

            var p = m.Transform(new XPoint(1, 0));

            Assert.Equal(0, p.X, precision: 8);
            Assert.Equal(1, p.Y, precision: 8);
        }

        [Fact]
        public void RotateAt_WithOrderParameter()
        {
            var m = XMatrix.Identity;
            m.RotateAt(90, new XPoint(0, 0), XMatrixOrder.Append);

            var p = m.Transform(new XPoint(1, 0));

            Assert.Equal(0, p.X, precision: 8);
            Assert.Equal(1, p.Y, precision: 8);
        }

        [Fact]
        public void ShearAppend_SkewsXByY()
        {
            var m = XMatrix.Identity;
            m.ShearAppend(0, 0);

            Assert.Equal(new XPoint(1, 1), m.Transform(new XPoint(1, 1)));
        }

        [Fact]
        public void Shear_WithOrderParameter()
        {
            var m = XMatrix.Identity;
            m.Shear(0, 0, XMatrixOrder.Append);

            Assert.Equal(new XPoint(1, 1), m.Transform(new XPoint(1, 1)));
        }

        [Fact]
        public void ShearPrepend_SkewsBeforeOtherTransforms()
        {
            var m = XMatrix.Identity;
            m.ShearPrepend(0, 0);

            Assert.Equal(new XPoint(1, 1), m.Transform(new XPoint(1, 1)));
        }

        [Fact]
        public void SkewAppend_And_SkewPrepend_WithZeroAngles_AreNoOps()
        {
            var appendMatrix = XMatrix.Identity;
            appendMatrix.SkewAppend(0, 0);

            var prependMatrix = XMatrix.Identity;
            prependMatrix.SkewPrepend(0, 0);

            Assert.Equal(new XPoint(3, 4), appendMatrix.Transform(new XPoint(3, 4)));
            Assert.Equal(new XPoint(3, 4), prependMatrix.Transform(new XPoint(3, 4)));
        }

        [Fact]
        public void Transform_ArrayOfPoints_TransformsEachInPlace()
        {
            var m = XMatrix.Identity;
            m.TranslateAppend(1, 1);
            var points = new[] { new XPoint(0, 0), new XPoint(1, 1) };

            m.Transform(points);

            Assert.Equal(new XPoint(1, 1), points[0]);
            Assert.Equal(new XPoint(2, 2), points[1]);
        }

        [Fact]
        public void Transform_NullPointArray_DoesNotThrow()
        {
            var m = XMatrix.Identity;
            XPoint[]? points = null;

            m.Transform(points!);
        }

        [Fact]
        public void TransformPoints_OnIdentity_LeavesPointsUnchanged()
        {
            var points = new[] { new XPoint(1, 2), new XPoint(3, 4) };

            XMatrix.Identity.TransformPoints(points);

            Assert.Equal(new XPoint(1, 2), points[0]);
            Assert.Equal(new XPoint(3, 4), points[1]);
        }

        [Fact]
        public void TransformPoints_NullArray_Throws()
        {
            var m = XMatrix.Identity;
            m.TranslateAppend(1, 1);

            Assert.Throws<ArgumentNullException>(() => m.TransformPoints(null!));
        }

        [Fact]
        public void TransformPoints_AppliesTranslation()
        {
            var m = XMatrix.Identity;
            m.TranslateAppend(1, 1);
            var points = new[] { new XPoint(0, 0) };

            m.TransformPoints(points);

            Assert.Equal(new XPoint(1, 1), points[0]);
        }

        [Fact]
        public void Transform_Vector_IgnoresTranslation()
        {
            var m = XMatrix.Identity;
            m.TranslateAppend(5, 5);

            var v = m.Transform(new XVector(1, 1));

            Assert.Equal(new XVector(1, 1), v);
        }

        [Fact]
        public void Transform_VectorArray_TransformsEachInPlace()
        {
            var m = XMatrix.Identity;
            m.ScaleAppend(2, 2);
            var vectors = new[] { new XVector(1, 1), new XVector(2, 2) };

            m.Transform(vectors);

            Assert.Equal(new XVector(2, 2), vectors[0]);
            Assert.Equal(new XVector(4, 4), vectors[1]);
        }

        [Fact]
        public void Transform_NullVectorArray_DoesNotThrow()
        {
            var m = XMatrix.Identity;
            XVector[]? vectors = null;

            m.Transform(vectors!);
        }

        [Fact]
        public void Determinant_OfIdentityIsOne()
        {
            Assert.Equal(1, XMatrix.Identity.Determinant);
        }

        [Fact]
        public void Determinant_OfScalingMatrix()
        {
            var m = XMatrix.Identity;
            m.ScaleAppend(2, 3);

            Assert.Equal(6, m.Determinant, precision: 8);
        }

        [Fact]
        public void HasInverse_TrueForNonSingularMatrix()
        {
            var m = XMatrix.Identity;
            m.ScaleAppend(2, 2);

            Assert.True(m.HasInverse);
        }

        [Fact]
        public void HasInverse_FalseForSingularMatrix()
        {
            var m = new XMatrix(0, 0, 0, 0, 0, 0);

            Assert.False(m.HasInverse);
        }

        [Fact]
        public void Invert_UndoesTranslation()
        {
            var m = XMatrix.Identity;
            m.TranslateAppend(5, 5);
            m.Invert();

            var p = m.Transform(new XPoint(5, 5));

            Assert.Equal(0, p.X, precision: 8);
            Assert.Equal(0, p.Y, precision: 8);
        }

        [Fact]
        public void Invert_UndoesScaling()
        {
            var m = XMatrix.Identity;
            m.ScaleAppend(2, 4);
            m.Invert();

            var p = m.Transform(new XPoint(2, 4));

            Assert.Equal(1, p.X, precision: 8);
            Assert.Equal(1, p.Y, precision: 8);
        }

        [Fact]
        public void Invert_UndoesScaleAndTranslate()
        {
            var m = XMatrix.Identity;
            m.ScaleAppend(2, 2);
            m.TranslateAppend(4, 4);
            m.Invert();

            var p = m.Transform(new XPoint(6, 6));

            Assert.Equal(1, p.X, precision: 8);
            Assert.Equal(1, p.Y, precision: 8);
        }

        [Fact]
        public void Invert_UndoesGeneralMatrix()
        {
            var m = new XMatrix(1, 2, 3, 4, 5, 6);
            var original = m;
            m.Invert();

            var combined = original * m;

            Assert.Equal(1, combined.M11, precision: 8);
            Assert.Equal(0, combined.M12, precision: 8);
            Assert.Equal(0, combined.M21, precision: 8);
            Assert.Equal(1, combined.M22, precision: 8);
            Assert.Equal(0, combined.OffsetX, precision: 8);
            Assert.Equal(0, combined.OffsetY, precision: 8);
        }

        [Fact]
        public void Invert_Singular_Throws()
        {
            var m = new XMatrix(0, 0, 0, 0, 0, 0);

            Assert.Throws<InvalidOperationException>(() => m.Invert());
        }

        [Fact]
        public void M11M22Properties_ReadAndWrite()
        {
            var m = XMatrix.Identity;

            Assert.Equal(1, m.M11);
            Assert.Equal(0, m.M12);
            Assert.Equal(0, m.M21);
            Assert.Equal(1, m.M22);
            Assert.Equal(0, m.OffsetX);
            Assert.Equal(0, m.OffsetY);

            m.M11 = 2;
            m.M12 = 3;
            m.M21 = 4;
            m.M22 = 5;
            m.OffsetX = 6;
            m.OffsetY = 7;

            Assert.Equal(2, m.M11);
            Assert.Equal(3, m.M12);
            Assert.Equal(4, m.M21);
            Assert.Equal(5, m.M22);
            Assert.Equal(6, m.OffsetX);
            Assert.Equal(7, m.OffsetY);
        }

        [Fact]
        public void EqualityOperators_CompareByValue()
        {
            var a = new XMatrix(1, 0, 0, 1, 2, 3);
            var b = new XMatrix(1, 0, 0, 1, 2, 3);
            var c = new XMatrix(1, 0, 0, 1, 2, 4);

            Assert.True(a == b);
            Assert.False(a == c);
            Assert.True(a != c);
            Assert.True(XMatrix.Equals(a, b));
            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals((object)"not a matrix"));
        }

        [Fact]
        public void Equality_TreatsIdentityMatricesAsEqualRegardlessOfConstruction()
        {
            var explicitIdentity = new XMatrix(1, 0, 0, 1, 0, 0);

            Assert.True(XMatrix.Identity == explicitIdentity);
            Assert.True(explicitIdentity.IsIdentity);
        }

        [Fact]
        public void GetHashCode_IdentityIsZero()
        {
            Assert.Equal(0, XMatrix.Identity.GetHashCode());
        }

        [Fact]
        public void GetHashCode_SameForEqualMatrices()
        {
            var a = new XMatrix(1, 2, 3, 4, 5, 6);
            var b = new XMatrix(1, 2, 3, 4, 5, 6);

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void ToString_Identity_ReturnsIdentityLiteral()
        {
            Assert.Equal("Identity", XMatrix.Identity.ToString());
        }

        [Fact]
        public void ToString_RoundTripsThroughParse()
        {
            var m = new XMatrix(1, 2, 3, 4, 5, 6);

            var text = m.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var parsed = XMatrix.Parse(text);

            Assert.Equal(m, parsed);
        }

        [Fact]
        public void Parse_IdentityLiteral_ReturnsIdentity()
        {
            var parsed = XMatrix.Parse("Identity");

            Assert.True(parsed.IsIdentity);
        }
    }
}
