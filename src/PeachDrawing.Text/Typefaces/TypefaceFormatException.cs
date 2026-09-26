using System;

namespace PeachDrawing.Text
{
    /// <summary>
    /// The exception thrown when data that was supposed to be a font cannot be read as one.
    /// </summary>
    public sealed class TypefaceFormatException : Exception
    {
        /// <summary>Creates the exception with a default message.</summary>
        public TypefaceFormatException()
            : base("The data is not a font this library can read.")
        {
        }

        /// <summary>Creates the exception with a message.</summary>
        /// <param name="message">What went wrong.</param>
        public TypefaceFormatException(string message)
            : base(message)
        {
        }

        /// <summary>Creates the exception with a message and the failure that caused it.</summary>
        /// <param name="message">What went wrong.</param>
        /// <param name="innerException">The failure inside the font reader.</param>
        public TypefaceFormatException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
