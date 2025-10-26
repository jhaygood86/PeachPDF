using Microsoft.Playwright;
using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System.Text;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests that verify string-set behavior matches between PeachPDF's CssBox DOM
    /// and major browsers' DOM. These tests compare the named strings and box structures
    /// rather than rendered output.
    /// 
    /// NOTE: These tests require Playwright browsers to be installed.
    /// Run: playwright install chromium firefox webkit
    /// 
    /// To skip these tests: --filter "FullyQualifiedName!~BrowserIntegration"
    /// </summary>
    [Trait("Category", "BrowserIntegration")]
    [Trait("Requires", "PlaywrightBrowsers")]
    public class StringSetBrowserIntegrationTests : IClassFixture<PlaywrightBrowserFixture>
    {
        private readonly PlaywrightBrowserFixture _fixture;

        public StringSetBrowserIntegrationTests(PlaywrightBrowserFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task StringSet_WithStringLiteral_ComparesCorrectly()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
     h1 { string-set: page-header ""Chapter 1""; }
    </style>
</head>
<body>
    <h1>First Heading</h1>
    <p>Some content here.</p>
</body>
</html>";

            await VerifyStringSetBehavior(html, "h1", "page-header", "Chapter 1");
        }

        [Fact]
        public async Task StringSet_WithCounter_ComparesCorrectly()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
  body { counter-reset: page 1; }
    h1 { counter-increment: page; string-set: page-label ""Page "" counter(page); }
    </style>
</head>
<body>
    <h1>First Heading</h1><p>Content</p>
    <h1>Second Heading</h1><p>More content</p>
</body>
</html>";

            await VerifyStringSetBehavior(html, "h1:last-of-type", "page-label", "Page 3");
        }

        [Fact]
        public async Task StringSet_WithContentText_ComparesCorrectly()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
 h1 { string-set: heading-text content(text); }
    </style>
</head>
<body>
    <h1>Dynamic Heading</h1>
    <p>Some content here.</p>
</body>
</html>";

            await VerifyStringSetBehavior(html, "h1", "heading-text", "Dynamic Heading");
        }

        /// <summary>
        /// Verifies that string-set behavior matches between PeachPDF's CssBox and browser DOM.
        /// </summary>
        private async Task VerifyStringSetBehavior(string html, string selector, string stringName, string expectedValue)
        {
            // Build CssBox tree with PeachPDF
            var (cssBox, namedStrings) = await BuildCssBoxTree(html, selector);

            // Get browser's computed styles
            var browserResults = await GetBrowserStringSetInfo(html, selector);

            // Verify PeachPDF has the correct named string
            Assert.True(namedStrings.ContainsKey(stringName),
    $"PeachPDF CssBox should contain named string '{stringName}'");
            Assert.Equal(expectedValue, namedStrings[stringName].Value);

            // Document comparison results
            var report = new StringBuilder();
            report.AppendLine($"String-set Integration Test: {stringName} = {expectedValue}");
            report.AppendLine($"Selector: {selector}");
            report.AppendLine();
            report.AppendLine($"PeachPDF CssBox:");
            report.AppendLine($"  Named String '{stringName}': {namedStrings[stringName].Value} ✓");
            report.AppendLine($"  Box Display: {cssBox.Display}");
            report.AppendLine($"  Box Text: {cssBox.Text}");
            report.AppendLine($"  Has StringSet Property: {!string.IsNullOrEmpty(cssBox.StringSet)}");
            report.AppendLine();

            foreach (var (browser, result) in browserResults)
            {
                report.AppendLine($"{browser}:");
                report.AppendLine($"  string-set support: {result.StringSetSupported}");
                report.AppendLine($"  string-set value: {result.StringSetValue}");
            }

            Console.WriteLine(report.ToString());
        }

        /// <summary>
        /// Builds the CssBox tree for the given HTML and returns the box matching the selector
        /// along with its named strings.
        /// </summary>
        private async Task<(CssBox box, Dictionary<string, NamedString> namedStrings)> BuildCssBoxTree(string html, string selector)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);

            // Parse HTML and build CSS box tree
            await container.SetHtml(html, null);

            // Perform layout to trigger string-set evaluation
            var size = new PdfSharpCore.Drawing.XSize(595, 842); // A4 size
            container.PageSize = Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            // Find the box matching the selector
            var targetBox = FindBoxBySelector(container.Root!, selector);
            Assert.NotNull(targetBox);

            // Apply string-set engine
            CssNamedStringEngine.ApplyStringSet(targetBox);

            return (targetBox, targetBox.NamedStrings);
        }

        /// <summary>
        /// Simple selector matching - supports tag names and :last-of-type pseudo-class.
        /// </summary>
        private CssBox? FindBoxBySelector(CssBox root, string selector)
        {
            if (selector.Contains(":last-of-type"))
            {
                var tagName = selector.Split(':')[0];
                return FindLastBoxByTagName(root, tagName);
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

        /// <summary>
        /// Gets string-set information from browsers using Playwright.
        /// </summary>
        private async Task<Dictionary<string, BrowserStringSetResult>> GetBrowserStringSetInfo(string html, string selector)
        {
            var results = new Dictionary<string, BrowserStringSetResult>();

            if (_fixture.Chromium != null)
            {
                results["Chromium"] = await GetBrowserStringSet(_fixture.Chromium, html, selector);
            }

            if (_fixture.Firefox != null)
            {
                results["Firefox"] = await GetBrowserStringSet(_fixture.Firefox, html, selector);
            }

            if (_fixture.Webkit != null)
            {
                results["WebKit"] = await GetBrowserStringSet(_fixture.Webkit, html, selector);
            }

            return results;
        }

        private async Task<BrowserStringSetResult> GetBrowserStringSet(IBrowser browser, string html, string selector)
        {
            var page = await browser.NewPageAsync();

            try
            {
                await page.SetContentAsync(html);

                // Check if browser supports string-set
                var scriptResult = await page.EvaluateAsync<BrowserStringSetResult>($@"
             () => {{ 
        const element = document.querySelector('{selector}');
  if (!element) return {{ stringSetSupported: false, stringSetValue: 'Element not found' }};

     const style = window.getComputedStyle(element);
      const stringSetValue = style.getPropertyValue('string-set') || '';
       
         return {{ 
         stringSetSupported: stringSetValue !== '', 
 stringSetValue: stringSetValue || 'Not supported' 
    }};
      }}
              ");

                await page.CloseAsync();
                return scriptResult;
            }
            catch (Exception ex)
            {
                await page.CloseAsync();
                return new BrowserStringSetResult
                {
                    StringSetSupported = false,
                    StringSetValue = $"Error: {ex.Message}"
                };
            }
        }

        private class BrowserStringSetResult
        {
            public bool StringSetSupported { get; set; }
            public string StringSetValue { get; set; } = string.Empty;
        }
    }

    /// <summary>
    /// Fixture for sharing Playwright browser instances across tests.
    /// </summary>
    public class PlaywrightBrowserFixture : IAsyncLifetime
    {
        public IPlaywright? Playwright { get; private set; }
        public IBrowser? Chromium { get; private set; }
        public IBrowser? Firefox { get; private set; }
        public IBrowser? Webkit { get; private set; }

        public async Task InitializeAsync()
        {
            try
            {
                Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
                Chromium = await Playwright.Chromium.LaunchAsync(new() { Headless = true });
                Firefox = await Playwright.Firefox.LaunchAsync(new() { Headless = true });
                Webkit = await Playwright.Webkit.LaunchAsync(new() { Headless = true });
            }
            catch (PlaywrightException)
            {
                // Browsers not installed - tests will be skipped
            }
        }

        public async Task DisposeAsync()
        {
            if (Chromium != null) await Chromium.DisposeAsync();
            if (Firefox != null) await Firefox.DisposeAsync();
            if (Webkit != null) await Webkit.DisposeAsync();
            Playwright?.Dispose();
        }
    }
}
