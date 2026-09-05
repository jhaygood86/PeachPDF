namespace PeachPDF.Text
{
    /// <summary>
    /// Unicode's <c>Indic_Syllabic_Category</c> property (UAX #44) - one of HarfBuzz's Universal
    /// Shaping Engine's two raw per-codepoint inputs (alongside <see cref="IndicPositionalCategory"/>
    /// and .NET's own built-in General_Category), consumed by
    /// <see cref="PeachPDF.Text.Shaping.Use.UseCategoryClassifier"/> to derive the final USE shaping
    /// category. Every member name is the UCD value with its underscores stripped (matching how
    /// <c>assets/unicode/generate_use_category_tables.py</c> writes the embedded table), so
    /// <see cref="IndicSyllabicCategoryTable"/> can <c>Enum.Parse</c> directly with no translation
    /// table - e.g. <c>Vowel_Dependent</c> becomes <see cref="VowelDependent"/>.
    /// </summary>
    internal enum IndicSyllabicCategory : byte
    {
        /// <summary>The file's own <c>@missing</c> default - every codepoint the UCD data doesn't
        /// explicitly list.</summary>
        Other = 0,
        Bindu,
        Visarga,
        Avagraha,
        Nukta,
        Virama,
        PureKiller,
        ReorderingKiller,
        InvisibleStacker,
        VowelIndependent,
        VowelDependent,
        Vowel,
        ConsonantPlaceholder,
        Consonant,
        ConsonantDead,
        ConsonantWithStacker,
        ConsonantPrefixed,
        ConsonantPrecedingRepha,
        ConsonantInitialPostfixed,
        ConsonantSucceedingRepha,
        ConsonantSubjoined,
        ConsonantMedial,
        ConsonantFinal,
        ConsonantHeadLetter,
        ModifyingLetter,
        ToneLetter,
        ToneMark,
        GeminationMark,
        CantillationMark,
        RegisterShifter,
        SyllableModifier,
        ConsonantKiller,
        NonJoiner,
        Joiner,
        NumberJoiner,
        Number,
        BrahmiJoiningNumber,
    }
}
