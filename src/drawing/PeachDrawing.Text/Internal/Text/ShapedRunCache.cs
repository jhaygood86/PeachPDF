using PeachDrawing.Text.Shaping;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace PeachDrawing.Text.Internal.Text
{
    /// <summary>
    /// Shaped runs of one <see cref="Typeface"/>, kept so a word shaped once is not shaped again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Text is shaped far more often than it is different: a layout engine measures a word to place it, measures it again to
    /// size its line, and a painter shapes it again to draw it and a third time to build the font's character map, so a
    /// document shapes each word several times in a row, and a word that recurs shapes again at every recurrence. Shaping is a
    /// pure function of the face, the text and the settings, and a <see cref="GlyphRun"/> is immutable, so every one of those
    /// can share one result.
    /// </para>
    /// <para>
    /// Only settings that carry no per-call collections are cached. Joining forms, syllable categories and explicit features
    /// are lists a caller builds for a run and compares by reference, so a run shaped with them could never hit and would only
    /// fill the cache with keys nothing can find again; those are shaped every time, as before.
    /// </para>
    /// <para>
    /// The cache is bounded by entry count and emptied whole when it fills, rather than evicting one at a time: it holds words
    /// and a document's working set of them is small next to the bound, so the cost of starting over is a few misses, and no
    /// recency bookkeeping sits on the path every shaped word takes.
    /// </para>
    /// </remarks>
    internal sealed class ShapedRunCache
    {
        /// <summary>How many runs one typeface keeps before it starts over.</summary>
        internal const int Capacity = 1 << 14;

        private readonly ConcurrentDictionary<Key, GlyphRun> _runs = new();
        private int _count;

        /// <summary>How many runs are held, approximately: it is not synchronized with the dictionary.</summary>
        internal int Count => Volatile.Read(ref _count);

        /// <summary>Whether a run shaped with <paramref name="settings"/> can ever be found in the cache again.</summary>
        internal static bool IsCacheable(in ShapeSettings settings) =>
            settings.ExplicitFeatures is not { Count: > 0 }
            && settings.JoiningForms is not { Count: > 0 }
            && settings.UseCategories is not { Count: > 0 }
            && settings.KhmerCategories is not { Count: > 0 };

        /// <summary>The cached run for the text and settings, or null when there is none.</summary>
        internal GlyphRun? Find(string text, in ShapeSettings settings) =>
            _runs.TryGetValue(new Key(text, settings), out var run) ? run : null;

        /// <summary>Keeps <paramref name="run"/> as the result of shaping the text with the settings, and returns the run to use.</summary>
        internal GlyphRun Keep(string text, in ShapeSettings settings, GlyphRun run)
        {
            if (Volatile.Read(ref _count) >= Capacity)
            {
                _runs.Clear();
                Volatile.Write(ref _count, 0);
            }

            var key = new Key(text, settings);

            if (_runs.TryAdd(key, run))
            {
                Interlocked.Increment(ref _count);
                return run;
            }

            // Another thread shaped the same text first; use its run so both callers hold one.
            return _runs.TryGetValue(key, out var existing) ? existing : run;
        }

        private readonly record struct Key(string Text, ShapeSettings Settings);
    }
}
