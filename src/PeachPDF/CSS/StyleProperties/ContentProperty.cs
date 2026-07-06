using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace PeachPDF.CSS
{
    using static Converters;

    internal sealed class ContentProperty : Property
    {
        internal ContentProperty() : base(PropertyNames.Content)
        {
        }

        internal override IValueConverter Converter => StyleConverter;

        private static readonly FrozenDictionary<string, ContentMode> ContentModes =
            new Dictionary<string, ContentMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.OpenQuote, new OpenQuoteContentMode()},
                {Keywords.NoOpenQuote, new NoOpenQuoteContentMode()},
                {Keywords.CloseQuote, new CloseQuoteContentMode()},
                {Keywords.NoCloseQuote, new NoCloseQuoteContentMode()}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static readonly ContentMode[] Default = [new NormalContentMode()];

        private static readonly IValueConverter StyleConverter = Assign(Keywords.Normal, Default).OrNone().Or(
            ContentModes.ToConverter().Or(
                UrlConverter).Or(
                Converters.GradientConverter).Or(
                StringConverter).Or(
                AttrConverter).Or(
                CounterConverter).Or(
                new ContentFunctionConverter()).Or(
                new StringFunctionConverter()).Many()).OrDefault();

        private abstract class ContentMode
        {
        }

        private sealed class NormalContentMode : ContentMode
        {
        }

        private sealed class OpenQuoteContentMode : ContentMode
        {
        }

        private sealed class CloseQuoteContentMode : ContentMode
        {
        }

        private sealed class NoOpenQuoteContentMode : ContentMode
        {
        }

        private sealed class NoCloseQuoteContentMode : ContentMode
        {
        }
    }
}