using System;
using System.IO;
using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.Integration
{
    public class PdfMetadataIntegrationTests
    {
        [Fact]
        public async Task Title_IsExtractedFromTitleElement()
        {
            var html = """
                <!DOCTYPE html>
                <html>
                <head><title>My Document</title></head>
                <body><p>Hello</p></body>
                </html>
                """;

            var info = await GenerateInfo(html);

            Assert.Equal("My Document", info.Title);
        }

        [Fact]
        public async Task Author_IsExtractedFromMetaTag()
        {
            var html = """
                <html>
                <head><meta name="author" content="Jane Doe"></head>
                <body><p>Hello</p></body>
                </html>
                """;

            var info = await GenerateInfo(html);

            Assert.Equal("Jane Doe", info.Author);
        }

        [Fact]
        public async Task Subject_IsExtractedFromMetaTag()
        {
            var html = """
                <html>
                <head><meta name="subject" content="Unit Testing"></head>
                <body><p>Hello</p></body>
                </html>
                """;

            var info = await GenerateInfo(html);

            Assert.Equal("Unit Testing", info.Subject);
        }

        [Fact]
        public async Task Keywords_AreExtractedFromMetaTag()
        {
            var html = """
                <html>
                <head><meta name="keywords" content="test, pdf, metadata"></head>
                <body><p>Hello</p></body>
                </html>
                """;

            var info = await GenerateInfo(html);

            Assert.Equal("test, pdf, metadata", info.Keywords);
        }

        [Fact]
        public async Task Date_IsExtractedFromMetaTag_AndSetsCreationDate()
        {
            var html = """
                <html>
                <head><meta name="date" content="2024-03-15"></head>
                <body><p>Hello</p></body>
                </html>
                """;

            var info = await GenerateInfo(html);

            Assert.Equal(new DateTime(2024, 3, 15), info.CreationDate.Date);
        }

        [Fact]
        public async Task Generator_Meta_SetsCreator()
        {
            var html = """
                <html>
                <head><meta name="generator" content="MyApp 2.0"></head>
                <body><p>Hello</p></body>
                </html>
                """;

            var info = await GenerateInfo(html);

            Assert.Equal("MyApp 2.0", info.Creator);
        }

        [Fact]
        public async Task Creator_DefaultsToPeachPdf_WhenNoGeneratorMeta()
        {
            var html = "<html><body><p>Hello</p></body></html>";

            var info = await GenerateInfo(html);

            Assert.Equal(PeachPdfProductInfo.Generator, info.Creator);
        }

        [Fact]
        public async Task Producer_IsAlwaysPeachPdf()
        {
            var html = "<html><body><p>Hello</p></body></html>";

            var info = await GenerateInfo(html);

            Assert.Equal(PeachPdfProductInfo.Generator, info.Producer);
        }

        // Regression: Producer was previously wrapped as
        // "PDFsharp … (Original: PeachPDF …)" during Save().
        [Fact]
        public async Task Producer_IsNotMangledAfterSave()
        {
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><p>Hello</p></body></html>", PageSize.A4);

            result.Save(Stream.Null);

            Assert.Equal(PeachPdfProductInfo.Generator, result.PdfDocument.Info.Producer);
        }

        [Fact]
        public async Task AllMetadataFields_AreExtractedTogether()
        {
            var html = """
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Full Test</title>
                    <meta name="author" content="Jane Doe">
                    <meta name="subject" content="Integration Test">
                    <meta name="keywords" content="alpha, beta">
                    <meta name="date" content="2025-01-20">
                    <meta name="generator" content="TestGen 1.0">
                </head>
                <body><p>Hello</p></body>
                </html>
                """;

            var info = await GenerateInfo(html);

            Assert.Equal("Full Test",          info.Title);
            Assert.Equal("Jane Doe",           info.Author);
            Assert.Equal("Integration Test",   info.Subject);
            Assert.Equal("alpha, beta",        info.Keywords);
            Assert.Equal(new DateTime(2025, 1, 20), info.CreationDate.Date);
            Assert.Equal("TestGen 1.0",        info.Creator);
            Assert.Equal(PeachPdfProductInfo.Generator, info.Producer);
        }

        #region Helpers

        private static async Task<PdfDocumentInformation> GenerateInfo(string html)
        {
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);
            return result.PdfDocument.Info;
        }

        #endregion
    }
}
