using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// The <c>first</c>/<c>start</c>/<c>last</c>/<c>first-except</c> selection rules css-gcpm-3 defines
    /// identically for <c>string()</c> and <c>element()</c> - "which candidate, among every one
    /// registered under a given name, is current for a given page" - factored into one generic
    /// implementation both <see cref="MarginBoxRenderer.ResolveNamedString"/> (candidates: <see cref="Entities.NamedString"/>)
    /// and margin-box layout's running-element resolution (candidates: <see cref="Entities.RunningElement"/>)
    /// call, rather than each re-deriving the same four-way switch over a differently-typed candidate list.
    /// </summary>
    internal static class RunningSelectionEngine
    {
        /// <param name="candidates">The full document-order candidate list (every registered instance of
        /// every name, not pre-filtered to <paramref name="name"/>).</param>
        /// <param name="name">The name to select among.</param>
        /// <param name="keyword">One of <c>first</c>/<c>start</c>/<c>last</c>/<c>first-except</c>
        /// (case-insensitive), defaulting to the first candidate registered under <paramref name="name"/>
        /// for any other value - matching how an unrecognized <c>string()</c> keyword already degrades.</param>
        /// <param name="currentPageIndex">The pagination slot of the page being resolved for.</param>
        /// <param name="nameOf">Projects a candidate's name.</param>
        /// <param name="yOf">Projects a candidate's document Y.</param>
        /// <param name="pageIndexOf">Maps a document Y to the pagination slot that owns it - always
        /// <see cref="HtmlContainerInt.SlotStartingAt"/> in production, so every candidate is attributed to
        /// a page via the exact same authoritative table <paramref name="currentPageIndex"/> itself was
        /// derived from (see <see cref="MarginBoxRenderer.ResolveNamedString"/>'s own doc comment for why
        /// this - not a raw-Y window - is what makes page-boundary attribution unambiguous).</param>
        internal static T? SelectByKeyword<T>(
            IReadOnlyList<T> candidates,
            string name,
            string keyword,
            int currentPageIndex,
            Func<T, string> nameOf,
            Func<T, double> yOf,
            Func<double, int> pageIndexOf)
            where T : class
        {
            bool IsNamed(T c) => nameOf(c) == name;
            int PageOf(T c) => pageIndexOf(yOf(c));

            return keyword.ToLowerInvariant() switch
            {
                // last assignment that started before this page (running header)
                "start" => candidates.LastOrDefault(c => IsNamed(c) && PageOf(c) < currentPageIndex),
                // first assignment on this page, else the entry value
                "first" => candidates.FirstOrDefault(c => IsNamed(c) && PageOf(c) == currentPageIndex)
                    ?? candidates.LastOrDefault(c => IsNamed(c) && PageOf(c) < currentPageIndex),
                // last assignment on this page, else the entry value
                "last" => candidates.LastOrDefault(c => IsNamed(c) && PageOf(c) == currentPageIndex)
                    ?? candidates.LastOrDefault(c => IsNamed(c) && PageOf(c) < currentPageIndex),
                // identical to "first", except empty on the page where the assignment happens
                "first-except" => candidates.Any(c => IsNamed(c) && PageOf(c) == currentPageIndex)
                    ? null
                    : candidates.LastOrDefault(c => IsNamed(c) && PageOf(c) < currentPageIndex),
                _ => candidates.FirstOrDefault(IsNamed),
            };
        }
    }
}
