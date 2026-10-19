#nullable disable

using System.IO;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>Which nested block inside a <see cref="FontFeatureValuesRule"/> a name was declared in
    /// (CSS Fonts Module Level 4 §6.8) - part of <c>PeachPDF.Html.Core.RegisteredFontFeatureValues</c>'s
    /// registry key, since the same name can be declared independently in more than one block kind.</summary>
    internal enum FontFeatureValueBlockKind
    {
        Styleset,
        CharacterVariant,
        Swash,
        Ornaments,
        Annotation,
        Stylistic
    }

    /// <summary>
    /// One nested block inside a <see cref="FontFeatureValuesRule"/> - <c>@styleset</c>,
    /// <c>@character-variant</c>, <c>@swash</c>, <c>@ornaments</c>, <c>@annotation</c>, or
    /// <c>@stylistic</c> (CSS Fonts Module Level 4). All six share the identical grammar
    /// (<c>&lt;custom-ident&gt;: &lt;integer&gt;+;</c> declarations - not real CSS properties, so this
    /// is a single class parameterized by its block name rather than six near-identical subclasses,
    /// the same way <see cref="DeclarationRule"/>'s own <c>name</c> parameter is already per-instance).
    /// </summary>
    internal sealed class FontFeatureValueSetRule : DeclarationRule
    {
        internal FontFeatureValueSetRule(StylesheetParser parser, string blockName)
            : base(RuleType.FontFeatureValueSet, blockName, parser)
        {
            BlockName = blockName;
        }

        /// <summary>The block's own at-rule name (<c>styleset</c>, <c>character-variant</c>, etc.).</summary>
        public string BlockName { get; }

        protected override Property CreateNewProperty(string name)
        {
            return PropertyFactory.Instance.CreateFontFeatureValueDescriptor(name);
        }

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            var declarations = formatter.Declarations(Declarations.Where(d => d.HasValue).Select(d => d.ToCss(formatter)));
            writer.Write(string.Concat("@", BlockName, " { ", declarations, " }"));
        }

        /// <summary>
        /// The single source of truth for the six recognized block names, shared by
        /// <see cref="StylesheetComposer"/> (deciding whether to descend into a nested block at all) and
        /// <c>PeachPDF.Html.Core.RegisteredFontFeatureValues</c> (deciding which <see cref="FontFeatureValueBlockKind"/>
        /// a parsed block is) - CLAUDE.md's "don't write two independent parsers for the same CSS value
        /// grammar across layers" rule applies here even though both call sites are string-name checks,
        /// not full grammars: a name added to one and not the other silently desyncs which blocks parse
        /// from which blocks actually register.
        /// </summary>
        public static bool TryGetBlockKind(string blockName, out FontFeatureValueBlockKind kind)
        {
            if (blockName.Is(RuleNames.Styleset)) { kind = FontFeatureValueBlockKind.Styleset; return true; }
            if (blockName.Is(RuleNames.CharacterVariant)) { kind = FontFeatureValueBlockKind.CharacterVariant; return true; }
            if (blockName.Is(RuleNames.Swash)) { kind = FontFeatureValueBlockKind.Swash; return true; }
            if (blockName.Is(RuleNames.Ornaments)) { kind = FontFeatureValueBlockKind.Ornaments; return true; }
            if (blockName.Is(RuleNames.Annotation)) { kind = FontFeatureValueBlockKind.Annotation; return true; }
            if (blockName.Is(RuleNames.Stylistic)) { kind = FontFeatureValueBlockKind.Stylistic; return true; }

            kind = default;
            return false;
        }
    }
}
