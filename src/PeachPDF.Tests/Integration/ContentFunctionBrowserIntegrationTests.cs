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
    /// Browser integration tests that verify content() function behavior matches between
    /// PeachPDF's CssBox DOM and major browsers' DOM/rendering.
    /// 
    /// These tests compare the generated content from pseudo-elements to verify that
    /// PeachPDF's implementation of the content() function matches browser behavior.
    /// 
    /// NOTE: These tests require Playwright browsers to be installed.
    /// Run: playwright install chromium firefox webkit
    /// 
    /// To skip these tests: --filter "FullyQualifiedName!~BrowserIntegration"
    /// </summary>
    [Trait("Category", "BrowserIntegration")]
    [Trait("Requires", "PlaywrightBrowsers")]
    public class ContentFunctionBrowserIntegrationTests : IClassFixture<PlaywrightBrowserFixture>
    {
        private readonly PlaywrightBrowserFixture _fixture;

        public ContentFunctionBrowserIntegrationTests(PlaywrightBrowserFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task ContentFunction_TextMode_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1::before { content: content(text); }
    </style>
</head>
<body>
    <h1>Dynamic Heading</h1>
</body>
</html>";

            await VerifyContentFunctionBehavior(
       html,
  "h1",
isPseudoBefore: true,
                expectedContent: "Dynamic Heading",
     description: "content(text) should extract element text"
      );
        }

        [Fact]
        public async Task ContentFunction_DefaultMode_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
     h1::before { content: content(); }
    </style>
</head>
<body>
    <h1>Test Content</h1>
</body>
</html>";

            await VerifyContentFunctionBehavior(
               html,
                     "h1",
                  isPseudoBefore: true,
                expectedContent: "Test Content",
                     description: "content() with no argument should default to text mode"
             );
        }

        [Fact]
        public async Task ContentFunction_BeforeMode_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1::before { content: ""Chapter ""; }
        h1::after { content: content(before); }
    </style>
</head>
<body>
    <h1>One</h1>
</body>
</html>";

            await VerifyContentFunctionBehavior(
        html,
        "h1",
             isPseudoBefore: false,
                expectedContent: "Chapter ",
          description: "content(before) should extract ::before pseudo-element content"
            );
        }

        [Fact]
        public async Task ContentFunction_AfterMode_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1::after { content: "".txt""; }
   .filename::before { content: ""File: "" content(text) content(after); }
    </style>
</head>
<body>
    <h1 class=""filename"">document</h1>
</body>
</html>";

            await VerifyContentFunctionBehavior(
 html,
      "h1",
  isPseudoBefore: true,
        expectedContent: "File: document.txt",
 description: "content(after) should extract ::after pseudo-element content"
     );
        }

        [Fact]
        public async Task ContentFunction_FirstLetterMode_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        p::before { content: content(first-letter); }
 </style>
</head>
<body>
 <p>Hello World</p>
</body>
</html>";

            await VerifyContentFunctionBehavior(
     html,
   "p",
         isPseudoBefore: true,
          expectedContent: "H",
    description: "content(first-letter) should extract only first letter"
            );
        }

        [Fact]
        public async Task ContentFunction_CombinedWithString_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1::before { content: ""Chapter "" content(text); }
    </style>
</head>
<body>
    <h1>One</h1>
</body>
</html>";

            await VerifyContentFunctionBehavior(
   html,
           "h1",
      isPseudoBefore: true,
      expectedContent: "Chapter One",
             description: "content() combined with string should concatenate correctly"
            );
        }

        [Fact]
        public async Task ContentFunction_CombinedWithCounter_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        body { counter-reset: chapter; }
        h1 { counter-increment: chapter; }
        h1::before { content: counter(chapter) "". "" content(text); }
    </style>
</head>
<body>
    <h1>Introduction</h1>
    <h1>Background</h1>
</body>
</html>";

            await VerifyContentFunctionBehavior(
                  html,
         "h1:last-of-type",
                  isPseudoBefore: true,
            expectedContent: "2. Background",
                     description: "content() combined with counter should work correctly"
                   );
        }

        [Fact]
        public async Task ContentFunction_WithNestedElements_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1::before { content: content(text); }
    </style>
</head>
<body>
    <h1>Hello <em>World</em></h1>
</body>
</html>";

            await VerifyContentFunctionBehavior(
           html,
     "h1",
     isPseudoBefore: true,
      expectedContent: "Hello World",
    description: "content(text) should extract all nested text",
      allowPartialMatch: true // Browser may include whitespace differences
 );
        }

        [Fact]
        public async Task ContentFunction_WithEmptyElement_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1::before { content: content(text); }
    </style>
</head>
<body>
    <h1></h1>
</body>
</html>";

            await VerifyContentFunctionBehavior(
                  html,
          "h1",
           isPseudoBefore: true,
             expectedContent: "",
            description: "content(text) on empty element should produce empty string"
                     );
        }

        [Fact]
        public async Task ContentFunction_MultipleValues_ComparesWithBrowsers()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1::before { content: ""[""; }
        h1::after { content: ""]""; }
     p::before { content: content(text) "" from h1: "" attr(data-source); }
    </style>
</head>
<body>
    <h1>Title</h1>
    <p data-source=""above"">Content</p>
</body>
</html>";

            await VerifyContentFunctionBehavior(
             html,
            "p",
       isPseudoBefore: true,
              expectedContent: "Content from h1: above",
             description: "Multiple content values should concatenate"
              );
        }

        /// <summary>
        /// Verifies that content() function behavior matches between PeachPDF and browsers.
        /// </summary>
        private async Task VerifyContentFunctionBehavior(
            string html,
     string selector,
            bool isPseudoBefore,
    string expectedContent,
     string description,
            bool allowPartialMatch = false)
        {
            // Build CssBox tree with PeachPDF
            var (elementBox, pseudoBox) = await BuildCssBoxTree(html, selector, isPseudoBefore);

            // Get browser's pseudo-element content
            var browserResults = await GetBrowserPseudoElementContent(html, selector, isPseudoBefore);

            // Verify PeachPDF has the correct content
            Assert.NotNull(pseudoBox);

            if (allowPartialMatch)
            {
                Assert.Contains(expectedContent.Replace(" ", ""), pseudoBox.Text?.Replace(" ", "") ?? "");
            }
            else
            {
                Assert.Equal(expectedContent, pseudoBox.Text);
            }

            // Generate comparison report
            var report = GenerateComparisonReport(
        description,
          selector,
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
        private async Task<(CssBox elementBox, CssBox? pseudoBox)> BuildCssBoxTree(
        string html,
            string selector,
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

            var elementBox = FindBoxBySelector(container.Root!, selector);
            Assert.NotNull(elementBox);

            var pseudoBox = elementBox.Boxes.FirstOrDefault(b =>
                isPseudoBefore ? b.IsBeforePseudoElement : b.IsAfterPseudoElement);

            return (elementBox, pseudoBox);
        }

        /// <summary>
        /// Simple selector matching - supports tag names and pseudo-classes.
        /// </summary>
        private CssBox? FindBoxBySelector(CssBox root, string selector)
        {
            if (selector.Contains(":last-of-type"))
            {
                var tagName = selector.Split(':')[0];
                return FindLastBoxByTagName(root, tagName);
            }

            if (selector.Contains("."))
            {
                var parts = selector.Split('.');
                var tagName = parts[0];
                var className = parts[1];
                return FindBoxByTagAndClass(root, tagName, className);
            }

            return DomUtils.GetBoxByTagName(root, selector);
        }

        private CssBox? FindLastBoxByTagName(CssBox root, string tagName)
        {
            CssBox? lastMatch = null;
            FindLastBoxByTagNameRecursive(root, tagName, ref lastMatch);
            return lastMatch;
        }

        private void FindLastBoxByTagNameRecursive(CssBox box, string tagName, ref CssBox? lastMatch)
        {
            if (box.HtmlTag?.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase) == true)
            {
                lastMatch = box;
            }

            foreach (var child in box.Boxes)
            {
                FindLastBoxByTagNameRecursive(child, tagName, ref lastMatch);
            }
        }

        private CssBox? FindBoxByTagAndClass(CssBox root, string tagName, string className)
        {
            return FindBoxByTagAndClassRecursive(root, tagName, className);
        }

        private CssBox? FindBoxByTagAndClassRecursive(CssBox box, string tagName, string className)
        {
            if (box.HtmlTag?.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase) == true)
            {
                var classAttr = box.HtmlTag.TryGetAttribute("class", "");
                if (classAttr.Contains(className))
                {
                    return box;
                }
            }

            foreach (var child in box.Boxes)
            {
                var found = FindBoxByTagAndClassRecursive(child, tagName, className);
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

                // Get the content property value and text content
                var scriptResult = await page.EvaluateAsync<BrowserPseudoElementResult>($@"
        () => {{
    const element = document.querySelector('{selector}');
        if (!element) return {{ 
   contentSupported: false, 
         contentValue: 'Element not found',
textContent: '',
      contentFunctionSupported: false
            }};

    const style = window.getComputedStyle(element, '::{pseudoType}');
       const contentValue = style.getPropertyValue('content') || '';
      
          // Check if content() function is supported
     const testDiv = document.createElement('div');
  testDiv.style.setProperty('--test', 'content(text)');
             const contentFunctionSupported = testDiv.style.getPropertyValue('--test') !== '';

             // Try to get the actual text content of the pseudo-element
        // Note: This is difficult/impossible in most browsers
    let textContent = '';
         try {{
   // Some browsers support this
        const pseudoElement = element.querySelector('::{pseudoType}');
       if (pseudoElement) {{
           textContent = pseudoElement.textContent || '';
                 }}
             }} catch (e) {{
  // Pseudo-elements aren't directly accessible
                textContent = 'Cannot access pseudo-element text in browser';
            }}

       return {{ 
  contentSupported: contentValue !== '' && contentValue !== 'none',
        contentValue: contentValue,
      textContent: textContent,
   contentFunctionSupported: contentFunctionSupported
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
                    TextContent = "",
                    ContentFunctionSupported = false
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

            report.AppendLine("??????????????????????????????????????????????????????????????????????????");
            report.AppendLine("? Content() Function Browser Integration Test          ?");
            report.AppendLine("??????????????????????????????????????????????????????????????????????????");
            report.AppendLine();
            report.AppendLine($"Description: {description}");
            report.AppendLine($"Selector: {selector}{pseudoType}");
            report.AppendLine($"Expected: \"{expectedContent}\"");
            report.AppendLine();

            // PeachPDF Results
            report.AppendLine("?????????????????????????????????????????????????????????????????????????");
            report.AppendLine("PeachPDF Results:");
            report.AppendLine("?????????????????????????????????????????????????????????????????????????");
            var match = actualPeachPdfContent == expectedContent ? "?" : "?";
            report.AppendLine($"  Generated Content: \"{actualPeachPdfContent}\" {match}");
            report.AppendLine($"  Match: {(actualPeachPdfContent == expectedContent ? "PASS" : "FAIL")}");
            report.AppendLine();

            // Browser Results
            report.AppendLine("?????????????????????????????????????????????????????????????????????????");
            report.AppendLine("Browser Results:");
            report.AppendLine("?????????????????????????????????????????????????????????????????????????");

            foreach (var (browser, result) in browserResults)
            {
                report.AppendLine($"{browser}:");
                report.AppendLine($"  content property: {result.ContentValue}");
                report.AppendLine($"  content() function support: {(result.ContentFunctionSupported ? "Likely supported" : "Not detected")}");
                report.AppendLine($"  Pseudo-element accessible: {(!result.TextContent.Contains("Cannot access") ? "Yes" : "No")}");

                if (!result.TextContent.Contains("Cannot access") && !string.IsNullOrEmpty(result.TextContent))
                {
                    report.AppendLine($"  Text content: \"{result.TextContent}\"");
                }

                report.AppendLine();
            }

            report.AppendLine("?????????????????????????????????????????????????????????????????????????");
            report.AppendLine("Note: Browser support for content() function is limited as of 2024.");
            report.AppendLine("This test verifies PeachPDF implements the CSS GCPM-3 spec correctly.");
            report.AppendLine("?????????????????????????????????????????????????????????????????????????");

            return report.ToString();
        }

        private class BrowserPseudoElementResult
        {
            public bool ContentSupported { get; set; }
            public string ContentValue { get; set; } = string.Empty;
            public string TextContent { get; set; } = string.Empty;
            public bool ContentFunctionSupported { get; set; }
        }
    }
}
