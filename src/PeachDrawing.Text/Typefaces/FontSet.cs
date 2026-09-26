using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PeachDrawing.Text
{
    /// <summary>
    /// The fonts a piece of text can be set in: the fonts installed on the machine, plus the ones added to this set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A set starts out able to see the installed fonts. A font added under the name of an installed family joins that
    /// family for this set only: it takes the place of the face with the same weight, slant, width and code point
    /// ranges, and the family's other faces stay. Two sets never share the fonts added to them, so two callers can
    /// register different data under the same family name without one seeing the other's.
    /// </para>
    /// <para>
    /// A set is not safe for concurrent use: matching and searching fill caches that are private to it, so give each
    /// thread its own set or serialize the calls. What the machine has installed is scanned once per process and is safe
    /// to share. Adding a font clears what the set has cached, so a match made before a font was added never stands in
    /// for one made after.
    /// </para>
    /// </remarks>
    public sealed class FontSet
    {
        private static readonly Lazy<IReadOnlyList<string>> InstalledNames = new(() =>
            FontResolver.SystemFamilyDisplayNames.Where(name => !string.IsNullOrEmpty(name)).ToArray());

        /// <summary>Creates a set that sees the installed fonts and nothing else yet.</summary>
        public FontSet()
        {
            Resolver = new FontResolver();
        }

        /// <summary>The engine's resolver behind this set; PeachPDF's PDF writer reads what it needs through it for now.</summary>
        internal FontResolver Resolver { get; }

        /// <summary>
        /// The names of the font families installed on this machine, one for each family the operating system's fonts
        /// declare. The list is empty where fonts cannot be discovered, such as a browser or iOS.
        /// </summary>
        public static IReadOnlyList<string> InstalledFamilyNames => InstalledNames.Value;

        /// <summary>
        /// Adds a font to the set.
        /// </summary>
        /// <remarks>
        /// TrueType, OpenType (<c>glyf</c> and CFF), WOFF and WOFF2 data are recognised by their content, whatever
        /// the caller believes the format is. Of a TrueType or OpenType collection only the first face is added.
        /// </remarks>
        /// <param name="data">The bytes of the font file.</param>
        /// <param name="options">What to say about the font in place of what it says about itself, or <see langword="null"/> for nothing.</param>
        /// <returns>
        /// The family the font was added to: the one named in <paramref name="options"/>, or otherwise the family the font
        /// declares. It is spelled as the set spells that family, which is the spelling it was first registered under
        /// when the set already had one of that name.
        /// </returns>
        /// <exception cref="TypefaceFormatException">The data is not a font this library can read.</exception>
        public TypefaceFamily AddData(ReadOnlyMemory<byte> data, AddOptions? options = null)
        {
            try
            {
                byte[] fontBytes = FontFormatConverter.ToOpenType(data.ToArray());

                using var stream = new MemoryStream(fontBytes);
                var familyName = options?.FamilyName ?? TtfFontDescription.LoadDescription(stream).FontFamilyInvariantCulture;

                stream.Seek(0, SeekOrigin.Begin);
                Resolver.AddFont(stream, familyName, options?.Weight, options?.IsItalic, options?.Width, options?.UnicodeRanges);

                Resolver.TryGetFamilyName(familyName, out var registered);
                return new TypefaceFamily(this, registered);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new TypefaceFormatException("The data is not a font this library can read.", ex);
            }
        }

        /// <summary>Adds a font that is read from a stream, which is read to its end and left open.</summary>
        /// <param name="stream">A stream positioned at the start of the font file.</param>
        /// <param name="options">What to say about the font in place of what it says about itself, or <see langword="null"/> for nothing.</param>
        /// <returns>The family the font was added to.</returns>
        /// <exception cref="TypefaceFormatException">The data is not a font this library can read.</exception>
        public TypefaceFamily AddStream(Stream stream, AddOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(stream);

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return AddData(buffer.ToArray(), options);
        }

        /// <summary>Adds a font that is read from a file.</summary>
        /// <param name="path">The path of the font file.</param>
        /// <param name="options">What to say about the font in place of what it says about itself, or <see langword="null"/> for nothing.</param>
        /// <returns>The family the font was added to.</returns>
        /// <exception cref="TypefaceFormatException">The file is not a font this library can read.</exception>
        public TypefaceFamily AddFile(string path, AddOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(path);
            return AddData(File.ReadAllBytes(path), options);
        }

        /// <summary>Looks a family up by name, ignoring case.</summary>
        /// <param name="name">The family name.</param>
        /// <param name="family">The family, when the set has one of that name.</param>
        /// <returns><see langword="false"/> when the set has no family of that name.</returns>
        public bool TryFindFamily(string name, out TypefaceFamily family)
        {
            ArgumentNullException.ThrowIfNull(name);

            if (Resolver.TryGetFamilyName(name, out var registered))
            {
                family = new TypefaceFamily(this, registered);
                return true;
            }

            family = null!;
            return false;
        }

        /// <summary>
        /// Finds a family that can draw a character, for when none of the families a caller asked for can: the
        /// last-resort search of CSS Fonts 4 section 5.
        /// </summary>
        /// <remarks>
        /// Every family of the set is considered, the installed ones included. Where several qualify, the one whose
        /// coverage mostly lies in the character's own script wins, so a font made for Arabic is preferred to one that
        /// happens to include a single Arabic glyph. The answer is remembered per character and presentation.
        /// </remarks>
        /// <param name="rune">The character to draw.</param>
        /// <param name="presentation">
        /// The presentation wanted, for a character that has a text and an emoji form. With a preference, only faces
        /// that support that form qualify, so the answer can be <see langword="false"/> although some face covers the
        /// character; a caller then asks again with <see cref="EmojiPresentation.NoPreference"/>.
        /// </param>
        /// <param name="family">The family that covers the character.</param>
        /// <returns><see langword="false"/> when no family covers the character.</returns>
        public bool TryFindCoveringFamily(Rune rune, EmojiPresentation presentation, out TypefaceFamily family)
        {
            var key = Resolver.FindFamilyCoveringCodepoint(rune, presentation);
            if (key is null || !Resolver.TryGetFamilyName(key, out var name))
            {
                family = null!;
                return false;
            }

            family = new TypefaceFamily(this, name);
            return true;
        }

        /// <summary>
        /// Whether any face of a family declares the code points it is used for (a <c>unicode-range</c>), which means
        /// text set in the family has to be resolved character by character rather than as a whole.
        /// </summary>
        /// <param name="familyName">The family name.</param>
        public bool HasExplicitRanges(string familyName)
        {
            ArgumentNullException.ThrowIfNull(familyName);
            return Resolver.HasExplicitRanges(familyName);
        }

        /// <summary>
        /// Finds the best face of a family for a query, with a last resort for a family the set does not have.
        /// </summary>
        /// <remarks>
        /// This never fails for a set that holds a font: a family name the set does not know is answered with a face
        /// of the set's first family, which is what a renderer needs when even its default font is missing. To learn
        /// that a family is missing, or to require a character to be covered, use <see cref="TryFindFamily"/> and
        /// <see cref="TypefaceFamily.TryMatch"/>.
        /// </remarks>
        /// <param name="familyName">The family name.</param>
        /// <param name="query">What is wanted; its <see cref="TypefaceQuery.MustCover"/> has to be <see langword="null"/>.</param>
        /// <exception cref="ArgumentException">The query names a character to cover.</exception>
        /// <exception cref="InvalidOperationException">The set holds no font at all, installed or added.</exception>
        public TypefaceMatch MatchOrFallback(string familyName, in TypefaceQuery query)
        {
            ArgumentNullException.ThrowIfNull(familyName);

            if (query.MustCover is not null)
            {
                throw new ArgumentException("A query with a character to cover has no last resort; use TypefaceFamily.TryMatch.", nameof(query));
            }

            if (!Resolver.HasFamilies)
            {
                throw new InvalidOperationException("No fonts are installed on this device, and none has been added to the font set.");
            }

            return MatchCore(familyName, query);
        }

        /// <summary>
        /// Reads the data of an installed or added font by the font's own name.
        /// </summary>
        /// <remarks>
        /// This is how a CSS <c>src: local(...)</c> reference is served: the name is the one the font file gives itself,
        /// the full name of a face rather than the name of its family.
        /// </remarks>
        /// <param name="fontName">The name of the font.</param>
        /// <param name="data">
        /// The bytes of the font, as a standalone font file. They are the set's own, shared with every reader of that
        /// font, and are handed out read-only.
        /// </param>
        /// <returns><see langword="false"/> when the set has no font of that name.</returns>
        public bool TryGetFontData(string fontName, out ReadOnlyMemory<byte> data)
        {
            ArgumentNullException.ThrowIfNull(fontName);

            if (Resolver.HasFont(fontName))
            {
                data = Resolver.GetFont(fontName);
                return true;
            }

            data = ReadOnlyMemory<byte>.Empty;
            return false;
        }

        /// <summary>
        /// Names a family for a generic family on this platform, as Chromium does.
        /// </summary>
        /// <remarks>
        /// Windows, macOS and Android each have a fixed answer, and Linux asks fontconfig. The exception is
        /// <see cref="GenericFamily.Math"/>, which is the first family of a platform's list of math fonts that is
        /// available. An answer is only returned when it is available, so a caller can move on to its own default.
        /// </remarks>
        /// <param name="generic">The generic family.</param>
        /// <param name="isAvailable">
        /// Whether a family name is one the caller can use, or <see langword="null"/> to mean that the set has a family of
        /// that name. A caller that keeps its own list of usable names, aliases included, passes it here.
        /// </param>
        /// <returns>The family name, or <see langword="null"/> when the platform's answer is not available.</returns>
        public string? ResolveGeneric(GenericFamily generic, Func<string, bool>? isAvailable = null)
        {
            isAvailable ??= name => Resolver.TryGetFamilyName(name, out _);

            var isAndroid = OperatingSystem.IsAndroid();
            // Android is Linux-kernel-based and may also report as Linux; it has its own table and no fontconfig.
            var isLinux = OperatingSystem.IsLinux() && !isAndroid;
            var operatingSystemAnswer = isLinux && generic != GenericFamily.Math
                ? LinuxSystemFontResolver.ResolveGenericFamily(GenericFamilyTable.CssName(generic))
                : null;

            return GenericFamilyTable.Resolve(generic, operatingSystemAnswer, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), isAndroid, isAvailable);
        }

        internal TypefaceMatch MatchCore(string familyName, in TypefaceQuery query)
        {
            var options = new FontResolvingOptions(query.IsItalic ? FaceStyle.Italic : FaceStyle.Regular, query.Weight, query.Width)
            {
                Codepoint = query.MustCover
            };

            var face = LoadedTypeface.GetOrCreateFrom(familyName, options, Resolver);
            return new TypefaceMatch(new Typeface(face), face.StyleSimulations);
        }
    }
}
