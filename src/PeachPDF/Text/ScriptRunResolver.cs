using System.Collections.Generic;

namespace PeachPDF.Text
{
    /// <summary>
    /// Resolves <see cref="ScriptTable.Common"/>/<see cref="ScriptTable.Inherited"/> codepoints (shared
    /// punctuation/whitespace/digits, and combining marks that inherit their base character's script) to
    /// the actual surrounding script, per <see href="https://www.unicode.org/reports/tr24/#Common">UAX
    /// #24 §5.1</see> - "characters commonly used with more than one script&#8230; should be resolved
    /// into the script of the surrounding text". This is the run-level counterpart to
    /// <see cref="ScriptTable"/>'s own per-codepoint (unresolved) lookup: a GSUB script-tag decision
    /// needs to know the script driving a punctuation mark or diacritic between two Arabic letters is
    /// still <c>"Arabic"</c>, not <c>"Common"</c>/<c>"Inherited"</c> (which <see cref="OpenTypeScriptTags"/>
    /// deliberately has no tag for).
    /// </summary>
    internal static class ScriptRunResolver
    {
        /// <summary>
        /// Resolves each codepoint's raw <see cref="ScriptTable"/> value to its effective, run-resolved
        /// script: a real (non-<c>Common</c>/non-<c>Inherited</c>) value passes through unchanged; a
        /// <c>Common</c>/<c>Inherited</c> codepoint takes on the nearest preceding real script, or (for a
        /// leading run with no preceding real script yet - e.g. text that opens with punctuation) the
        /// nearest <em>following</em> one instead. A codepoint sequence with no real script anywhere
        /// (all punctuation/marks, or entirely unassigned codepoints) resolves every <c>Inherited</c>
        /// value to <see cref="ScriptTable.Common"/> and leaves <c>Common</c>/<see cref="ScriptTable.Unknown"/>
        /// as-is - there is nothing to resolve against.
        /// </summary>
        public static IReadOnlyList<string> Resolve(IReadOnlyList<int> codepoints)
        {
            var count = codepoints.Count;
            var raw = new string[count];
            for (var i = 0; i < count; i++)
                raw[i] = ScriptTable.Of(codepoints[i]);

            return ResolveRaw(raw);
        }

        /// <summary>Same as <see cref="Resolve(IReadOnlyList{int})"/>, taking already-looked-up raw
        /// <see cref="ScriptTable"/> values directly - lets a caller that already has them (e.g. reusing
        /// a per-codepoint script array computed for another purpose) skip the redundant lookup.</summary>
        public static IReadOnlyList<string> ResolveRaw(IReadOnlyList<string> raw)
        {
            var count = raw.Count;
            var resolved = new string[count];

            // Forward pass: every Common/Inherited codepoint takes on the nearest PRECEDING real script.
            string? current = null;
            for (var i = 0; i < count; i++)
            {
                var isCommonOrInherited = raw[i] == ScriptTable.Common || raw[i] == ScriptTable.Inherited;
                if (!isCommonOrInherited)
                    current = raw[i];

                resolved[i] = isCommonOrInherited ? current ?? raw[i] : raw[i];
            }

            // Backward-fill the leading run (before the first real script, if any) with the first real
            // script that appears - a string that OPENS with punctuation/marks still resolves them to
            // whatever script follows, rather than leaving them stranded as Common/Inherited.
            var firstRealIndex = -1;
            for (var i = 0; i < count; i++)
            {
                if (raw[i] != ScriptTable.Common && raw[i] != ScriptTable.Inherited)
                {
                    firstRealIndex = i;
                    break;
                }
            }

            if (firstRealIndex > 0)
            {
                var firstReal = raw[firstRealIndex];
                for (var i = 0; i < firstRealIndex; i++)
                    resolved[i] = firstReal;
            }
            else if (firstRealIndex < 0)
            {
                // No real script anywhere in this sequence - Inherited has nothing to inherit from, so
                // it collapses to Common (itself unresolved, but at least no longer claiming to have
                // inherited a script that was never there); Common/Unknown pass through unchanged.
                for (var i = 0; i < count; i++)
                    resolved[i] = raw[i] == ScriptTable.Inherited ? ScriptTable.Common : raw[i];
            }

            return resolved;
        }
    }
}
