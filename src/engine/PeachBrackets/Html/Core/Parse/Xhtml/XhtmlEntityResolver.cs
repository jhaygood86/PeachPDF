#nullable enable

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;

namespace PeachBrackets.Html.Parse
{
    /// <summary>
    /// What an <see cref="XmlReader"/> reading an XHTML document is allowed to load: the document's external DTD, whatever address it names, is
    /// the one embedded list of named character entities (the HTML 4 set the XHTML 1.0 DTDs declare), and every other external reference is
    /// empty. Nothing is read from the file system or the network, so a DOCTYPE cannot be used to reach either.
    /// </summary>
    internal sealed class XhtmlEntityResolver : XmlResolver
    {
        /// <summary>The address every external DTD is resolved to.</summary>
        internal const string EntitiesUri = "urn:peachbrackets:xhtml-entities";

        private const string EmptyUri = "urn:peachbrackets:empty";
        private const string ResourceName = "PeachBrackets.xhtml-entities.dtd";

        /// <summary>The DTD text: one entity declaration per named character, as the XHTML 1.0 DTDs give them.</summary>
        internal static string EntitiesDtd { get; } = ReadEntitiesDtd();

        /// <summary>
        /// Resolves any reference to a DTD (a <c>.dtd</c> or <c>.ent</c> address, or a formal public identifier) to <see cref="EntitiesUri"/> and anything else to an empty
        /// document. The reference is never combined with the base address, which would make a path of it.
        /// </summary>
        public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
        {
            var reference = relativeUri ?? string.Empty;
            // The reader asks for a public identifier before the system identifier when a DOCTYPE has both, so an identifier such as
            // "-//W3C//DTD XHTML 1.0 Strict//EN" is a DTD too.
            var isDtd = reference.EndsWith(".dtd", StringComparison.OrdinalIgnoreCase)
                || reference.EndsWith(".ent", StringComparison.OrdinalIgnoreCase)
                || reference.Contains("//DTD ", StringComparison.Ordinal)
                || reference.Contains("//ENTITIES ", StringComparison.Ordinal)
                || reference.Equals("about:legacy-compat", StringComparison.Ordinal)
                || reference.StartsWith(EntitiesUri, StringComparison.Ordinal);

            return new Uri(isDtd ? EntitiesUri : EmptyUri);
        }

        /// <summary>The embedded entity list for <see cref="EntitiesUri"/>, an empty stream for anything else.</summary>
        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var text = string.Equals(absoluteUri.OriginalString, EntitiesUri, StringComparison.Ordinal) ? EntitiesDtd : string.Empty;
            return new MemoryStream(new UTF8Encoding(false).GetBytes(text));
        }

        private static string ReadEntitiesDtd()
        {
            using var stream = typeof(XhtmlEntityResolver).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is missing.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
