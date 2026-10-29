using System;

namespace PeachDrawing.PDF
{
    internal static class PdfCustomString
    {
        public static void ValidateName(string name, string parameterName)
        {
            ArgumentException.ThrowIfNullOrEmpty(name, parameterName);
            if (name.Length == 1 || name[0] != '/')
                throw new ArgumentException($"'{name}' is not a valid PDF name.", parameterName);

            foreach (var character in name.AsSpan(1))
            {
                if (character == '\0' || char.IsWhiteSpace(character) ||
                    character is '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%')
                {
                    throw new ArgumentException($"'{name}' is not a valid PDF name.", parameterName);
                }
            }
        }
    }
}
