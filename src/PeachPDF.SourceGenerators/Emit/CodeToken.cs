using System.Collections.Generic;

namespace PeachPDF.SourceGenerators.Emit
{
    /// <summary>
    /// Splices <c>customSetter</c>/<c>customValidator</c> raw C# from css-properties.json into
    /// generated method bodies via literal token substitution — see CLAUDE.md's generator section
    /// for the token table (<c>{value}</c>/<c>{box}</c>/<c>{element}</c>/<c>{parser}</c>/<c>{ctx}</c>).
    /// This is trusted, in-repo build input reviewed like any other source, so substitution is a
    /// plain string replace — no escaping, no sandboxing.
    /// </summary>
    internal static class CodeToken
    {
        public static string Substitute(string code, string? value = null, string? box = null,
            string? element = null, string? parser = null, string? ctx = null)
        {
            var result = code;
            if (value is not null) result = result.Replace("{value}", value);
            if (box is not null) result = result.Replace("{box}", box);
            if (element is not null) result = result.Replace("{element}", element);
            if (parser is not null) result = result.Replace("{parser}", parser);
            if (ctx is not null) result = result.Replace("{ctx}", ctx);
            return result;
        }
    }
}
