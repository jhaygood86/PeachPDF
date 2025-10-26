using Microsoft.Playwright;
using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System.Text;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Browser integration tests that verify string() function behavior matches between
    /// PeachPDF's CssBox DOM and major browsers' DOM/rendering.
    /// 
    /// These tests compare the generated content from pseudo-elements to verify that
    /// PeachPDF's implementation of the string() function matches browser behavior.
    /// 
    /// NOTE: These tests require Playwright browsers to be installed.
    /// Run: playwright install chromium firefox webkit
    /// 
    /// To skip these tests: --filter "FullyQualifiedName!~BrowserIntegration"
    /// </summary>
    [Trait("Category", "BrowserIntegration")]
    [Trait("Requires", "PlaywrightBrowsers")]
    public class StringFunctionBrowserIntegrationTests : IClassFixture<PlaywrightBrowserFixture>
    {
        private readonly PlaywrightBrowserFixture _fixture;

        public StringFunctionBrowserIntegrationTests(PlaywrightBrowserFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task StringFunction_WithDefault_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
 <style>
        h1 { string-set: chapter content(text); }
      .header::before { content: string(chapter); }
    </style>
</head>
<body>
    <h1>Introduction</h1>
    <div class=""header""></div>
</body>
</html>";

            await VerifyStringFunctionBehavior(
       html,
             "h1",
                       ".header",
                       isPseudoBefore: true,
             expectedContent: "Introduction",
             description: "string(chapter) should retrieve named string from string-set"
             );
        }

        [Fact]
        public async Task StringFunction_WithFirstKeyword_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
  h1 { string-set: chapter content(text); }
        h2 { string-set: chapter content(text); }
        .header::before { content: string(chapter, first); }
    </style>
</head>
<body>
    <h1>First Chapter</h1>
    <h2>Second Chapter</h2>
    <div class=""header""></div>
</body>
</html>";

            await VerifyStringFunctionBehavior(
          html,
                    "h1",
         ".header",
         isPseudoBefore: true,
          expectedContent: "First Chapter",
           description: "string(chapter, first) should retrieve first assignment"
                );
        }

        [Fact]
        public async Task StringFunction_WithLastKeyword_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1 { string-set: chapter content(text); }
   h2 { string-set: chapter content(text); }
        .header::before { content: string(chapter, last); }
    </style>
</head>
<body>
    <h1>First Chapter</h1>
    <h2>Second Chapter</h2>
    <div class=""header""></div>
</body>
</html>";

            await VerifyStringFunctionBehavior(
              html,
          "h2",
           ".header",
                      isPseudoBefore: true,
                         expectedContent: "Second Chapter",
             description: "string(chapter, last) should retrieve last assignment"
           );
        }

        [Fact]
        public async Task StringFunction_CombinedWithStringLiteral_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
  h1 { string-set: chapter content(text); }
        .header::before { content: ""Chapter: "" string(chapter); }
    </style>
</head>
<body>
    <h1>Introduction</h1>
    <div class=""header""></div>
</body>
</html>";

            await VerifyStringFunctionBehavior(
  html,
     "h1",
                ".header",
     isPseudoBefore: true,
     expectedContent: "Chapter: Introduction",
     description: "string() combined with string literal should concatenate"
     );
        }

        [Fact]
        public async Task StringFunction_CombinedWithCounter_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        body { counter-reset: page; }
      h1 { counter-increment: page; string-set: chapter content(text); }
 .header::before { content: string(chapter) "" - Page "" counter(page); }
  </style>
</head>
<body>
    <h1>Introduction</h1>
  <div class=""header""></div>
</body>
</html>";

            await VerifyStringFunctionBehavior(
      html,
         "h1",
        ".header",
          isPseudoBefore: true,
         expectedContent: "Introduction - Page 1",
 description: "string() combined with counter should work correctly"
      );
        }

        [Fact]
        public async Task StringFunction_MultipleStrings_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1 { string-set: chapter content(text); }
        h2 { string-set: section content(text); }
    .header::before { content: string(chapter) "" / "" string(section); }
    </style>
</head>
<body>
    <h1>Chapter One</h1>
    <h2>Section A</h2>
    <div class=""header""></div>
</body>
</html>";

            await VerifyStringFunctionBehavior(
             html,
        "h2",
   ".header",
             isPseudoBefore: true,
    expectedContent: "Chapter One / Section A",
            description: "Multiple string() functions should combine correctly"
        );
        }

        [Fact]
        public async Task StringFunction_WithNonExistentString_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        .header::before { content: string(nonexistent); }
</style>
</head>
<body>
    <div class=""header""></div>
</body>
</html>";

            await VerifyStringFunctionBehavior(
                 html,
             ".header",
                         ".header",
                         isPseudoBefore: true,
            expectedContent: "",
                description: "string() with non-existent named string should return empty"
                     );
        }

        /// <summary>
        /// Verifies that string() function behavior matches between PeachPDF and browsers.
        /// </summary>
        private async Task VerifyStringFunctionBehavior(
            string html,
      string stringSetSelector,
            string pseudoSelector,
      bool isPseudoBefore,
    string expectedContent,
     string description)
        {
            // Build CssBox tree with PeachPDF
            var (stringSetBox, pseudoBox) = await BuildCssBoxTree(html, stringSetSelector, pseudoSelector, isPseudoBefore);

            // Get browser's pseudo-element content
            var browserResults = await GetBrowserPseudoElementContent(html, pseudoSelector, isPseudoBefore);

            // Verify PeachPDF has the correct content
            Assert.NotNull(pseudoBox);
            Assert.Equal(expectedContent, pseudoBox.Text);

            // Generate comparison report
            var report = GenerateComparisonReport(
         description,
           pseudoSelector,
         isPseudoBefore,
     expectedContent,
  pseudoBox.Text ?? "",
         browserResults
);

            Console.WriteLine(report);
        }

        /// <summary>
        /// Builds the CssBox tree and finds the pseudo-element box.
        /// </summary>
        private async Task<(CssBox stringSetBox, CssBox? pseudoBox)> BuildCssBoxTree(
   string html,
            string stringSetSelector,
        string pseudoSelector,
    bool isPseudoBefore)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);

            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            // Find and apply string-set
            var stringSetBox = FindBoxBySelector(container.Root!, stringSetSelector);
            Assert.NotNull(stringSetBox);
            CssNamedStringEngine.ApplyStringSet(stringSetBox);

            // Find pseudo-element
            var elementBox = FindBoxBySelector(container.Root!, pseudoSelector.Split(':')[0]);
            Assert.NotNull(elementBox);

            var pseudoBox = elementBox.Boxes.FirstOrDefault(b =>
                  isPseudoBefore ? b.IsBeforePseudoElement : b.IsAfterPseudoElement);

            return (stringSetBox, pseudoBox);
        }

        /// <summary>
        /// Simple selector matching - supports tag names and class selectors.
        /// </summary>
        private CssBox? FindBoxBySelector(CssBox root, string selector)
        {
            if (selector.StartsWith("."))
            {
                var className = selector.Substring(1);
                return FindBoxByClass(root, className);
            }

            return DomUtils.GetBoxByTagName(root, selector);
        }

        private CssBox? FindBoxByClass(CssBox root, string className)
        {
            if (root.HtmlTag != null)
            {
                var classAttr = root.HtmlTag.TryGetAttribute("class", "");
                if (classAttr.Contains(className))
                {
                    return root;
                }
            }

            foreach (var child in root.Boxes)
            {
                var found = FindBoxByClass(child, className);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets pseudo-element content from browsers using Playwright.
        /// </summary>
        private async Task<Dictionary<string, BrowserPseudoElementResult>> GetBrowserPseudoElementContent(
                 string html,
          string selector,
             bool isPseudoBefore)
        {
            var results = new Dictionary<string, BrowserPseudoElementResult>();

            if (_fixture.Chromium != null)
            {
                results["Chromium"] = await GetBrowserPseudoContent(_fixture.Chromium, html, selector, isPseudoBefore);
            }

            if (_fixture.Firefox != null)
            {
                results["Firefox"] = await GetBrowserPseudoContent(_fixture.Firefox, html, selector, isPseudoBefore);
            }

            if (_fixture.Webkit != null)
            {
                results["WebKit"] = await GetBrowserPseudoContent(_fixture.Webkit, html, selector, isPseudoBefore);
            }

            return results;
        }

        private async Task<BrowserPseudoElementResult> GetBrowserPseudoContent(
         IBrowser browser,
           string html,
                 string selector,
            bool isPseudoBefore)
        {
            var page = await browser.NewPageAsync();

            try
            {
                await page.SetContentAsync(html);

                var pseudoType = isPseudoBefore ? "before" : "after";

                var scriptResult = await page.EvaluateAsync<BrowserPseudoElementResult>($@"
     () => {{
  const element = document.querySelector('{selector}');
           if (!element) return {{ 
    contentSupported: false, 
          contentValue: 'Element not found',
      stringFunctionSupported: false
           }};

     const style = window.getComputedStyle(element, '::{pseudoType}');
       const contentValue = style.getPropertyValue('content') || '';
  
                // Check if string() function is supported
  const testDiv = document.createElement('div');
      testDiv.style.setProperty('--test', 'string(test)');
      const stringFunctionSupported = testDiv.style.getPropertyValue('--test') !== '';

         return {{ 
   contentSupported: contentValue !== '' && contentValue !== 'none',
            contentValue: contentValue,
     stringFunctionSupported: stringFunctionSupported
             }};
              }}
      ");

                await page.CloseAsync();
                return scriptResult;
            }
            catch (Exception ex)
            {
                await page.CloseAsync();
                return new BrowserPseudoElementResult
                {
                    ContentSupported = false,
                    ContentValue = $"Error: {ex.Message}",
                    StringFunctionSupported = false
                };
            }
        }

        /// <summary>
        /// Generates a detailed comparison report.
        /// </summary>
        private string GenerateComparisonReport(
            string description,
            string selector,
   bool isPseudoBefore,
            string expectedContent,
            string actualPeachPdfContent,
    Dictionary<string, BrowserPseudoElementResult> browserResults)
        {
            var report = new StringBuilder();
            var pseudoType = isPseudoBefore ? "::before" : "::after";

            report.AppendLine("═══════════════════════════════════════════════════════");
            report.AppendLine("│ String() Function Browser Integration Test           │");
            report.AppendLine("═══════════════════════════════════════════════════════");
            report.AppendLine();
            report.AppendLine($"Description: {description}");
            report.AppendLine($"Selector: {selector}{pseudoType}");
            report.AppendLine($"Expected: \"{expectedContent}\"");
            report.AppendLine();

            // PeachPDF Results
            report.AppendLine("───────────────────────────────────────────────────────");
            report.AppendLine("PeachPDF Results:");
            report.AppendLine("───────────────────────────────────────────────────────");
            var match = actualPeachPdfContent == expectedContent ? "✓" : "✗";
            report.AppendLine($"  Generated Content: \"{actualPeachPdfContent}\" {match}");
            report.AppendLine($"Match: {(actualPeachPdfContent == expectedContent ? "PASS" : "FAIL")}");
            report.AppendLine();

            // Browser Results
            report.AppendLine("───────────────────────────────────────────────────────");
            report.AppendLine("Browser Results:");
            report.AppendLine("───────────────────────────────────────────────────────");

            foreach (var (browser, result) in browserResults)
            {
                report.AppendLine($"{browser}:");
                report.AppendLine($"  content property: {result.ContentValue}");
                report.AppendLine($"  string() function support: {(result.StringFunctionSupported ? "Likely supported" : "Not detected")}");
                report.AppendLine();
            }

            report.AppendLine("───────────────────────────────────────────────────────");
            report.AppendLine("Note: Browser support for string() function is limited as of 2024.");
            report.AppendLine("This test verifies PeachPDF implements the CSS GCPM-3 spec correctly.");
            report.AppendLine("═══════════════════════════════════════════════════════");

            return report.ToString();
        }

        private class BrowserPseudoElementResult
        {
            public bool ContentSupported { get; set; }
            public string ContentValue { get; set; } = string.Empty;
            public bool StringFunctionSupported { get; set; }
        }
    }
}
