using System.Collections.Generic;

namespace PeachPDF.SourceGenerators.Model
{
    internal enum PropertyCategory
    {
        Normal,
        Logical,
        Unsupported,
    }

    internal enum InitialValueKind
    {
        /// <summary>No initial value, by design (a logical scratch field, or a genuinely unimplemented property).</summary>
        None,
        Literal,
        Expression,
    }

    internal enum KeywordComparison
    {
        Ordinal,
        OrdinalIgnoreCase,
        InvariantIgnoreCase,
    }

    internal sealed class LogicalTarget
    {
        public string Group { get; }
        public string Axis { get; }
        public string Side { get; }

        public LogicalTarget(string group, string axis, string side)
        {
            Group = group;
            Axis = axis;
            Side = side;
        }
    }

    /// <summary>One parsed <c>css-properties.json</c> entry — the model layer's view of the schema described in CLAUDE.md.</summary>
    internal sealed class PropertyEntry
    {
        public string Name { get; }
        public bool Inherited { get; }
        public InitialValueKind InitialValueKind { get; }
        public string? InitialValueText { get; }
        public PropertyCategory Category { get; }
        public IReadOnlyList<DataTypeSpec> CssDataTypes { get; }
        public IReadOnlyList<string>? SupportedValues { get; }
        public KeywordComparison KeywordComparison { get; }

        /// <summary>
        /// An optional, stricter grammar used only by <c>Supports_*</c>/<c>SupportsDeclaration</c> (the
        /// @supports oracle), leaving <c>Validate_*</c>/<c>Set_*</c> (real cascade dispatch) on the base
        /// <see cref="CssDataTypes"/>. Needed when a value is genuinely accepted and stored by the real
        /// renderer (matching actual CSS parsing semantics: a value list with one unimplemented member,
        /// like a `transform` function or a `break-before` keyword, is still syntactically valid and the
        /// implemented members still apply) but that acceptance is broader than "PeachPDF's renderer
        /// actually honors this specific value" — which is the stricter standard @supports uses here
        /// (see issue #283). Null (the common case) means Supports_* simply forwards to Validate_*.
        /// </summary>
        public IReadOnlyList<DataTypeSpec>? SupportsCssDataTypes { get; }
        public IReadOnlyList<string>? SupportsSupportedValues { get; }
        public KeywordComparison? SupportsKeywordComparison { get; }

        public string? AliasOf { get; }
        public LogicalTarget? ResolvesTo { get; }
        public HtmlBinding? Html { get; }
        public SvgBinding? Svg { get; }

        /// <summary>Source location of this entry's JSON object, for diagnostics raised after initial parsing (e.g. cross-entry alias checks, symbol validation).</summary>
        public int Line { get; }
        public int Column { get; }

        public PropertyEntry(string name, bool inherited, InitialValueKind initialValueKind, string? initialValueText,
            PropertyCategory category, IReadOnlyList<DataTypeSpec> cssDataTypes, IReadOnlyList<string>? supportedValues,
            KeywordComparison keywordComparison, string? aliasOf, LogicalTarget? resolvesTo,
            HtmlBinding? html, SvgBinding? svg, int line, int column,
            IReadOnlyList<DataTypeSpec>? supportsCssDataTypes = null, IReadOnlyList<string>? supportsSupportedValues = null,
            KeywordComparison? supportsKeywordComparison = null)
        {
            Name = name;
            Inherited = inherited;
            InitialValueKind = initialValueKind;
            InitialValueText = initialValueText;
            Category = category;
            CssDataTypes = cssDataTypes;
            SupportedValues = supportedValues;
            KeywordComparison = keywordComparison;
            SupportsCssDataTypes = supportsCssDataTypes;
            SupportsSupportedValues = supportsSupportedValues;
            SupportsKeywordComparison = supportsKeywordComparison;
            AliasOf = aliasOf;
            ResolvesTo = resolvesTo;
            Html = html;
            Svg = svg;
            Line = line;
            Column = column;
        }
    }
}
