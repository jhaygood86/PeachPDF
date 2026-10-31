namespace PeachPDF.SourceGenerators.Model
{
    /// <summary>
    /// A <c>css-properties.json</c> entry's <c>cssom</c> block: what the CSS-OM <c>Property</c> class generated for
    /// it needs beyond what the entry already says. Inheritance comes from the entry's own <c>inherited</c>, and
    /// the initial value is the entry's <c>initialValue</c> (the CSS-OM <c>initial</c> keyword carries no typed
    /// value - only the render layer reads one), so neither is restated here.
    /// </summary>
    internal sealed class CssomBinding
    {
        /// <summary>C# expression for the grammar, compiled inside <c>PeachBrackets.Css</c> (so <c>Converters.X</c> resolves).</summary>
        public string Converter { get; }

        /// <summary>The generated class's name when it is not the PascalCased property name plus <c>Property</c>.</summary>
        public string? ClassName { get; }

        /// <summary><c>PropertyFlags.Animatable</c> on the class.</summary>
        public bool Animatable { get; }

        public bool Hashless { get; }
        public bool Unitless { get; }

        /// <summary>Registered as animatable with <c>PropertyFactory</c> (a list distinct from the class's own flag).</summary>
        public bool RegistryAnimatable { get; }

        /// <summary>Registered with <c>PropertyFactory</c>'s <c>@font-face</c> descriptor set.</summary>
        public bool Font { get; }

        /// <summary>
        /// The grammar's own value converter is the property's converter as it stands, without the CSS-wide keywords
        /// (<c>"globals": "none"</c>): <c>flex-grow</c> and the like, whose grammar accepts them itself. Otherwise the
        /// generated class wraps the grammar in <c>OrDefault()</c>.
        /// </summary>
        public bool NoGlobals { get; }

        public CssomBinding(string converter, string? className, bool animatable, bool hashless, bool unitless,
            bool registryAnimatable, bool font, bool noGlobals = false)
        {
            NoGlobals = noGlobals;
            Converter = converter;
            ClassName = className;
            Animatable = animatable;
            Hashless = hashless;
            Unitless = unitless;
            RegistryAnimatable = registryAnimatable;
            Font = font;
        }
    }
}
