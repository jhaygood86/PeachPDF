using System;
using System.Collections.Generic;

namespace PeachBrackets.Css
{
    /// <summary>
    /// The functions whose value is substituted as text when a declaration's computed value is formed
    /// rather than parsed as a typed value: <c>var()</c>, <c>env()</c>, <c>sibling-index()</c> and
    /// <c>sibling-count()</c>. A declaration holding one cannot be validated (or split from a shorthand)
    /// until the cascade has the element in hand, so the parse layer defers it as it does <c>var()</c>.
    /// </summary>
    internal static class SubstitutionFunctions
    {
        /// <summary>Whether declaration text contains one of the substituted functions.</summary>
        public static bool InText(string value) =>
            value.Contains("var(", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("env(", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("sibling-", StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether a token list contains (at any depth) one of the substituted functions.</summary>
        public static bool InTokens(IReadOnlyList<Token> tokens) =>
            tokens.ContainsFunction(FunctionNames.Var) ||
            tokens.ContainsFunction(FunctionNames.Env) ||
            tokens.ContainsFunction(FunctionNames.SiblingIndex) ||
            tokens.ContainsFunction(FunctionNames.SiblingCount);
    }
}
