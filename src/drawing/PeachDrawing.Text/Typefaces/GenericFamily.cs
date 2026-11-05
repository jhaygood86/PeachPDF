namespace PeachDrawing.Text
{
    /// <summary>
    /// A generic font family: a kind of typeface that each platform has its own answer for, named as CSS Fonts
    /// names them.
    /// </summary>
    /// <remarks>
    /// <see cref="FontSet.ResolveGeneric"/> turns one into the name of a family the machine is likely to have.
    /// </remarks>
    public enum GenericFamily
    {
        /// <summary>Serif letterforms (<c>serif</c>).</summary>
        Serif,

        /// <summary>Sans-serif letterforms (<c>sans-serif</c>).</summary>
        SansSerif,

        /// <summary>Fixed-pitch letterforms (<c>monospace</c>).</summary>
        Monospace,

        /// <summary>Handwriting-style letterforms (<c>cursive</c>).</summary>
        Cursive,

        /// <summary>Decorative letterforms (<c>fantasy</c>).</summary>
        Fantasy,

        /// <summary>The typeface the platform's own interface is set in (<c>system-ui</c>).</summary>
        SystemUi,

        /// <summary>A typeface made for mathematical expressions, one with an OpenType MATH table (<c>math</c>).</summary>
        Math,

        /// <summary>The serif typeface the platform's own interface offers (<c>ui-serif</c>): New York on Apple platforms, the platform's serif elsewhere.</summary>
        UiSerif,

        /// <summary>The sans-serif typeface the platform's own interface is set in (<c>ui-sans-serif</c>): the same typeface as <see cref="SystemUi"/>.</summary>
        UiSansSerif,

        /// <summary>The fixed-pitch typeface the platform's own interface offers (<c>ui-monospace</c>): SF Mono on Apple platforms, the platform's monospace elsewhere.</summary>
        UiMonospace,

        /// <summary>The rounded typeface the platform's own interface offers (<c>ui-rounded</c>): SF Rounded on Apple platforms, the interface typeface elsewhere.</summary>
        UiRounded
    }
}
