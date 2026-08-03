namespace PeachPDF.SourceGenerators.Json
{
    /// <summary>A JSON syntax error, positioned for a Roslyn diagnostic (1-based line/column).</summary>
    internal readonly struct JsonError
    {
        public string Message { get; }
        public int Line { get; }
        public int Column { get; }

        public JsonError(string message, int line, int column)
        {
            Message = message;
            Line = line;
            Column = column;
        }
    }
}
