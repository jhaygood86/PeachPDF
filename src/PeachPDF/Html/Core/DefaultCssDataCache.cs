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

using PeachDrawing.Abstractions;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core
{
    /// <summary>
    /// Caches the parsed W3 default (user-agent) stylesheet. Lives outside <see cref="RenderContext"/> because
    /// parsing a stylesheet is CSS-engine knowledge, not something a generic render context should own -
    /// <see cref="RenderContext"/> is destined to become part of a standalone rendering-abstraction package with
    /// no CSS concept at all.
    /// </summary>
    /// <remarks>
    /// Shared process-wide rather than per-instance. A fresh <see cref="RenderContext"/> is constructed per
    /// document/test in this codebase (see
    /// <c>.claude/recent-fixes/2026-09-12-pdfsharpadapter-reuses-cached-system-font-names.md</c>), and
    /// <see cref="CssDefaults.DefaultStyleSheet"/> is a fixed ~11.5KB constant, identical regardless of
    /// which adapter parses it, so re-tokenizing and re-parsing it per instance was the same shape of bug
    /// already fixed for system font checksums/bytes (see
    /// <c>.claude/recent-fixes/2026-09-13-font-checksum-recomputed-on-every-lookup-even-cache-hits.md</c>):
    /// a per-test cost for something that's actually process-invariant. Safe to share across every caller:
    /// <see cref="CssData.Clone"/> is a shallow copy, and <c>DomParser.CloneCssData</c> already
    /// clones-before-mutate the first time any document adds its own author (&lt;style&gt;/&lt;link&gt;)
    /// rules on top of this - so nothing ever mutates this shared instance's <c>Stylesheets</c> list in
    /// place.
    /// </remarks>
    internal static class DefaultCssDataCache
    {
        private static Task<CssData>? _defaultCssDataTask;
        private static readonly object _defaultCssDataLock = new();

        /// <summary>
        /// Get the default CSS stylesheet data, parsed (via <paramref name="adapter"/>, for any
        /// <c>@import</c> resource resolution it needs) once per process.
        /// </summary>
        public static Task<CssData> GetAsync(RenderContext adapter)
        {
            if (_defaultCssDataTask is not null)
                return _defaultCssDataTask;

            lock (_defaultCssDataLock)
            {
                _defaultCssDataTask ??= CreateAsync(adapter);
            }
            return _defaultCssDataTask;
        }

        private static async Task<CssData> CreateAsync(RenderContext adapter)
        {
            var defaultCssData = await CssData.Parse(adapter, CssDefaults.DefaultStyleSheet, false);
            foreach (var s in defaultCssData.Stylesheets)
                s.IsUserAgent = true;
            return defaultCssData;
        }
    }
}
