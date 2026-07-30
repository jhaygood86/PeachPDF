using System;
using System.Collections.Generic;

namespace PeachPDF.Text.Bidi
{
    /// <summary>
    /// The Unicode Bidirectional Algorithm (UAX #9, https://www.unicode.org/reports/tr9/), implementing
    /// P2-P3 (paragraph level), X1-X8 (explicit levels/isolates - a full stack model, not a narrowed
    /// subset, since CSS's own <c>unicode-bidi: embed/isolate/bidi-override/isolate-override/plaintext</c>
    /// values are implemented as synthetic pushes onto this same stack via <see cref="BidiIsolateOverride"/>),
    /// X9/§5.2 (retaining BN/explicit-formatting characters rather than physically removing them, so string
    /// indices into the original text stay stable throughout), X10/BD13 (isolating run sequences), W1-W7
    /// (weak types), N0-N2 (neutral/bracket-pair types), I1-I2 (implicit levels), and L1 (embedding-level
    /// resets for separators and trailing whitespace/isolate-formatting runs). L2 (visual reordering) is
    /// exposed separately as <see cref="ReorderLine"/> and L1's clause 4 as
    /// <see cref="ApplyLineTrailingWhitespaceReset"/>, since both are inherently line-scoped operations that
    /// can only run once a layout engine has decided where lines break - <see cref="Resolve"/> itself
    /// produces paragraph-wide levels only.
    /// <para>
    /// Validated end to end against Unicode's own <c>BidiCharacterTest.txt</c> conformance suite (Unicode
    /// 17.0.0, ~91.7k cases covering P2-P3/X1-X9/X10/W1-W7/N0-N2/I1-I2/L1/L2) - see
    /// <c>BidiResolverConformanceTests</c> - with a 100% pass rate, including BD16's canonical-equivalence
    /// note (U+2329/U+232A treated as equivalent to U+3008/U+3009 for bracket-pairing purposes, see
    /// <see cref="CanonicalBracketForm"/>) and N0's sub-rule (c) (NSM characters immediately following a
    /// bracket N0 resolved take that bracket's type too, see <see cref="ApplyNsmAfterBracket"/>) - both
    /// found to matter for real conformance cases while validating, not just theoretical edge cases. The
    /// one known, deliberate simplification: BD16's 63-element bracket stack cap stops ALL further bracket
    /// pairing for the rest of the sequence once hit (matching the spec's own intent), rather than
    /// resuming after the overflow clears - correct behavior for the 64+-nested-brackets-on-one-line case
    /// the spec itself treats as a give-up condition.
    /// </para>
    /// </summary>
    internal static class BidiResolver
    {
        private const int MaxDepth = 125;

        private struct StackEntry
        {
            public byte Level;
            public BidiClass Override; // ON = no override in effect
            public bool IsolateInitiator;
        }

        public static BidiResolverResult Resolve(
            string text,
            BidiParagraphDirection paragraphDirection,
            IReadOnlyList<BidiIsolateOverride>? explicitOverrides = null)
        {
            var n = text.Length;
            var originalTypes = new BidiClass[n];
            FillOriginalTypes(text, originalTypes);

            var overrides = explicitOverrides ?? [];

            var paragraphLevel = paragraphDirection switch
            {
                BidiParagraphDirection.Ltr => (byte)0,
                BidiParagraphDirection.Rtl => (byte)1,
                _ => ComputeAutoParagraphLevel(originalTypes, 0, n)
            };

            var levels = new byte[n];
            var resolvedTypes = (BidiClass[])originalTypes.Clone();

            ResolveExplicitLevels(originalTypes, resolvedTypes, levels, overrides, paragraphLevel);

            var matchingPdi = ComputeMatchingPdi(resolvedTypes, n);
            var sequences = ComputeIsolatingRunSequences(resolvedTypes, levels, matchingPdi, n);

            foreach (var sequence in sequences)
            {
                ResolveSequence(text, resolvedTypes, levels, sequence, matchingPdi, n, paragraphLevel);
            }

            // §5.2's "retained BN/explicit-formatting character takes the level of the character before
            // it" guidance is only meaningful once that preceding character's FINAL level is known -
            // ResolveExplicitLevels could only assign the PRE-resolution (X-stage) level, since I1/I2 (via
            // ResolveSequence above) haven't run yet at that point. Re-stamp now so a retained character
            // never leaves a spurious level-run boundary between two significant characters that actually
            // resolved to the same final level (confirmed by a real conformance failure - a ZWJ sitting
            // between two bracket characters that both resolved to R was splitting one level-1 run into
            // three, changing the line's visual order).
            for (var i = 0; i < n; i++)
            {
                if (originalTypes[i] is BidiClass.BN or BidiClass.RLE or BidiClass.LRE or BidiClass.RLO or BidiClass.LRO or BidiClass.PDF)
                {
                    levels[i] = i > 0 ? levels[i - 1] : paragraphLevel;
                }
            }

            ApplyL1(originalTypes, levels, n, paragraphLevel);

            return new BidiResolverResult(levels, paragraphLevel) { OriginalTypesForLineBreaking = originalTypes };
        }

        /// <summary>
        /// L1 clause 4: resets the embedding level of any trailing run of whitespace/isolate-formatting
        /// characters at the end of one line to the paragraph level. Must be applied per line, once a
        /// layout engine has decided where that line ends - <see cref="Resolve"/> already applies clauses
        /// 1-3 (segment/paragraph separators and the whitespace/isolate-formatting runs preceding them),
        /// which don't depend on line boundaries.
        /// </summary>
        public static void ApplyLineTrailingWhitespaceReset(
            byte[] levels, BidiClass[] originalTypes, int lineStart, int lineLength, byte paragraphLevel)
        {
            var end = lineStart + lineLength;
            var j = end - 1;
            while (j >= lineStart && IsWhitespaceOrIsolateFormatting(originalTypes[j]))
            {
                levels[j] = paragraphLevel;
                j--;
            }
        }

        /// <summary>
        /// L2: reorders one line's already-resolved levels into visual (left-to-right paint) order,
        /// returning the line's level runs - each a maximal same-level span of the ORIGINAL text - listed in
        /// visual rather than logical order. Characters within one run stay in logical order; only whole
        /// runs are reordered/reversed, from the highest level present down to the lowest odd level.
        /// </summary>
        public static List<BidiRun> ReorderLine(byte[] levels, int lineStart, int lineLength)
        {
            var runs = new List<BidiRun>();
            var i = 0;
            while (i < lineLength)
            {
                var j = i + 1;
                while (j < lineLength && levels[lineStart + j] == levels[lineStart + i]) j++;
                runs.Add(new BidiRun(lineStart + i, j - i, levels[lineStart + i]));
                i = j;
            }

            if (runs.Count <= 1) return runs;

            var maxLevel = 0;
            var minOddLevel = int.MaxValue;
            foreach (var r in runs)
            {
                if (r.Level > maxLevel) maxLevel = r.Level;
                if ((r.Level & 1) == 1 && r.Level < minOddLevel) minOddLevel = r.Level;
            }

            if (minOddLevel == int.MaxValue) return runs;

            for (var level = maxLevel; level >= minOddLevel; level--)
            {
                var k = 0;
                while (k < runs.Count)
                {
                    if (runs[k].Level >= level)
                    {
                        var l = k;
                        while (l < runs.Count && runs[l].Level >= level) l++;
                        runs.Reverse(k, l - k);
                        k = l;
                    }
                    else k++;
                }
            }

            return runs;
        }

        private static void FillOriginalTypes(string text, BidiClass[] types)
        {
            var i = 0;
            while (i < text.Length)
            {
                System.Text.Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var consumed);
                var cls = BidiClassTable.Of(rune);
                types[i] = cls;
                if (consumed == 2) types[i + 1] = cls;
                i += consumed;
            }
        }

        // ----- P2/P3 -----

        private static byte ComputeAutoParagraphLevel(BidiClass[] types, int start, int endExclusive)
        {
            var isolateDepth = 0;
            for (var i = start; i < endExclusive; i++)
            {
                var t = types[i];
                if (t is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI)
                {
                    isolateDepth++;
                    continue;
                }
                if (t == BidiClass.PDI)
                {
                    if (isolateDepth > 0) isolateDepth--;
                    continue;
                }
                if (isolateDepth > 0) continue;

                if (t == BidiClass.L) return 0;
                if (t is BidiClass.AL or BidiClass.R) return 1;
            }
            return 0;
        }

        private static int FindMatchingPdiForFsi(BidiClass[] types, int start, int n)
        {
            var depth = 1;
            for (var i = start; i < n; i++)
            {
                var t = types[i];
                if (t is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI) depth++;
                else if (t == BidiClass.PDI)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return n;
        }

        // ----- X1-X8 (explicit levels), X9/§5.2 (retain BN/explicit-formatting) -----

        private static byte NextLevel(byte level, bool rtl)
        {
            if (rtl) return (byte)(level % 2 == 0 ? level + 1 : level + 2);
            return (byte)(level % 2 == 0 ? level + 2 : level + 1);
        }

        private static void ResolveExplicitLevels(
            BidiClass[] originalTypes,
            BidiClass[] resolvedTypes,
            byte[] levels,
            IReadOnlyList<BidiIsolateOverride> overrides,
            byte paragraphLevel)
        {
            var n = originalTypes.Length;
            var stack = new List<StackEntry>(16)
            {
                new() { Level = paragraphLevel, Override = BidiClass.ON, IsolateInitiator = false }
            };
            var overflowIsolateCount = 0;
            var overflowEmbeddingCount = 0;
            var validIsolateCount = 0;

            Dictionary<int, List<BidiIsolateOverride>>? startsAt = null;
            Dictionary<int, List<BidiIsolateOverride>>? endsAt = null;
            foreach (var o in overrides)
            {
                if (o.Length <= 0) continue;
                startsAt ??= new Dictionary<int, List<BidiIsolateOverride>>();
                endsAt ??= new Dictionary<int, List<BidiIsolateOverride>>();
                if (!startsAt.TryGetValue(o.Start, out var sl)) startsAt[o.Start] = sl = [];
                sl.Add(o);
                if (!endsAt.TryGetValue(o.End, out var el)) endsAt[o.End] = el = [];
                el.Add(o);
            }

            // Two overrides sharing an End index (e.g. CSS unicode-bidi: isolate-override maps to an
            // outer LRI and an inner, nested LRO both spanning the same box's whole text range) must
            // close in the opposite order they opened - proper LIFO stack discipline. They were just
            // appended to each endsAt list in the same (push) order as startsAt, so reverse each list
            // once here rather than getting every future multi-override-per-span caller to pre-sort its
            // input; a single override per span (the overwhelming common case) is a one-element list a
            // reverse is a no-op on.
            if (endsAt != null)
            {
                foreach (var list in endsAt.Values) list.Reverse();
            }

            byte PrevLevel(int i) => i > 0 ? levels[i - 1] : paragraphLevel;

            for (var i = 0; i < n; i++)
            {
                if (endsAt != null && endsAt.TryGetValue(i, out var closing))
                {
                    foreach (var o in closing)
                        PopSyntheticPush(o.Push, stack, ref overflowIsolateCount, ref overflowEmbeddingCount, ref validIsolateCount);
                }

                if (startsAt != null && startsAt.TryGetValue(i, out var opening))
                {
                    foreach (var o in opening)
                        PushSyntheticPush(o.Push, stack, ref overflowIsolateCount, ref overflowEmbeddingCount, ref validIsolateCount);
                }

                var cls = originalTypes[i];
                var top = stack[^1];

                switch (cls)
                {
                    case BidiClass.RLE:
                    case BidiClass.LRE:
                    case BidiClass.RLO:
                    case BidiClass.LRO:
                    {
                        levels[i] = PrevLevel(i);
                        var rtl = cls is BidiClass.RLE or BidiClass.RLO;
                        var newLevel = NextLevel(top.Level, rtl);
                        if (newLevel <= MaxDepth && overflowIsolateCount == 0 && overflowEmbeddingCount == 0)
                        {
                            stack.Add(new StackEntry
                            {
                                Level = newLevel,
                                Override = cls == BidiClass.RLO ? BidiClass.R : cls == BidiClass.LRO ? BidiClass.L : BidiClass.ON,
                                IsolateInitiator = false
                            });
                        }
                        else if (overflowIsolateCount == 0)
                        {
                            overflowEmbeddingCount++;
                        }
                        break;
                    }

                    case BidiClass.RLI:
                    case BidiClass.LRI:
                    case BidiClass.FSI:
                    {
                        levels[i] = top.Level;
                        var rtl = cls == BidiClass.RLI ||
                                  (cls == BidiClass.FSI &&
                                   ComputeAutoParagraphLevel(originalTypes, i + 1, FindMatchingPdiForFsi(originalTypes, i + 1, n)) == 1);
                        var newLevel = NextLevel(top.Level, rtl);
                        if (newLevel <= MaxDepth && overflowIsolateCount == 0 && overflowEmbeddingCount == 0)
                        {
                            validIsolateCount++;
                            stack.Add(new StackEntry { Level = newLevel, Override = BidiClass.ON, IsolateInitiator = true });
                        }
                        else
                        {
                            overflowIsolateCount++;
                        }
                        break;
                    }

                    case BidiClass.PDI:
                    {
                        if (overflowIsolateCount > 0)
                        {
                            overflowIsolateCount--;
                        }
                        else if (validIsolateCount > 0)
                        {
                            overflowEmbeddingCount = 0;
                            while (!stack[^1].IsolateInitiator) stack.RemoveAt(stack.Count - 1);
                            stack.RemoveAt(stack.Count - 1);
                            validIsolateCount--;
                        }
                        levels[i] = stack[^1].Level;
                        break;
                    }

                    case BidiClass.PDF:
                    {
                        levels[i] = PrevLevel(i);
                        if (overflowIsolateCount > 0)
                        {
                            // matched an overflowed isolate scope - do nothing
                        }
                        else if (overflowEmbeddingCount > 0)
                        {
                            overflowEmbeddingCount--;
                        }
                        else if (stack.Count >= 2 && !stack[^1].IsolateInitiator)
                        {
                            stack.RemoveAt(stack.Count - 1);
                        }
                        break;
                    }

                    case BidiClass.B:
                    {
                        levels[i] = paragraphLevel;
                        stack.Clear();
                        stack.Add(new StackEntry { Level = paragraphLevel, Override = BidiClass.ON, IsolateInitiator = false });
                        overflowIsolateCount = 0;
                        overflowEmbeddingCount = 0;
                        validIsolateCount = 0;
                        break;
                    }

                    case BidiClass.BN:
                        levels[i] = PrevLevel(i);
                        break;

                    default:
                        levels[i] = top.Level;
                        if (top.Override != BidiClass.ON)
                            resolvedTypes[i] = top.Override;
                        break;
                }
            }
        }

        private static void PushSyntheticPush(
            BidiExplicitPush push, List<StackEntry> stack,
            ref int overflowIsolateCount, ref int overflowEmbeddingCount, ref int validIsolateCount)
        {
            var top = stack[^1];
            switch (push)
            {
                case BidiExplicitPush.Lre:
                case BidiExplicitPush.Rle:
                case BidiExplicitPush.Lro:
                case BidiExplicitPush.Rlo:
                {
                    var rtl = push is BidiExplicitPush.Rle or BidiExplicitPush.Rlo;
                    var newLevel = NextLevel(top.Level, rtl);
                    if (newLevel <= MaxDepth && overflowIsolateCount == 0 && overflowEmbeddingCount == 0)
                    {
                        stack.Add(new StackEntry
                        {
                            Level = newLevel,
                            Override = push == BidiExplicitPush.Rlo ? BidiClass.R : push == BidiExplicitPush.Lro ? BidiClass.L : BidiClass.ON,
                            IsolateInitiator = false
                        });
                    }
                    else if (overflowIsolateCount == 0)
                    {
                        overflowEmbeddingCount++;
                    }
                    break;
                }

                case BidiExplicitPush.Lri:
                case BidiExplicitPush.Rli:
                case BidiExplicitPush.Fsi:
                {
                    // A synthetic Fsi push (CSS unicode-bidi:plaintext on an inline box) can't re-derive
                    // P2/P3 the way a real FSI character can (there is no bounded "matching PDI" text
                    // range to inspect ahead of time here) - treat as Lri, matching the common case where
                    // plaintext content that reaches this path is LTR by default; a block-level box using
                    // plaintext instead goes through BidiParagraphDirection.Auto at the Resolve() entry
                    // point, which does the real P2/P3 detection.
                    var rtl = push == BidiExplicitPush.Rli;
                    var newLevel = NextLevel(top.Level, rtl);
                    if (newLevel <= MaxDepth && overflowIsolateCount == 0 && overflowEmbeddingCount == 0)
                    {
                        validIsolateCount++;
                        stack.Add(new StackEntry { Level = newLevel, Override = BidiClass.ON, IsolateInitiator = true });
                    }
                    else
                    {
                        overflowIsolateCount++;
                    }
                    break;
                }
            }
        }

        private static void PopSyntheticPush(
            BidiExplicitPush push, List<StackEntry> stack,
            ref int overflowIsolateCount, ref int overflowEmbeddingCount, ref int validIsolateCount)
        {
            if (push is BidiExplicitPush.Lri or BidiExplicitPush.Rli or BidiExplicitPush.Fsi)
            {
                if (overflowIsolateCount > 0)
                {
                    overflowIsolateCount--;
                }
                else if (validIsolateCount > 0)
                {
                    overflowEmbeddingCount = 0;
                    while (!stack[^1].IsolateInitiator) stack.RemoveAt(stack.Count - 1);
                    stack.RemoveAt(stack.Count - 1);
                    validIsolateCount--;
                }
            }
            else
            {
                if (overflowIsolateCount > 0)
                {
                    // nothing
                }
                else if (overflowEmbeddingCount > 0)
                {
                    overflowEmbeddingCount--;
                }
                else if (stack.Count >= 2 && !stack[^1].IsolateInitiator)
                {
                    stack.RemoveAt(stack.Count - 1);
                }
            }
        }

        // ----- X10/BD13 (isolating run sequences) -----

        private static Dictionary<int, int> ComputeMatchingPdi(BidiClass[] types, int n)
        {
            var result = new Dictionary<int, int>();
            var stack = new Stack<int>();
            for (var i = 0; i < n; i++)
            {
                var t = types[i];
                if (t is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI)
                {
                    stack.Push(i);
                }
                else if (t == BidiClass.PDI && stack.Count > 0)
                {
                    result[stack.Pop()] = i;
                }
            }
            return result;
        }

        private static List<int[]> ComputeIsolatingRunSequences(
            BidiClass[] resolvedTypes, byte[] levels, Dictionary<int, int> matchingPdi, int n)
        {
            var sequences = new List<int[]>();
            if (n == 0) return sequences;

            var runs = new List<(int Start, int End)>();
            var i = 0;
            while (i < n)
            {
                var j = i + 1;
                while (j < n && levels[j] == levels[i]) j++;
                runs.Add((i, j));
                i = j;
            }

            var runStartIndex = new Dictionary<int, int>();
            for (var r = 0; r < runs.Count; r++) runStartIndex[runs[r].Start] = r;

            var isContinuation = new bool[runs.Count];
            foreach (var kv in matchingPdi)
            {
                if (runStartIndex.TryGetValue(kv.Value, out var runIdx))
                    isContinuation[runIdx] = true;
            }

            var visited = new bool[runs.Count];
            for (var r = 0; r < runs.Count; r++)
            {
                if (visited[r] || isContinuation[r]) continue;

                var positions = new List<int>();
                var cur = r;
                while (true)
                {
                    visited[cur] = true;
                    var (s, e) = runs[cur];
                    for (var p = s; p < e; p++) positions.Add(p);

                    var lastPos = e - 1;
                    var lastType = resolvedTypes[lastPos];
                    if (lastType is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI
                        && matchingPdi.TryGetValue(lastPos, out var pdiPos)
                        && runStartIndex.TryGetValue(pdiPos, out var nextRun))
                    {
                        cur = nextRun;
                        continue;
                    }
                    break;
                }
                sequences.Add(positions.ToArray());
            }

            return sequences;
        }

        // ----- W1-W7, N0-N2, I1-I2, applied per isolating run sequence -----

        private static void ResolveSequence(
            string text, BidiClass[] resolvedTypes, byte[] levels, int[] sequence,
            Dictionary<int, int> matchingPdi, int n, byte paragraphLevel)
        {
            var significant = new List<int>(sequence.Length);
            foreach (var pos in sequence)
            {
                var t = resolvedTypes[pos];
                if (t is BidiClass.BN or BidiClass.RLE or BidiClass.LRE or BidiClass.RLO or BidiClass.LRO or BidiClass.PDF)
                    continue;
                significant.Add(pos);
            }
            if (significant.Count == 0) return;

            var m = significant.Count;
            var work = new BidiClass[m];
            for (var k = 0; k < m; k++) work[k] = resolvedTypes[significant[k]];

            // N0 sub-rule (c) needs to know which positions were NSM BEFORE W1 folded them into their
            // preceding character's type, so it can re-override a bracket-adjacent run of them once the
            // bracket itself resolves - snapshot that here, before W1 mutates work[] in place.
            var wasNsm = new bool[m];
            for (var k = 0; k < m; k++) wasNsm[k] = work[k] == BidiClass.NSM;

            var seqLevel = levels[significant[0]];
            var sos = ComputeSos(levels, significant[0], seqLevel, paragraphLevel);
            var eos = ComputeEos(levels, resolvedTypes, significant[^1], seqLevel, paragraphLevel, matchingPdi, n);

            ApplyW1ToW7(work, sos);

            var e = (seqLevel & 1) == 1 ? BidiClass.R : BidiClass.L;
            var o = (seqLevel & 1) == 1 ? BidiClass.L : BidiClass.R;
            ApplyN0(work, significant, text, e, o, sos, wasNsm);
            ApplyN1N2(work, sos, eos, e);

            for (var k = 0; k < m; k++)
            {
                var pos = significant[k];
                levels[pos] = ApplyImplicit(levels[pos], work[k]);
            }
        }

        private static BidiClass ComputeSos(byte[] levels, int firstPos, byte seqLevel, byte paragraphLevel)
        {
            var otherLevel = firstPos > 0 ? levels[firstPos - 1] : paragraphLevel;
            var higher = Math.Max(seqLevel, otherLevel);
            return (higher & 1) == 1 ? BidiClass.R : BidiClass.L;
        }

        private static BidiClass ComputeEos(
            byte[] levels, BidiClass[] resolvedTypes, int lastPos, byte seqLevel, byte paragraphLevel,
            Dictionary<int, int> matchingPdi, int n)
        {
            var lastType = resolvedTypes[lastPos];
            byte otherLevel;
            if (lastType is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI && !matchingPdi.ContainsKey(lastPos))
            {
                otherLevel = paragraphLevel;
            }
            else
            {
                otherLevel = lastPos + 1 < n ? levels[lastPos + 1] : paragraphLevel;
            }
            var higher = Math.Max(seqLevel, otherLevel);
            return (higher & 1) == 1 ? BidiClass.R : BidiClass.L;
        }

        private static void ApplyW1ToW7(BidiClass[] work, BidiClass sos)
        {
            var m = work.Length;

            // W1
            var prev = sos;
            for (var i = 0; i < m; i++)
            {
                if (work[i] == BidiClass.NSM)
                {
                    work[i] = prev is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI
                        ? BidiClass.ON
                        : prev;
                }
                prev = work[i];
            }

            // W2
            var lastStrong = sos;
            for (var i = 0; i < m; i++)
            {
                var t = work[i];
                if (t is BidiClass.L or BidiClass.R or BidiClass.AL) lastStrong = t;
                else if (t == BidiClass.EN && lastStrong == BidiClass.AL) work[i] = BidiClass.AN;
            }

            // W3
            for (var i = 0; i < m; i++)
                if (work[i] == BidiClass.AL) work[i] = BidiClass.R;

            // W4
            for (var i = 1; i < m - 1; i++)
            {
                if (work[i] == BidiClass.ES && work[i - 1] == BidiClass.EN && work[i + 1] == BidiClass.EN)
                {
                    work[i] = BidiClass.EN;
                }
                else if (work[i] == BidiClass.CS)
                {
                    if (work[i - 1] == BidiClass.EN && work[i + 1] == BidiClass.EN) work[i] = BidiClass.EN;
                    else if (work[i - 1] == BidiClass.AN && work[i + 1] == BidiClass.AN) work[i] = BidiClass.AN;
                }
            }

            // W5
            {
                var i = 0;
                while (i < m)
                {
                    if (work[i] != BidiClass.ET) { i++; continue; }
                    var j = i;
                    while (j < m && work[j] == BidiClass.ET) j++;
                    var before = i > 0 ? work[i - 1] : sos;
                    var after = j < m ? work[j] : sos;
                    if (before == BidiClass.EN || after == BidiClass.EN)
                    {
                        for (var k = i; k < j; k++) work[k] = BidiClass.EN;
                    }
                    i = j;
                }
            }

            // W6
            for (var i = 0; i < m; i++)
                if (work[i] is BidiClass.ES or BidiClass.ET or BidiClass.CS) work[i] = BidiClass.ON;

            // W7
            lastStrong = sos;
            for (var i = 0; i < m; i++)
            {
                var t = work[i];
                if (t is BidiClass.L or BidiClass.R) lastStrong = t;
                else if (t == BidiClass.EN && lastStrong == BidiClass.L) work[i] = BidiClass.L;
            }
        }

        private static BidiClass NumbersAsR(BidiClass t) => t is BidiClass.EN or BidiClass.AN ? BidiClass.R : t;

        private static void ApplyN0(
            BidiClass[] work, List<int> significantPositions, string text, BidiClass e, BidiClass o, BidiClass sos,
            bool[] wasNsm)
        {
            var pairs = IdentifyBracketPairs(text, significantPositions, work);

            foreach (var (openIdx, closeIdx) in pairs)
            {
                var foundE = false;
                var foundO = false;
                for (var k = openIdx + 1; k < closeIdx; k++)
                {
                    var t = NumbersAsR(work[k]);
                    if (t == e) { foundE = true; break; }
                    if (t == o) foundO = true;
                }

                BidiClass? resolved = null;
                if (foundE)
                {
                    resolved = e;
                }
                else if (foundO)
                {
                    var context = sos;
                    for (var k = openIdx - 1; k >= 0; k--)
                    {
                        var t = NumbersAsR(work[k]);
                        if (t is BidiClass.L or BidiClass.R) { context = t; break; }
                    }
                    resolved = context == o ? o : e;
                }
                // else: no strong type inside - leave as ON for N1/N2, and rule (c) below doesn't apply
                // either (only a bracket that actually changed to L or R carries its type onto following
                // originally-NSM characters).

                if (resolved is not { } value) continue;

                work[openIdx] = value;
                work[closeIdx] = value;

                // N0 rule (c): any originally-NSM characters immediately following EITHER bracket of this
                // pair (in the isolating run sequence, i.e. skipping only the BN/explicit-formatting
                // characters already excluded from `work`) take the bracket's newly resolved type too,
                // rather than being left for W1/N1/N2 to resolve independently.
                ApplyNsmAfterBracket(work, wasNsm, openIdx, value);
                ApplyNsmAfterBracket(work, wasNsm, closeIdx, value);
            }
        }

        private static void ApplyNsmAfterBracket(BidiClass[] work, bool[] wasNsm, int bracketIdx, BidiClass value)
        {
            var k = bracketIdx + 1;
            while (k < work.Length && wasNsm[k])
            {
                work[k] = value;
                k++;
            }
        }

        // BD16's own canonical-equivalence note: for pairing purposes only, U+2329/U+232A (the deprecated
        // "Bidi_Paired_Bracket"-only angle brackets) are treated as equivalent to their canonical-
        // decomposition targets U+3008/U+3009 (the CJK angle brackets) - the only such equivalence in the
        // whole bracket set, so a small hardcoded normalization covers it without a general canonical-
        // decomposition lookup.
        private static char CanonicalBracketForm(char c) => c switch
        {
            '\u2329' => '\u3008', // LEFT-POINTING ANGLE BRACKET -> canonical CJK LEFT ANGLE BRACKET
            '\u232A' => '\u3009', // RIGHT-POINTING ANGLE BRACKET -> canonical CJK RIGHT ANGLE BRACKET
            _ => c
        };

        private static List<(int Open, int Close)> IdentifyBracketPairs(
            string text, List<int> significantPositions, BidiClass[] work)
        {
            var pairs = new List<(int, int)>();
            var stack = new List<(int ExpectedClose, int Idx)>();

            for (var k = 0; k < significantPositions.Count; k++)
            {
                if (work[k] != BidiClass.ON) continue;

                var pos = significantPositions[k];
                if (pos >= text.Length) continue; // defensive: shouldn't happen, brackets are BMP
                var cp = text[pos];
                if (!BidiBrackets.TryGetBracket(cp, out var paired, out var type)) continue;

                if (type == BidiBracketType.Open)
                {
                    // BD16: once the stack is full, stop processing BD16 entirely for the rest of the
                    // sequence (not just further opens) - an extraordinarily rare case (64+ levels of
                    // nested brackets on one line) the spec deliberately gives up on rather than half-pair.
                    if (stack.Count >= 63) break;
                    stack.Add((CanonicalBracketForm((char)paired), k));
                }
                else
                {
                    var closeForm = CanonicalBracketForm(cp);
                    for (var s = stack.Count - 1; s >= 0; s--)
                    {
                        if (stack[s].ExpectedClose == closeForm)
                        {
                            pairs.Add((stack[s].Idx, k));
                            stack.RemoveRange(s, stack.Count - s);
                            break;
                        }
                    }
                }
            }

            pairs.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return pairs;
        }

        private static bool IsNi(BidiClass t) =>
            t is BidiClass.B or BidiClass.S or BidiClass.WS or BidiClass.ON
                or BidiClass.FSI or BidiClass.LRI or BidiClass.RLI or BidiClass.PDI;

        private static void ApplyN1N2(BidiClass[] work, BidiClass sos, BidiClass eos, BidiClass e)
        {
            var m = work.Length;
            var i = 0;
            while (i < m)
            {
                if (!IsNi(work[i])) { i++; continue; }
                var j = i;
                while (j < m && IsNi(work[j])) j++;

                var before = i > 0 ? NumbersAsR(work[i - 1]) : sos;
                var after = j < m ? NumbersAsR(work[j]) : eos;
                var resolved = before == after ? before : e;
                for (var k = i; k < j; k++) work[k] = resolved;
                i = j;
            }
        }

        private static byte ApplyImplicit(byte level, BidiClass t)
        {
            var isEven = (level & 1) == 0;
            if (isEven)
            {
                return t switch
                {
                    BidiClass.R => (byte)(level + 1),
                    BidiClass.AN or BidiClass.EN => (byte)(level + 2),
                    _ => level
                };
            }

            return t switch
            {
                BidiClass.L or BidiClass.EN or BidiClass.AN => (byte)(level + 1),
                _ => level
            };
        }

        // ----- L1 (clauses 1-3; clause 4 is ApplyLineTrailingWhitespaceReset above) -----

        private static void ApplyL1(BidiClass[] originalTypes, byte[] levels, int n, byte paragraphLevel)
        {
            for (var i = 0; i < n; i++)
            {
                var t = originalTypes[i];
                if (t != BidiClass.S && t != BidiClass.B) continue;

                levels[i] = paragraphLevel;
                var j = i - 1;
                while (j >= 0 && IsWhitespaceOrIsolateFormatting(originalTypes[j]))
                {
                    levels[j] = paragraphLevel;
                    j--;
                }
            }
        }

        private static bool IsWhitespaceOrIsolateFormatting(BidiClass t) =>
            t is BidiClass.WS or BidiClass.FSI or BidiClass.LRI or BidiClass.RLI or BidiClass.PDI
                or BidiClass.RLE or BidiClass.LRE or BidiClass.RLO or BidiClass.LRO or BidiClass.PDF or BidiClass.BN;
    }
}
