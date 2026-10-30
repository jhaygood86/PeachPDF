using PeachBrackets.Css;
using PeachBrackets.Svg;
using PeachDrawing.Core;
using PeachPDF.Rendering;
using System.Linq;
using System.Xml.Linq;

namespace PeachBrackets.Engine.Tests.Svg
{
    /// <summary>
    /// <c>initial</c> on an SVG element is the property's initial value, not the parent's: the CSS-wide keyword reads the same whether it is written as a
    /// presentation attribute, in a <c>style</c> attribute or in a stylesheet rule. Every case puts a non-initial value on the parent so that "inherited"
    /// and "initial" differ, which at the top of the tree they would not.
    /// </summary>
    public class SvgInitialKeywordTests
    {
        private static readonly HtmlRenderContext RenderContext = new();

        private static readonly string[] TextProperties = ["direction", "writing-mode", "text-orientation", "letter-spacing", "word-spacing"];

        // direction, writing-mode and text-orientation are stored only on the text element, which has the pen model that consumes them.
        private static string ChildMarkup(string property, string childAttributes) =>
            Array.IndexOf(TextProperties, property) >= 0
                ? $"<text class=\"r\" {childAttributes} x=\"0\" y=\"10\">A</text>"
                : $"<rect class=\"r\" {childAttributes} width=\"10\" height=\"10\"/>";

        private static SvgDocument Build(string element, string rule)
        {
            var markup = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\"><style>{rule}</style>{element}</svg>";
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root), new PeachPDF.Rendering.HtmlRenderContext());
            return SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, cssData, "print"), RenderContext);
        }

        private static SvgElement BuildChild(string property, string parentAttribute, string childAttributes, string rule = "")
        {
            var markup = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\"><style>{rule}</style><g {parentAttribute}>{ChildMarkup(property, childAttributes)}</g></svg>";
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root), new PeachPDF.Rendering.HtmlRenderContext());
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, cssData, "print"), RenderContext);
            return Assert.IsType<SvgGroupElement>(document.Children.Single()).Children.Single();
        }

        private static SvgElement InForm(string form, string property, string parentValue, string childValue) => form switch
        {
            "attribute" => BuildChild(property, $"{property}=\"{parentValue}\"", $"{property}=\"{childValue}\""),
            "style" => BuildChild(property, $"{property}=\"{parentValue}\"", $"style=\"{property}: {childValue}\""),
            "rule" => BuildChild(property, $"{property}=\"{parentValue}\"", "", $".r {{ {property}: {childValue} }}"),
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
        };

        private static readonly (string Property, string ParentValue)[] Properties =
        [
            ("fill", "red"), ("stroke", "blue"), ("stroke-width", "5"), ("stroke-miterlimit", "7"),
            ("fill-rule", "evenodd"), ("fill-opacity", "0.5"), ("stroke-opacity", "0.5"),
            ("stroke-linecap", "round"), ("stroke-linejoin", "round"), ("stroke-dasharray", "5 3"),
            ("stroke-dashoffset", "4"), ("direction", "rtl"), ("writing-mode", "vertical-rl"),
            ("text-orientation", "upright"), ("letter-spacing", "5"), ("word-spacing", "9"), ("visibility", "hidden"),
        ];

        /// <summary>Every property written three ways: as an attribute, in a style attribute, and in a stylesheet rule.</summary>
        public static TheoryData<string, string, string> PropertyForms()
        {
            var data = new TheoryData<string, string, string>();
            foreach (var (property, parentValue) in Properties)
                foreach (var form in new[] { "attribute", "style", "rule" })
                    data.Add(property, parentValue, form);
            return data;
        }

        [Theory]
        [MemberData(nameof(PropertyForms))]
        public void Initial_IsTheInitialValue(string property, string parentValue, string form) =>
            AssertInitial(property, InForm(form, property, parentValue, "initial"));

        [Theory]
        [MemberData(nameof(PropertyForms))]
        public void Inherit_IsTheParentsValue(string property, string parentValue, string form) =>
            AssertInherited(property, InForm(form, property, parentValue, "inherit"));

        [Theory]
        [MemberData(nameof(PropertyForms))]
        public void AnInvalidValue_IsTheParentsValue(string property, string parentValue, string form) =>
            AssertInherited(property, InForm(form, property, parentValue, "bogus"));

        [Theory]
        [MemberData(nameof(PropertyForms))]
        public void Initial_IsRead_WhateverItsCaseAndPadding(string property, string parentValue, string form) =>
            AssertInitial(property, InForm(form, property, parentValue, "  INITIAL "));

        // A stylesheet rule beats the presentation attribute, so initial in a rule must replace what the attribute says with the initial value.
        private static SvgElement Single(string element, string rule)
        {
            var markup = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\"><style>{rule}</style>{element}</svg>";
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root), new PeachPDF.Rendering.HtmlRenderContext());
            return SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, cssData, "print"), RenderContext).Children.Single();
        }

        [Fact]
        public void InAStylesheetRule_TransformInitialIsNone() =>
            Assert.Null(Single("<rect class=\"c\" transform=\"translate(50 0)\" width=\"10\" height=\"10\"/>", ".c { transform: initial }").Transform);

        [Fact]
        public void InAStylesheetRule_DisplayInitialShowsAnElementTheAttributeHides()
        {
            var document = Build("<rect class=\"c\" display=\"none\" width=\"10\" height=\"10\"/>", ".c { display: initial }");

            Assert.Single(document.Children);
        }

        [Fact]
        public void InAStylesheetRule_RectGeometryInitialIsZero()
        {
            var rect = Assert.IsType<SvgRectElement>(Single("<rect class=\"c\" x=\"50\" y=\"50\" width=\"10\" height=\"10\" rx=\"4\"/>",
                ".c { x: initial; y: initial; width: initial; height: initial; rx: initial }"));

            Assert.Equal(0, rect.X);
            Assert.Equal(0, rect.Y);
            Assert.Equal(0, rect.Width);
            Assert.Equal(0, rect.Height);
            Assert.Equal(0, rect.Rx);
        }

        [Fact]
        public void InAStylesheetRule_CircleGeometryInitialIsZero()
        {
            var circle = Assert.IsType<SvgCircleElement>(Single("<circle class=\"c\" cx=\"50\" cy=\"50\" r=\"5\"/>", ".c { cx: initial; cy: initial; r: initial }"));

            Assert.Equal(0, circle.Cx);
            Assert.Equal(0, circle.Cy);
            Assert.Equal(0, circle.R);
        }

        private static SvgTextElement Text(SvgElement element) => Assert.IsType<SvgTextElement>(element);

        private static void AssertInitial(string property, SvgElement element)
        {
            switch (property)
            {
                case "fill": Assert.Equal(SvgPaintKind.Solid, element.Fill.Kind); Assert.Equal(PaintColor.Black, element.Fill.PaintColor); break;
                case "stroke": Assert.Equal(SvgPaintKind.None, element.Stroke.Kind); break;
                case "stroke-width": Assert.Equal(1, element.StrokeWidth); break;
                case "stroke-miterlimit": Assert.Equal(4, element.StrokeMiterLimit); break;
                case "fill-rule": Assert.Equal(FillMode.Nonzero, element.FillRule); break;
                case "fill-opacity": Assert.Equal(1, element.FillOpacity); break;
                case "stroke-opacity": Assert.Equal(1, element.StrokeOpacity); break;
                case "stroke-linecap": Assert.Equal(LineCap.Butt, element.StrokeLineCap); break;
                case "stroke-linejoin": Assert.Equal(LineJoin.Miter, element.StrokeLineJoin); break;
                case "stroke-dasharray": Assert.Empty(element.StrokeDashArray); break;
                case "stroke-dashoffset": Assert.Equal(0, element.StrokeDashOffset); break;
                case "direction": Assert.Equal("ltr", Text(element).Direction); break;
                case "writing-mode": Assert.Equal(WritingMode.HorizontalTb, Text(element).WritingMode); break;
                case "text-orientation": Assert.Equal(TextOrientation.Mixed, Text(element).TextOrientation); break;
                case "letter-spacing": Assert.Equal(0, Text(element).LetterSpacing); break;
                case "word-spacing": Assert.Equal(0, Text(element).WordSpacing); break;
                case "visibility": Assert.True(element.Visible); break;
                default: throw new ArgumentOutOfRangeException(nameof(property), property, null);
            }
        }

        private static void AssertInherited(string property, SvgElement element)
        {
            switch (property)
            {
                case "fill": Assert.Equal(SvgPaintKind.Solid, element.Fill.Kind); Assert.Equal(PaintColor.FromArgb(255, 0, 0), element.Fill.PaintColor); break;
                case "stroke": Assert.Equal(SvgPaintKind.Solid, element.Stroke.Kind); Assert.Equal(PaintColor.FromArgb(0, 0, 255), element.Stroke.PaintColor); break;
                case "stroke-width": Assert.Equal(5, element.StrokeWidth); break;
                case "stroke-miterlimit": Assert.Equal(7, element.StrokeMiterLimit); break;
                case "fill-rule": Assert.Equal(FillMode.EvenOdd, element.FillRule); break;
                case "fill-opacity": Assert.Equal(0.5, element.FillOpacity); break;
                case "stroke-opacity": Assert.Equal(0.5, element.StrokeOpacity); break;
                case "stroke-linecap": Assert.Equal(LineCap.Round, element.StrokeLineCap); break;
                case "stroke-linejoin": Assert.Equal(LineJoin.Round, element.StrokeLineJoin); break;
                case "stroke-dasharray": Assert.Equal([5d, 3d], element.StrokeDashArray); break;
                case "stroke-dashoffset": Assert.Equal(4, element.StrokeDashOffset); break;
                case "direction": Assert.Equal("rtl", Text(element).Direction); break;
                case "writing-mode": Assert.Equal(WritingMode.VerticalRl, Text(element).WritingMode); break;
                case "text-orientation": Assert.Equal(TextOrientation.Upright, Text(element).TextOrientation); break;
                case "letter-spacing": Assert.Equal(5, Text(element).LetterSpacing); break;
                case "word-spacing": Assert.Equal(9, Text(element).WordSpacing); break;
                case "visibility": Assert.False(element.Visible); break;
                default: throw new ArgumentOutOfRangeException(nameof(property), property, null);
            }
        }
    }
}
