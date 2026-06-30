using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class PagedMediaIntegrationTests
    {
        // ── @page { size: ... } ────────────────────────────────────────────────

        [Fact]
        public async Task AtPage_SizeA4_SetsPdfPageDimensions()
        {
            const string html = """
                <!DOCTYPE html>
                <html>
                <head><style>@page { size: A4; margin: 20mm; }</style></head>
                <body><p>Hello</p></body>
                </html>
                """;

            var doc = await new PdfGenerator().GeneratePdf(html, PageSize.Letter);
            var page = doc.PdfDocument.Pages[0];

            // A4 = 595.28 × 841.89 pt
            Assert.Equal(595.0, page.Width.Point, 0);
            Assert.Equal(842.0, page.Height.Point, 0);
        }

        [Fact]
        public async Task AtPage_SizeLetter_SetsPdfPageDimensions()
        {
            const string html = """
                <!DOCTYPE html>
                <html>
                <head><style>@page { size: letter; margin: 20mm; }</style></head>
                <body><p>Hello</p></body>
                </html>
                """;

            var doc = await new PdfGenerator().GeneratePdf(html, PageSize.A4);
            var page = doc.PdfDocument.Pages[0];

            // US Letter = 612 × 792 pt
            Assert.Equal(612.0, page.Width.Point, 0);
            Assert.Equal(792.0, page.Height.Point, 0);
        }

        [Fact]
        public async Task AtPage_SizeA4Landscape_SwapsWidthAndHeight()
        {
            const string html = """
                <!DOCTYPE html>
                <html>
                <head><style>@page { size: A4 landscape; margin: 20mm; }</style></head>
                <body><p>Hello</p></body>
                </html>
                """;

            var doc = await new PdfGenerator().GeneratePdf(html, PageSize.A4);
            var page = doc.PdfDocument.Pages[0];

            // Landscape: width > height
            Assert.True(page.Width.Point > page.Height.Point,
                $"Expected landscape but got {page.Width.Point} × {page.Height.Point}");
        }

        [Fact]
        public async Task AtPage_SizeExplicitMm_SetsPdfPageDimensions()
        {
            const string html = """
                <!DOCTYPE html>
                <html>
                <head><style>@page { size: 200mm 100mm; margin: 10mm; }</style></head>
                <body><p>Hello</p></body>
                </html>
                """;

            var doc = await new PdfGenerator().GeneratePdf(html, PageSize.A4);
            var page = doc.PdfDocument.Pages[0];

            // 200mm ≈ 566.93pt, 100mm ≈ 283.46pt
            Assert.Equal(567.0, page.Width.Point, 0);
            Assert.Equal(283.0, page.Height.Point, 0);
        }

        // ── page count ─────────────────────────────────────────────────────────

        [Fact]
        public async Task MultiPage_CorrectNumberOfPagesGenerated()
        {
            var html = """
                <!DOCTYPE html>
                <html>
                <head><style>@page { size: A4; margin: 20mm; } p { line-height: 2; }</style></head>
                <body>
                """ +
                string.Concat(Enumerable.Range(1, 60).Select(i => $"<p>Paragraph {i}</p>")) +
                """
                </body>
                </html>
                """;

            var doc = await new PdfGenerator().GeneratePdf(html, PageSize.A4);
            Assert.True(doc.PdfDocument.PageCount >= 2,
                $"Expected at least 2 pages but got {doc.PdfDocument.PageCount}");
        }

        // ── margin box content ─────────────────────────────────────────────────

        [Fact]
        public async Task AtPage_WithMarginBoxes_DocumentGeneratesWithoutError()
        {
            const string html = """
                <!DOCTYPE html>
                <html>
                <head>
                <style>
                @page {
                    size: A4;
                    margin: 20mm;
                    @top-center    { content: "Header"; font-size: 10pt; }
                    @bottom-center { content: "Page " counter(page) " of " counter(pages); font-size: 9pt; }
                }
                </style>
                </head>
                <body><p>Hello World</p></body>
                </html>
                """;

            var ex = await Record.ExceptionAsync(() =>
                new PdfGenerator().GeneratePdf(html, PageSize.A4));

            Assert.Null(ex);
        }

        [Fact]
        public async Task AtPage_WithMarginBoxes_SavesWithoutError()
        {
            const string html = """
                <!DOCTYPE html>
                <html>
                <head>
                <style>
                @page {
                    size: A4;
                    margin: 20mm;
                    @top-left   { content: "Company"; font-size: 8pt; }
                    @top-center { content: "Title"; font-size: 8pt; }
                    @top-right  { content: "Confidential"; font-size: 8pt; }
                    @bottom-center { content: counter(page); font-size: 8pt; }
                }
                </style>
                </head>
                <body><p>Test content</p></body>
                </html>
                """;

            var doc = await new PdfGenerator().GeneratePdf(html, PageSize.A4);
            var ex = Record.Exception(() => doc.Save(Stream.Null));
            Assert.Null(ex);
        }

        // ── @page pseudo-selectors ─────────────────────────────────────────────

        [Fact]
        public async Task AtPage_FirstPseudoSelector_DocumentGeneratesWithoutError()
        {
            const string html = """
                <!DOCTYPE html>
                <html>
                <head>
                <style>
                @page {
                    size: A4;
                    margin: 20mm;
                    @top-center { content: "Running Header"; }
                }
                @page :first {
                    @top-center { content: none; }
                }
                </style>
                </head>
                <body><p>Cover page</p></body>
                </html>
                """;

            var ex = await Record.ExceptionAsync(() =>
                new PdfGenerator().GeneratePdf(html, PageSize.A4));

            Assert.Null(ex);
        }

        [Fact]
        public async Task AtPage_LeftRightPseudoSelectors_DocumentGeneratesWithoutError()
        {
            var html = """
                <!DOCTYPE html>
                <html>
                <head>
                <style>
                @page { size: A4; margin: 20mm; }
                @page :left  { @bottom-left  { content: counter(page); } }
                @page :right { @bottom-right { content: counter(page); } }
                </style>
                </head>
                <body>
                """ +
                string.Concat(Enumerable.Range(1, 40).Select(i => $"<p>Line {i}</p>")) +
                """
                </body>
                </html>
                """;

            var ex = await Record.ExceptionAsync(() =>
                new PdfGenerator().GeneratePdf(html, PageSize.A4));

            Assert.Null(ex);
        }

        // ── content: none suppresses margin box ───────────────────────────────

        [Fact]
        public async Task AtPage_ContentNone_DocumentGeneratesWithoutError()
        {
            const string html = """
                <!DOCTYPE html>
                <html>
                <head>
                <style>
                @page {
                    size: A4;
                    margin: 20mm;
                    @top-center { content: none; }
                }
                </style>
                </head>
                <body><p>Hello</p></body>
                </html>
                """;

            var ex = await Record.ExceptionAsync(() =>
                new PdfGenerator().GeneratePdf(html, PageSize.A4));

            Assert.Null(ex);
        }

        // ── per-page margin variation ──────────────────────────────────────────

        [Fact]
        public async Task AtPage_FirstPseudoSelector_ChangesTopMargin_NoError()
        {
            const string html = """
                <!DOCTYPE html>
                <html>
                <head>
                <style>
                @page {
                    size: A4;
                    margin: 20mm;
                    @top-center { content: "Header"; }
                }
                @page :first {
                    margin-top: 40mm;
                    @top-center { content: none; }
                }
                </style>
                </head>
                <body><p>Page 1</p></body>
                </html>
                """;

            var ex = await Record.ExceptionAsync(() =>
                new PdfGenerator().GeneratePdf(html, PageSize.A4));

            Assert.Null(ex);
        }

        // ── named pages ────────────────────────────────────────────────────────

        [Fact]
        public async Task AtPage_NamedPage_SelectorApplied_NoError()
        {
            var html = """
                <!DOCTYPE html>
                <html>
                <head>
                <style>
                @page { size: A4; margin: 20mm; }
                @page chapter { @top-right { content: "Chapter"; font-size: 8pt; } }
                </style>
                </head>
                <body>
                """ +
                string.Concat(Enumerable.Range(1, 3).Select(i =>
                    $"<h1 style=\"page: chapter\">Chapter {i}</h1>" +
                    string.Concat(Enumerable.Range(1, 15).Select(j => $"<p>Paragraph {j}</p>")))) +
                """
                </body>
                </html>
                """;

            var ex = await Record.ExceptionAsync(() =>
                new PdfGenerator().GeneratePdf(html, PageSize.A4));

            Assert.Null(ex);
        }

        // ── margin box explicit width ───────────────────────────────────────────

        [Fact]
        public async Task AtPage_MarginBoxExplicitWidth_NoError()
        {
            const string html = """
                <!DOCTYPE html>
                <html>
                <head>
                <style>
                @page {
                    size: A4;
                    margin: 20mm;
                    @top-left   { content: "Left"; width: 80pt; font-size: 8pt; }
                    @top-center { content: "Center"; font-size: 8pt; }
                    @top-right  { content: "Right"; width: 60pt; font-size: 8pt; }
                }
                </style>
                </head>
                <body><p>Hello World</p></body>
                </html>
                """;

            var ex = await Record.ExceptionAsync(() =>
                new PdfGenerator().GeneratePdf(html, PageSize.A4));

            Assert.Null(ex);
        }

        // ── regression: existing page layout unaffected ───────────────────────

        [Fact]
        public async Task NoAtPageRule_DocumentGeneratesNormally()
        {
            const string html = """
                <!DOCTYPE html>
                <html>
                <body><h1>Hello</h1><p>World</p></body>
                </html>
                """;

            var doc = await new PdfGenerator().GeneratePdf(html, PageSize.A4);
            Assert.Equal(1, doc.PdfDocument.PageCount);

            var page = doc.PdfDocument.Pages[0];
            // A4 default
            Assert.Equal(595.0, page.Width.Point, 0);
            Assert.Equal(842.0, page.Height.Point, 0);
        }
    }
}
