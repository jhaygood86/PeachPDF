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
// eager parse is still wrapped in `lock (face)` against the shared, mutable-cursor OpenTypeFontface.
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

using System.Collections.Generic;

namespace PeachPDF.Fonts.OpenType
{
    /// <summary>One pre-sized glyph variant for a stretchy shape (<c>MathGlyphVariantRecord</c>):
    /// an alternate glyph and its measurement (advance width for a horizontally-growing variant,
    /// advance height for a vertically-growing one) in the direction of extension.</summary>
    internal readonly record struct MathGlyphVariant(ushort GlyphId, double AdvanceMeasurement);

    /// <summary>One part of a glyph assembly (<c>GlyphPart</c> record) - a single glyph plus how much
    /// of its start/end can overlap with a neighboring part's connector, its own full advance, and
    /// whether it's an extender (repeatable/skippable to reach a target size).</summary>
    internal readonly record struct MathGlyphPart(
        ushort GlyphId,
        double StartConnectorLength,
        double EndConnectorLength,
        double FullAdvance,
        bool IsExtender);

    /// <summary>How to assemble a stretchy shape from glyph parts when no pre-sized
    /// <see cref="MathGlyphVariant"/> is large enough (<c>GlyphAssembly</c> table).</summary>
    internal sealed class MathGlyphAssembly
    {
        public required double ItalicsCorrection { get; init; }

        /// <summary>Left-to-right (horizontal extension) or bottom-to-top (vertical extension) order.</summary>
        public required IReadOnlyList<MathGlyphPart> Parts { get; init; }
    }

    /// <summary>Everything needed to find or build an enlarged variant of one glyph
    /// (<c>MathGlyphConstruction</c> table): its pre-sized variants (smallest to largest, per spec
    /// order) and, if the font provides one, a part-based assembly for sizes beyond the largest
    /// pre-sized variant.</summary>
    internal sealed class MathGlyphConstruction
    {
        public MathGlyphAssembly? Assembly { get; init; }
        public required IReadOnlyList<MathGlyphVariant> Variants { get; init; }
    }

    /// <summary>
    /// The ~50 named constants MathML Core's layout algorithm reads for fraction/radical/script/
    /// stack/limit positioning (see <c>MathLayoutEngine</c>). Every field is in font design units
    /// (FUnits) - the caller scales by <c>fontSize / unitsPerEm</c>. Field order matches the OpenType
    /// spec's MathConstants table exactly (a fixed-size, fully sequential layout - no offsets to
    /// chase), so the constructor is a straight sequential read.
    /// </summary>
    internal sealed class MathConstantsTable
    {
        public double ScriptPercentScaleDown { get; }
        public double ScriptScriptPercentScaleDown { get; }
        public double DelimitedSubFormulaMinHeight { get; }
        public double DisplayOperatorMinHeight { get; }
        public double MathLeading { get; }
        public double AxisHeight { get; }
        public double AccentBaseHeight { get; }
        public double FlattenedAccentBaseHeight { get; }
        public double SubscriptShiftDown { get; }
        public double SubscriptTopMax { get; }
        public double SubscriptBaselineDropMin { get; }
        public double SuperscriptShiftUp { get; }
        public double SuperscriptShiftUpCramped { get; }
        public double SuperscriptBottomMin { get; }
        public double SuperscriptBaselineDropMax { get; }
        public double SubSuperscriptGapMin { get; }
        public double SuperscriptBottomMaxWithSubscript { get; }
        public double SpaceAfterScript { get; }
        public double UpperLimitGapMin { get; }
        public double UpperLimitBaselineRiseMin { get; }
        public double LowerLimitGapMin { get; }
        public double LowerLimitBaselineDropMin { get; }
        public double StackTopShiftUp { get; }
        public double StackTopDisplayStyleShiftUp { get; }
        public double StackBottomShiftDown { get; }
        public double StackBottomDisplayStyleShiftDown { get; }
        public double StackGapMin { get; }
        public double StackDisplayStyleGapMin { get; }
        public double StretchStackTopShiftUp { get; }
        public double StretchStackBottomShiftDown { get; }
        public double StretchStackGapAboveMin { get; }
        public double StretchStackGapBelowMin { get; }
        public double FractionNumeratorShiftUp { get; }
        public double FractionNumeratorDisplayStyleShiftUp { get; }
        public double FractionDenominatorShiftDown { get; }
        public double FractionDenominatorDisplayStyleShiftDown { get; }
        public double FractionNumeratorGapMin { get; }
        public double FractionNumDisplayStyleGapMin { get; }
        public double FractionRuleThickness { get; }
        public double FractionDenominatorGapMin { get; }
        public double FractionDenomDisplayStyleGapMin { get; }
        public double SkewedFractionHorizontalGap { get; }
        public double SkewedFractionVerticalGap { get; }
        public double OverbarVerticalGap { get; }
        public double OverbarRuleThickness { get; }
        public double OverbarExtraAscender { get; }
        public double UnderbarVerticalGap { get; }
        public double UnderbarRuleThickness { get; }
        public double UnderbarExtraDescender { get; }
        public double RadicalVerticalGap { get; }
        public double RadicalDisplayStyleVerticalGap { get; }
        public double RadicalRuleThickness { get; }
        public double RadicalExtraAscender { get; }
        public double RadicalKernBeforeDegree { get; }
        public double RadicalKernAfterDegree { get; }
        public double RadicalDegreeBottomRaisePercent { get; }

        public MathConstantsTable(OpenTypeFontface face, int tableStart)
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

    /// <summary>The <c>MathGlyphInfo</c> table: per-glyph italics correction, top-accent attachment,
    /// and which glyphs are "extended shapes" (already-tall/wide variants that should be positioned
    /// by their own ink box rather than the default MathConstants-driven position - see
    /// <c>IsExtendedShape</c>'s use in <c>MathLayoutEngine</c>). <c>MathKernInfo</c> is present in
    /// the font but intentionally not read - see this file's header comment.</summary>
    internal sealed class MathGlyphInfoTable
    {
        readonly MathPerGlyphValueTable? _italicsCorrection;
        readonly MathPerGlyphValueTable? _topAccentAttachment;
        readonly CoverageTable? _extendedShapeCoverage;

        public MathGlyphInfoTable(OpenTypeFontface face, int tableStart)
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
        public double GetItalicsCorrection(ushort glyphId) => _italicsCorrection?.GetValue(glyphId) ?? 0;

        /// <summary>The glyph's top-accent horizontal attachment point, in design units, or null if
        /// the font provides none for this glyph - callers fall back to the glyph's own geometric
        /// center (advance width / 2), per the OpenType spec's own guidance for an uncovered glyph.</summary>
        public double? GetTopAccentAttachment(ushort glyphId)
        {
            if (_topAccentAttachment is null)
                return null;

            // MathPerGlyphValueTable.GetValue can't itself distinguish "uncovered" from "covered
            // with value 0", so re-check coverage directly rather than trusting a 0 return.
            return _topAccentAttachment.HasValue(glyphId) ? _topAccentAttachment.GetValue(glyphId) : null;
        }

        public bool IsExtendedShape(ushort glyphId) => _extendedShapeCoverage?.IndexOfGlyph(glyphId) >= 0;
    }

    /// <summary>The <c>MathVariants</c> table: for glyphs that need to stretch (fences, radicals,
    /// accents, arrows, ...), the pre-sized variants and/or glyph-assembly parts to grow them
    /// vertically or horizontally - see <c>MathLayoutEngine</c>'s stretch algorithm.</summary>
    internal sealed class MathVariantsTable
    {
        readonly CoverageTable? _vertCoverage;
        readonly CoverageTable? _horizCoverage;
        readonly MathGlyphConstruction?[] _vertConstructions;
        readonly MathGlyphConstruction?[] _horizConstructions;

        public double MinConnectorOverlap { get; }

        public MathVariantsTable(OpenTypeFontface face, int tableStart)
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

        public MathGlyphConstruction? GetVerticalConstruction(ushort glyphId) =>
            GetConstruction(_vertCoverage, _vertConstructions, glyphId);

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
                Variants = variants,
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

            return new MathGlyphAssembly { ItalicsCorrection = italicsCorrectionValue, Parts = parts };
        }
    }

    /// <summary>The top-level <c>MATH</c> table: <see cref="Constants"/>, <see cref="GlyphInfo"/>,
    /// and <see cref="Variants"/>.</summary>
    internal sealed class MathTable
    {
        public MathConstantsTable Constants { get; }
        public MathGlyphInfoTable GlyphInfo { get; }
        public MathVariantsTable Variants { get; }

        public MathTable(OpenTypeFontface face, int tableStart)
        {
            // MathTable instances are cached and shared process-wide, exactly like GsubTable/
            // GdefTable/GposTable (see GdefTable.cs) - lock around the whole eager parse against
            // the shared, mutable-cursor OpenTypeFontface.
            lock (face)
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
