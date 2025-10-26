using PeachPDF.CSS;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using System.Linq;
using System.Text;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Engine for evaluating and applying CSS string-set properties.
    /// Handles named strings defined by the string-set property from CSS GCPM spec.
    /// </summary>
    internal static class CssNamedStringEngine
    {
        /// <summary>
        /// Applies string-set declarations to a CSS box by evaluating content-lists
        /// and storing the resulting named strings in the box's NamedStrings dictionary.
        /// </summary>
        /// <param name="cssBox">The CSS box to apply string-set to</param>
        public static void ApplyStringSet(CssBox cssBox)
        {
            if (cssBox.StringSet is "none")
            {
                return;
            }

            // Parse the string-set value into tokens
            var tokens = CssValueParser.GetCssTokens(cssBox.StringSet);

            string? currentName = null;
            var contentItems = new System.Collections.Generic.List<Token>();

            foreach (var token in tokens)
            {
                // Commas separate different name/content-list pairs
                if (token.Type == TokenType.Comma)
                {
                    if (currentName != null && contentItems.Count > 0)
                    {
                        // Evaluate and store the current named string
                        var value = EvaluateContentList(cssBox, contentItems);
                        var namedString = new NamedString(currentName, value);
                        cssBox.NamedStrings[currentName] = namedString;

                        // Register with document-level storage if container is available
                        if (cssBox.HtmlContainer != null)
                        {
                            cssBox.HtmlContainer.RegisterNamedString(namedString);
                        }
                    }

                    currentName = null;
                    contentItems.Clear();
                    continue;
                }

                // Skip whitespace
                if (token.Type == TokenType.Whitespace)
                {
                    continue;
                }

                // First identifier after comma (or at start) is the name
                if (currentName == null && token is KeywordToken keywordToken)
                {
                    currentName = keywordToken.Data;
                }
                else
                {
                    // Everything else is part of the content-list
                    contentItems.Add(token);
                }
            }

            // Process the last name/content-list pair
            if (currentName != null && contentItems.Count > 0)
            {
                var value = EvaluateContentList(cssBox, contentItems);
                var namedString = new NamedString(currentName, value);
                cssBox.NamedStrings[currentName] = namedString;

                // Register with document-level storage if container is available
                if (cssBox.HtmlContainer != null)
                {
                    cssBox.HtmlContainer.RegisterNamedString(namedString);
                }
            }
        }

        /// <summary>
        /// Evaluates a content-list (list of content items) into a string value.
        /// Supports: string literals, counter(), counters(), attr(), content(), string() functions.
        /// </summary>
        /// <param name="cssBox">The CSS box context for evaluation</param>
        /// <param name="contentItems">The tokens representing the content-list</param>
        /// <returns>The evaluated string value</returns>
        private static string EvaluateContentList(CssBox cssBox, System.Collections.Generic.List<Token> contentItems)
        {
            var result = new StringBuilder();

            foreach (var token in contentItems)
            {
                switch (token)
                {
                    case StringToken stringToken:
                        // Direct string literal
                        result.Append(stringToken.Data);
                        break;

                    case FunctionToken functionToken:
                        result.Append(EvaluateFunction(cssBox, functionToken));
                        break;
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Evaluates a function token (counter, counters, attr, content, string) into its string value.
        /// </summary>
        /// <param name="cssBox">The CSS box context for evaluation</param>
        /// <param name="functionToken">The function token to evaluate</param>
        /// <returns>The evaluated string value of the function</returns>
        private static string EvaluateFunction(CssBox cssBox, FunctionToken functionToken)
        {
            var functionName = functionToken.Data.ToLowerInvariant();

            switch (functionName)
            {
                case "counter":
                    return EvaluateCounterFunction(cssBox, functionToken);

                case "counters":
                    return EvaluateCountersFunction(cssBox, functionToken);

                case "attr":
                    return EvaluateAttrFunction(cssBox, functionToken);

                case "content":
                    return EvaluateContentFunction(cssBox, functionToken);

                case "string":
                    return EvaluateStringFunction(cssBox, functionToken);

                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Evaluates counter() function - returns the value of a named counter.
        /// Syntax: counter(name) or counter(name, style)
        /// </summary>
        private static string EvaluateCounterFunction(CssBox cssBox, FunctionToken functionToken)
        {
            var arguments = functionToken.ArgumentTokens.ToArray();

            if (arguments.Length == 0)
            {
                return string.Empty;
            }

            // First argument is the counter name
            if (arguments[0] is KeywordToken counterNameToken)
            {
                var counter = CssCounterEngine.GetCounter(cssBox, counterNameToken.Data);
                var counterValue = counter?.Value ?? 0;

                // TODO: Second argument would be the list-style (decimal, roman, etc.)
                // For now, just return the numeric value
                return counterValue.ToString();
            }

            return string.Empty;
        }

        /// <summary>
        /// Evaluates counters() function - returns all counter values with separator.
        /// Syntax: counters(name, separator) or counters(name, separator, style)
        /// </summary>
        private static string EvaluateCountersFunction(CssBox cssBox, FunctionToken functionToken)
        {
            var arguments = functionToken.ArgumentTokens
                .Where(t => t.Type != TokenType.Comma && t.Type != TokenType.Whitespace)
                  .ToArray();

            if (arguments.Length < 2)
            {
                return string.Empty;
            }

            // First argument is the counter name
            if (arguments[0] is not KeywordToken counterNameToken)
            {
                return string.Empty;
            }

            // Second argument is the separator string
            var separator = arguments[1] is StringToken separatorToken
              ? separatorToken.Data
                         : ".";

            // Collect all counter values in the scope chain
            var values = new System.Collections.Generic.List<int>();
            var counter = CssCounterEngine.GetCounter(cssBox, counterNameToken.Data);

            while (counter != null)
            {
                values.Insert(0, counter.Value); // Insert at beginning to get correct order
                counter = counter.ParentScope;
            }

            // TODO: Third argument would be the list-style (decimal, roman, etc.)
            // For now, just return the numeric values joined by separator
            return values.Count > 0
         ? string.Join(separator, values)
                  : "0";
        }

        /// <summary>
        /// Evaluates attr() function - returns the value of an element attribute.
        /// Syntax: attr(attribute-name)
        /// </summary>
        private static string EvaluateAttrFunction(CssBox cssBox, FunctionToken functionToken)
        {
            var arguments = functionToken.ArgumentTokens
                          .Where(t => t.Type != TokenType.Whitespace)
            .ToArray();

            if (arguments.Length == 0)
            {
                return string.Empty;
            }

            // First argument is the attribute name
            if (arguments[0] is KeywordToken attrNameToken)
            {
                var attributeName = attrNameToken.Data;
                return cssBox.GetAttribute(attributeName, string.Empty);
            }

            return string.Empty;
        }

        /// <summary>
        /// Evaluates content() function - returns element content based on mode.
        /// Syntax: content() or content(text) or content(before) or content(after) or content(first-letter)
        /// </summary>
        private static string EvaluateContentFunction(CssBox cssBox, FunctionToken functionToken)
        {
            var arguments = functionToken.ArgumentTokens
         .Where(t => t.Type != TokenType.Whitespace)
       .ToArray();

            // Default mode is "text"
            var mode = "text";

            if (arguments.Length > 0 && arguments[0] is KeywordToken modeToken)
            {
                mode = modeToken.Data.ToLowerInvariant();
            }

            return mode switch
            {
                "text" => GetElementText(cssBox),
                "before" => GetPseudoElementContent(cssBox, true),
                "after" => GetPseudoElementContent(cssBox, false),
                "first-letter" => GetFirstLetter(cssBox),
                _ => string.Empty
            };
        }

        /// <summary>
        /// Evaluates string() function - returns a named string value.
        /// Syntax: string(name) or string(name, keyword)
        /// Keywords: first (default), start, last, first-except
        /// </summary>
        private static string EvaluateStringFunction(CssBox cssBox, FunctionToken functionToken)
        {
            var arguments = functionToken.ArgumentTokens
             .Where(t => t.Type != TokenType.Comma && t.Type != TokenType.Whitespace)
       .ToArray();

            if (arguments.Length == 0)
            {
                return string.Empty;
            }

            // First argument is the named string identifier
            if (arguments[0] is not KeywordToken nameToken)
            {
                return string.Empty;
            }

            var stringName = nameToken.Data;

            // Second argument is the optional keyword (first, start, last, first-except)
            // Default is "first"
            var keyword = "first";
            if (arguments.Length > 1 && arguments[1] is KeywordToken keywordToken)
            {
                keyword = keywordToken.Data.ToLowerInvariant();
            }

            return GetNamedStringValue(cssBox, stringName, keyword);
        }

        /// <summary>
        /// Gets a named string value based on the specified keyword.
        /// </summary>
        /// <param name="cssBox">The CSS box context</param>
        /// <param name="name">The name of the string to retrieve</param>
        /// <param name="keyword">The keyword: first, start, last, or first-except</param>
        /// <returns>The named string value</returns>
        private static string GetNamedStringValue(CssBox cssBox, string name, string keyword)
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

            // Fallback to tree-based search if no container (shouldn't happen in normal flow)
            // This maintains backward compatibility
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

        /// <summary>
        /// Gets the text content of an element (normalized as if white-space: normal).
        /// </summary>
        private static string GetElementText(CssBox cssBox)
        {
            if (!string.IsNullOrEmpty(cssBox.Text))
            {
                // Normalize whitespace: collapse multiple spaces to single space
                return System.Text.RegularExpressions.Regex.Replace(
                 cssBox.Text.Trim(),
                     @"\s+",
                   " "
                         );
            }

            // If no direct text, collect text from child boxes
            var textBuilder = new StringBuilder();
            CollectTextFromChildren(cssBox, textBuilder);

            var text = textBuilder.ToString().Trim();
            return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        }

        /// <summary>
        /// Recursively collects text content from child boxes.
        /// </summary>
        private static void CollectTextFromChildren(CssBox box, StringBuilder builder)
        {
            foreach (var childBox in box.Boxes)
            {
                if (childBox.IsPseudoElement)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(childBox.Text))
                {
                    builder.Append(childBox.Text);
                    builder.Append(' ');
                }
                else
                {
                    CollectTextFromChildren(childBox, builder);
                }
            }
        }

        /// <summary>
        /// Gets the content of ::before or ::after pseudo-element.
        /// </summary>
        private static string GetPseudoElementContent(CssBox cssBox, bool isBefore)
        {
            // Look for pseudo-element boxes
            var pseudoBox = cssBox.Boxes.FirstOrDefault(b =>
     isBefore ? b.IsBeforePseudoElement : b.IsAfterPseudoElement);

            return pseudoBox != null ? GetElementText(pseudoBox) : string.Empty;
        }

        /// <summary>
        /// Gets the first letter of the element's text content.
        /// </summary>
        private static string GetFirstLetter(CssBox cssBox)
        {
            var text = GetElementText(cssBox);
            return text.Length > 0 ? text[0].ToString() : string.Empty;
        }

        /// <summary>
        /// Gets a named string value from the box or its ancestors.
        /// This can be used to retrieve named strings set by string-set.
        /// </summary>
        /// <param name="cssBox">The CSS box to search from</param>
        /// <param name="name">The name of the string to retrieve</param>
        /// <returns>The named string value, or empty string if not found</returns>
        public static string GetNamedString(CssBox cssBox, string name)
        {
            // Search up the box tree for the named string
            var box = cssBox;
            while (box != null)
            {
                if (box.NamedStrings.TryGetValue(name, out var namedString))
                {
                    return namedString.Value;
                }
                box = box.ParentBox;
            }

            return string.Empty;
        }
    }
}
