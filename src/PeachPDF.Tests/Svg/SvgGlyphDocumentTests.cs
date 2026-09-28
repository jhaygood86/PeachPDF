using PeachDrawing.Text;
using PeachDrawing.Text.Outlines;
using PeachDrawing.Abstractions;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Turning the SVG document of a font's glyph into a drawing: the element for the glyph, palette variables, the text colour for
    /// context paint, and refusing what cannot be drawn.
    /// </summary>
    public class SvgGlyphDocumentTests
    {
        private static readonly PaintColor Text = PaintColor.FromArgb(255, 1, 2, 3);

        private static SvgGlyph Glyph(string document, string id = "glyph2", int first = 2, int last = 2) => new(document, id, first, last, 1000);

        private static SvgDocument? Build(SvgGlyph glyph, params PaintColor?[] palette) =>
            SvgGlyphDocument.Build(glyph, i => i < palette.Length ? palette[i] : null, palette.Length, Text, new TestGraphicsAdapter());

        private static SvgElement Only(SvgDocument document) => Assert.Single(Flatten(document.Children));

        private static IEnumerable<SvgElement> Flatten(IEnumerable<SvgElement> elements)
        {
            foreach (var element in elements)
            {
                if (element is SvgGroupElement group)
                {
                    foreach (var child in Flatten(group.Children))
                        yield return child;
                }
                else
                {
                    yield return element;
                }
            }
        }

        private const string Ns = "xmlns=\"http://www.w3.org/2000/svg\"";

        [Fact]
        public void APaletteVariable_InAStyleProperty_TakesThePaletteColour()
        {
            var doc = Build(Glyph($"<svg {Ns}><g id=\"glyph2\"><rect width=\"10\" height=\"10\" style=\"fill: var(--color1, red)\"/></g></svg>"),
                PaintColor.FromArgb(255, 9, 9, 9), PaintColor.FromArgb(255, 10, 20, 30));

            Assert.NotNull(doc);
            Assert.Equal(PaintColor.FromArgb(255, 10, 20, 30), Only(doc!).Fill.PaintColor);
        }

        [Fact]
        public void APaletteVariable_InAPresentationAttribute_TakesThePaletteColour()
        {
            var doc = Build(Glyph($"<svg {Ns}><g id=\"glyph2\"><rect width=\"10\" height=\"10\" fill=\"var(--color0, red)\"/></g></svg>"),
                PaintColor.FromArgb(255, 9, 8, 7));

            Assert.Equal(PaintColor.FromArgb(255, 9, 8, 7), Only(doc!).Fill.PaintColor);
        }

        [Fact]
        public void APaletteVariable_WithNoEntry_UsesTheDocumentsFallback()
        {
            var doc = Build(Glyph($"<svg {Ns}><g id=\"glyph2\"><rect width=\"10\" height=\"10\" fill=\"var(--color5, #00ff00)\"/></g></svg>"),
                PaintColor.FromArgb(255, 9, 8, 7));

            Assert.Equal(PaintColor.FromArgb(255, 0, 255, 0), Only(doc!).Fill.PaintColor);
        }

        [Theory]
        [InlineData("fill=\"context-fill\"")]
        [InlineData("style=\"fill: context-fill\"")]
        [InlineData("fill=\"currentColor\"")]
        public void ContextPaint_AndCurrentColor_AreTheTextColour(string paint)
        {
            var doc = Build(Glyph($"<svg {Ns}><g id=\"glyph2\"><rect width=\"10\" height=\"10\" {paint}/></g></svg>"));

            Assert.Equal(Text, Only(doc!).Fill.PaintColor);
        }

        [Fact]
        public void ContextStroke_IsNoPaint_BecauseTheTextHasNoStroke()
        {
            // With plain inheritance the group's red stroke would win.
            var doc = Build(Glyph($"<svg {Ns}><g id=\"glyph2\" stroke=\"#ff0000\"><rect width=\"10\" height=\"10\" fill=\"none\" stroke=\"context-stroke\" stroke-width=\"3\"/></g></svg>"));

            Assert.Equal(SvgPaintKind.None, Only(doc!).Stroke.Kind);
        }

        [Fact]
        public void AUseInsideTheGlyph_IsTheContextOfItsShape_AndTheTextIsTheContextOfTheRest()
        {
            var doc = Build(Glyph($"<svg {Ns} xmlns:xlink=\"http://www.w3.org/1999/xlink\"><defs><rect id=\"s\" width=\"10\" height=\"10\" fill=\"context-fill\" stroke=\"context-stroke\"/></defs>" +
                "<g id=\"glyph2\"><use xlink:href=\"#s\" fill=\"#ff0000\" stroke=\"#0000ff\"/><circle r=\"5\" fill=\"context-fill\"/></g></svg>"));

            var elements = Flatten(doc!.Children).ToList();
            var rect = Assert.IsType<SvgRectElement>(Assert.IsType<SvgUseElement>(elements[0]).Target);
            Assert.Equal(PaintColor.FromArgb(255, 255, 0, 0), rect.Fill.PaintColor);
            Assert.Equal(PaintColor.FromArgb(255, 0, 0, 255), rect.Stroke.PaintColor);
            Assert.Equal(Text, elements[1].Fill.PaintColor);
        }

        [Fact]
        public void OnlyTheElementOfTheGlyphAsked_IsKept()
        {
            var shared = $"<svg {Ns}><g id=\"glyph3\"><rect width=\"1\" height=\"1\" fill=\"#111111\"/></g><g id=\"glyph4\"><rect width=\"2\" height=\"2\" fill=\"#222222\"/></g></svg>";

            var three = Build(Glyph(shared, "glyph3", 3, 4));
            var four = Build(Glyph(shared, "glyph4", 3, 4));

            Assert.Equal(PaintColor.FromArgb(255, 0x11, 0x11, 0x11), Only(three!).Fill.PaintColor);
            Assert.Equal(PaintColor.FromArgb(255, 0x22, 0x22, 0x22), Only(four!).Fill.PaintColor);
        }

        [Fact]
        public void DefinitionsSharedByTheDocument_SurviveThePruning()
        {
            var doc = Build(Glyph($"<svg {Ns}><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"red\"/><stop offset=\"1\" stop-color=\"blue\"/></linearGradient></defs>" +
                "<g id=\"glyph2\"><rect width=\"10\" height=\"10\" fill=\"url(#g)\"/></g><g id=\"glyph3\"><rect width=\"5\" height=\"5\"/></g></svg>"));

            Assert.Single(doc!.Gradients);
            Assert.Single(Flatten(doc.Children));
        }

        [Fact]
        public void ASingleGlyphDocumentWithNoElementForIt_IsDrawnWhole()
        {
            var doc = Build(Glyph($"<svg {Ns}><rect width=\"10\" height=\"10\" fill=\"#008080\"/></svg>", "glyph5", 5, 5));

            Assert.Equal(PaintColor.FromArgb(255, 0, 128, 128), Only(doc!).Fill.PaintColor);
        }

        [Fact]
        public void ASharedDocumentWithNoElementForTheGlyph_IsRefused()
        {
            Assert.Null(Build(Glyph($"<svg {Ns}><rect width=\"10\" height=\"10\"/></svg>", "glyph5", 5, 6)));
        }

        [Fact]
        public void AGlyphWithinTheLeastCanvas_KeepsIt()
        {
            var doc = Build(Glyph($"<svg {Ns} viewBox=\"0 0 5 5\"><g id=\"glyph2\"><rect x=\"100\" y=\"-800\" width=\"800\" height=\"800\"/></g></svg>"));

            Assert.Equal(new Rect(-1000, -1500, 3000, 2000), doc!.ViewBox);
            Assert.Equal(3000, doc.Width);
            Assert.Equal(2000, doc.Height);
        }

        [Fact]
        public void AGlyphDrawingBeyondTheLeastCanvas_GetsACanvasThatHoldsIt()
        {
            var doc = Build(Glyph($"<svg {Ns}><g id=\"glyph2\"><rect x=\"-1600\" y=\"-1800\" width=\"400\" height=\"300\"/><rect x=\"2200\" y=\"100\" width=\"600\" height=\"900\"/></g></svg>"));

            var canvas = doc!.ViewBox!.Value;
            // the least canvas grown to the artwork and a hair (10 units) of margin, on the sides it overflows only
            Assert.Equal(-1610, canvas.X);
            Assert.Equal(-1810, canvas.Y);
            Assert.Equal(2810, canvas.X + canvas.Width);
            Assert.Equal(1010, canvas.Y + canvas.Height);
            Assert.Equal(canvas.Width, doc.Width);
            Assert.Equal(canvas.Height, doc.Height);
        }

        [Fact]
        public void AStrokeAndATransform_CountTowardsTheCanvas()
        {
            var doc = Build(Glyph($"<svg {Ns}><g id=\"glyph2\" transform=\"translate(2000 0)\"><path d=\"M0 -100 L500 -100\" stroke=\"#000\" stroke-width=\"200\" stroke-linejoin=\"round\" fill=\"none\"/></g></svg>"));

            // the line, moved by 2000, ends at x = 2500 and its stroke reaches 100 further (a round join: no miter allowance): only the right side overflows
            var canvas = doc!.ViewBox!.Value;
            Assert.Equal(-1000, canvas.X);
            Assert.Equal(2610, canvas.X + canvas.Width);
        }

        [Fact]
        public void ADocumentThatDrawsFarBeyondAnyGlyph_IsCappedAtTheReach()
        {
            var doc = Build(Glyph($"<svg {Ns}><g id=\"glyph2\"><rect x=\"-90000\" y=\"-90000\" width=\"180000\" height=\"180000\"/></g></svg>"));

            var canvas = doc!.ViewBox!.Value;
            var reach = SvgGlyphDocument.MaxCanvasReachEms * 1000;
            Assert.Equal(-reach, canvas.X);
            Assert.Equal(-reach, canvas.Y);
            Assert.Equal(reach, canvas.X + canvas.Width);
            Assert.Equal(reach, canvas.Y + canvas.Height);
        }

        [Fact]
        public void ANonFiniteExtent_IsIgnored()
        {
            Assert.Equal(new Rect(-1000, -1500, 3000, 2000), SvgGlyphDocument.CanvasFor(new Rect(double.NegativeInfinity, 0, double.PositiveInfinity, 1), 1000));
            Assert.Equal(new Rect(-1000, -1500, 3000, 2000), SvgGlyphDocument.CanvasFor(new Rect(double.NaN, 0, 5, 5), 1000));
        }

        [Fact]
        public void ADocumentWithNothingOfKnownExtent_KeepsTheLeastCanvas()
        {
            var doc = Build(Glyph($"<svg {Ns}><g id=\"glyph2\"><rect width=\"0\" height=\"0\"/></g></svg>"));

            Assert.Equal(new Rect(-1000, -1500, 3000, 2000), doc!.ViewBox);
        }

        [Fact]
        public void TheCanvas_ReplacesTheDocumentsOwnViewBox()
        {
            var doc = Build(Glyph($"<svg {Ns} viewBox=\"0 0 5 5\" width=\"5\" height=\"5\"><g id=\"glyph2\"><rect width=\"10\" height=\"10\"/></g></svg>"));

            Assert.Equal(new Rect(-1000, -1500, 3000, 2000), doc!.ViewBox);
        }

        // ---- hostile documents ------------------------------------------------------------------------------------------------------

        [Fact]
        public void UseElementsThatExpandExponentially_AreRefused()
        {
            var text = new System.Text.StringBuilder($"<svg {Ns}><defs>");
            for (int level = 0; level < 8; level++)
            {
                text.Append($"<g id=\"a{level}\">");
                for (int i = 0; i < 20; i++)
                    text.Append($"<use href=\"#a{level + 1}\"/>");
                text.Append("</g>");
            }

            text.Append("<g id=\"a8\"><rect width=\"1\" height=\"1\"/></g></defs><g id=\"glyph2\"><use href=\"#a0\"/></g></svg>");

            Assert.Null(Build(Glyph(text.ToString())));
        }

        [Fact]
        public void AUseThatReachesItself_IsRefused()
        {
            Assert.Null(Build(Glyph($"<svg {Ns}><g id=\"glyph2\"><use id=\"u\" href=\"#glyph2\"/></g></svg>")));
        }

        [Fact]
        public void ElementsNestedTooDeeply_AreRefused()
        {
            var depth = SvgGlyphDocument.MaxDepth + 5;
            var open = string.Concat(System.Linq.Enumerable.Repeat("<g>", depth));
            var close = string.Concat(System.Linq.Enumerable.Repeat("</g>", depth));

            Assert.Null(Build(Glyph($"<svg {Ns}><g id=\"glyph2\">{open}<rect width=\"1\" height=\"1\"/>{close}</g></svg>")));
        }

        [Fact]
        public void ARealisticDocument_IsWithinTheBudget()
        {
            var open = string.Concat(System.Linq.Enumerable.Repeat("<g>", 20));
            var close = string.Concat(System.Linq.Enumerable.Repeat("</g>", 20));

            Assert.NotNull(Build(Glyph($"<svg {Ns}><g id=\"glyph2\">{open}<rect width=\"1\" height=\"1\"/>{close}</g></svg>")));
        }

        [Fact]
        public void APaletteWithManyEntries_IsOnlyDefinedWhereTheDocumentNamesThem()
        {
            var palette = new PaintColor?[5000];
            for (int i = 0; i < palette.Length; i++)
                palette[i] = PaintColor.FromArgb(255, i % 256, 0, 0);

            var doc = Build(Glyph($"<svg {Ns}><g id=\"glyph2\"><rect width=\"1\" height=\"1\" fill=\"var(--color4000, blue)\"/></g></svg>"), palette);

            Assert.Equal(PaintColor.FromArgb(255, 4000 % 256, 0, 0), Only(doc!).Fill.PaintColor);
        }

        [Fact]
        public void ANestedSvgImageWithAnEntityDeclaration_IsNotExpanded()
        {
            var nested = Uri.EscapeDataString("<!DOCTYPE svg [<!ENTITY x \"boom\">]><svg xmlns=\"http://www.w3.org/2000/svg\"><text>&x;</text></svg>");
            var doc = Build(Glyph($"<svg {Ns} xmlns:xlink=\"http://www.w3.org/1999/xlink\"><g id=\"glyph2\"><image width=\"10\" height=\"10\" href=\"data:image/svg+xml,{nested}\"/></g></svg>"));

            // The document builds (the image is simply not drawn) and nothing was expanded or thrown.
            Assert.NotNull(doc);
        }

        [Fact]
        public void WhatTheGlyphReferences_SurvivesEvenWhenItIsNamedLikeAGlyph()
        {
            var doc = Build(Glyph($"<svg {Ns}><defs><g id=\"glyph9\"><rect width=\"3\" height=\"3\" fill=\"#333333\"/></g></defs>" +
                "<g id=\"glyph2\"><use href=\"#glyph9\"/></g><g id=\"glyph3\"><rect width=\"1\" height=\"1\"/></g></svg>"));

            var use = Assert.IsType<SvgUseElement>(Only(doc!));
            Assert.NotNull(use.Target);
        }

        [Fact]
        public void ContextPaintKeywords_AreNotConfusedWithIdsOrClasses()
        {
            var doc = Build(Glyph($"<svg {Ns}><g id=\"glyph2\"><rect id=\"context-fill\" class=\"context-stroke\" width=\"1\" height=\"1\" fill=\"#010101\"/></g></svg>"));

            Assert.Equal("context-fill", Only(doc!).Id);
        }

        [Theory]
        [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><g id=\"glyph2\">")]                                        // not well formed
        [InlineData("<!DOCTYPE svg [<!ENTITY x \"boom\">]><svg xmlns=\"http://www.w3.org/2000/svg\"><g id=\"glyph2\"/></svg>")]     // a DTD
        [InlineData("")]
        public void ADocumentThatCannotBeParsed_GivesNothing(string document)
        {
            Assert.Null(Build(Glyph(document)));
        }
    }
}
