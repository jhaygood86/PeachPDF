namespace PeachPDF.CSS
{
    /// <summary>
    /// A named counter style declared with an <c>@counter-style</c> at-rule (CSS Counter Styles Level 3 §3).
    /// Exposes the prelude name and the raw descriptor values; the Layer-B registry parses them.
    /// </summary>
    internal interface ICounterStyleRule : IRule, IProperties
    {
        /// <summary>The counter style name, a case-sensitive custom ident.</summary>
        string Name { get; set; }

        /// <summary>The raw value of descriptor <paramref name="descriptor"/>, empty when absent.</summary>
        string GetDescriptor(string descriptor);
    }
}
