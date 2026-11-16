namespace PeachBrackets.Css
{
    internal struct ParserOptions
    {
        public bool IncludeUnknownRules { get; set; }
        public bool IncludeUnknownDeclarations { get; set; }
        public bool AllowInvalidSelectors { get; set; }
        public bool AllowInvalidValues { get; set; }
        public bool AllowInvalidConstraints { get; set; }
        public bool PreserveComments { get; set; }
        public bool PreserveDuplicateProperties { get; set; }

        /// <summary>Whether the text is read for a document in quirks mode: a hex color may lack its <c>#</c> and a length its unit (<see cref="QuirkyValues"/>).</summary>
        public bool QuirksMode { get; set; }
    }
}