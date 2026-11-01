#nullable enable

using System.Collections.Generic;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;

namespace PeachBrackets.Css
{
    /// <summary>
    /// One node of a <c>&lt;container-condition&gt;</c> tree whose leaves are a mix of size features and <c>style()</c> queries
    /// (CSS Containment 3 section 3). A condition of only one kind is held as a <see cref="MediaList"/> or an
    /// <see cref="IStyleQueryCondition"/> instead; this tree exists for the rules that combine both with <c>and</c>/<c>or</c>/<c>not</c>.
    /// Evaluation is three-valued as in the spec: <c>null</c> is "unknown" (a leaf with no eligible container), which is falsy
    /// at the top and which <c>not</c> leaves unknown.
    /// </summary>
    internal interface IContainerQueryTerm
    {
        bool? Evaluate(CssBox node, string? containerName, ContainerQuerySizes? sizes);
    }

    internal sealed class ContainerStyleTerm(IStyleQueryCondition condition) : IContainerQueryTerm
    {
        public bool? Evaluate(CssBox node, string? containerName, ContainerQuerySizes? sizes) =>
            ContainerQueryMatcher.EvaluateStyle(condition, node, containerName);
    }

    internal sealed class ContainerSizeTerm(MediaList condition) : IContainerQueryTerm
    {
        public bool? Evaluate(CssBox node, string? containerName, ContainerQuerySizes? sizes) =>
            ContainerQueryMatcher.EvaluateSize(condition, node, containerName, sizes);
    }

    internal sealed class ContainerNotTerm(IContainerQueryTerm content) : IContainerQueryTerm
    {
        public bool? Evaluate(CssBox node, string? containerName, ContainerQuerySizes? sizes) =>
            !content.Evaluate(node, containerName, sizes);
    }

    internal sealed class ContainerAndTerm(List<IContainerQueryTerm> terms) : IContainerQueryTerm
    {
        public bool? Evaluate(CssBox node, string? containerName, ContainerQuerySizes? sizes)
        {
            bool? result = true;
            foreach (var term in terms)
            {
                var value = term.Evaluate(node, containerName, sizes);
                if (value == false) return false;
                if (value is null) result = null;
            }

            return result;
        }
    }

    internal sealed class ContainerOrTerm(List<IContainerQueryTerm> terms) : IContainerQueryTerm
    {
        public bool? Evaluate(CssBox node, string? containerName, ContainerQuerySizes? sizes)
        {
            bool? result = false;
            foreach (var term in terms)
            {
                var value = term.Evaluate(node, containerName, sizes);
                if (value == true) return true;
                if (value is null) result = null;
            }

            return result;
        }
    }
}
