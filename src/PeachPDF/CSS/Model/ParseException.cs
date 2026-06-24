using System;

namespace PeachPDF.CSS
{
    internal class ParseException : Exception
    {
        public ParseException(string message) : base(message)
        {
        }
    }
}