using System;

namespace PeachDrawing.PDF
{
    /// <summary>A PDF could not be opened because it needs a password that was not given, or the one given does not open it.</summary>
    public sealed class PdfPasswordException : NotSupportedException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">what went wrong</param>
        /// <param name="passwordWasGiven"><see langword="true"/> when a non-empty password was supplied and was wrong</param>
        public PdfPasswordException(string message, bool passwordWasGiven = false)
            : base(message)
        {
            PasswordWasGiven = passwordWasGiven;
        }

        /// <summary>Whether a non-empty password was supplied (and was wrong), as opposed to none being given.</summary>
        public bool PasswordWasGiven { get; }
    }
}
