namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// How a character takes part in cursive joining: the <c>Joining_Type</c> property of the Unicode Character Database, which
    /// Arabic, Syriac, N'Ko, Mandaic, Mongolian and several other scripts share.
    /// </summary>
    /// <remarks>
    /// The names are the property's own one-letter abbreviations, which the joining rules are written in.
    /// </remarks>
    public enum ArabicJoiningType : byte
    {
        /// <summary>Non_Joining: joins nothing on either side. This is what most code points are.</summary>
        U,

        /// <summary>Right_Joining: joins the character before it but not the one after it.</summary>
        R,

        /// <summary>Dual_Joining: joins the characters on both sides, as most Arabic letters do.</summary>
        D,

        /// <summary>Join_Causing: makes its neighbours take joining forms without having a joining form of its own (tatweel and the zero-width joiner).</summary>
        C,

        /// <summary>Left_Joining: joins the character after it but not the one before it; a few Syriac and Manichaean letters only.</summary>
        L,

        /// <summary>Transparent: has no effect on its neighbours, which join across it, as they do across most combining marks.</summary>
        T
    }
}
