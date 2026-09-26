using PeachDrawing.Text;
using PeachDrawing.Text.Outlines;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Turning the SVG document of a font's glyph into a drawing: the element for the glyph, palette variables, the text colour for
    /// context paint, and refusing what cannot be drawn.
    /// </summary>
    public class SvgGlyphDocumentTests
    {
        private static readonly RColor Text = RColor.FromArgb(255, 1, 2, 3);

        private static SvgGlyph Glyph(string document, string id = "glyph2", int first = 2, int last = 2) => new(document, id, first, last, 1000);

        private static SvgDocument? Build(SvgGlyph glyph, params RColor?[] palette) =>
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
                RColor.FromArgb(255, 9, 9, 9), RColor.FromArgb(255, 10, 20, 30));

            Assert.NotNull(doc);
            Assert.Equal(RColor.FromArgb(255, 10, 20, 30), Only(doc!).Fill.Color);
        }

        [Fact]
        public void APaletteVariable_InAPresentationAttribute_TakesThePaletteColour()
        {
            var doc = Build(Glyph($"<svg {Ns}><g id=\"glyph2\"><rect width=\"10\" height=\"10\" fill=\"var(--color0, red)\"/></g></svg>"),
                RColor.FromArgb(255, 9, 8, 7));

            Assert.Equal(RColor.FromArgb(255, 9, 8, 7), Only(doc!).Fill.Color);
        }

        [Fact]
        public void APaletteVariable_WithNoEntry_UsesTheDocumentsFallback()
        {
            var doc = Build(Glyph($"<svg {Ns}><g id=\"glyph2\"><rect width=\"10\" height=\"10\" fill=\"var(--color5, #00ff00)\"/></g></svg>"),
                RColor.FromArgb(255, 9, 8, 7));

            Assert.Equal(RColor.FromArgb(255, 0, 255, 0), Only(doc!).Fill.Color);
        }

        [Theory]
        [InlineData("fill=\"context-fill\"")]
        [InlineData("style=\"fill: context-fill\"")]
        [InlineData("fill=\"currentColor\"")]
        public void ContextPaint_AndCurrentColor_AreTheTextColour(string paint)
        {
            var doc = Build(Glyph($"<svg {Ns}><g id=\"glyph2\"><rect width=\"10\" height=\"10\" {paint}/></g></svg>"));

            Assert.Equal(Text, Only(doc!).Fill.Color);
        }

        [Fact]
        public void OnlyTheElementOfTheGlyphAsked_IsKept()
        {
            var shared = $"<svg {Ns}><g id=\"glyph3\"><rect width=\"1\" height=\"1\" fill=\"#111111\"/></g><g id=\"glyph4\"><rect width=\"2\" height=\"2\" fill=\"#222222\"/></g></svg>";

            var three = Build(Glyph(shared, "glyph3", 3, 4));
            var four = Build(Glyph(shared, "glyph4", 3, 4));

            Assert.Equal(RColor.FromArgb(255, 0x11, 0x11, 0x11), Only(three!).Fill.Color);
            Assert.Equal(RColor.FromArgb(255, 0x22, 0x22, 0x22), Only(four!).Fill.Color);
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

            Assert.Equal(RColor.FromArgb(255, 0, 128, 128), Only(doc!).Fill.Color);
        }

        [Fact]
        public void ASharedDocumentWithNoElementForTheGlyph_IsRefused()
        {
            Assert.Null(Build(Glyph($"<svg {Ns}><rect width=\"10\" height=\"10\"/></svg>", "glyph5", 5, 6)));
        }

        [Fact]
        public void TheCanvas_ReplacesTheDocumentsOwnViewBox()
        {
            var doc = Build(Glyph($"<svg {Ns} viewBox=\"0 0 5 5\" width=\"5\" height=\"5\"><g id=\"glyph2\"><rect width=\"10\" height=\"10\"/></g></svg>"));

            Assert.Equal(new RRect(-1000, -1500, 3000, 2000), doc!.ViewBox);
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
            var palette = new RColor?[5000];
            for (int i = 0; i < palette.Length; i++)
                palette[i] = RColor.FromArgb(255, i % 256, 0, 0);

            var doc = Build(Glyph($"<svg {Ns}><g id=\"glyph2\"><rect width=\"1\" height=\"1\" fill=\"var(--color4000, blue)\"/></g></svg>"), palette);

            Assert.Equal(RColor.FromArgb(255, 4000 % 256, 0, 0), Only(doc!).Fill.Color);
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
        public void ContextPaintRewriting_LeavesIdsAndReferencesAlone()
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
