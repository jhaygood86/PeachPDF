#nullable disable

using System.IO;

// ReSharper disable UnusedMember.Global

namespace PeachPDF.CSS
{
    internal abstract class Property : StylesheetNode, IProperty
    {
        private readonly PropertyFlags _flags;

        internal Property(string name, PropertyFlags flags = PropertyFlags.None)
        {
            Name = name;
            _flags = flags;
        }

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            writer.Write(formatter.Declaration(Name, Value, IsImportant));
        }


        internal bool TrySetValue(TokenValue newTokenValue)
        {
            var tokenValue = newTokenValue ?? TokenValue.Initial;
            var converter = tokenValue.ContainsFunction(FunctionNames.Var) ? Converters.Any : Converter;
            var value = converter.Convert(tokenValue);

            if (value == null) return false;
            DeclaredValue = value;
            return true;
        }

        /// <summary>
        /// Memoized. <see cref="IPropertyValue.CssText"/> is not a stored string — nearly every
        /// converter re-serializes it on each read, several with LINQ and string.Join over their
        /// components (BorderRadiusConverter, EndListValueConverter, BackgroundPositionValueConverter,
        /// …).
        ///
        /// A parsed declaration is shared by every box its rule matches, and the cascade reads this
        /// up to three times per declaration per box (the global-keyword switch, its fall-through arm,
        /// and CssGlobalKeywords.TryParse), so one document-wide rule re-serializes its values
        /// thousands of times in a single render.
        ///
        /// Invalidated by <see cref="DeclaredValue"/>'s setter, which is the only way the underlying
        /// value changes; the value objects a converter builds are themselves immutable.
        ///
        /// <c>null</c> is the "not computed yet" marker, the same way <see cref="DeclaredValue"/>'s
        /// own <c>null</c> means "no declaration" — this file is <c>#nullable disable</c>, so the
        /// field carries no annotation. A converter that returned a null <c>CssText</c> would only
        /// re-serialize on the next read; it could never report a stale value.
        /// </summary>
        public string Value => _valueText ??= DeclaredValue != null ? DeclaredValue.CssText : Keywords.Initial;

        private string _valueText;

        public string Original => DeclaredValue != null ? DeclaredValue.Original.Text : Keywords.Initial;

        public bool IsInherited => (_flags & PropertyFlags.Inherited) == PropertyFlags.Inherited && IsInitial ||
                                   DeclaredValue != null && Value.Is(Keywords.Inherit);

        public bool IsAnimatable => (_flags & PropertyFlags.Animatable) == PropertyFlags.Animatable;

        public bool IsInitial => DeclaredValue == null || Value.Is(Keywords.Initial);

        internal bool HasValue => DeclaredValue != null;

        internal bool CanBeHashless => (_flags & PropertyFlags.Hashless) == PropertyFlags.Hashless;

        internal bool CanBeUnitless => (_flags & PropertyFlags.Unitless) == PropertyFlags.Unitless;

        public bool CanBeInherited => (_flags & PropertyFlags.Inherited) == PropertyFlags.Inherited;

        internal bool IsShorthand => (_flags & PropertyFlags.Shorthand) == PropertyFlags.Shorthand;

        public string Name { get; }

        public bool IsImportant { get; set; }

        public string CssText => this.ToCss();

        internal abstract IValueConverter Converter { get; }

        internal IPropertyValue DeclaredValue
        {
            get => _declaredValue;
            set
            {
                _declaredValue = value;
                _valueText = null;
            }
        }

        private IPropertyValue _declaredValue = null!;
    }
}