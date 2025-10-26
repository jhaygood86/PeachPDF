using PeachPDF.CSS;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System.Linq;
using System.Text;

namespace PeachPDF.Html.Core.Dom
{
    internal static class CssContentEngine
    {
        public static void ApplyContent(CssBox cssBox)
        {
            if (cssBox.Content is CssConstants.None or CssConstants.Normal)
            {
                return;
            }

            var tokens = CssValueParser.GetCssTokens(cssBox.Content);

            var contentText = new StringBuilder();

            foreach (var token in tokens)
            {
                switch (token)
                {
                    case StringToken stringToken:
                        contentText.Append(stringToken.Data);
                        break;
                    case FunctionToken { Data: CssConstants.Counter } functionToken:
                        {
                            var arguments = functionToken.ArgumentTokens.ToArray();

                            var counterName = (KeywordToken)arguments[0];
                            var counter = CssCounterEngine.GetCounter(cssBox, counterName.Data);

                            var counterValue = counter?.Value ?? 1;

                            if (arguments.Length is 1)
                            {
                                contentText.Append(counterValue);
                            }

                            break;
                        }
                    case FunctionToken { Data: "content" } contentFunctionToken:
                        {
                            var contentValue = ExtractContentValue(cssBox, contentFunctionToken);
                            if (!string.IsNullOrEmpty(contentValue))
                            {
                                contentText.Append(contentValue);
                            }
                            break;
                        }
                    case FunctionToken { Data: "string" } stringFunctionToken:
                        {
                            var stringValue = ExtractStringValue(cssBox, stringFunctionToken);
                            if (!string.IsNullOrEmpty(stringValue))
                            {
                                contentText.Append(stringValue);
                            }
                            break;
                        }
                    case FunctionToken { Data: "attr" } attrFunctionToken:
                        {
                            // Handle attr() function
                            if (attrFunctionToken.ArgumentTokens.Any())
                            {
                                var attrNameToken = attrFunctionToken.ArgumentTokens.FirstOrDefault();
                                if (attrNameToken is KeywordToken keywordToken)
                                {
                                    var attrName = keywordToken.Data;
                                    // Get attribute from parent element if this is a pseudo-element
                                    var sourceBox = cssBox.IsPseudoElement && cssBox.ParentBox != null
                                        ? cssBox.ParentBox
                                        : cssBox;
                                    var attrValue = sourceBox.GetAttribute(attrName, "");
                                    if (!string.IsNullOrEmpty(attrValue))
                                    {
                                        contentText.Append(attrValue);
                                    }
                                }
                            }
                            break;
                        }
                }
            }

            cssBox.Text = contentText.ToString();
        }

        private static string? ExtractStringValue(CssBox cssBox, FunctionToken stringFunctionToken)
        {
            var arguments = stringFunctionToken.ArgumentTokens
   .Where(t => t.Type != TokenType.Comma && t.Type != TokenType.Whitespace)
        .ToArray();

            if (arguments.Length == 0)
            {
                return null;
            }

            // First argument is the named string identifier
            if (arguments[0] is not KeywordToken nameToken)
            {
                return null;
            }

            var stringName = nameToken.Data;

            // Second argument is the optional keyword (first, start, last, first-except)
            // Default is "first"
            var keyword = "first";
            if (arguments.Length > 1 && arguments[1] is KeywordToken keywordToken)
            {
                keyword = keywordToken.Data.ToLowerInvariant();
            }

            // Use the CssNamedStringEngine to retrieve the named string
            return GetNamedStringValue(cssBox, stringName, keyword);
        }

        private static string? GetNamedStringValue(CssBox cssBox, string name, string keyword)
        {
            // Get document-level named strings from the HTML container
            if (cssBox.HtmlContainer != null)
            {
                var documentStrings = cssBox.HtmlContainer.NamedStrings;

                // Filter to only this named string
                NamedString? firstMatch = null;
                NamedString? lastMatch = null;

                foreach (var namedString in documentStrings)
                {
                    if (namedString.Name == name)
                    {
                        if (firstMatch == null)
                        {
                            firstMatch = namedString;
                        }
                        lastMatch = namedString;
                    }
                }

                // Apply keyword logic
                return keyword switch
                {
                    "first" => firstMatch?.Value ?? string.Empty,
                    "start" => firstMatch?.Value ?? string.Empty, // TODO: Implement proper start logic (first on page)
                    "last" => lastMatch?.Value ?? string.Empty,
                    "first-except" => string.Empty, // TODO: Implement proper first-except logic
                    _ => firstMatch?.Value ?? string.Empty
                };
            }

            // Fallback to tree-based search if no container
            var box = cssBox;
            NamedString? nearestAssignment = null;
            NamedString? farthestAssignment = null;

            while (box != null)
            {
                if (box.NamedStrings.TryGetValue(name, out var namedString))
                {
                    if (nearestAssignment == null)
                    {
                        nearestAssignment = namedString;
                    }
                    farthestAssignment = namedString;
                }
                box = box.ParentBox;
            }

            return keyword switch
            {
                "first" => farthestAssignment?.Value ?? string.Empty,
                "start" => farthestAssignment?.Value ?? string.Empty,
                "last" => nearestAssignment?.Value ?? string.Empty,
                "first-except" => string.Empty,
                _ => farthestAssignment?.Value ?? string.Empty
            };
        }

        private static string? ExtractContentValue(CssBox cssBox, FunctionToken contentFunctionToken)
        {
            // Default mode is "text" if no argument provided
            var mode = "text";

            if (contentFunctionToken.ArgumentTokens.Any())
            {
                var argToken = contentFunctionToken.ArgumentTokens.FirstOrDefault();
                if (argToken is KeywordToken keywordToken)
                {
                    mode = keywordToken.Data.ToLowerInvariant();
                }
            }

            return mode switch
            {
                "text" => ExtractText(cssBox),
                "before" => ExtractPseudoElementContent(cssBox, isBeforePseudo: true),
                "after" => ExtractPseudoElementContent(cssBox, isBeforePseudo: false),
                "first-letter" => ExtractFirstLetter(cssBox),
                _ => null
            };
        }

        private static string? ExtractText(CssBox cssBox)
        {
            // Get the text content of the element (normalized whitespace)
            // If this is a pseudo-element, get the parent's text
            var sourceBox = cssBox.IsPseudoElement && cssBox.ParentBox != null
                ? cssBox.ParentBox
                : cssBox;

            return GetTextContent(sourceBox, excludePseudoElements: true);
        }

        private static string? ExtractPseudoElementContent(CssBox cssBox, bool isBeforePseudo)
        {
            // Find the pseudo-element box
            // If we're in a pseudo-element, look at the parent element's pseudo-elements
            var sourceBox = cssBox.IsPseudoElement && cssBox.ParentBox != null
                ? cssBox.ParentBox
                : cssBox;

            var pseudoElement = sourceBox.Boxes.FirstOrDefault(b =>
                isBeforePseudo ? b.IsBeforePseudoElement : b.IsAfterPseudoElement);

            if (pseudoElement == null)
            {
                return null;
            }

            // Extract the content value by evaluating the content property
            // This is similar to ApplyContent but returns the result instead of setting Text
            if (pseudoElement.Content is CssConstants.None or CssConstants.Normal)
            {
                return null;
            }

            var tokens = CssValueParser.GetCssTokens(pseudoElement.Content);
            var contentText = new StringBuilder();

            foreach (var token in tokens)
            {
                switch (token)
                {
                    case StringToken stringToken:
                        contentText.Append(stringToken.Data);
                        break;
                    case FunctionToken { Data: CssConstants.Counter } functionToken:
                        {
                            var arguments = functionToken.ArgumentTokens.ToArray();
                            var counterName = (KeywordToken)arguments[0];
                            var counter = CssCounterEngine.GetCounter(pseudoElement, counterName.Data);
                            var counterValue = counter?.Value ?? 1;

                            if (arguments.Length is 1)
                            {
                                contentText.Append(counterValue);
                            }
                            break;
                        }
                    case FunctionToken { Data: "attr" } attrFunctionToken:
                        {
                            if (attrFunctionToken.ArgumentTokens.Any())
                            {
                                var attrNameToken = attrFunctionToken.ArgumentTokens.FirstOrDefault();
                                if (attrNameToken is KeywordToken keywordToken)
                                {
                                    var attrName = keywordToken.Data;
                                    var targetBox = pseudoElement.IsPseudoElement && pseudoElement.ParentBox != null
                                        ? pseudoElement.ParentBox
                                        : pseudoElement;
                                    var attrValue = targetBox.GetAttribute(attrName, "");
                                    if (!string.IsNullOrEmpty(attrValue))
                                    {
                                        contentText.Append(attrValue);
                                    }
                                }
                            }
                            break;
                        }
                        // Note: We don't recursively process content() here to avoid infinite loops
                        // If a pseudo-element's content contains content(), it would have already
                        // been evaluated when we extracted it
                }
            }

            return contentText.Length > 0 ? contentText.ToString() : null;
        }

        private static string? ExtractFirstLetter(CssBox cssBox)
        {
            var text = ExtractText(cssBox);
            return string.IsNullOrEmpty(text) ? null : text.Substring(0, 1);
        }

        private static string? GetTextContent(CssBox box, bool excludePseudoElements)
        {
            if (!string.IsNullOrEmpty(box.Text))
            {
                return box.Text;
            }

            var textBuilder = new StringBuilder();
            foreach (var childBox in box.Boxes)
            {
                // Skip pseudo-elements when extracting regular text content
                if (excludePseudoElements && childBox.IsPseudoElement)
                {
                    continue;
                }

                var childText = GetTextContent(childBox, excludePseudoElements);
                if (!string.IsNullOrEmpty(childText))
                {
                    textBuilder.Append(childText);
                }
            }

            return textBuilder.Length > 0 ? textBuilder.ToString() : null;
        }
    }
}
