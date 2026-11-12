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

using System;
using System.Collections.Generic;
using System.Text;

namespace PeachBrackets.Html.Parse
{
    /// <summary>
    /// Resolves CSS <c>var(...)</c> custom-property references in a value string against an
    /// <see cref="ICssDomNode"/>'s cascaded <see cref="ICssDomNode.CustomProperties"/>. Extracted from the
    /// HTML cascade (<see cref="DomParser"/>) so both it and the SVG subsystem share one resolver instead
    /// of duplicating the (quote-aware, paren-depth-aware, cycle-detecting) substitution logic. Custom
    /// properties are stored raw (they may themselves contain <c>var()</c>), so resolution is graph-based
    /// and memoized via a caller-owned <c>resolvedCache</c>/<c>resolving</c>/<c>cyclic</c> triple.
    /// </summary>
    internal static class CssVarResolver
    {
        /// <summary>
        /// Result of attempting to resolve every var() reference in a declaration's value. Success is false
        /// only when a reference is "guaranteed-invalid" (no matching custom property, no fallback, or a
        /// cyclic reference) — per spec this invalidates the whole value, not just the failing substring.
        /// </summary>
        internal readonly record struct VarResolution(bool Success, string? Value);

        /// <summary>
        /// Optional <c>@property</c> registry context: supplies a registered custom property's
        /// <c>initial-value</c> when the property isn't set on the node, and validates a set value against the
        /// registered <c>syntax</c> (a mismatch is invalid at computed-value time and also falls back to the
        /// initial-value). Null on paths with no registry (e.g. standalone SVG), which simply skip both.
        /// </summary>
        internal sealed record VarContext(IReadOnlyDictionary<string, RegisteredProperty> Registered, CssValueParser ValueParser);

        /// <summary>
        /// Single-shot convenience: resolves every <c>var()</c> in <paramref name="value"/> against
        /// <paramref name="node"/>'s custom properties, returning the substituted string, or null if the
        /// value is guaranteed-invalid. Used where no cross-declaration cache sharing is needed (e.g. the
        /// SVG tree builder resolving one paint/style value at a time). Pass <paramref name="context"/> to
        /// honor <c>@property</c> registrations (initial-value fallback + syntax validation) on this path.
        /// </summary>
        internal static string? Resolve(ICssDomNode node, string value, VarContext? context = null)
        {
            var result = Substitute(node, value, [], [], [], context);
            return result.Success ? result.Value : null;
        }

        /// <summary>
        /// Resolves every var(...) occurrence in <paramref name="value"/> to plain text. Quote-aware (so
        /// `content: "var(--x)"` is left untouched) and paren-depth-aware (so a fallback containing nested
        /// commas, e.g. `var(--a, var(--b, red))` or a fallback with a comma-taking function, splits correctly).
        /// The <c>resolvedCache</c>/<c>resolving</c>/<c>cyclic</c> triple is caller-owned so multiple
        /// declarations on the same node share cycle detection.
        /// </summary>
        internal static VarResolution Substitute(ICssDomNode node, string value, Dictionary<string, string> resolvedCache, HashSet<string> resolving, HashSet<string> cyclic, VarContext? context = null)
        {
            if (!NeedsSubstitution(value))
                return new VarResolution(true, value);

            var sb = new StringBuilder();
            var i = 0;
            while (i < value.Length)
            {
                if (TryMatchVarCall(value, i, out var argsStart, out var callEnd))
                {
                    var (name, fallback) = SplitFirstTopLevelComma(value, argsStart, callEnd - 1);

                    if (TryResolveCustomProperty(node, name, resolvedCache, resolving, cyclic, context, out var found))
                    {
                        sb.Append(found);
                    }
                    else if (fallback != null)
                    {
                        var fallbackResult = Substitute(node, fallback, resolvedCache, resolving, cyclic, context);
                        if (!fallbackResult.Success) return new VarResolution(false, null);
                        sb.Append(fallbackResult.Value);
                    }
                    else
                    {
                        return new VarResolution(false, null); // guaranteed-invalid
                    }

                    i = callEnd;
                }
                else if (TryMatchCall(value, i, EnvCall, out var envArgs, out var envEnd))
                {
                    // env(<name>, <fallback>?): the user-agent environment variables. A paged document has no
                    // safe area, title bar or on-screen keyboard, so every defined name is its initial 0px.
                    var (envName, envFallback) = SplitFirstTopLevelComma(value, envArgs, envEnd - 1);
                    if (IsEnvironmentVariable(envName))
                    {
                        sb.Append("0px");
                    }
                    else if (envFallback != null)
                    {
                        var fallbackResult = Substitute(node, envFallback, resolvedCache, resolving, cyclic, context);
                        if (!fallbackResult.Success) return new VarResolution(false, null);
                        sb.Append(fallbackResult.Value);
                    }
                    else
                    {
                        return new VarResolution(false, null);
                    }

                    i = envEnd;
                }
                else if (TryMatchCall(value, i, SiblingIndexCall, out var indexArgs, out var indexEnd) && IsBlank(value, indexArgs, indexEnd - 1))
                {
                    sb.Append(SiblingPosition(node).Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    i = indexEnd;
                }
                else if (TryMatchCall(value, i, SiblingCountCall, out var countArgs, out var countEnd) && IsBlank(value, countArgs, countEnd - 1))
                {
                    sb.Append(SiblingPosition(node).Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    i = countEnd;
                }
                else if (value[i] is '"' or '\'')
                {
                    var quoteEnd = SkipQuotedString(value, i);
                    sb.Append(value, i, quoteEnd - i);
                    i = quoteEnd;
                }
                else
                {
                    sb.Append(value[i]);
                    i++;
                }
            }

            return new VarResolution(true, sb.ToString());
        }

        private const string EnvCall = "env(";
        private const string SiblingIndexCall = "sibling-index(";
        private const string SiblingCountCall = "sibling-count(";

        /// <summary>
        /// Whether a declaration value holds a function that is substituted as text at computed-value time
        /// (<c>var()</c>, <c>env()</c>, <c>sibling-index()</c>, <c>sibling-count()</c>).
        /// </summary>
        internal static bool NeedsSubstitution(string value) =>
            value.Contains("var(", StringComparison.OrdinalIgnoreCase) ||
            value.Contains(EnvCall, StringComparison.OrdinalIgnoreCase) ||
            value.Contains("sibling-", StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether <paramref name="value"/> uses <c>sibling-index()</c> or <c>sibling-count()</c>, which make a style depend on the element's position.</summary>
        internal static bool UsesSiblingPosition(string value) =>
            value.Contains(SiblingIndexCall, StringComparison.OrdinalIgnoreCase) ||
            value.Contains(SiblingCountCall, StringComparison.OrdinalIgnoreCase);

        private static bool IsEnvironmentVariable(string name) => name.ToLowerInvariant() is
            "safe-area-inset-top" or "safe-area-inset-right" or "safe-area-inset-bottom" or "safe-area-inset-left" or
            "safe-area-max-inset-top" or "safe-area-max-inset-right" or "safe-area-max-inset-bottom" or "safe-area-max-inset-left" or
            "titlebar-area-x" or "titlebar-area-y" or "titlebar-area-width" or "titlebar-area-height" or
            "keyboard-inset-top" or "keyboard-inset-right" or "keyboard-inset-bottom" or "keyboard-inset-left" or
            "keyboard-inset-width" or "keyboard-inset-height";

        private static bool IsBlank(string value, int start, int end)
        {
            for (var i = start; i < end; i++)
                if (!char.IsWhiteSpace(value[i])) return false;
            return true;
        }

        /// <summary>The 1-based index of <paramref name="node"/> among its parent's element children, and how many there are.</summary>
        private static (int Index, int Count) SiblingPosition(ICssDomNode node)
        {
            if (node.Parent is not { } parent) return (1, 1);

            var index = 0;
            var count = 0;
            foreach (var child in parent.Children)
            {
                if (child.TagName is null) continue;
                count++;
                if (index == 0 && child.Equals(node)) index = count;
            }

            return index == 0 ? (1, Math.Max(count, 1)) : (index, count);
        }

        /// <summary>
        /// Recursive + memoized: resolves node.CustomProperties[name]'s own var() references first (so a custom
        /// property may reference another custom property, in any declaration order), using `resolving` as a
        /// visited-set for cycle detection across the whole reference graph for this node.
        /// <paramref name="cyclic"/> permanently marks a property that was found to (directly or transitively)
        /// reference itself. This is distinct from a plain "not found" — per the CSS spec, a property that
        /// references itself is guaranteed-invalid regardless of any fallback written inside that same
        /// reference (e.g. `--self: var(--self, red);` must NOT resolve to "red": writing var(--self, ...)
        /// inside --self's own definition is a self-reference, full stop, matching real browsers). Without
        /// this permanent marker, the fallback used to locally satisfy the mid-cycle lookup would get cached
        /// as if it were --self's legitimately resolved value.
        /// </summary>
        private static bool TryResolveCustomProperty(ICssDomNode node, string name, Dictionary<string, string> resolvedCache, HashSet<string> resolving, HashSet<string> cyclic, VarContext? context, out string? value)
        {
            if (cyclic.Contains(name))
            {
                value = null;
                return false;
            }

            if (resolvedCache.TryGetValue(name, out value)) return true;

            RegisteredProperty? registered = null;
            context?.Registered.TryGetValue(name, out registered);

            if (node.CustomProperties == null || !node.CustomProperties.TryGetValue(name, out var rawValue))
            {
                // Not set on this node. A registered property (typed or universal) falls back to its
                // initial-value (CSS Properties & Values API §2.2). Otherwise it is guaranteed-invalid.
                return TryUseInitialValue(node, name, registered, resolvedCache, resolving, cyclic, context, out value);
            }

            if (!resolving.Add(name))
            {
                cyclic.Add(name); // name is referenced while already being resolved — a cycle
                value = null;
                return false;
            }

            var result = Substitute(node, rawValue, resolvedCache, resolving, cyclic, context);
            resolving.Remove(name);

            if (cyclic.Contains(name) || !result.Success)
            {
                cyclic.Add(name);
                value = null;
                return false;
            }

            // A set value that doesn't match the registered syntax is invalid at computed-value time and
            // falls back to the initial-value (CSS Properties & Values API §2.2 "computationally invalid").
            if (registered is not null && context is not null && !registered.Accepts(result.Value!, context.ValueParser))
                return TryUseInitialValue(node, name, registered, resolvedCache, resolving, cyclic, context, out value);

            resolvedCache[name] = value = result.Value!;
            return true;
        }

        /// <summary>
        /// Resolves a registered property's <c>initial-value</c> (itself run through substitution for safety)
        /// and caches it under <paramref name="name"/>. Returns false (guaranteed-invalid) when there is no
        /// registration, no initial-value, or the initial-value itself fails to resolve.
        /// </summary>
        private static bool TryUseInitialValue(ICssDomNode node, string name, RegisteredProperty? registered, Dictionary<string, string> resolvedCache, HashSet<string> resolving, HashSet<string> cyclic, VarContext? context, out string? value)
        {
            if (registered?.InitialValue is { } initial)
            {
                var initialResult = Substitute(node, initial, resolvedCache, resolving, cyclic, context);
                if (initialResult.Success)
                {
                    resolvedCache[name] = value = initialResult.Value!;
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Matches a case-insensitive "var(" starting at <paramref name="start"/> and scans forward for the
        /// matching close paren. On success, <paramref name="argsStart"/> is the index of the first argument
        /// character and <paramref name="callEnd"/> is the index just past the closing paren.
        /// </summary>
        private static bool TryMatchVarCall(string value, int start, out int argsStart, out int callEnd) =>
            TryMatchCall(value, start, "var(", out argsStart, out callEnd);

        /// <summary>
        /// <see cref="TryMatchVarCall"/> for any function whose name (with its open paren) is <paramref name="prefix"/>.
        /// A match must start a function name, so <c>xenv(</c> is not an <c>env(</c> call.
        /// </summary>
        private static bool TryMatchCall(string value, int start, string prefix, out int argsStart, out int callEnd)
        {
            argsStart = 0;
            callEnd = 0;

            var length = prefix.Length;
            if (start + length > value.Length) return false;
            if (string.Compare(value, start, prefix, 0, length, StringComparison.OrdinalIgnoreCase) != 0) return false;
            if (start > 0 && (char.IsLetterOrDigit(value[start - 1]) || value[start - 1] is '-' or '_')) return false;

            var depth = 1;
            var i = start + length;
            while (i < value.Length)
            {
                var c = value[i];
                if (c is '"' or '\'')
                {
                    i = SkipQuotedString(value, i);
                    continue;
                }

                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        argsStart = start + length;
                        callEnd = i + 1;
                        return true;
                    }
                }

                i++;
            }

            return false;
        }

        /// <summary>
        /// Splits a var() argument list at the first top-level (paren-depth 0, outside quotes) comma.
        /// The text before it is the custom property name (trimmed); everything after — commas and all —
        /// is the fallback, per spec.
        /// </summary>
        private static (string Name, string? Fallback) SplitFirstTopLevelComma(string value, int start, int end)
        {
            var depth = 0;
            var i = start;
            while (i < end)
            {
                var c = value[i];
                if (c is '"' or '\'')
                {
                    i = SkipQuotedString(value, i);
                    continue;
                }

                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    var name = value[start..i].Trim();
                    var fallback = value[(i + 1)..end].Trim();
                    return (name, fallback.Length > 0 ? fallback : null);
                }

                i++;
            }

            return (value[start..end].Trim(), null);
        }

        /// <summary>
        /// Advances past a quoted string literal starting at <paramref name="start"/> (which must point at the
        /// opening quote), honoring backslash escapes so an escaped quote doesn't end the string early.
        /// Returns the index just past the closing quote (or the string's end, if unterminated).
        /// </summary>
        private static int SkipQuotedString(string value, int start)
        {
            var quote = value[start];
            var i = start + 1;
            while (i < value.Length)
            {
                if (value[i] == '\\' && i + 1 < value.Length)
                {
                    i += 2;
                    continue;
                }

                if (value[i] == quote)
                    return i + 1;

                i++;
            }

            return i;
        }
    }
}
