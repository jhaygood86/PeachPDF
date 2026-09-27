using System;
using System.IO;
using System.Reflection;

namespace PeachDrawing.Text.Data
{
    /// <summary>
    /// Opens the embedded Unicode, hyphenation and dictionary resources this assembly carries, by manifest-name suffix. This
    /// project targets <c>netstandard2.0</c> (see its own project file for why), whose reference assemblies do not include
    /// <c>System.IO.Compression.BrotliStream</c> - decompressing what this opens is therefore
    /// <c>PeachDrawing.Text.Internal.Text.TextDataResources</c>'s job, in the assembly that actually has it.
    /// </summary>
    internal static class TextData
    {
        /// <summary>
        /// This assembly, so a caller in <c>PeachDrawing.Text</c> that needs to enumerate resource names itself (a hyphenation
        /// pattern set exists per language, so there is no fixed suffix to look one up by) does not have to know where the data
        /// physically lives beyond this one property.
        /// </summary>
        internal static readonly Assembly Assembly = typeof(TextData).Assembly;

        /// <summary>Opens the raw (still compressed) embedded resource whose manifest name ends with <paramref name="suffix"/>.</summary>
        internal static Stream? OpenRaw(string suffix)
        {
            string? name = null;
            foreach (var candidate in Assembly.GetManifestResourceNames())
            {
                if (candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = candidate;
                    break;
                }
            }

            return name is null ? null : Assembly.GetManifestResourceStream(name);
        }
    }
}
