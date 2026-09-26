#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Reader for the OpenType `MATH` table: MathConstants (the ~50 named layout constants MathML Core's
// layout algorithm reads for fractions, radicals, sub/superscripts, stacks and limits),
// MathGlyphInfo (per-glyph italics correction, top-accent attachment, and extended-shape coverage),
// and MathVariants (pre-sized glyph variants and glyph-assembly parts for stretchy operators,
// radical signs, and stretched accents).
//
// Every value in this table is read eagerly, in design units (FUnits) - unlike GSUB/GPOS's lazy
// per-lookup parsing, the MATH table's total data volume is modest (no lookup-list tree to defer),
// so there is nothing to gain from deferring any of it. MathTable instances are still cached and
// shared process-wide exactly like GsubTable/GdefTable/GposTable (see GdefTable.cs), so the whole
// eager parse is still wrapped in `lock (face.SyncRoot)` against the shared, mutable-cursor OpenTypeFontface.
//
// Not read: each MathValueRecord's own deviceOffset (device-table pixel corrections for specific
// PPEM sizes) - irrelevant for PDF output, which is resolution-independent vector content, not
// rendered at any specific pixel size. Also not read: MathKernInfo (per-glyph corner kerning for
// sub/superscript placement) - a fine-grained spacing refinement, not required for correct layout;
// left as a possible future enhancement rather than v1 scope.
//
// https://learn.microsoft.com/en-us/typography/opentype/spec/math
//
#endregion

using PeachDrawing.Text.Internal.Fonts.OpenType;
using System.Collections.Generic;

namespace PeachDrawing.Text.OpenType
{
    /// <summary>One pre-sized variant of a glyph that can stretch (the <c>MathGlyphVariantRecord</c> of the MATH table).</summary>
    /// <param name="GlyphId">The alternate glyph.</param>
    /// <param name="AdvanceMeasurement">How far the variant extends in the direction it grows: its advance width for a horizontally growing one, and its advance height for a vertically growing one, in design units.</param>
    public readonly record struct MathGlyphVariant(ushort GlyphId, double AdvanceMeasurement);

    /// <summary>One part of a glyph assembly (the <c>GlyphPart</c> record of the MATH table).</summary>
    /// <param name="GlyphId">The part's glyph.</param>
    /// <param name="StartConnectorLength">How much of the start of the part can overlap the connector of the part before it.</param>
    /// <param name="EndConnectorLength">How much of the end of the part can overlap the connector of the part after it.</param>
    /// <param name="FullAdvance">The part's full advance in the direction of assembly.</param>
    /// <param name="IsExtender">Whether the part may be repeated, or left out, to reach a target size.</param>
    public readonly record struct MathGlyphPart(
        ushort GlyphId,
        double StartConnectorLength,
        double EndConnectorLength,
        double FullAdvance,
        bool IsExtender);

    /// <summary>How to build a stretchy shape out of glyph parts when no pre-sized <see cref="MathGlyphVariant"/> is large enough (the <c>GlyphAssembly</c> table).</summary>
    public sealed class MathGlyphAssembly
    {
        internal MathGlyphAssembly()
        {
        }

        /// <summary>The italics correction of the assembled glyph, in design units.</summary>
        public double ItalicsCorrection { get; internal init; }

        /// <summary>The parts, from left to right for a horizontal extension and from bottom to top for a vertical one.</summary>
        public IReadOnlyList<MathGlyphPart> Parts { get; internal init; } = [];
    }

    /// <summary>Everything needed to find or build an enlarged version of one glyph (the <c>MathGlyphConstruction</c> table).</summary>
    public sealed class MathGlyphConstruction
    {
        internal MathGlyphConstruction()
        {
        }

        /// <summary>How to assemble the glyph from parts for a size beyond the largest variant, or <see langword="null"/> when the font gives none.</summary>
        public MathGlyphAssembly? Assembly { get; internal init; }

        /// <summary>The pre-sized variants, from the smallest to the largest.</summary>
        public IReadOnlyList<MathGlyphVariant> Variants { get; internal init; } = [];
    }

    /// <summary>
    /// The named constants a math layout algorithm reads to position fractions, radicals, scripts, stacks and limits (the
    /// <c>MathConstants</c> table).
    /// </summary>
    /// <remarks>
    /// Every value is in design units, apart from the percentages, so a length at a size is the value times the size divided by
    /// <see cref="TypefaceMetrics.UnitsPerEm"/>. The values are those of the table's records; the device tables that
    /// adjust a value at particular pixel sizes are not read.
    /// </remarks>
    public sealed class MathConstantsTable
    {
        /// <summary>The percentage a level 1 superscript or subscript is scaled down to, such as 80.</summary>
        public double ScriptPercentScaleDown { get; }
        /// <summary>The percentage a level 2 superscript or subscript is scaled down to, such as 60.</summary>
        public double ScriptScriptPercentScaleDown { get; }
        /// <summary>The least height a delimited expression must have to be treated as a subformula.</summary>
        public double DelimitedSubFormulaMinHeight { get; }
        /// <summary>The least height of an n-ary operator (an integral or a summation) in display style.</summary>
        public double DisplayOperatorMinHeight { get; }
        /// <summary>The white space to leave between formulas so that lines keep a proper spacing.</summary>
        public double MathLeading { get; }
        /// <summary>The height of the math axis above the baseline: the line that fraction bars and operators are centred on.</summary>
        public double AxisHeight { get; }
        /// <summary>The tallest base an accent is placed on without being raised: on a taller base the accent moves up with it.</summary>
        public double AccentBaseHeight { get; }
        /// <summary>The least height of a base for which the flattened form of an accent is used.</summary>
        public double FlattenedAccentBaseHeight { get; }
        /// <summary>The standard distance a subscript is shifted down from the baseline.</summary>
        public double SubscriptShiftDown { get; }
        /// <summary>The greatest height of the top of a subscript that does not need moving further down.</summary>
        public double SubscriptTopMax { get; }
        /// <summary>The least drop of a subscript's baseline below the bottom of its base.</summary>
        public double SubscriptBaselineDropMin { get; }
        /// <summary>The standard distance a superscript is shifted up from the baseline.</summary>
        public double SuperscriptShiftUp { get; }
        /// <summary>The standard distance a superscript is shifted up in cramped style.</summary>
        public double SuperscriptShiftUpCramped { get; }
        /// <summary>The least height of the bottom of a superscript that does not need moving further up.</summary>
        public double SuperscriptBottomMin { get; }
        /// <summary>The greatest drop of a superscript's baseline below the top of its base.</summary>
        public double SuperscriptBaselineDropMax { get; }
        /// <summary>The least gap between the bottom of a superscript and the top of a subscript when both are present.</summary>
        public double SubSuperscriptGapMin { get; }
        /// <summary>The greatest height the bottom of a superscript is pushed up to open a gap from a subscript, before the subscript starts moving down.</summary>
        public double SuperscriptBottomMaxWithSubscript { get; }
        /// <summary>The extra white space added after each subscript and superscript.</summary>
        public double SpaceAfterScript { get; }
        /// <summary>The least gap between the bottom of an upper limit and the top of its base operator.</summary>
        public double UpperLimitGapMin { get; }
        /// <summary>The least distance between the baseline of an upper limit and the top of its base operator.</summary>
        public double UpperLimitBaselineRiseMin { get; }
        /// <summary>The least gap between the top of a lower limit and the bottom of its base operator.</summary>
        public double LowerLimitGapMin { get; }
        /// <summary>The least distance between the baseline of a lower limit and the bottom of its base operator.</summary>
        public double LowerLimitBaselineDropMin { get; }
        /// <summary>The standard distance the top element of a stack is shifted up.</summary>
        public double StackTopShiftUp { get; }
        /// <summary>The standard distance the top element of a stack is shifted up in display style.</summary>
        public double StackTopDisplayStyleShiftUp { get; }
        /// <summary>The standard distance the bottom element of a stack is shifted down.</summary>
        public double StackBottomShiftDown { get; }
        /// <summary>The standard distance the bottom element of a stack is shifted down in display style.</summary>
        public double StackBottomDisplayStyleShiftDown { get; }
        /// <summary>The least gap between the bottom of the top element of a stack and the top of the bottom element.</summary>
        public double StackGapMin { get; }
        /// <summary>The least gap between the elements of a stack in display style.</summary>
        public double StackDisplayStyleGapMin { get; }
        /// <summary>The standard distance the top element of a stretch stack is shifted up.</summary>
        public double StretchStackTopShiftUp { get; }
        /// <summary>The standard distance the bottom element of a stretch stack is shifted down.</summary>
        public double StretchStackBottomShiftDown { get; }
        /// <summary>The least gap between the ink of the stretched element of a stretch stack and the bottom of the element above it.</summary>
        public double StretchStackGapAboveMin { get; }
        /// <summary>The least gap between the ink of the stretched element of a stretch stack and the top of the element below it.</summary>
        public double StretchStackGapBelowMin { get; }
        /// <summary>The standard distance a numerator is shifted up.</summary>
        public double FractionNumeratorShiftUp { get; }
        /// <summary>The standard distance a numerator is shifted up in display style.</summary>
        public double FractionNumeratorDisplayStyleShiftUp { get; }
        /// <summary>The standard distance a denominator is shifted down.</summary>
        public double FractionDenominatorShiftDown { get; }
        /// <summary>The standard distance a denominator is shifted down in display style.</summary>
        public double FractionDenominatorDisplayStyleShiftDown { get; }
        /// <summary>The least gap between the bottom of a numerator and the fraction rule.</summary>
        public double FractionNumeratorGapMin { get; }
        /// <summary>The least gap between the bottom of a numerator and the fraction rule in display style.</summary>
        public double FractionNumDisplayStyleGapMin { get; }
        /// <summary>The thickness of the fraction rule.</summary>
        public double FractionRuleThickness { get; }
        /// <summary>The least gap between the fraction rule and the top of a denominator.</summary>
        public double FractionDenominatorGapMin { get; }
        /// <summary>The least gap between the fraction rule and the top of a denominator in display style.</summary>
        public double FractionDenomDisplayStyleGapMin { get; }
        /// <summary>The horizontal distance between the top and bottom elements of a skewed fraction.</summary>
        public double SkewedFractionHorizontalGap { get; }
        /// <summary>The vertical distance between the ink of the top and bottom elements of a skewed fraction.</summary>
        public double SkewedFractionVerticalGap { get; }
        /// <summary>The distance between an overbar and the top of its base.</summary>
        public double OverbarVerticalGap { get; }
        /// <summary>The thickness of an overbar.</summary>
        public double OverbarRuleThickness { get; }
        /// <summary>The extra white space reserved above an overbar.</summary>
        public double OverbarExtraAscender { get; }
        /// <summary>The distance between an underbar and the bottom of its base.</summary>
        public double UnderbarVerticalGap { get; }
        /// <summary>The thickness of an underbar.</summary>
        public double UnderbarRuleThickness { get; }
        /// <summary>The extra white space reserved below an underbar.</summary>
        public double UnderbarExtraDescender { get; }
        /// <summary>The space between the top of the radicand and the radical rule, in text style.</summary>
        public double RadicalVerticalGap { get; }
        /// <summary>The space between the top of the radicand and the radical rule, in display style.</summary>
        public double RadicalDisplayStyleVerticalGap { get; }
        /// <summary>The thickness of the radical rule.</summary>
        public double RadicalRuleThickness { get; }
        /// <summary>The extra white space reserved above a radical.</summary>
        public double RadicalExtraAscender { get; }
        /// <summary>The extra white space reserved before the degree of a radical.</summary>
        public double RadicalKernBeforeDegree { get; }
        /// <summary>The kern after the degree of a radical, which is usually negative.</summary>
        public double RadicalKernAfterDegree { get; }
        /// <summary>The height of the bottom of the degree of a radical as a percentage of the ascent of the radical sign.</summary>
        public double RadicalDegreeBottomRaisePercent { get; }

        internal MathConstantsTable(OpenTypeFontface face, int tableStart)
        {
            face.Position = tableStart;

            ScriptPercentScaleDown = face.ReadShort();
            ScriptScriptPercentScaleDown = face.ReadShort();
            DelimitedSubFormulaMinHeight = face.ReadUShort();
            DisplayOperatorMinHeight = face.ReadUShort();
            MathLeading = ReadValue(face);
            AxisHeight = ReadValue(face);
            AccentBaseHeight = ReadValue(face);
            FlattenedAccentBaseHeight = ReadValue(face);
            SubscriptShiftDown = ReadValue(face);
            SubscriptTopMax = ReadValue(face);
            SubscriptBaselineDropMin = ReadValue(face);
            SuperscriptShiftUp = ReadValue(face);
            SuperscriptShiftUpCramped = ReadValue(face);
            SuperscriptBottomMin = ReadValue(face);
            SuperscriptBaselineDropMax = ReadValue(face);
            SubSuperscriptGapMin = ReadValue(face);
            SuperscriptBottomMaxWithSubscript = ReadValue(face);
            SpaceAfterScript = ReadValue(face);
            UpperLimitGapMin = ReadValue(face);
            UpperLimitBaselineRiseMin = ReadValue(face);
            LowerLimitGapMin = ReadValue(face);
            LowerLimitBaselineDropMin = ReadValue(face);
            StackTopShiftUp = ReadValue(face);
            StackTopDisplayStyleShiftUp = ReadValue(face);
            StackBottomShiftDown = ReadValue(face);
            StackBottomDisplayStyleShiftDown = ReadValue(face);
            StackGapMin = ReadValue(face);
            StackDisplayStyleGapMin = ReadValue(face);
            StretchStackTopShiftUp = ReadValue(face);
            StretchStackBottomShiftDown = ReadValue(face);
            StretchStackGapAboveMin = ReadValue(face);
            StretchStackGapBelowMin = ReadValue(face);
            FractionNumeratorShiftUp = ReadValue(face);
            FractionNumeratorDisplayStyleShiftUp = ReadValue(face);
            FractionDenominatorShiftDown = ReadValue(face);
            FractionDenominatorDisplayStyleShiftDown = ReadValue(face);
            FractionNumeratorGapMin = ReadValue(face);
            FractionNumDisplayStyleGapMin = ReadValue(face);
            FractionRuleThickness = ReadValue(face);
            FractionDenominatorGapMin = ReadValue(face);
            FractionDenomDisplayStyleGapMin = ReadValue(face);
            SkewedFractionHorizontalGap = ReadValue(face);
            SkewedFractionVerticalGap = ReadValue(face);
            OverbarVerticalGap = ReadValue(face);
            OverbarRuleThickness = ReadValue(face);
            OverbarExtraAscender = ReadValue(face);
            UnderbarVerticalGap = ReadValue(face);
            UnderbarRuleThickness = ReadValue(face);
            UnderbarExtraDescender = ReadValue(face);
            RadicalVerticalGap = ReadValue(face);
            RadicalDisplayStyleVerticalGap = ReadValue(face);
            RadicalRuleThickness = ReadValue(face);
            RadicalExtraAscender = ReadValue(face);
            RadicalKernBeforeDegree = ReadValue(face);
            RadicalKernAfterDegree = ReadValue(face);
            RadicalDegreeBottomRaisePercent = face.ReadShort();
        }

        /// <summary>Reads one MathValueRecord: a signed FWORD value followed by a device-table
        /// offset this reader intentionally discards (see file header).</summary>
        static double ReadValue(OpenTypeFontface face)
        {
            short value = face.ReadShort();
            face.ReadUShort(); // deviceOffset - ignored
            return value;
        }
    }

    /// <summary>Per-glyph italics correction (<c>MathItalicsCorrectionInfo</c>) or top-accent
    /// attachment (<c>MathTopAccentAttachment</c>) - both tables share this exact shape (a Coverage
    /// table plus one MathValueRecord per covered glyph, in coverage order), so one reader serves
    /// both.</summary>
    internal sealed class MathPerGlyphValueTable
    {
        readonly CoverageTable _coverage;
        readonly double[] _values;

        MathPerGlyphValueTable(CoverageTable coverage, double[] values)
        {
            _coverage = coverage;
            _values = values;
        }

        public static MathPerGlyphValueTable Read(OpenTypeFontface face, int offset)
        {
            face.Position = offset;
            int coverageOffset = offset + face.ReadUShort();
            int count = face.ReadUShort();

            var values = new double[count];
            for (int i = 0; i < count; i++)
            {
                short value = face.ReadShort();
                face.ReadUShort(); // deviceOffset - ignored, see MathTable.cs file header
                values[i] = value;
            }

            return new MathPerGlyphValueTable(CoverageTable.Read(face, coverageOffset), values);
        }

        /// <summary>Returns the covered value for <paramref name="glyphId"/>, or 0 if the font
        /// provides none (both tables' spec text defines an uncovered glyph's value as zero).</summary>
        public double GetValue(ushort glyphId)
        {
            int index = _coverage.IndexOfGlyph(glyphId);
            return index >= 0 && index < _values.Length ? _values[index] : 0;
        }

        /// <summary>Whether <paramref name="glyphId"/> is covered at all - distinguishes "uncovered"
        /// from "covered with an explicit value of 0", which <see cref="GetValue"/> alone can't.</summary>
        public bool HasValue(ushort glyphId) => _coverage.IndexOfGlyph(glyphId) >= 0;
    }

    /// <summary>The <c>MathGlyphInfo</c> table: per-glyph italics correction, top-accent attachment, and which glyphs are extended shapes.</summary>
    /// <remarks>The per-glyph corner kerning of the <c>MathKernInfo</c> table is not read.</remarks>
    public sealed class MathGlyphInfoTable
    {
        readonly MathPerGlyphValueTable? _italicsCorrection;
        readonly MathPerGlyphValueTable? _topAccentAttachment;
        readonly CoverageTable? _extendedShapeCoverage;

        internal MathGlyphInfoTable(OpenTypeFontface face, int tableStart)
        {
            face.Position = tableStart;

            // Read every header offset first, before dereferencing any of them - each Read call
            // below moves the shared cursor as a side effect, so an offset not yet captured before
            // that point would be lost (same ordering rule GdefTable's constructor documents).
            int italicsCorrectionOffset = face.ReadUShort();
            int topAccentAttachmentOffset = face.ReadUShort();
            int extendedShapeCoverageOffset = face.ReadUShort();
            face.ReadUShort(); // mathKernInfoOffset - not read, see file header

            _italicsCorrection = italicsCorrectionOffset != 0
                ? MathPerGlyphValueTable.Read(face, tableStart + italicsCorrectionOffset)
                : null;
            _topAccentAttachment = topAccentAttachmentOffset != 0
                ? MathPerGlyphValueTable.Read(face, tableStart + topAccentAttachmentOffset)
                : null;
            _extendedShapeCoverage = extendedShapeCoverageOffset != 0
                ? CoverageTable.Read(face, tableStart + extendedShapeCoverageOffset)
                : null;
        }

        /// <summary>The glyph's italics correction, in design units, or 0 if the font provides none
        /// for this glyph (including when the font has no MathItalicsCorrectionInfo table at all).</summary>
        /// <param name="glyphId">The glyph.</param>
        public double GetItalicsCorrection(ushort glyphId) => _italicsCorrection?.GetValue(glyphId) ?? 0;

        /// <summary>The glyph's top-accent horizontal attachment point, in design units, or null if
        /// the font provides none for this glyph - callers fall back to the glyph's own geometric
        /// center (advance width / 2), per the OpenType spec's own guidance for an uncovered glyph.</summary>
        /// <param name="glyphId">The glyph.</param>
        public double? GetTopAccentAttachment(ushort glyphId)
        {
            if (_topAccentAttachment is null)
                return null;

            // MathPerGlyphValueTable.GetValue can't itself distinguish "uncovered" from "covered
            // with value 0", so re-check coverage directly rather than trusting a 0 return.
            return _topAccentAttachment.HasValue(glyphId) ? _topAccentAttachment.GetValue(glyphId) : null;
        }

        /// <summary>Whether the font lists the glyph as an extended shape (its <c>ExtendedShapeCoverage</c> table), which a math layout treats differently from an ordinary glyph when it positions scripts and accents.</summary>
        /// <param name="glyphId">The glyph.</param>
        public bool IsExtendedShape(ushort glyphId) => _extendedShapeCoverage?.IndexOfGlyph(glyphId) >= 0;
    }

    /// <summary>The <c>MathVariants</c> table: for glyphs that have to stretch (fences, radicals, accents, arrows), the pre-sized variants or the glyph parts to grow them vertically or horizontally.</summary>
    public sealed class MathVariantsTable
    {
        readonly CoverageTable? _vertCoverage;
        readonly CoverageTable? _horizCoverage;
        readonly MathGlyphConstruction?[] _vertConstructions;
        readonly MathGlyphConstruction?[] _horizConstructions;

        /// <summary>The least overlap of the connectors of two adjacent parts of an assembly, in design units.</summary>
        public double MinConnectorOverlap { get; }

        internal MathVariantsTable(OpenTypeFontface face, int tableStart)
        {
            face.Position = tableStart;

            MinConnectorOverlap = face.ReadUShort();
            int vertGlyphCoverageOffset = face.ReadUShort();
            int horizGlyphCoverageOffset = face.ReadUShort();
            int vertGlyphCount = face.ReadUShort();
            int horizGlyphCount = face.ReadUShort();

            var vertConstructionOffsets = new int[vertGlyphCount];
            for (int i = 0; i < vertGlyphCount; i++)
                vertConstructionOffsets[i] = face.ReadUShort();

            var horizConstructionOffsets = new int[horizGlyphCount];
            for (int i = 0; i < horizGlyphCount; i++)
                horizConstructionOffsets[i] = face.ReadUShort();

            _vertCoverage = vertGlyphCoverageOffset != 0
                ? CoverageTable.Read(face, tableStart + vertGlyphCoverageOffset)
                : null;
            _horizCoverage = horizGlyphCoverageOffset != 0
                ? CoverageTable.Read(face, tableStart + horizGlyphCoverageOffset)
                : null;

            _vertConstructions = new MathGlyphConstruction?[vertGlyphCount];
            for (int i = 0; i < vertGlyphCount; i++)
                _vertConstructions[i] = ReadGlyphConstruction(face, tableStart + vertConstructionOffsets[i]);

            _horizConstructions = new MathGlyphConstruction?[horizGlyphCount];
            for (int i = 0; i < horizGlyphCount; i++)
                _horizConstructions[i] = ReadGlyphConstruction(face, tableStart + horizConstructionOffsets[i]);
        }

        /// <summary>How a glyph grows vertically.</summary>
        /// <param name="glyphId">The glyph.</param>
        /// <returns>The variants and assembly, or <see langword="null"/> when the glyph does not grow vertically.</returns>
        public MathGlyphConstruction? GetVerticalConstruction(ushort glyphId) =>
            GetConstruction(_vertCoverage, _vertConstructions, glyphId);

        /// <summary>How a glyph grows horizontally.</summary>
        /// <param name="glyphId">The glyph.</param>
        /// <returns>The variants and assembly, or <see langword="null"/> when the glyph does not grow horizontally.</returns>
        public MathGlyphConstruction? GetHorizontalConstruction(ushort glyphId) =>
            GetConstruction(_horizCoverage, _horizConstructions, glyphId);

        static MathGlyphConstruction? GetConstruction(
            CoverageTable? coverage, MathGlyphConstruction?[] constructions, ushort glyphId)
        {
            if (coverage is null)
                return null;

            int index = coverage.IndexOfGlyph(glyphId);
            return index >= 0 && index < constructions.Length ? constructions[index] : null;
        }

        static MathGlyphConstruction ReadGlyphConstruction(OpenTypeFontface face, int offset)
        {
            face.Position = offset;
            int glyphAssemblyOffset = face.ReadUShort();
            int variantCount = face.ReadUShort();

            var variants = new MathGlyphVariant[variantCount];
            for (int i = 0; i < variantCount; i++)
            {
                ushort variantGlyph = face.ReadUShort();
                ushort advanceMeasurement = face.ReadUShort();
                variants[i] = new MathGlyphVariant(variantGlyph, advanceMeasurement);
            }

            return new MathGlyphConstruction
            {
                Assembly = glyphAssemblyOffset != 0 ? ReadGlyphAssembly(face, offset + glyphAssemblyOffset) : null,
                Variants = System.Array.AsReadOnly(variants),
            };
        }

        static MathGlyphAssembly ReadGlyphAssembly(OpenTypeFontface face, int offset)
        {
            face.Position = offset;
            short italicsCorrectionValue = face.ReadShort();
            face.ReadUShort(); // deviceOffset - ignored, see MathTable.cs file header
            int partCount = face.ReadUShort();

            var parts = new MathGlyphPart[partCount];
            for (int i = 0; i < partCount; i++)
            {
                ushort glyphId = face.ReadUShort();
                ushort startConnectorLength = face.ReadUShort();
                ushort endConnectorLength = face.ReadUShort();
                ushort fullAdvance = face.ReadUShort();
                ushort partFlags = face.ReadUShort();
                parts[i] = new MathGlyphPart(
                    glyphId, startConnectorLength, endConnectorLength, fullAdvance,
                    IsExtender: (partFlags & 0x0001) != 0);
            }

            return new MathGlyphAssembly { ItalicsCorrection = italicsCorrectionValue, Parts = System.Array.AsReadOnly(parts) };
        }
    }

    /// <summary>The <c>MATH</c> table of a font that is made for setting mathematics: <see cref="Constants"/>, <see cref="GlyphInfo"/> and <see cref="Variants"/>.</summary>
    public sealed class MathTable
    {
        /// <summary>The layout constants.</summary>
        public MathConstantsTable Constants { get; }

        /// <summary>The per-glyph information.</summary>
        public MathGlyphInfoTable GlyphInfo { get; }

        /// <summary>The variants and assemblies of glyphs that stretch.</summary>
        public MathVariantsTable Variants { get; }

        internal MathTable(OpenTypeFontface face, int tableStart)
        {
            // MathTable instances are cached and shared process-wide, exactly like GsubTable/
            // GdefTable/GposTable (see GdefTable.cs) - lock around the whole eager parse against
            // the shared, mutable-cursor OpenTypeFontface.
            lock (face.SyncRoot)
            {
                face.Position = tableStart;
                face.ReadUShort(); // majorVersion
                face.ReadUShort(); // minorVersion
                int constantsOffset = tableStart + face.ReadUShort();
                int glyphInfoOffset = tableStart + face.ReadUShort();
                int variantsOffset = tableStart + face.ReadUShort();

                Constants = new MathConstantsTable(face, constantsOffset);
                GlyphInfo = new MathGlyphInfoTable(face, glyphInfoOffset);
                Variants = new MathVariantsTable(face, variantsOffset);
            }
        }
    }
}
