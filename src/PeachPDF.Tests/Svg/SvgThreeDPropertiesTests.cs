using PeachPDF.PdfBackend;
using PeachPDF.Svg;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// The 3D properties of an SVG element (CSS Transforms 2 sections 8 to 11): <c>perspective</c>, <c>perspective-origin</c>, <c>transform-style</c> and
    /// <c>backface-visibility</c>, and the 4x4 matrix a transform list holding a 3D function stands for, as the tree builder reads them from an attribute,
    /// a <c>style</c> attribute and a stylesheet rule.
    /// </summary>
    public class SvgThreeDPropertiesTests
    {
        private static readonly PdfRenderContext RenderContext = new();

        private static SvgDocument Build(string rule, string content, string rootAttributes = "width=\"200\" height=\"100\"")
        {
            var markup = $"<svg xmlns=\"http://www.w3.org/2000/svg\" {rootAttributes}><style>{rule}</style>{content}</svg>";
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root), new PeachPDF.PdfBackend.PdfRenderContext());
            return SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, cssData, "print"), RenderContext);
        }

        private static SvgElement Group(string css, string attributes = "") =>
            Build($"#g {{ {css} }}", $"<g id=\"g\" {attributes}><rect width=\"10\" height=\"10\"/></g>").Children.Single();

        // ---- perspective ----

        [Theory]
        [InlineData("perspective: 300px", 300)]
        [InlineData("perspective: 0.25px", 1)]      // under one pixel is one pixel
        [InlineData("perspective: 1in", 96)]
        public void Perspective_IsTheDistanceInUserUnits(string css, double expected)
        {
            Assert.Equal(expected, Group(css).Perspective);
        }

        [Theory]
        [InlineData("perspective: none")]
        [InlineData("perspective: 0")]              // zero is none, as in a browser
        [InlineData("perspective: -5px")]
        [InlineData("perspective: 50%")]
        [InlineData("perspective: 300")]            // a CSS length needs its unit (the presentation attribute, below, does not)
        [InlineData("perspective: abc")]
        [InlineData("")]
        public void Perspective_NoneZeroAndInvalidValues_GiveNoPerspective(string css)
        {
            Assert.Null(Group(css).Perspective);
        }

        [Fact]
        public void Perspective_IsNotInherited()
        {
            var document = Build("#g { perspective: 300px }", "<g id=\"g\"><g id=\"inner\"/></g>");

            var inner = ((SvgGroupElement)document.Children.Single()).Children.Single();
            Assert.Null(inner.Perspective);
        }

        [Fact]
        public void Perspective_ComesFromAPresentationAttributeToo_AndARuleBeatsIt()
        {
            Assert.Equal(200, Group("", "perspective=\"200\"").Perspective);
            Assert.Equal(300, Group("perspective: 300px", "perspective=\"200\"").Perspective);
        }

        // ---- perspective-origin ----

        [Fact]
        public void PerspectiveOrigin_DefaultsToTheMiddleOfTheReferenceBox()
        {
            var document = Build("#g { perspective: 300px }", "<g id=\"g\"><rect width=\"10\" height=\"10\"/></g>", "width=\"200\" height=\"100\" viewBox=\"0 0 200 100\"");
            var group = document.Children.Single();

            Assert.NotNull(group.PerspectiveOrigin);
            var (x, y, z) = group.PerspectiveOrigin!.Resolve(group);
            Assert.Equal(100, x);
            Assert.Equal(50, y);
            Assert.Equal(0, z);
        }

        [Fact]
        public void PerspectiveOrigin_TakesLengthsAndPercentagesOfTheViewBox()
        {
            var document = Build("#g { perspective: 300px; perspective-origin: 20px 25% }", "<g id=\"g\"><rect width=\"10\" height=\"10\"/></g>", "width=\"200\" height=\"100\" viewBox=\"0 0 200 100\"");
            var group = document.Children.Single();

            var (x, y, _) = group.PerspectiveOrigin!.Resolve(group);
            Assert.Equal(20, x);
            Assert.Equal(25, y);
        }

        [Fact]
        public void PerspectiveOrigin_FillBox_IsMeasuredInTheElementsBox()
        {
            var document = Build("#g { perspective: 300px; perspective-origin: left bottom; transform-box: fill-box }", "<g id=\"g\"><rect x=\"30\" y=\"20\" width=\"40\" height=\"10\"/></g>");
            var group = document.Children.Single();

            var (x, y, _) = group.PerspectiveOrigin!.Resolve(group);
            Assert.Equal(30, x);
            Assert.Equal(30, y);
        }

        [Fact]
        public void PerspectiveOrigin_WithAThirdValue_IsInvalid_AndTheInitialOneStays()
        {
            var document = Build("#g { perspective: 300px; perspective-origin: 10px 20px 30px }", "<g id=\"g\"><rect width=\"10\" height=\"10\"/></g>", "width=\"200\" height=\"100\" viewBox=\"0 0 200 100\"");
            var group = document.Children.Single();

            var (x, y, _) = group.PerspectiveOrigin!.Resolve(group);
            Assert.Equal(100, x);
            Assert.Equal(50, y);
        }

        // ---- transform-style and backface-visibility ----

        [Theory]
        [InlineData("transform-style: preserve-3d", true)]
        [InlineData("transform-style: PRESERVE-3D", true)]
        [InlineData("transform-style: flat", false)]
        [InlineData("transform-style: nonsense", false)]
        [InlineData("", false)]
        public void TransformStyle_PreserveThreeD(string css, bool expected)
        {
            Assert.Equal(expected, Group(css).PreserveThreeD);
        }

        [Theory]
        [InlineData("backface-visibility: hidden", true)]
        [InlineData("backface-visibility: visible", false)]
        [InlineData("", false)]
        public void BackfaceVisibility_Hidden(string css, bool expected)
        {
            Assert.Equal(expected, Group(css).BackfaceHidden);
        }

        // ---- The matrix of a 3D transform ----

        [Fact]
        public void Transform3D_IsTheFourByFourMatrix_WithTheOriginAppliedAtItsZOffset()
        {
            var document = Build("#r { transform: rotateY(90deg); transform-origin: 20px 10px 30px }", "<rect id=\"r\" x=\"0\" y=\"0\" width=\"40\" height=\"20\"/>");
            var rect = document.Children.Single();

            Assert.NotNull(rect.Transform3D);
            var m = rect.Transform3D!.Value;

            // About (20, 10, 30): the point at the origin turns to z = 30 - 20 ... Take the matrix to the point (20, 10, 30) itself: it stays.
            var fixedPoint = Vector3.Transform(new Vector3(20, 10, 30), m);
            Assert.Equal(20, fixedPoint.X, 3);
            Assert.Equal(10, fixedPoint.Y, 3);
            Assert.Equal(30, fixedPoint.Z, 3);
        }

        [Fact]
        public void Transform_OfA3DElement_IsTheProjectionOfItsFullMatrix()
        {
            var rect = Build("#r { transform: translate3d(5px, 6px, 7px) }", "<rect id=\"r\" width=\"10\" height=\"10\"/>").Children.Single();

            Assert.Equal(5, rect.Transform!.Value.M31);
            Assert.Equal(6, rect.Transform!.Value.M32);
            Assert.Equal(7, rect.Transform3D!.Value.M43);
        }

        [Fact]
        public void Transform3D_IsNullForA2DList()
        {
            Assert.Null(Build("#r { transform: translate(5px, 6px) }", "<rect id=\"r\" width=\"10\" height=\"10\"/>").Children.Single().Transform3D);
        }

        [Theory]
        [InlineData("#r { transform: rotateY(10) }")]         // a unitless angle is not valid CSS: the declaration is dropped
        [InlineData("#r { transform: translate3d(1px, 2px) }")] // wrong argument count
        [InlineData("#r { transform: perspective(-3px) }")]
        public void AnInvalid3DDeclaration_IsDropped(string rule)
        {
            Assert.Null(Build(rule, "<rect id=\"r\" width=\"10\" height=\"10\"/>").Children.Single().Transform3D);
        }

        [Fact]
        public void A3DFunction_InADisplayContentsElement_DoesNothing()
        {
            var document = Build("#g { display: contents; transform: rotateY(40deg); perspective: 300px; transform-style: preserve-3d; backface-visibility: hidden }", "<g id=\"g\"><rect width=\"10\" height=\"10\"/></g>");
            var group = document.Children.Single();

            Assert.Null(group.Transform3D);
            Assert.Null(group.Perspective);
            Assert.False(group.PreserveThreeD);
            Assert.False(group.BackfaceHidden);
        }

        // ---- The root <svg> ----

        [Fact]
        public void TheRootsPerspective_IsHeldByAGroupStandingForIt()
        {
            var document = Build("", "<rect width=\"10\" height=\"10\"/>", "width=\"200\" height=\"100\" style=\"perspective: 250px; transform-style: preserve-3d\"");

            Assert.NotNull(document.RootProjection);
            Assert.Equal(250, document.RootProjection!.Perspective);
            Assert.True(document.RootProjection.PreserveThreeD);
            Assert.Same(document.Children.Single(), document.RootProjection.Children.Single());
        }

        [Fact]
        public void ARootWithNo3DProperties_NeedsNoStandInGroup()
        {
            Assert.Null(Build("", "<rect width=\"10\" height=\"10\"/>").RootProjection);
        }
    }
}
