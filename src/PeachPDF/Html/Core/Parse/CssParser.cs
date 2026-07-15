// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

#nullable enable

using PeachPDF;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Network;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Parse
{
    /// <summary>
    /// Parser to parse CSS stylesheet source string into CSS objects.
    /// </summary>
    internal sealed class CssParser
    {
        #region Fields and Consts

        /// <summary>
        /// 
        /// </summary>
        private readonly RAdapter _adapter;

        /// <summary>
        /// Utility for value parsing.
        /// </summary>
        private readonly CssValueParser _valueParser;

        private readonly HtmlContainerInt? _htmlContainer;

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        public CssParser(RAdapter adapter, HtmlContainerInt? htmlContainer)
        {
            ArgumentNullException.ThrowIfNull(adapter, "global");

            _valueParser = new CssValueParser(adapter);
            _adapter = adapter;
            _htmlContainer = htmlContainer;
        }

        /// <summary>
        /// Parse the given stylesheet source to CSS blocks dictionary.<br/>
        /// The CSS blocks are organized into two level buckets of media type and class name.<br/>
        /// Root media type are found under 'all' bucket.<br/>
        /// If <paramref name="combineWithDefault"/> is true the parsed css blocks are added to the 
        /// default css data (as defined by W3), merged if class name already exists. If false only the data in the given stylesheet is returned.
        /// </summary>
        /// <seealso href="http://www.w3.org/TR/CSS21/sample.html">CSS 2.1 default stylesheet for HTML</seealso>
        /// <param name="stylesheet">raw css stylesheet to parse</param>
        /// <param name="combineWithDefault">true - combine the parsed css data with default css data, false - return only the parsed css data</param>
        /// <returns>the CSS data with parsed CSS objects (never null)</returns>
        public async Task<CssData> ParseStyleSheet(string stylesheet, bool combineWithDefault)
        {
            var cssData = combineWithDefault ? await _adapter.GetDefaultCssData() : new CssData();

            if (!string.IsNullOrEmpty(stylesheet))
            {
                await ParseStyleSheet(cssData, stylesheet);
            }

            return cssData;
        }

        public static Stylesheet ParseStyleSheet(string stylesheet)
        {
            StylesheetParser parser = new();
            return parser.Parse(stylesheet);
        }

        /// <summary>
        /// Parse the given stylesheet source to CSS blocks dictionary.<br/>
        /// The CSS blocks are organized into two level buckets of media type and class name.<br/>
        /// Root media type are found under 'all' bucket.<br/>
        /// The parsed css blocks are added to the given css data, merged if class name already exists.
        /// </summary>
        /// <param name="cssData">the CSS data to fill with parsed CSS objects</param>
        /// <param name="stylesheet">raw css stylesheet to parse</param>
        /// <param name="baseUri">
        /// The resolved absolute URI this stylesheet was loaded from (e.g. a &lt;link&gt; href), so that
        /// relative references inside it (nested <c>@import</c>, <c>@font-face src</c>) resolve against
        /// its own location rather than the document's base. Null for inline &lt;style&gt; content or
        /// caller-supplied CSS text, which resolve against the document base as before.
        /// </param>
        public async Task ParseStyleSheet(CssData cssData, string stylesheet, RUri? baseUri = null)
        {
            if (!string.IsNullOrEmpty(stylesheet))
            {
                await ParseStyle(cssData, stylesheet, baseUri, new HashSet<string>());
            }
        }

        /// <summary>
        /// Parses a color value in CSS style; e.g. #ff0000, red, rgb(255,0,0), rgb(100%, 0, 0) 
        /// </summary>
        /// <param name="colorStr">color string value to parse</param>
        /// <returns>color value</returns>
        public RColor ParseColor(string colorStr)
        {
            return _valueParser.GetActualColor(colorStr);
        }

        public bool IsColorValid(string colorValue)
        {
            return _valueParser.IsColorValid(colorValue);
        }

        private async Task ParseStyle(CssData data, string stylesheetText, RUri? baseUri, HashSet<string> visitedImportUris)
        {
            var stylesheet = ParseStyleSheet(stylesheetText);
            stylesheet.BaseUri = baseUri;

            // Guards against circular @import chains (A imports B imports A) recursing forever; also
            // stops a stylesheet from importing itself directly.
            if (baseUri is not null)
            {
                visitedImportUris.Add(baseUri.AbsoluteUri);
            }

            var hasReachedNonImportRules = false;

            foreach (var rule in stylesheet.Children)
            {
                if (rule is IImportRule importRule && !hasReachedNonImportRules)
                {
                    if (importRule.Href == null) continue;

                    if (_htmlContainer is null)
                    {
                        throw new HtmlRenderException("Cannot import stylesheet without html container", HtmlRenderErrorType.CssParsing);
                    }

                    // Relative references inside an already-loaded stylesheet resolve against that
                    // stylesheet's own location, not the document's base.
                    var (importedContent, importedUri) = await StylesheetLoadHandler.LoadStylesheet(_htmlContainer, importRule.Href, baseUri);

                    if (importedContent is not null && (importedUri is null || visitedImportUris.Add(importedUri.AbsoluteUri)))
                    {
                        await ParseStyle(data, importedContent, importedUri, visitedImportUris);
                    }
                }
                else
                {
                    hasReachedNonImportRules = true;
                }
            }

            data.Stylesheets.Add(stylesheet);
        }
    }
}