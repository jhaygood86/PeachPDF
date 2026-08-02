namespace PeachPDF.SourceGenerators.Model
{
    /// <summary>A JSON/schema-level finding, positioned for a Roslyn diagnostic (1-based line/column).</summary>
    internal readonly struct ModelDiagnostic
    {
        public DiagnosticCode Code { get; }
        public string Message { get; }
        public int Line { get; }
        public int Column { get; }

        public ModelDiagnostic(DiagnosticCode code, string message, int line, int column)
        {
            Code = code;
            Message = message;
            Line = line;
            Column = column;
        }
    }
}
