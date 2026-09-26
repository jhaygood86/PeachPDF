using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
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

        private readonly (int Start, int End, RunStyle Style)[] _runs;
        private readonly BidiAnalysis _bidi;
        private readonly string[] _scripts;
        private readonly ArabicJoiningForm[] _joining;
        private readonly UseCategory[]? _use;
        private readonly bool[] _isGraphemeBoundary;
        private readonly Atom[] _atoms;
        private const int MaxShapedPieces = 8192;

        private readonly Dictionary<(int, int), GlyphRun> _shaped = [];

        /// <summary>A piece of the text that is shaped as one: one style, one direction level and one script.</summary>
        internal readonly record struct Atom(int Start, int End, int Run, byte Level, string Script);

        internal Paragraph(string text, (int, int, RunStyle)[] runs, ParagraphStyle style)
        {
            Text = text;
            Style = style;
            _runs = runs;
            _bidi = Bidi.Analyze(text, style.Direction);

            (_scripts, _joining, _use) = ResolveScripts(text);
            Opportunities = LineBreaker.FindOpportunities(text, style.LineBreak);

            _isGraphemeBoundary = new bool[text.Length + 1];
            foreach (var boundary in Segmenter.FindGraphemeBoundaries(text))
            {
                _isGraphemeBoundary[boundary] = true;
            }

            _isGraphemeBoundary[0] = true;
            _isGraphemeBoundary[text.Length] = true;
            _atoms = BuildAtoms();
        }

        /// <summary>The text of the paragraph.</summary>
        public string Text { get; }

        /// <summary>The style the paragraph was built with.</summary>
        public ParagraphStyle Style { get; }

        /// <summary>Whether the paragraph as a whole runs right to left.</summary>
        public bool IsRightToLeft => _bidi.IsParagraphRtl;

        internal LineBreakOpportunity[] Opportunities { get; }

        internal byte ParagraphLevel => _bidi.ParagraphLevel;

        internal byte[] Levels => _bidi.Levels;

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
                i++;
                while (i < length
                    && !IsLineTerminator(Text[i])
                    && _runs[run].End > i
                    && _bidi.Levels[i] == level
                    && _scripts[i] == script)
                {
                    i++;
                }

                atoms.Add(new Atom(start, i, run, level, script));
            }

            return atoms.ToArray();
        }

        private static (string[] Scripts, ArabicJoiningForm[] Joining, UseCategory[]? Use) ResolveScripts(string text)
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
            for (int c = 0; c < resolved.Count; c++)
            {
                if (UseShapedScripts.Contains(resolved[c]))
                {
                    categories ??= new UseCategory[codepoints.Count];
                    categories[c] = UniversalShaping.Classify(codepoints[c]);
                }
            }

            var scripts = new string[length];
            var charForms = new ArabicJoiningForm[length];
            UseCategory[]? charCategories = categories is null ? null : new UseCategory[length];
            for (int c = 0; c < length; c++)
            {
                int cp = codepointOfChar[c];
                scripts[c] = resolved[cp];
                charForms[c] = forms[cp];
                if (charCategories is not null)
                {
                    charCategories[c] = categories![cp];
                }
            }

            return (scripts, charForms, charCategories);
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

            var style = _runs[atom.Run].Style;
            var settings = style.Shape ?? ShapeSettings.Default;
            settings = settings with
            {
                ScriptTag = OpenTypeTags.ForScript(atom.Script) ?? settings.ScriptTag,
                ReverseForDisplay = (atom.Level & 1) == 1,
            };

            var pieceText = Text.Substring(start, end - start);
            var forms = new List<ArabicJoiningForm>();
            List<UseCategory>? categories = _use is null ? null : [];
            bool anyJoining = false;
            for (int i = start; i < end;)
            {
                Rune.DecodeFromUtf16(Text.AsSpan(i), out _, out int consumed);
                forms.Add(_joining[i]);
                anyJoining |= _joining[i] != ArabicJoiningForm.None;
                categories?.Add(_use![i]);
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

            var run = Shaper.Shape(style.Typeface, pieceText, settings);
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

        /// <summary>The width, in layout units, of a shaped piece set in <paramref name="style"/>.</summary>
        internal static double WidthOf(GlyphRun run, RunStyle style) => run.Advance * style.Size / run.Typeface.Metrics.UnitsPerEm;

        /// <summary>The width of the text <c>[start, end)</c> laid on one line, with the line-ending characters left out.</summary>
        internal double Measure(int start, int end)
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
                    width += WidthOf(ShapePiece(atom, from, to), _runs[atom.Run].Style);
                }
            }

            return width;
        }

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
                if (Style.OverflowWrap == OverflowWrap.Anywhere)
                {
                    for (int g = segmentStart; g < contentEnd;)
                    {
                        int next = g + 1;
                        while (next < contentEnd && !_isGraphemeBoundary[next])
                        {
                            next++;
                        }

                        min = Math.Max(min, Measure(g, next));
                        g = next;
                    }
                }
                else
                {
                    min = Math.Max(min, Measure(segmentStart, contentEnd));
                }

                if (opportunities[i] == LineBreakOpportunity.Mandatory)
                {
                    max = Math.Max(max, Measure(lineStart, ContentEnd(lineStart, i)));
                    lineStart = i;
                }

                segmentStart = i;
            }

            return new ContentWidths(min, max);
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
        private ParagraphStyle _style = new();

        /// <summary>
        /// Creates a builder whose text is set in <paramref name="baseStyle"/> until a run is pushed.
        /// </summary>
        /// <param name="baseStyle">The style of text that no pushed run covers.</param>
        /// <exception cref="ArgumentNullException">The style has no typeface.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The size is not a positive, finite number.</exception>
        public ParagraphBuilder(RunStyle baseStyle)
        {
            Validate(baseStyle, nameof(baseStyle));
            _stack.Push(baseStyle);
        }

        /// <summary>Sets how the paragraph as a whole is set.</summary>
        /// <param name="style">The paragraph style.</param>
        /// <returns>This builder.</returns>
        public ParagraphBuilder SetStyle(ParagraphStyle style)
        {
            _style = style;
            return this;
        }

        /// <summary>Starts a run: the text added until the matching <see cref="PopRun"/> is set in <paramref name="style"/>.</summary>
        /// <param name="style">The style of the run.</param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentNullException">The style has no typeface.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The size is not a positive, finite number.</exception>
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

            return new Paragraph(text, runs, _style);
        }
    }
}
