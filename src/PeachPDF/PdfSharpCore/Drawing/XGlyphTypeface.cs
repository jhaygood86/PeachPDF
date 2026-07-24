#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2016 empira Software GmbH, Cologne Area (Germany)
//
// http://www.PdfSharp.com
// http://sourceforge.net/projects/pdfsharp
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion

using PeachPDF.Fonts;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Utils;
using System;
using System.Diagnostics;
using System.Globalization;

namespace PeachPDF.PdfSharpCore.Drawing
{
    /// <summary>
    /// Specifies a physical font face that corresponds to a font file on the disk or in memory.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    internal sealed class XGlyphTypeface
    {
        // Implementation Notes
        // XGlyphTypeface is the centerpiece for font management. There is a one to one relationship
        // between XFont an XGlyphTypeface.
        //
        // * Each XGlyphTypeface can belong to one or more XFont objects.
        // * An XGlyphTypeface hold an XFontFamily.
        // * XGlyphTypeface hold a reference to an OpenTypeFontface. 
        // * 
        //

        const string KeyPrefix = "tk:";  // "typeface key"

        public XGlyphTypeface(string key, XFontSource fontSource, XStyleSimulations styleSimulations = XStyleSimulations.None)
        {
            string familyName = fontSource.Fontface.name.Name;
            _fontFamily = new XFontFamily(familyName, false);
            _fontface = fontSource.Fontface;
            _isBold = _fontface.os2.IsBold;
            _isItalic = _fontface.os2.IsItalic;
            _styleSimulations = styleSimulations;

            _key = key;
            //_fontFamily =xfont  FontFamilyCache.GetFamilyByName(familyName);
            _fontSource = fontSource;

            Initialize();
        }

        // ReSharper disable once UnusedMember.Global
        public XGlyphTypeface(string key, XFontFamily fontFamily, XFontSource fontSource, XStyleSimulations styleSimulations)
        {
            _key = key;
            _fontFamily = fontFamily;
            _fontSource = fontSource;
            _styleSimulations = styleSimulations;
            _fontface = OpenTypeFontface.CetOrCreateFrom(fontSource);

            Initialize();
        }

        public static XGlyphTypeface GetOrCreateFrom(string familyName, FontResolvingOptions fontResolvingOptions, IFontResolver fontResolver)
        {
            // Per-codepoint resolution (unicode-range / glyph-coverage fallback) takes a separate path
            // that keys caches by the RESOLVED face rather than by (family, style), so the many codepoints
            // a multi-script run resolves to a handful of faces don't each spawn their own glyph typeface.
            // It is always driven by our own FontResolver instance.
            if (fontResolvingOptions.Codepoint is { } codepoint && fontResolver is FontResolver codepointResolver)
            {
                return GetOrCreateForCodepoint(familyName, fontResolvingOptions, codepoint, codepointResolver);
            }

            string typefaceKey = ComputeKey(familyName, fontResolvingOptions);

            // Custom (AddFont/@font-face-registered) families must never share the global, process-wide
            // caches below with a different FontResolver instance (i.e. a different PdfGenerator) that
            // might have registered DIFFERENT bytes under the same family name - route those through
            // this specific instance's own cache instead. Pure system-font requests keep using the
            // global caches unchanged, preserving real cross-instance sharing for the common case (no
            // need to re-resolve e.g. "Arial Bold" separately per PdfGenerator).
            var instanceResolver = fontResolver as FontResolver;
            bool useInstanceCache = instanceResolver != null && instanceResolver.IsCustomFamily(familyName);

            if (useInstanceCache)
            {
                if (instanceResolver!.InstanceGlyphTypefacesByKey.TryGetValue(typefaceKey, out var instanceCached))
                    return instanceCached;
            }
            else if (GlyphTypefaceCache.TryGetGlyphTypeface(typefaceKey, out var globalCached))
            {
                return globalCached;
            }

            FontResolverInfo? fontResolverInfo;
            XFontSource fontSource;

            if (useInstanceCache)
            {
                if (!instanceResolver!.InstanceFontResolverInfosByTypefaceKey.TryGetValue(typefaceKey, out fontResolverInfo))
                {
                    fontResolverInfo = instanceResolver.ResolveTypeface(familyName, fontResolvingOptions.Weight, fontResolvingOptions.IsItalic, fontResolvingOptions.Stretch);
                    if (fontResolverInfo == null)
                        throw new InvalidOperationException("No appropriate font found.");

                    instanceResolver.InstanceFontResolverInfosByTypefaceKey[typefaceKey] = fontResolverInfo;
                }

                // The actual glyph bytes still safely reuse FontFactory's global, content-addressed
                // XFontSource cache (keyed by a hash of the font's own bytes, not by family/face name -
                // confirmed collision-free across instances, so there's no need for a third cache here).
                byte[] bytes = instanceResolver.GetFont(fontResolverInfo.FaceName);
                fontSource = XFontSource.GetOrCreateFrom(bytes);
            }
            else
            {
                // Resolve typeface by FontFactory.
                fontResolverInfo = FontFactory.ResolveTypeface(familyName, fontResolvingOptions, typefaceKey, fontResolver);
                if (fontResolverInfo == null)
                {
                    // No fallback - just stop.
                    throw new InvalidOperationException("No appropriate font found.");
                }

                // We have a valid font resolver info. That means we also have an XFontSource object loaded in the cache.
                fontSource = FontFactory.GetFontSourceByFontName(fontResolverInfo.FaceName);
                Debug.Assert(fontSource != null);
            }

            // Each font source already contains its OpenTypeFontface. Carry through whichever
            // bold/italic simulation flags the resolver decided this face needs (see
            // FontResolver.ResolveTypeface's nearest-weight/style matching) - previously computed here
            // and then dropped, since this constructor never used to accept them at all.
            var glyphTypeface = new XGlyphTypeface(typefaceKey, fontSource, fontResolverInfo.StyleSimulations);

            if (useInstanceCache)
            {
                glyphTypeface.OwningInstanceResolver = instanceResolver;
                instanceResolver!.InstanceGlyphTypefacesByKey[typefaceKey] = glyphTypeface;
            }
            else
            {
                GlyphTypefaceCache.AddGlyphTypeface(glyphTypeface);
            }

            return glyphTypeface;
        }
        private static XGlyphTypeface GetOrCreateForCodepoint(string familyName, FontResolvingOptions options, System.Text.Rune codepoint, FontResolver resolver)
        {
            var info = resolver.ResolveTypeface(familyName, options.Weight, options.IsItalic, options.Stretch, codepoint);
            if (info == null)
            {
                // The caller (PdfSharpAdapter) pre-checks coverage before ever building the XFont, so a
                // null here is not expected - guard defensively rather than dereferencing null.
                throw new InvalidOperationException("No appropriate font found.");
            }

            var simSuffix = (info.MustSimulateBold ? "/b+" : "") + (info.MustSimulateItalic ? "/i+" : "");
            var key = KeyPrefix + "cp/" + info.FaceName.ToLowerInvariant() + simSuffix;

            if (resolver.InstanceGlyphTypefacesByKey.TryGetValue(key, out var cached))
                return cached;

            var fontSource = XFontSource.GetOrCreateFrom(resolver.GetFont(info.FaceName));
            var glyphTypeface = new XGlyphTypeface(key, fontSource, info.StyleSimulations)
            {
                OwningInstanceResolver = resolver
            };
            resolver.InstanceGlyphTypefacesByKey[key] = glyphTypeface;
            return glyphTypeface;
        }

        /// <summary>
        /// The specific <see cref="PeachPDF.Fonts.FontResolver"/> instance this typeface was resolved through its
        /// OWN per-instance cache for (i.e. a custom/<c>@font-face</c>-registered family - see
        /// <see cref="GetOrCreateFrom"/>), or null if it came from the global, process-wide caches (a
        /// pure system-font request, safe to share). <see cref="Drawing.XFont"/> reads this to route
        /// <see cref="Fonts.FontDescriptorCache"/> lookups the same way - that cache is ALSO keyed by
        /// this typeface's <see cref="Key"/> string alone, so without this it would silently reintroduce
        /// the exact cross-instance collision the split above fixes, just one layer further down.
        /// </summary>
        internal FontResolver? OwningInstanceResolver { get; set; }

        public XFontFamily FontFamily
        {
            get { return _fontFamily; }
        }
        readonly XFontFamily _fontFamily;

        internal OpenTypeFontface Fontface
        {
            get { return _fontface; }
        }
        readonly OpenTypeFontface _fontface;

        public XFontSource FontSource
        {
            get { return _fontSource; }
        }
        readonly XFontSource _fontSource;






















        void Initialize()
        {
            _familyName = _fontface.name.Name;
            if (string.IsNullOrEmpty(_faceName) || _faceName.StartsWith("?"))
                _faceName = _familyName;
            _styleName = _fontface.name.Style;
            _displayName = _fontface.name.FullFontName;
            if (string.IsNullOrEmpty(_displayName))
            {
                _displayName = _familyName;
                if (string.IsNullOrEmpty(_styleName))
                    _displayName += " (" + _styleName + ")";
            }

            // Bold, as defined in OS/2 table.
            _isBold = _fontface.os2.IsBold;
            // Debug.Assert(_isBold == (_fontface.os2.usWeightClass > 400), "Check font weight.");

            // Italic, as defined in OS/2 table.
            _isItalic = _fontface.os2.IsItalic;
        }

        /// <summary>
        /// Gets the name of the font face. This can be a file name, an uri, or a GUID.
        /// </summary>
        internal string FaceName
        {
            get { return _faceName; }
        }
        string _faceName = null!;

        /// <summary>
        /// Gets the English family name of the font, for example "Arial".
        /// </summary>
        public string FamilyName
        {
            get { return _familyName; }
        }
        string _familyName = null!;

        /// <summary>
        /// Gets the English subfamily name of the font,
        /// for example "Bold".
        /// </summary>
        public string StyleName
        {
            get { return _styleName; }
        }
        string _styleName = null!;

        /// <summary>
        /// Gets the English display name of the font,
        /// for example "Arial italic".
        /// </summary>
        public string DisplayName
        {
            get { return _displayName; }
        }
        string _displayName = null!;

        /// <summary>
        /// Gets a value indicating whether the font weight is bold.
        /// </summary>
        public bool IsBold
        {
            get { return _isBold; }
        }
        bool _isBold;

        /// <summary>
        /// Gets a value indicating whether the font style is italic.
        /// </summary>
        public bool IsItalic
        {
            get { return _isItalic; }
        }
        bool _isItalic;

        public XStyleSimulations StyleSimulations
        {
            get { return _styleSimulations; }
        }
        XStyleSimulations _styleSimulations;

        /// <summary>
        /// Gets the suffix of the face name in a PDF font and font descriptor.
        /// The name based on the effective value of bold and italic from the OS/2 table.
        /// </summary>
        string GetFaceNameSuffix()
        {
            // Use naming of Microsoft Word.
            if (IsBold)
                return IsItalic ? ",BoldItalic" : ",Bold";
            return IsItalic ? ",Italic" : "";
        }

        internal string GetBaseName()
        {
            string name = DisplayName;
            int ich = name.IndexOf("bold", StringComparison.OrdinalIgnoreCase);
            if (ich > 0)
                name = name.Substring(0, ich) + name.Substring(ich + 4, name.Length - ich - 4);
            ich = name.IndexOf("italic", StringComparison.OrdinalIgnoreCase);
            if (ich > 0)
                name = name.Substring(0, ich) + name.Substring(ich + 6, name.Length - ich - 6);
            //name = name.Replace(" ", "");
            name = name.Trim();
            name += GetFaceNameSuffix();
            return name;
        }

        /// <summary>
        /// Computes the bijective key for a typeface.
        /// </summary>
        internal static string ComputeKey(string familyName, FontResolvingOptions fontResolvingOptions)
        {
            // Compute a human readable key.
            string simulationSuffix = "";
            if (fontResolvingOptions.OverrideStyleSimulations)
            {
                switch (fontResolvingOptions.StyleSimulations)
                {
                    case XStyleSimulations.BoldSimulation: simulationSuffix = "|b+/i-"; break;
                    case XStyleSimulations.ItalicSimulation: simulationSuffix = "|b-/i+"; break;
                    case XStyleSimulations.BoldItalicSimulation: simulationSuffix = "|b+/i+"; break;
                    case XStyleSimulations.None: break;
                    default: throw new ArgumentOutOfRangeException();
                }
            }
            string key = KeyPrefix + familyName.ToLowerInvariant()
                + (fontResolvingOptions.IsItalic ? "/i" : "/n") // normal / oblique / italic
                + "/" + fontResolvingOptions.Weight
                + "/" + fontResolvingOptions.Stretch
                + simulationSuffix;
            return key;
        }

        /// <summary>
        /// Computes the bijective key for a typeface.
        /// </summary>
        internal static string ComputeKey(string familyName, bool isBold, bool isItalic)
        {
            return ComputeKey(familyName, new FontResolvingOptions(FontHelper.CreateStyle(isBold, isItalic)));
        }
        public string Key
        {
            get { return _key; }
        }
        readonly string _key;

        /// <summary>
        /// Gets the DebuggerDisplayAttribute text.
        /// </summary>
        // ReSharper disable UnusedMember.Local
        internal string DebuggerDisplay
        // ReSharper restore UnusedMember.Local
        {
            get { return string.Format(CultureInfo.InvariantCulture, "{0} - {1} ({2})", FamilyName, StyleName, FaceName); }
        }
    }
}