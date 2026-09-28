using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeachDrawing.Text.Layout
{
    /// <summary>
    /// Text prepared for laying out: its direction, scripts and joining resolved, its line break opportunities found. It does not depend
    /// on a width, so one paragraph can be laid out at any number of widths.
    /// </summary>
    /// <remarks>
    /// A paragraph is immutable as far as a caller can tell, and safe to lay out from several threads (it remembers the pieces it has shaped, which
    /// is not visible). Build one with <see cref="ParagraphBuilder"/>. The whole text is one bidirectional paragraph with one base direction: text
    /// with paragraph separators in it is not split into several, so a caller that wants each of its paragraphs to detect its own direction builds one
    /// <see cref="Paragraph"/> for each. Line widths are found by shaping the words between break opportunities on their own, and the lines are then
    /// shaped whole, so a line's <see cref="LineBox.Width"/> can differ slightly from the width it was fitted at where a font kerns across a space.
    /// </remarks>
    public sealed class Paragraph
    {
        // Scripts the Universal Shaping Engine classification covers; other text never gets categories.
        private static readonly HashSet<string> UseShapedScripts = ["Devanagari", "Bengali", "Gujarati", "Tamil"];

        // The script HarfBuzz's own (pre-Universal-Shaping-Engine) Khmer shaper covers - kept separate
        // from UseShapedScripts since Khmer is a genuinely different shaping model, not a fifth
        // Universal-Shaping-Engine script (see KhmerCategory's own remarks).
        private static readonly HashSet<string> KhmerShapedScripts = ["Khmer"];

        private readonly (int Start, int End, RunStyle Style)[] _runs;
        private readonly BidiAnalysis _bidi;
        private readonly string[] _scripts;
        private readonly ArabicJoiningForm[] _joining;
        private readonly UseCategory[]? _use;
        private readonly KhmerCategory[]? _khmer;
        private readonly bool[] _isGraphemeBoundary;
        private readonly int[] _nextOpportunity;
        private readonly Atom[] _atoms;
        private readonly IReadOnlyDictionary<int, InlineBox>? _boxes;
        private readonly Typeface?[]? _fallbackFaces;
        private const int MaxShapedPieces = 8192;

        private readonly Dictionary<(int, int), GlyphRun> _shaped = [];
        private readonly Dictionary<(int, int), int[]> _hyphenPoints = [];
        private readonly Dictionary<(RunStyle, string), (GlyphRun Run, RunStyle Style, double Width)> _hyphens = [];
        private const int MaxHyphenatedWords = 4096;
        private const char SoftHyphen = '\u00AD';

        /// <summary>A piece of the text that is shaped as one: one style, one direction level and one script.</summary>
        internal readonly record struct Atom(int Start, int End, int Run, byte Level, string Script, RunStyle Style);

        internal Paragraph(string text, (int, int, RunStyle)[] runs, ParagraphStyle style, IReadOnlyDictionary<int, InlineBox>? boxes = null)
        {
            Text = text;
            _boxes = boxes is { Count: > 0 } ? boxes : null;
            Style = style;
            _runs = runs;
            _bidi = Bidi.Analyze(text, style.Direction);

            (_scripts, _joining, _use, _khmer) = ResolveScripts(text);
            Opportunities = LineBreaker.FindOpportunities(text, style.LineBreak);
            HasSoftHyphens = style.Hyphens != Hyphens.None && text.Contains(SoftHyphen);
            if (style.Hyphens == Hyphens.None)
            {
                // Without hyphenation a soft hyphen is not a place to break.
                for (int i = 1; i <= text.Length; i++)
                {
                    if (text[i - 1] == SoftHyphen && Opportunities[i] == LineBreakOpportunity.Allowed)
                    {
                        Opportunities[i] = LineBreakOpportunity.Prohibited;
                    }
                }
            }

            _isGraphemeBoundary = new bool[text.Length + 1];
            foreach (var boundary in Segmenter.FindGraphemeBoundaries(text))
            {
                _isGraphemeBoundary[boundary] = true;
            }

            _isGraphemeBoundary[0] = true;
            _isGraphemeBoundary[text.Length] = true;
            AllowBreaksAroundBoxes();

            _nextOpportunity = new int[text.Length + 2];
            _nextOpportunity[text.Length + 1] = text.Length + 1;
            for (int i = text.Length; i >= 0; i--)
            {
                _nextOpportunity[i] = i >= 1 && Opportunities[i] != LineBreakOpportunity.Prohibited ? i : _nextOpportunity[i + 1];
            }

            _fallbackFaces = ResolveFallbackFaces();
            _atoms = BuildAtoms();
        }

        /// <summary>
        /// Makes a line breakable before and after every inline box (CSS Text 3 section 5.1: a soft wrap opportunity on both sides of an atomic inline, even next to a
        /// character that would suppress one, such as a no-break space, a closing bracket or a full stop), except next to a joiner or word joiner, which keep it, and
        /// before a space, which hangs.
        /// </summary>
        private void AllowBreaksAroundBoxes()
        {
            if (_boxes is null)
            {
                return;
            }

            foreach (int index in _boxes.Keys)
            {
                if (index > 0 && Opportunities[index] == LineBreakOpportunity.Prohibited && !SuppressesBreakAt(Text[index - 1]))
                {
                    Opportunities[index] = LineBreakOpportunity.Allowed;
                }

                int after = index + 1;
                if (after < Text.Length && _isGraphemeBoundary[after] && Opportunities[after] == LineBreakOpportunity.Prohibited
                    && !SuppressesBreakAt(Text[after]) && !IsHangingSpace(Text[after]) && Text[after] != (char)0x200B)
                {
                    Opportunities[after] = LineBreakOpportunity.Allowed;
                }
            }
        }

        /// <summary>The characters that keep a break from a box beside them: line terminators, the zero width joiner, the word joiner and the zero width no-break space.</summary>
        private static bool SuppressesBreakAt(char c) => IsLineTerminator(c) || c is (char)0x200D or (char)0x2060 or (char)0xFEFF;

        /// <summary>The text of the paragraph.</summary>
        public string Text { get; }

        /// <summary>The style the paragraph was built with.</summary>
        public ParagraphStyle Style { get; }

        /// <summary>Whether the paragraph as a whole runs right to left.</summary>
        public bool IsRightToLeft => _bidi.IsParagraphRtl;

        /// <summary>Whether the text holds a soft hyphen that is a place to break: a break after it needs the hyphen to fit.</summary>
        internal bool HasSoftHyphens { get; }

        internal LineBreakOpportunity[] Opportunities { get; }

        internal byte ParagraphLevel => _bidi.ParagraphLevel;

        internal byte[] Levels => _bidi.Levels;

        /// <summary>The first offset at or after <paramref name="index"/> that is a place a line may or must end, or one past the text when there is none.</summary>
        internal int NextOpportunityAtOrAfter(int index) => _nextOpportunity[Math.Min(index, Text.Length + 1)];

        internal bool IsGraphemeBoundary(int index) => _isGraphemeBoundary[index];

        internal ReadOnlySpan<Atom> Atoms => _atoms;

        /// <summary>The index of the first atom that ends after <paramref name="index"/>.</summary>
        internal int FirstAtomAfter(int index)
        {
            int low = 0, high = _atoms.Length;
            while (low < high)
            {
                int middle = (low + high) >>> 1;
                if (_atoms[middle].End <= index)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        internal RunStyle StyleOfRun(int run) => _runs[run].Style;

        /// <summary>The style of the run the character at <paramref name="index"/> belongs to (the nearest run for an offset at the end).</summary>
        internal RunStyle StyleAt(int index)
        {
            return _runs[RunAt(index)].Style;
        }

        private int RunAt(int index)
        {
            int low = 0, high = _runs.Length - 1;
            while (low < high)
            {
                int middle = (low + high + 1) >>> 1;
                if (_runs[middle].Start <= index)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return low;
        }

        /// <summary>The characters that end a line and take no space themselves.</summary>
        internal static bool IsLineTerminator(char c) => c is (char)0x0A or (char)0x0D or (char)0x0B or (char)0x0C or (char)0x85 or (char)0x2028 or (char)0x2029;

        /// <summary>The spaces that hang at the end of a line: they are kept in the text, and take no part in fitting or aligning it.</summary>
        internal static bool IsHangingSpace(char c) => c is (char)0x20 or (char)0x09 or (char)0x1680 || (c >= (char)0x2000 && c <= (char)0x2006) || (c >= (char)0x2008 && c <= (char)0x200A) || c == (char)0x205F || c == (char)0x3000;

        /// <summary>
        /// For every character, the typeface that stands in for the run's own where it cannot draw the character's grapheme cluster, or
        /// <see langword="null"/> for the run's own; the array itself is null when no run has a fallback.
        /// </summary>
        private Typeface?[]? ResolveFallbackFaces()
        {
            Typeface?[]? faces = null;
            foreach (var (start, end, style) in _runs)
            {
                if (style.Fallback is not { } fallback)
                {
                    continue;
                }

                for (int i = start; i < end;)
                {
                    int next = i + 1;
                    while (next < end && !_isGraphemeBoundary[next])
                    {
                        next++;
                    }

                    Rune.DecodeFromUtf16(Text.AsSpan(i), out var first, out _);
                    // Spaces, controls and format characters (zero width space, joiners, bidi marks, the soft hyphen) draw nothing, so no
                    // face is asked for them: a stand-in would only change the line's height and cut the shaping around it.
                    if (!IsLineTerminator(Text[i]) && !IsBoxAt(i) && !Rune.IsWhiteSpace(first) && !Rune.IsControl(first)
                        && Rune.GetUnicodeCategory(first) != System.Globalization.UnicodeCategory.Format
                        && !style.Typeface.TryMapRune(first, out _)
                        && fallback(first) is { } face)
                    {
                        faces ??= new Typeface?[Text.Length];
                        for (int k = i; k < next; k++)
                        {
                            faces[k] = face;
                        }
                    }

                    i = next;
                }
            }

            return faces;
        }

        private Atom[] BuildAtoms()
        {
            var atoms = new List<Atom>();
            int i = 0;
            int length = Text.Length;
            while (i < length)
            {
                if (IsLineTerminator(Text[i]))
                {
                    i++;
                    continue;
                }

                int run = RunAt(i);
                int start = i;
                byte level = _bidi.Levels[i];
                string script = _scripts[i];
                var face = _fallbackFaces?[i];
                bool tab = Text[i] == '\t' || IsBoxAt(i);
                i++;
                // A tab is an atom of its own: its width comes from the tab stops, not from a glyph. So is an inline box.
                while (!tab
                    && i < length
                    && Text[i] != '\t'
                    && !IsBoxAt(i)
                    && !IsLineTerminator(Text[i])
                    && _runs[run].End > i
                    && _bidi.Levels[i] == level
                    && _scripts[i] == script
                    && Equals(_fallbackFaces?[i], face))
                {
                    i++;
                }

                var style = _runs[run].Style;
                atoms.Add(new Atom(start, i, run, level, script, face is null ? style : style with { Typeface = face }));
            }

            return atoms.ToArray();
        }

        private static (string[] Scripts, ArabicJoiningForm[] Joining, UseCategory[]? Use, KhmerCategory[]? Khmer) ResolveScripts(string text)
        {
            int length = text.Length;
            var codepoints = new List<int>(length);
            var codepointOfChar = new int[length];
            int i = 0;
            while (i < length)
            {
                Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out int consumed);
                int index = codepoints.Count;
                codepoints.Add(rune.Value);
                for (int k = 0; k < consumed; k++)
                {
                    codepointOfChar[i + k] = index;
                }

                i += consumed;
            }

            var raw = new string[codepoints.Count];
            for (int c = 0; c < raw.Length; c++)
            {
                raw[c] = Scripts.Of(codepoints[c]);
            }

            var resolved = Scripts.ResolveLooked(raw);
            var forms = ArabicJoining.Resolve(codepoints);

            UseCategory[]? categories = null;
            KhmerCategory[]? khmerCategories = null;
            for (int c = 0; c < resolved.Count; c++)
            {
                if (UseShapedScripts.Contains(resolved[c]))
                {
                    categories ??= new UseCategory[codepoints.Count];
                    categories[c] = UniversalShaping.Classify(codepoints[c]);
                }
                else if (KhmerShapedScripts.Contains(resolved[c]))
                {
                    khmerCategories ??= new KhmerCategory[codepoints.Count];
                    khmerCategories[c] = KhmerShaping.Classify(codepoints[c]);
                }
            }

            var scripts = new string[length];
            var charForms = new ArabicJoiningForm[length];
            UseCategory[]? charCategories = categories is null ? null : new UseCategory[length];
            KhmerCategory[]? charKhmerCategories = khmerCategories is null ? null : new KhmerCategory[length];
            for (int c = 0; c < length; c++)
            {
                int cp = codepointOfChar[c];
                scripts[c] = resolved[cp];
                charForms[c] = forms[cp];
                if (charCategories is not null)
                {
                    charCategories[c] = categories![cp];
                }
                if (charKhmerCategories is not null)
                {
                    charKhmerCategories[c] = khmerCategories![cp];
                }
            }

            return (scripts, charForms, charCategories, charKhmerCategories);
        }

        /// <summary>Shapes the piece <c>[start, end)</c> of one atom, once.</summary>
        internal GlyphRun ShapePiece(in Atom atom, int start, int end)
        {
            lock (_shaped)
            {
                if (_shaped.TryGetValue((start, end), out var cached))
                {
                    return cached;
                }
            }

            var style = atom.Style;
            var settings = style.Shape ?? ShapeSettings.Default;
            settings = settings with
            {
                ScriptTag = OpenTypeTags.ForScript(atom.Script) ?? settings.ScriptTag,
                ReverseForDisplay = (atom.Level & 1) == 1,
            };

            if (style.LetterSpacing != 0)
            {
                // CSS Text 3: text with letter spacing does not get its optional ligatures.
                settings = settings with { Ligatures = settings.Ligatures & ~(LigatureSet.Common | LigatureSet.Discretionary | LigatureSet.Historical) };
            }

            var pieceText = Text.Substring(start, end - start);
            var forms = new List<ArabicJoiningForm>();
            // Built only when this piece's own script is actually the shaped one - _use/_khmer are
            // paragraph-wide arrays (allocated the moment ANY codepoint anywhere in the paragraph needs
            // one), so a non-USE/non-Khmer piece sharing a paragraph with USE/Khmer text (e.g. an
            // embedded Latin-numeral run inside Khmer body text) would otherwise build a throwaway list
            // every time it's shaped, for no reason: neither list is ever read below unless the matching
            // Contains check on the next line also passes.
            List<UseCategory>? categories = _use is not null && UseShapedScripts.Contains(atom.Script) ? [] : null;
            List<KhmerCategory>? khmerCategories = _khmer is not null && KhmerShapedScripts.Contains(atom.Script) ? [] : null;
            bool anyJoining = false;
            for (int i = start; i < end;)
            {
                Rune.DecodeFromUtf16(Text.AsSpan(i), out _, out int consumed);
                forms.Add(_joining[i]);
                anyJoining |= _joining[i] != ArabicJoiningForm.None;
                categories?.Add(_use![i]);
                khmerCategories?.Add(_khmer![i]);
                i += consumed;
            }

            if (anyJoining)
            {
                settings = settings with { JoiningForms = forms };
            }
            else if (categories is not null && UseShapedScripts.Contains(atom.Script))
            {
                settings = settings with { UseCategories = categories };
            }
            else if (khmerCategories is not null && KhmerShapedScripts.Contains(atom.Script))
            {
                settings = settings with { KhmerCategories = khmerCategories };
            }

            var run = Shaper.Shape(style.Typeface, pieceText, settings);
            if (pieceText.Contains(SoftHyphen))
            {
                run = WithoutSoftHyphens(run, pieceText);
            }

            lock (_shaped)
            {
                // Laying out at many widths (a resize) makes many pieces; what is dropped is only shaped again.
                if (_shaped.Count >= MaxShapedPieces)
                {
                    _shaped.Clear();
                }

                _shaped[(start, end)] = run;
            }

            return run;
        }

        /// <summary>
        /// A soft hyphen draws nothing where the line does not end: where it does, the layout generates the hyphen the line ends with. So the glyph a face may have
        /// for it is dropped from the run (a mark attached to it loses its anchor), and the characters it stood for stay in the text.
        /// </summary>
        private static GlyphRun WithoutSoftHyphens(GlyphRun run, string pieceText)
        {
            var glyphs = run.Glyphs;
            var remap = new int[glyphs.Count];
            var kept = new List<PlacedGlyph>(glyphs.Count);
            for (int i = 0; i < glyphs.Count; i++)
            {
                var glyph = glyphs[i];
                bool isSoftHyphen = glyph.ClusterLength == 1 && glyph.ClusterStart >= 0 && glyph.ClusterStart < pieceText.Length && pieceText[glyph.ClusterStart] == SoftHyphen;
                remap[i] = isSoftHyphen ? -1 : kept.Count;
                if (!isSoftHyphen)
                {
                    kept.Add(glyph);
                }
            }

            if (kept.Count == glyphs.Count)
            {
                return run;
            }

            for (int i = 0; i < kept.Count; i++)
            {
                if (kept[i].AttachedToIndex is { } attached)
                {
                    kept[i] = kept[i] with { AttachedToIndex = attached >= 0 && attached < remap.Length && remap[attached] >= 0 ? remap[attached] : null };
                }
            }

            return new GlyphRun(run.Typeface, kept);
        }

        /// <summary>The width, in layout units, of a shaped piece set in <paramref name="style"/>.</summary>
        internal double WidthOf(GlyphRun run, RunStyle style, int from)
        {
            double width = run.Advance * style.Size / run.Typeface.Metrics.UnitsPerEm;
            if (style.LetterSpacing != 0 || style.WordSpacing != 0)
            {
                int clusters = 0;
                for (int i = 0; i < run.Glyphs.Count; i++)
                {
                    if (EndsCluster(run, from, i))
                    {
                        clusters++;
                    }
                }

                width += style.LetterSpacing * clusters + style.WordSpacing * CountSpaces(run, from);
            }

            return width;
        }

        /// <summary>Whether <paramref name="c"/> is a word separator, which word spacing and justification widen.</summary>
        internal static bool IsWordSeparator(char c) => c == ' ' || c == (char)0xA0;

        /// <summary>
        /// Whether the glyph at <paramref name="index"/> of a piece that starts at <paramref name="from"/> is the last of the glyphs of one
        /// user-perceived character (a base and its marks are several glyphs, a ligature is one glyph for several characters): the place letter
        /// and word spacing are added, once for each such character.
        /// </summary>
        internal bool EndsCluster(GlyphRun run, int from, int index)
        {
            var glyphs = run.Glyphs;
            var glyph = glyphs[index];
            if (glyph.ClusterLength <= 0)
            {
                return false;
            }

            for (int next = index + 1; next < glyphs.Count; next++)
            {
                if (glyphs[next].ClusterLength > 0)
                {
                    return GraphemeStartOf(from + glyph.ClusterStart) != GraphemeStartOf(from + glyphs[next].ClusterStart);
                }
            }

            return true;
        }

        /// <summary>Whether the user-perceived character that holds the offset starts with a word separator (a space with a mark on it still is one).</summary>
        internal bool IsWordSeparatorAt(int index) => index >= 0 && index < Text.Length && IsWordSeparator(Text[GraphemeStartOf(index)]);

        /// <summary>Whether the character at <paramref name="index"/> is a letter of a script written without spaces, so that the boundaries next to it are justification opportunities.</summary>
        internal bool IsBlockScriptAt(int index)
        {
            if (index < 0 || index >= Text.Length)
            {
                return false;
            }

            Rune.DecodeFromUtf16(Text.AsSpan(index), out var rune, out _);
            // The prolonged sound mark is Common, but it belongs to the kana around it.
            return rune.Value == 0x30FC || Scripts.Of(rune) is "Han" or "Hiragana" or "Katakana" or "Bopomofo" or "Yi";
        }

        /// <summary>Whether the character at <paramref name="index"/> is written joined to the one after it (a cursive script), so that room added between them would break the join.</summary>
        internal bool JoinsNext(int index) => index >= 0 && index < _joining.Length && _joining[index] is ArabicJoiningForm.Init or ArabicJoiningForm.Medi or ArabicJoiningForm.Med2;

        private int GraphemeStartOf(int index)
        {
            index = Math.Clamp(index, 0, Text.Length);
            while (index > 0 && !_isGraphemeBoundary[index])
            {
                index--;
            }

            return index;
        }

        /// <summary>How many clusters of a piece that starts at <paramref name="from"/> are a word separator.</summary>
        internal int CountSpaces(GlyphRun run, int from)
        {
            int count = 0;
            for (int i = 0; i < run.Glyphs.Count; i++)
            {
                var glyph = run.Glyphs[i];
                if (EndsCluster(run, from, i) && IsWordSeparatorAt(from + glyph.ClusterStart))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>The language of the text at <paramref name="index"/>: its run's, or else the one line breaking is tailored for.</summary>
        internal string? LanguageAt(int index) => StyleAt(index).Shape?.Language ?? Style.LineBreak.Language;

        /// <summary>The style of the atom that holds <paramref name="index"/>: the run's, or the stand-in a fallback chose.</summary>
        internal RunStyle AtomStyleAt(int index)
        {
            int a = FirstAtomAfter(index);
            return a < _atoms.Length && _atoms[a].Start <= index ? _atoms[a].Style : StyleAt(index);
        }

        /// <summary>
        /// The places automatic hyphenation may break the word in <c>[start, end)</c>, as offsets in the text, in increasing order: the patterns of the word's language,
        /// with <see cref="ParagraphStyle.HyphenateLimitChars"/> applied. A stretch that is not one word of letters, or a word of more than 128 UTF-16 units, has none.
        /// </summary>
        internal int[] HyphenationPoints(int start, int end)
        {
            // The rest of a word after a hyphenated line is hyphenated as part of the whole word, so the limits count from the word's own start.
            int first = start;
            while (first < end && !char.IsLetter(Text[first]))
            {
                first++;
            }

            if (first == start)
            {
                while (first > 0 && char.IsLetter(Text[first - 1]))
                {
                    first--;
                }
            }

            int last = end;
            while (last > first && !char.IsLetter(Text[last - 1]))
            {
                last--;
            }

            var key = (first, last);
            int[]? points;
            lock (_hyphenPoints)
            {
                _hyphenPoints.TryGetValue(key, out points);
            }

            if (points is null)
            {
                points = FindHyphenationPoints(first, last);
                lock (_hyphenPoints)
                {
                    if (_hyphenPoints.Count >= MaxHyphenatedWords)
                    {
                        _hyphenPoints.Clear();
                    }

                    _hyphenPoints[key] = points;
                }
            }

            return first < start && points.Length > 0 ? points.Where(point => point > start).ToArray() : points;
        }

        /// <summary>The longest word hyphenation is tried on, in UTF-16 units: the patterns work on words, and a longer run of letters is not one.</summary>
        private const int MaxHyphenatedWordLength = 128;

        private int[] FindHyphenationPoints(int start, int end)
        {
            var limits = Style.HyphenateLimitChars;
            int minWord = limits.WordLength ?? 5;
            int minBefore = limits.BeforeBreak ?? 2;
            int minAfter = limits.AfterBreak ?? 2;
            if (end - start < Math.Max(minWord, 2) || end - start > MaxHyphenatedWordLength || LanguageAt(start) is not { Length: > 0 } language)
            {
                return [];
            }

            var points = new List<int>();
            foreach (int index in Hyphenator.FindBreakPoints(Text.Substring(start, end - start), language))
            {
                int offset = start + index;
                if (index >= minBefore && end - offset >= minAfter && IsGraphemeBoundary(offset))
                {
                    points.Add(offset);
                }
            }

            return points.ToArray();
        }

        /// <summary>The hyphen a line ends with when it is broken at <paramref name="index"/>: its glyphs, its style (the face of the character before the break) and its width.</summary>
        internal (GlyphRun Run, RunStyle Style, double Width) HyphenAt(int index)
        {
            var style = AtomStyleAt(Math.Max(0, index - 1));
            var text = Style.HyphenateCharacter ?? (style.Typeface.TryMapRune(new Rune(0x2010), out _) ? "\u2010" : "-");
            return Generate(style, text);
        }

        /// <summary>
        /// The ellipsis a line cut at <paramref name="cut"/> ends with, in the style of the last character that is drawn (the first of the line if none is): the paragraph's
        /// own string, or U+2026 where the face has it and three full stops where it does not.
        /// </summary>
        internal (GlyphRun Run, RunStyle Style, double Width) EllipsisAt(int cut, int lineStart)
        {
            var style = AtomStyleAt(cut > lineStart ? cut - 1 : lineStart);
            var text = Style.Ellipsis;
            if (text is null)
            {
                Rune.DecodeFromUtf16("\u2026".AsSpan(), out var rune, out _);
                text = style.Typeface.TryMapRune(rune, out _) || style.Fallback?.Invoke(rune) is not null ? "\u2026" : "...";
            }

            return Generate(style, text);
        }

        /// <summary>Shapes text that is not the paragraph's, for a generated run: its glyphs, its style (with a stand-in face where the run's cannot draw it) and its width.</summary>
        private (GlyphRun Run, RunStyle Style, double Width) Generate(RunStyle style, string text)
        {
            var key = (style, text);
            lock (_hyphens)
            {
                if (_hyphens.TryGetValue(key, out var cached))
                {
                    return cached;
                }
            }

            if (text.Length == 0)
            {
                return (new GlyphRun(style.Typeface, []), style, 0);
            }

            Rune.DecodeFromUtf16(text.AsSpan(), out var first, out _);
            if (!style.Typeface.TryMapRune(first, out _) && style.Fallback?.Invoke(first) is { } stand)
            {
                style = style with { Typeface = stand };
            }

            var run = Shaper.Shape(style.Typeface, text, (style.Shape ?? ShapeSettings.Default) with { ScriptTag = null, JoiningForms = null, UseCategories = null, KhmerCategories = null, ReverseForDisplay = false });
            var clusterStarts = new HashSet<int>();
            foreach (var glyph in run.Glyphs)
            {
                if (glyph.ClusterLength > 0)
                {
                    clusterStarts.Add(glyph.ClusterStart);
                }
            }

            double width = (run.Advance * style.Size / style.Typeface.Metrics.UnitsPerEm) + (style.LetterSpacing * clusterStarts.Count);
            var made = (run, style, width);
            lock (_hyphens)
            {
                if (_hyphens.Count >= 256)
                {
                    _hyphens.Clear();
                }

                _hyphens[key] = made;
            }

            return made;
        }

        /// <summary>Whether the character at <paramref name="index"/> stands for an inline box.</summary>
        internal bool IsBoxAt(int index) => _boxes is not null && _boxes.ContainsKey(index);

        /// <summary>Whether the atom is an inline box.</summary>
        internal bool IsBox(in Atom atom) => _boxes is not null && _boxes.ContainsKey(atom.Start);

        /// <summary>The inline box the character at <paramref name="index"/> stands for.</summary>
        internal InlineBox BoxAt(int index) => _boxes![index];

        /// <summary>Whether the atom is one tab character.</summary>
        internal bool IsTab(in Atom atom) => atom.End == atom.Start + 1 && Text[atom.Start] == '\t';

        /// <summary>The width of a space in <paramref name="style"/>'s face, with the spacing that goes with it: the unit <c>tab-size</c> counts in.</summary>
        private static double SpaceWidth(RunStyle style)
        {
            double width = style.LetterSpacing + style.WordSpacing;
            if (style.Typeface.TryMapRune(new Rune(' '), out var glyph))
            {
                width += style.Typeface.GetAdvance(glyph) * style.Size / style.Typeface.Metrics.UnitsPerEm;
            }

            return width;
        }

        /// <summary>How far a tab moves the pen from <paramref name="pen"/>, the distance from the paragraph's start edge, to the next tab stop.</summary>
        internal double TabAdvance(RunStyle style, double pen)
        {
            var size = Style.TabSize;
            double stop = size.IsLength ? size.Value : size.Value * SpaceWidth(style);
            if (!(stop > 0) || !double.IsFinite(stop) || !double.IsFinite(pen))
            {
                return 0;
            }

            // The pen is a sum of scaled advances and the stop a product of another, so a pen that is on a stop can come out a rounding error short of it;
            // the small allowance keeps such a tab from covering nothing where it should cover a whole stop.
            double advance = ((Math.Floor((pen / stop) + 1e-9) + 1) * stop) - pen;
            // A stop far smaller than the pen can overflow the division, and rounding can put the next stop at or behind the pen.
            return double.IsFinite(advance) && advance > 0 ? advance : 0;
        }

        /// <summary>
        /// The width of the text <c>[start, end)</c> laid on one line, with the line-ending characters left out, where the pen is
        /// <paramref name="pen"/> from the paragraph's start edge before the first of it (a tab reaches the next stop from there).
        /// </summary>
        internal double Measure(int start, int end, double pen = 0)
        {
            double width = 0;
            for (int a = FirstAtomAfter(start); a < _atoms.Length; a++)
            {
                var atom = _atoms[a];
                if (atom.Start >= end)
                {
                    break;
                }

                int from = Math.Max(start, atom.Start);
                int to = Math.Min(end, atom.End);
                if (to > from)
                {
                    width += IsBox(atom) ? BoxAt(atom.Start).Width : IsTab(atom) ? TabAdvance(atom.Style, pen + width) : WidthOf(ShapePiece(atom, from, to), atom.Style, from);
                }
            }

            return width;
        }

        /// <summary>
        /// How far the text of the line that starts at <paramref name="lineStart"/> is moved in from the start edge: the paragraph's <c>text-indent</c> for the
        /// lines it applies to.
        /// </summary>
        internal double IndentAt(int lineStart) => IndentOf(lineStart == 0, lineStart > 0 && IsLineTerminator(Text[lineStart - 1]));

        /// <summary>The indent of a line that is the first of the paragraph, or follows a forced break, or neither.</summary>
        internal double IndentOf(bool isFirst, bool followsForcedBreak)
        {
            var indent = Style.TextIndent;
            bool selected = isFirst || (indent.EachLine && followsForcedBreak);
            return (indent.Hanging ? !selected : selected) ? indent.Length : 0;
        }

        /// <summary>
        /// Starts laying the paragraph out one line at a time, for a caller that decides how much room each line has (floats, columns, pages): see <see cref="LineFlow"/>.
        /// </summary>
        /// <returns>The flow, which holds nothing but the paragraph and can be used from several threads.</returns>
        public LineFlow CreateFlow() => new(this);

        /// <summary>
        /// Lays the paragraph out at a width.
        /// </summary>
        /// <param name="availableWidth">The width lines may fill, in layout units, or <see cref="double.PositiveInfinity"/> for lines that break only where they are forced to.</param>
        /// <returns>The lines, in an immutable snapshot.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="availableWidth"/> is negative or not a number.</exception>
        public ParagraphLayout Layout(double availableWidth)
        {
            if (double.IsNaN(availableWidth) || availableWidth < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(availableWidth), availableWidth, "The width is negative or not a number.");
            }

            return ParagraphLayoutBuilder.Build(this, availableWidth);
        }

        /// <summary>
        /// The narrowest and the widest the paragraph can be laid out.
        /// </summary>
        /// <returns>The two widths, in layout units.</returns>
        public ContentWidths MeasureContent()
        {
            double max = 0;
            double min = 0;
            int segmentStart = 0;
            int lineStart = 0;
            var opportunities = Opportunities;
            for (int i = 1; i <= Text.Length; i++)
            {
                if (opportunities[i] == LineBreakOpportunity.Prohibited)
                {
                    continue;
                }

                int contentEnd = ContentEnd(segmentStart, i);

                // With every break taken, a segment is alone on its line: the first of a hard line gets the indent of the first (or a forced-break) line, and
                // the others that of a line that is neither.
                double segmentIndent = segmentStart == lineStart ? IndentAt(lineStart) : IndentOf(false, false);
                if (Style.OverflowWrap == OverflowWrap.Anywhere)
                {
                    for (int g = segmentStart; g < contentEnd;)
                    {
                        int next = g + 1;
                        while (next < contentEnd && !_isGraphemeBoundary[next])
                        {
                            next++;
                        }

                        double graphemeIndent = g == segmentStart ? segmentIndent : IndentOf(false, false);
                        min = Math.Max(min, Measure(g, next, graphemeIndent) + graphemeIndent);
                        g = next;
                    }
                }
                else
                {
                    min = Math.Max(min, MinimumWidth(segmentStart, contentEnd, segmentIndent));
                }

                if (opportunities[i] == LineBreakOpportunity.Mandatory)
                {
                    double indent = IndentAt(lineStart);
                    max = Math.Max(max, Measure(lineStart, ContentEnd(lineStart, i), indent) + indent);
                    lineStart = i;
                }

                segmentStart = i;
            }

            return new ContentWidths(min, max);
        }

        /// <summary>
        /// The narrowest a line that holds the segment <c>[start, contentEnd)</c> can be, indent included: the segment as one piece, or with hyphenation the
        /// widest piece of it between two places it can be broken, with the hyphen the piece ends with.
        /// </summary>
        private double MinimumWidth(int start, int contentEnd, double indent)
        {
            if (Style.Hyphens == Hyphens.None)
            {
                return Measure(start, contentEnd, indent) + indent;
            }

            var points = Style.Hyphens == Hyphens.Auto ? HyphenationPoints(start, contentEnd) : [];
            double widest = 0;
            int from = start;
            foreach (int point in points)
            {
                widest = Math.Max(widest, Measure(from, point, from == start ? indent : 0) + HyphenAt(point).Width + (from == start ? indent : 0));
                from = point;
            }

            double last = Measure(from, contentEnd, from == start ? indent : 0) + (from == start ? indent : 0);
            if (contentEnd > from && Text[contentEnd - 1] == SoftHyphen)
            {
                last += HyphenAt(contentEnd).Width;
            }

            return Math.Max(widest, last);
        }

        /// <summary>The end of the text <c>[start, end)</c> once the hanging spaces and line-ending characters at its end are left out.</summary>
        internal int ContentEnd(int start, int end)
        {
            while (end > start && (IsHangingSpace(Text[end - 1]) || IsLineTerminator(Text[end - 1])))
            {
                end--;
            }

            return end;
        }
    }

    /// <summary>
    /// Collects the text and the runs of a <see cref="Paragraph"/>.
    /// </summary>
    public sealed class ParagraphBuilder
    {
        private readonly StringBuilder _text = new();
        private readonly List<(int Start, RunStyle Style)> _runs = [];
        private readonly Stack<RunStyle> _stack = new();
        private readonly Dictionary<int, InlineBox> _boxes = [];
        private ParagraphStyle _style = new();

        /// <summary>
        /// Creates a builder whose text is set in <paramref name="baseStyle"/> until a run is pushed.
        /// </summary>
        /// <param name="baseStyle">The style of text that no pushed run covers.</param>
        /// <exception cref="ArgumentNullException">The style has no typeface.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The size is not a positive, finite number, or a spacing is not finite.</exception>
        public ParagraphBuilder(RunStyle baseStyle)
        {
            Validate(baseStyle, nameof(baseStyle));
            _stack.Push(baseStyle);
        }

        /// <summary>Sets how the paragraph as a whole is set.</summary>
        /// <param name="style">The paragraph style.</param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The text indent, the hyphenation zone, a hyphenation limit or the most lines is not a finite number in its range, or the hyphenation character is not from 1 to 32 UTF-16 units long, or the ellipsis is longer than 32.</exception>
        public ParagraphBuilder SetStyle(ParagraphStyle style)
        {
            if (!double.IsFinite(style.TextIndent.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(style), style.TextIndent.Length, "The text indent must be a finite number.");
            }

            var limits = style.HyphenateLimitChars;
            if (limits.WordLength < 0 || limits.BeforeBreak < 0 || limits.AfterBreak < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(style), limits, "The hyphenation limits must not be negative.");
            }

            if (style.HyphenateLimitLines < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(style), style.HyphenateLimitLines, "The limit on hyphenated lines must not be negative.");
            }

            if (!double.IsFinite(style.HyphenateLimitZone) || style.HyphenateLimitZone < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(style), style.HyphenateLimitZone, "The hyphenation zone must be a finite number, zero or more.");
            }

            if (style.MaxLines < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(style), style.MaxLines, "The most lines must be one or more.");
            }

            if (style.Ellipsis is { Length: > 32 })
            {
                throw new ArgumentOutOfRangeException(nameof(style), style.Ellipsis.Length, "The ellipsis must be at most 32 UTF-16 units long.");
            }

            if (style.HyphenateCharacter is { } hyphen && (hyphen.Length is < 1 or > 32))
            {
                throw new ArgumentOutOfRangeException(nameof(style), hyphen.Length, "The hyphenation character must be from 1 to 32 UTF-16 units long.");
            }

            _style = style;
            return this;
        }

        /// <summary>Starts a run: the text added until the matching <see cref="PopRun"/> is set in <paramref name="style"/>.</summary>
        /// <param name="style">The style of the run.</param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentNullException">The style has no typeface.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The size is not a positive, finite number, or a spacing is not finite.</exception>
        public ParagraphBuilder PushRun(RunStyle style)
        {
            Validate(style, nameof(style));
            _stack.Push(style);
            return this;
        }

        private static void Validate(RunStyle style, string name)
        {
            ArgumentNullException.ThrowIfNull(style.Typeface, name);
            if (!double.IsFinite(style.Size) || style.Size <= 0)
            {
                throw new ArgumentOutOfRangeException(name, style.Size, "The size must be a positive, finite number.");
            }

            if (!double.IsFinite(style.LetterSpacing) || !double.IsFinite(style.WordSpacing))
            {
                throw new ArgumentOutOfRangeException(name, "The letter and word spacing must be finite numbers.");
            }
        }

        /// <summary>Ends the innermost run started by <see cref="PushRun"/>.</summary>
        /// <returns>This builder.</returns>
        /// <exception cref="InvalidOperationException">There is no run to end.</exception>
        public ParagraphBuilder PopRun()
        {
            if (_stack.Count <= 1)
            {
                throw new InvalidOperationException("There is no pushed run to pop.");
            }

            _stack.Pop();
            return this;
        }

        /// <summary>Adds text in the current run.</summary>
        /// <param name="text">The text.</param>
        /// <returns>This builder.</returns>
        public ParagraphBuilder AddText(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (text.Length == 0)
            {
                return this;
            }

            var current = _stack.Peek();
            if (_runs.Count == 0 || !_runs[^1].Style.Equals(current))
            {
                _runs.Add((_text.Length, current));
            }

            _text.Append(text);
            return this;
        }

        /// <summary>Adds an inline box in the current run: it is one character of the paragraph's text (U+FFFC), sized as given.</summary>
        /// <param name="box">The box.</param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentOutOfRangeException">A size is negative, or a number is not finite or is more than 1,000,000,000 (in either direction, for the baseline and its shift).</exception>
        public ParagraphBuilder AddInlineBox(InlineBox box)
        {
            const double Limit = 1e9;
            if (!(box.Width >= 0 && box.Width <= Limit) || !(box.Height >= 0 && box.Height <= Limit))
            {
                throw new ArgumentOutOfRangeException(nameof(box), box, "The size of a box must be from zero to 1,000,000,000.");
            }

            if (box.Baseline is { } baseline && !(Math.Abs(baseline) <= Limit) || !(Math.Abs(box.BaselineShift) <= Limit))
            {
                throw new ArgumentOutOfRangeException(nameof(box), box, "The baseline and its shift must be at most 1,000,000,000 either way.");
            }

            if (!Enum.IsDefined(box.VerticalAlign))
            {
                throw new ArgumentOutOfRangeException(nameof(box), box.VerticalAlign, "The alignment is not one of the values.");
            }

            _boxes[_text.Length] = box;
            return AddText("\uFFFC");
        }

        /// <summary>Builds the paragraph from what has been added.</summary>
        /// <returns>The paragraph.</returns>
        public Paragraph Build()
        {
            var text = _text.ToString();
            var runs = new (int, int, RunStyle)[Math.Max(1, _runs.Count)];
            if (_runs.Count == 0)
            {
                runs[0] = (0, 0, _stack.Peek());
            }

            for (int i = 0; i < _runs.Count; i++)
            {
                int end = i + 1 < _runs.Count ? _runs[i + 1].Start : text.Length;
                runs[i] = (_runs[i].Start, end, _runs[i].Style);
            }

            return new Paragraph(text, runs, _style, new Dictionary<int, InlineBox>(_boxes));
        }
    }
}
